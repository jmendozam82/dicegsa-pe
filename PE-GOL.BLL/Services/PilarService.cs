using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Pilares Estratégicos — Spec HU-013 § Lógica BLL (pasos 1-5).
/// Implementación real (fase IMPLEMENT); los 34 tests de PilarServiceTests son la especificación
/// ejecutable (TEST-01/03/05).
/// Reglas:
///  · D12: re-validación defensiva de rol — solo el Gerente crea/edita/elimina (D-G, RN-006);
///    el JefeArea solo lee (RN-007). SEC-07 NO APLICA (D-E): pilar es corporativa (sin area_id).
///  · RC-12/RN-004: un ciclo Cerrado es de solo lectura (422). Borrador y Activo permitidos (D-F).
///  · Normalización (trim) + re-validación BLL (fuente de verdad, UX-04): nombre requerido y
///    ≤ 150 chars, estrategia ≤ 2000 chars (D-C), orden no negativo (D-H).
///  · CA #2 (D-C): máximo 8 pilares por ciclo (constante MaxPilaresPorCiclo) → 422.
///  · CA #1 (D-B): código PEC-N auto-generado en BLL con DAL-P5 (MAX(regexp_match)+1 por ciclo);
///    capa 2 = UNIQUE (ciclo_id, codigo) 23505 → 422 (ADR-007).
///  · CA #3 (D-A): DELETE físico con validación BLL de dependencias (DAL-P9 → 422) + red de
///    seguridad FK objetivo_cg.pilar_id/okr.pilar_id sin CASCADE (23503 → 422, patrón HU-002 D2).
///  · D-H: orden opcional — null → default secuencial (MAX(orden)+1 del ciclo) en create; null →
///    conserva el existente en update.
///  · Escrituras (INSERT/UPDATE/DELETE + auditoría) en UNA sola transacción IDbTransaction
///    (patrón HU-001..HU-012); rollback explícito antes de propagar ValidacionException en
///    capturas 23505/23503 (ADR-007).
///  · Auditoría (ADR-003): Entidad="Pilar", snapshot SOLO del alcance HU-013
///    {"codigo","nombre","estrategia_victoria","orden"} (D-K); valor_anterior=null en CREATE,
///    valor_nuevo=null en DELETE. objetivo_q1..q4 NO se auditan (HU-014).
/// Ctor aprobado por spec: (ICicloRepository, TenantContext) + overload con ILogger (D13).
/// </summary>
public class PilarService : IPilarService
{
    private const string RolGerente = "Gerente";
    private const string EntidadAuditoria = "Pilar";

    /// <summary>CA #2 (D-C): máximo de pilares por ciclo.</summary>
    private const int MaxPilaresPorCiclo = 8;

    /// <summary>VARCHAR(150) del DDL pilar.nombre.</summary>
    private const int MaxLongitudNombre = 150;

    /// <summary>D-C: límite razonable para estrategia_victoria (TEXT sin límite en DDL).</summary>
    private const int MaxLongitudEstrategia = 2000;

    /// <summary>
    /// Opciones de serialización para la auditoría (log_auditoria.valor_anterior/valor_nuevo).
    /// UnsafeRelaxedJsonEscaping: NO escapa caracteres no-ASCII → el JSON del log es legible
    /// por humanos (RNF-023 / ADR-003).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ICicloRepository _repository;
    private readonly TenantContext _tenantContext; // D17: TenantId nullable; D12: re-validación de rol
    private readonly ILogger<PilarService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public PilarService(ICicloRepository repository, TenantContext tenantContext)
        : this(repository, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public PilarService(ICicloRepository repository, TenantContext tenantContext, ILogger<PilarService>? logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ListarAsync: tenantId (404) → ciclo (404) → DAL-P1 → List&lt;PilarResponse&gt;
    /// (conteos CG/OKRs, CA #5; ORDER BY orden ASC, codigo ASC — D-A). SEC-07 NO APLICA (D-E):
    /// el JEF ve TODOS los pilares del ciclo (RN-007: solo lectura).</summary>
    public async Task<List<PilarResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: el filtro por tenant_id garantiza que un ciclo de otro tenant también devuelve null → 404 sin fuga.
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var pilares = await _repository.ListarPilaresConConteosAsync(tenantId, cicloId, ct);

        // D-A: orden de presentación — Orden asc, desempate por Codigo asc (Caso 31 de
        // PilarServiceTests). El DAL ya ordena en SQL, pero la BLL re-ordena de forma
        // idempotente (estable) para garantizar el contrato de presentación.
        return pilares
            .OrderBy(p => p.Orden)
            .ThenBy(p => p.Codigo, StringComparer.Ordinal)
            .Select(p => MapToResponse(p, tenantId))
            .ToList();
    }

    /// <summary>Spec §2 · ObtenerAsync: tenantId (404) → ciclo (404) → DAL-P2 → PilarResponse
    /// (conteos, CA #5). 404 si el pilar no existe o es de otro tenant (sin fuga).</summary>
    public async Task<PilarResponse> ObtenerAsync(Guid cicloId, Guid pilarId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var pilar = await _repository.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, ct)
            ?? throw new NotFoundException($"El pilar '{pilarId}' no existe");

        return MapToResponse(pilar, tenantId);
    }

    /// <summary>Spec §3 · CrearAsync: rol GER (403, D12) → tenantId (404) → ciclo (404) → RC-12
    /// Cerrado (422) → normalizar trim → re-validación BLL (nombre/estrategia/orden, 422) → CA #2
    /// máx 8 (422) → CA #1 código PEC-N + orden (DAL-P5) → tx: INSERT (DAL-P6) + auditoría CREATE
    /// (ADR-003) → commit → re-lectura (DAL-P2) → 201. Captura 23505 → 422 (ADR-007).</summary>
    public async Task<PilarResponse> CrearAsync(Guid cicloId, PilarCreateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-G/RN-006: el GER es el único con escritura del
        // contenido estratégico — mismo criterio D-A de HU-011/HU-012).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Gerente puede crear pilares estratégicos");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-F).
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // Paso 5: normalizar (trim) — se eliminan espacios iniciales/finales.
        var nombre = request.Nombre.Trim();
        var estrategia = request.EstrategiaVictoria?.Trim();

        // Paso 6: re-validación BLL (fuente de verdad, UX-04).
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ValidacionException("El nombre del pilar es requerido");
        if (nombre.Length > MaxLongitudNombre)
            throw new ValidacionException($"El nombre no puede exceder {MaxLongitudNombre} caracteres");
        if (estrategia?.Length > MaxLongitudEstrategia)
            throw new ValidacionException($"La estrategia de victoria no puede exceder {MaxLongitudEstrategia} caracteres");
        if (request.Orden is < 0)
            throw new ValidacionException("El orden no puede ser negativo");

        // CA #2 (D-C): máximo 8 pilares por ciclo (regla de negocio blanda — carrera TOCTOU aceptada).
        if (await _repository.ContarPilaresDelCicloAsync(tenantId, cicloId, ct) >= MaxPilaresPorCiclo)
            throw new ValidacionException($"Máximo {MaxPilaresPorCiclo} pilares por ciclo");

        // CA #1 (D-B): código PEC-N auto-generado + orden secuencial (D-H) — DAL-P5, una sola query.
        var sec = await _repository.ObtenerSiguienteSecuenciaPilarAsync(tenantId, cicloId, ct);
        var codigo = $"PEC-{sec.SiguienteN}";
        var orden = request.Orden ?? sec.SiguienteOrden;

        var dto = new PilarInsertDto
        {
            TenantId = tenantId,
            CicloId = cicloId,
            Codigo = codigo,
            Nombre = nombre,
            EstrategiaVictoria = estrategia,
            Orden = orden
        };

        // Spec §3 paso 9: INSERT (DAL-P6) + auditoría CREATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var pilarId = await _repository.InsertarPilarAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el pilar: id nulo.");

            _logger?.LogInformation(
                "Pilar {PilarId} creado en ciclo {CicloId} por {UserId}. Modulo=Ciclo, Accion=CREATE, Entidad={Entidad}",
                pilarId, cicloId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=CREATE, valor_anterior=null, snapshot del
            // alcance HU-013 (D-K, ADR-003 — JSON legible con UnsafeRelaxedJsonEscaping).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = pilarId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SnapshotPilar(codigo, nombre, estrategia, orden)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (paso 10).
            var creado = await _repository.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, ct)
                ?? throw new InvalidOperationException($"El pilar '{pilarId}' recién creado no se pudo leer.");
            return MapToResponse(creado, tenantId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-007 · Capa 2: el UNIQUE (ciclo_id, codigo) del DDL L190 elimina la carrera TOCTOU
            // del código PEC-N concurrente (dos GER creando en paralelo con el mismo N).
            // Rollback explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear pilar en ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("Ya existe un pilar con el código PEC-N en este ciclo; reintente");
        }
        catch
        {
            // Rollback para garantizar atomicidad (INSERT + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ActualizarAsync: rol GER (403, D12) → tenantId (404) → ciclo (404) →
    /// RC-12 Cerrado (422) → pilar (404, DAL-P3) → normalizar trim → re-validación BLL (422) →
    /// ordenFinal = request.Orden ?? original.Orden (D-H) → snapshot previo (ADR-003) → tx:
    /// UPDATE (DAL-P7, NO toca codigo — CA #1) + auditoría UPDATE → commit → re-lectura (DAL-P2)
    /// → 200. Captura 23505 → 422 (residual defensivo, ADR-007).</summary>
    public async Task<PilarResponse> ActualizarAsync(Guid cicloId, Guid pilarId, PilarUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-G/RN-006).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Gerente puede editar pilares estratégicos");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var original = await _repository.ObtenerPilarAsync(tenantId, cicloId, pilarId, ct)
            ?? throw new NotFoundException($"El pilar '{pilarId}' no existe");

        // Normalizar (trim) + re-validación BLL (mismas reglas que POST, pasos 5-6).
        var nombre = request.Nombre.Trim();
        var estrategia = request.EstrategiaVictoria?.Trim();

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ValidacionException("El nombre del pilar es requerido");
        if (nombre.Length > MaxLongitudNombre)
            throw new ValidacionException($"El nombre no puede exceder {MaxLongitudNombre} caracteres");
        if (estrategia?.Length > MaxLongitudEstrategia)
            throw new ValidacionException($"La estrategia de victoria no puede exceder {MaxLongitudEstrategia} caracteres");
        if (request.Orden is < 0)
            throw new ValidacionException("El orden no puede ser negativo");

        // D-H: Orden=null → conserva el orden existente del pilar original.
        var ordenFinal = request.Orden ?? original.Orden;

        // Snapshot de auditoría (ADR-003): valor_anterior = estado previo del alcance HU-013 (D-K).
        var valorAnterior = SnapshotPilar(original);

        var dto = new PilarUpdateDto
        {
            TenantId = tenantId,
            CicloId = cicloId,
            PilarId = pilarId,
            Nombre = nombre,
            EstrategiaVictoria = estrategia,
            Orden = ordenFinal
        };

        // Spec §4 paso 9: UPDATE (DAL-P7) + auditoría UPDATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var filas = await _repository.ActualizarPilarAsync(dto, tx, ct);
            if (filas == 0)
                throw new InvalidOperationException("No se pudo actualizar el pilar");

            _logger?.LogInformation(
                "Pilar {PilarId} actualizado. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                pilarId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=UPDATE, snapshot anterior/nuevo (ADR-003, D-K).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = pilarId.ToString(),
                ValorAnterior = valorAnterior,
                ValorNuevo = SnapshotPilar(original.Codigo, nombre, estrategia, ordenFinal)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (paso 10).
            var actualizado = await _repository.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, ct)
                ?? throw new NotFoundException($"El pilar '{pilarId}' no existe");
            return MapToResponse(actualizado, tenantId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-007 · Capa 2 (residual defensivo): el código no cambia en PUT, pero se conserva
            // el patrón. Rollback explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar pilar {PilarId}. Modulo=Ciclo", pilarId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("Ya existe un pilar con el código PEC-N en este ciclo; reintente");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §5 · EliminarAsync: rol GER (403, D12) → tenantId (404) → ciclo (404) →
    /// RC-12 Cerrado (422) → pilar (404, DAL-P3) → CA #3 dependencias (DAL-P9 → 422) → snapshot
    /// previo (ADR-003) → tx: DELETE físico (DAL-P8, D-A) + auditoría DELETE (valor_nuevo=null) →
    /// commit → 200 con snapshot del pilar eliminado (conteos 0). Captura 23503 → 422 (capa 2 FK,
    /// patrón ADR-007/HU-002 D2).</summary>
    public async Task<PilarResponse> EliminarAsync(Guid cicloId, Guid pilarId, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-G/RN-006).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Gerente puede eliminar pilares estratégicos");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var original = await _repository.ObtenerPilarAsync(tenantId, cicloId, pilarId, ct)
            ?? throw new NotFoundException($"El pilar '{pilarId}' no existe");

        // CA #3 (D-A): no se elimina un pilar con Objetivos CG u OKRs asociados (RN-014).
        var dependencias = await _repository.ContarDependenciasPilarAsync(tenantId, pilarId, ct);
        if (dependencias.TotalObjetivosCg > 0)
            throw new ValidacionException($"No se puede eliminar el pilar porque tiene {dependencias.TotalObjetivosCg} Objetivos CG asociados");
        if (dependencias.TotalOkrs > 0)
            throw new ValidacionException($"No se puede eliminar el pilar porque tiene {dependencias.TotalOkrs} OKRs asociados");

        // Snapshot de auditoría (ADR-003): valor_anterior = estado previo; valor_nuevo = null (DELETE).
        var valorAnterior = SnapshotPilar(original);

        // Spec §5 paso 8: DELETE físico (DAL-P8) + auditoría DELETE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var filas = await _repository.EliminarPilarAsync(tenantId, cicloId, pilarId, tx, ct);
            if (filas == 0)
                throw new InvalidOperationException("No se pudo eliminar el pilar");

            _logger?.LogInformation(
                "Pilar {PilarId} eliminado. Modulo=Ciclo, Accion=DELETE, Entidad={Entidad}",
                pilarId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=DELETE, valor_anterior=snapshot, valor_nuevo=null.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DELETE",
                Entidad = EntidadAuditoria,
                EntidadId = pilarId.ToString(),
                ValorAnterior = valorAnterior,
                ValorNuevo = null
            }, tx, ct);

            tx.Commit();

            // 200 con el snapshot del pilar eliminado (conteos 0 — patrón de retornar la entidad
            // afectada, consistente con HU-009/HU-010).
            return MapToResponse(original, tenantId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // ADR-007 extendido a 23503 · Capa 2 BD: FK objetivo_cg.pilar_id/okr.pilar_id SIN
            // ON DELETE CASCADE (carrera TOCTOU entre el conteo DAL-P9 y el DELETE). Rollback
            // explícito antes de propagar (422) — mismo precedente que PlanService HU-002 D2.
            _logger?.LogWarning(ex, "Violación de FK 23503 al eliminar pilar {PilarId}. Modulo=Ciclo", pilarId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23503. Modulo=Ciclo"); }
            throw new ValidacionException("No se puede eliminar el pilar porque tiene Objetivos CG u OKRs asociados");
        }
        catch
        {
            // Rollback para garantizar atomicidad (DELETE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en EliminarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>D17: TenantId null (SuperAdmin sin tenant) → 404 defensivo (los roles autorizados
    /// siempre tienen tenant_id; el SuperAdmin queda fuera por [Authorize(Roles=...)]).</summary>
    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("El tenant no existe");
        return _tenantContext.TenantId.Value;
    }

    /// <summary>Snapshot de auditoría del alcance HU-013 (D-K, ADR-003): JSON con
    /// UnsafeRelaxedJsonEscaping (legible sin escapes Unicode). SOLO los campos que gestiona
    /// esta HU — objetivo_q1..q4 NO se incluyen (HU-014 los auditará cuando los gestione).</summary>
    private static string SnapshotPilar(string codigo, string nombre, string? estrategiaVictoria, int orden)
        => JsonSerializer.Serialize(new
        {
            codigo,
            nombre,
            estrategia_victoria = estrategiaVictoria,
            orden
        }, JsonOpcionesAuditoria);

    /// <summary>Snapshot del estado previo para valor_anterior en UPDATE/DELETE (ADR-003, D-K).</summary>
    private static string SnapshotPilar(PilarEntity e)
        => SnapshotPilar(e.Codigo, e.Nombre, e.EstrategiaVictoria, e.Orden);

    /// <summary>Mapeo DAL → response (listado/detalle con conteos, CA #5). TenantId del
    /// TenantContext (SEC-06), nunca del DTO.</summary>
    private static PilarResponse MapToResponse(PilarConteosDto e, Guid tenantId) => new()
    {
        Id = e.Id,
        CicloId = e.CicloId,
        TenantId = tenantId,
        Codigo = e.Codigo,
        Nombre = e.Nombre,
        EstrategiaVictoria = e.EstrategiaVictoria,
        Orden = e.Orden,
        TotalObjetivosCg = e.TotalObjetivosCg,
        TotalOkrs = e.TotalOkrs,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    /// <summary>Mapeo entidad → response para DELETE (snapshot del pilar eliminado con conteos 0 —
    /// patrón de retornar la entidad afectada, consistente con HU-009/HU-010).</summary>
    private static PilarResponse MapToResponse(PilarEntity e, Guid tenantId) => new()
    {
        Id = e.Id,
        CicloId = e.CicloId,
        TenantId = tenantId,
        Codigo = e.Codigo,
        Nombre = e.Nombre,
        EstrategiaVictoria = e.EstrategiaVictoria,
        Orden = e.Orden,
        TotalObjetivosCg = 0,
        TotalOkrs = 0,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
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
/// Servicio de Visión y Misión del ciclo — Spec HU-011 § Lógica BLL (pasos 1-2);
/// Spec HU-012 § Lógica BLL (extensión aditiva: ActualizarValoresAsync + mapeo de Valores).
/// Implementación real (fase IMPLEMENT); los 15 tests de FilosofiaServiceTests + 3 de
/// CicloServiceTests.ActivarFilosofia + 18 de HU-012 son la especificación ejecutable.
/// Reglas:
///  · D12: re-validación defensiva de rol — solo el Gerente registra/edita (D-A); el JefeArea
///    solo lee (RN-007). SEC-07 NO APLICA (D-E): filosofia es corporativa (sin area_id).
///  · RC-12/RN-004: un ciclo Cerrado es de solo lectura (422).
///  · D-C: sanitización HTML con allowlist (p, br, b, strong, i, em, ul, ol, li; sin atributos;
///    bloques peligrosos eliminados con su contenido) — SEC-05 (XSS).
///  · CA #2 (Flag #1 — validación dura): el texto visible (StripHtml) de Visión y Misión no puede
///    quedar vacío (422). Longitud máxima 5000 caracteres (422).
///  · D-D: snapshot previo para auditoría — null si no existe fila (primer guardado = UPDATE con
///    valor_anterior null). D-B: sin fila default en CrearAsync → el UPSERT (DAL-F2/F3, DB-06) crea
///    la fila en el primer guardado.
///  · Escritura (UPSERT + auditoría) en UNA sola transacción IDbTransaction (patrón HU-001..HU-010);
///    captura SQLSTATE 23505 → rollback explícito + ValidacionException (ADR-001).
///  · Auditoría (ADR-003): Entidad="Filosofia", EntidadId=cicloId.ToString(), JSON legible con
///    UnsafeRelaxedJsonEscaping; se audita como UPDATE (D-K, enum accion_auditoria).
///  · HU-012 · ActualizarValoresAsync: CA #4 (min 1, max 15, trim, no vacío, ≤100 chars, sin
///    duplicados case-insensitive — D-G); orden = índice del array JSONB (D-C); UPSERT DAL-F3
///    solo toca valores/updated_by/updated_at, NO pisa vision/mision (D-B); auditoría del array
///    {"valores":[...]} (D-D).
/// Ctor aprobado por spec: (ICicloRepository, TenantContext) + overload con ILogger (D-J/D13).
/// </summary>
public class FilosofiaService : IFilosofiaService
{
    private const string RolGerente = "Gerente";
    private const string EntidadAuditoria = "Filosofia";
    private const int LongitudMaxima = 5000;

    // HU-012 · CA #4 (D-G): límites de la lista de Valores Corporativos.
    private const int MinValores = 1;
    private const int MaxValores = 15;
    private const int MaxLongitudValor = 100;

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
    private readonly ILogger<FilosofiaService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public FilosofiaService(ICicloRepository repository, TenantContext tenantContext)
        : this(repository, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public FilosofiaService(ICicloRepository repository, TenantContext tenantContext, ILogger<FilosofiaService>? logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ObtenerAsync: tenantId (404) → ciclo (404) → DAL-F1 → FilosofiaResponse
    /// (defensivo D-H: Id=Guid.Empty, Vision/Mision="", UpdatedBy/UpdatedAt=null si no existe fila).</summary>
    public async Task<FilosofiaResponse> ObtenerAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: el filtro por tenant_id garantiza que un ciclo de otro tenant también devuelve null → 404 sin fuga.
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // D-H: sin fila filosofia → respuesta defensiva con defaults (sin escritura, patrón D5 HU-008).
        var filosofia = await _repository.ObtenerFilosofiaAsync(tenantId, cicloId, ct);
        return MapToResponse(filosofia, tenantId, cicloId);
    }

    /// <summary>Spec §2 · ActualizarAsync: rol Gerente (403, D12) → tenantId (404) → ciclo (404)
    /// → RC-12 Cerrado (422) → sanitizar D-C → CA #2 texto visible obligatorio (422) → longitud
    /// ≤ 5000 (422) → snapshot previo (D-D) → tx: DAL-F2 UPSERT + auditoría UPDATE (ADR-003) →
    /// commit → re-lectura → 200. Captura 23505 → 422.</summary>
    public async Task<FilosofiaResponse> ActualizarAsync(Guid cicloId, FilosofiaUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el Gerente registra/edita; el JEF solo lee, RN-007).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Gerente puede registrar la Visión y Misión del ciclo");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-H).
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // D-C: sanitización con allowlist (sin atributos, sin bloques peligrosos, sin comentarios).
        var vision = HtmlSanitizerHelper.SanitizarHtml(request.Vision);
        var mision = HtmlSanitizerHelper.SanitizarHtml(request.Mision);

        // CA #2 (Flag #1 — validación dura): el texto visible tras quitar etiquetas no puede quedar vacío.
        if (string.IsNullOrWhiteSpace(HtmlSanitizerHelper.StripHtml(vision)) ||
            string.IsNullOrWhiteSpace(HtmlSanitizerHelper.StripHtml(mision)))
            throw new ValidacionException("La Visión y la Misión son obligatorias");

        if (vision.Length > LongitudMaxima || mision.Length > LongitudMaxima)
            throw new ValidacionException($"La Visión y la Misión no pueden exceder {LongitudMaxima} caracteres");

        // D-D: snapshot previo para auditoría (null si no existe fila — primer guardado = UPDATE).
        var previo = await _repository.ObtenerFilosofiaAsync(tenantId, cicloId, ct);

        var dto = new FilosofiaUpsertDto
        {
            TenantId = tenantId,
            CicloId = cicloId,
            Vision = vision,
            Mision = mision,
            UpdatedBy = _tenantContext.UserId ?? Guid.Empty
        };

        // Spec §2 paso 9: UPSERT (DAL-F2) + auditoría UPDATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var filas = await _repository.UpsertFilosofiaAsync(dto, tx, ct);
            if (filas == 0)
                throw new InvalidOperationException("No se pudo registrar la filosofía del ciclo");

            _logger?.LogInformation(
                "Filosofia actualizada en ciclo {CicloId} por {UserId}. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                cicloId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=UPDATE, valor_anterior=null si primer guardado
            // (D-D), JSON legible (ADR-003). EntidadId = cicloId (la filosofía es 1:1 con el ciclo).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId ?? Guid.Empty,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = cicloId.ToString(),
                ValorAnterior = previo is null ? null : SerializarSnapshot(previo),
                ValorNuevo = SerializarFilosofia(vision, mision)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (D-H).
            var postCommit = await _repository.ObtenerFilosofiaAsync(tenantId, cicloId, ct)
                ?? throw new InvalidOperationException("La filosofía recién guardada no se pudo leer.");
            return MapToResponse(postCommit, tenantId, cicloId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el UNIQUE (tenant_id, ciclo_id) del DDL L173 elimina las carreras
            // TOCTOU. Rollback explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar filosofía en ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("Ya existe un registro de filosofía para este ciclo");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPSERT + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec HU-012 §2 · ActualizarValoresAsync: rol Gerente (403, D12) → tenantId (404)
    /// → ciclo (404) → RC-12 Cerrado (422) → trim (D-G) → CA #4 re-validación BLL (min 1, max 15,
    /// no vacío tras trim, ≤100 chars, sin duplicados case-insensitive) → snapshot previo (D-D) →
    /// tx: DAL-F3 UPSERT solo valores + auditoría UPDATE {"valores":[...]} (ADR-003) → commit →
    /// re-lectura → 200. Captura 23505 → 422.</summary>
    public async Task<FilosofiaResponse> ActualizarValoresAsync(Guid cicloId, ValoresUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (RN-006: el GER es el único con escritura del
        // contenido estratégico — mismo criterio D-A de HU-011).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Gerente puede registrar los Valores Corporativos del ciclo");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-F).
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // D-G: normalizar (trim) — se eliminan espacios iniciales/finales de cada valor.
        var valores = request.Valores.Select(v => v.Trim()).ToList();

        // CA #4 — re-validación BLL (fuente de verdad, UX-04).
        if (valores.Count < MinValores)
            throw new ValidacionException("Debe registrar al menos 1 valor corporativo");
        if (valores.Count > MaxValores)
            throw new ValidacionException($"Máximo {MaxValores} valores corporativos");
        if (valores.Any(v => string.IsNullOrWhiteSpace(v)))
            throw new ValidacionException("Cada valor es requerido");
        if (valores.Any(v => v.Length > MaxLongitudValor))
            throw new ValidacionException($"Cada valor no puede exceder {MaxLongitudValor} caracteres");
        if (valores.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ValidacionException("No se permiten valores duplicados");

        // D-D: snapshot de auditoría — solo el array valores (D-D de HU-012); null si no existía
        // fila (primer guardado = UPDATE con valor_anterior null).
        var previo = await _repository.ObtenerFilosofiaAsync(tenantId, cicloId, ct);
        var valorAnterior = previo is null ? null : SerializarValores(DeserializarValores(previo.Valores));

        // Serializar: string JSON para el parámetro @Valores::jsonb de DAL-F3 (SEC-05).
        var valoresJson = JsonSerializer.Serialize(valores);

        // Spec §2 paso 9: UPSERT (DAL-F3) + auditoría UPDATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var filas = await _repository.UpsertFilosofiaValoresAsync(new FilosofiaValoresUpsertDto
            {
                TenantId = tenantId,
                CicloId = cicloId,
                Valores = valoresJson,
                UpdatedBy = _tenantContext.UserId ?? Guid.Empty
            }, tx, ct);
            if (filas == 0)
                throw new InvalidOperationException("No se pudieron registrar los valores corporativos del ciclo");

            _logger?.LogInformation(
                "Valores corporativos actualizados en ciclo {CicloId} por {UserId}. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                cicloId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=UPDATE, snapshot del array {"valores":[...]}
            // (D-D, ADR-003 — JSON legible con UnsafeRelaxedJsonEscaping). EntidadId = cicloId.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId ?? Guid.Empty,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = cicloId.ToString(),
                ValorAnterior = valorAnterior,
                ValorNuevo = SerializarValores(valores)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (D-H).
            var postCommit = await _repository.ObtenerFilosofiaAsync(tenantId, cicloId, ct)
                ?? throw new InvalidOperationException("La filosofía recién guardada no se pudo leer.");
            return MapToResponse(postCommit, tenantId, cicloId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el UNIQUE (tenant_id, ciclo_id) del DDL L173 elimina las carreras
            // TOCTOU. Rollback explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar valores en ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("Ya existe un registro de filosofía para este ciclo");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPSERT + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarValoresAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    private Guid ObtenerTenantIdOThrow()
        => _tenantContext.TenantId ?? throw new NotFoundException("El tenant no existe");

    /// <summary>Snapshot de auditoría (ADR-003): JSON de la filosofía previa con
    /// UnsafeRelaxedJsonEscaping (legible sin escapes Unicode). Usado en valor_anterior de UPDATE.</summary>
    private static string SerializarSnapshot(FilosofiaEntity e)
        => JsonSerializer.Serialize(new { vision = e.Vision, mision = e.Mision }, JsonOpcionesAuditoria);

    /// <summary>Snapshot de auditoría (ADR-003): JSON de la filosofía nueva (ya sanitizada, D-C).
    /// Usado en valor_nuevo de UPDATE.</summary>
    private static string SerializarFilosofia(string vision, string mision)
        => JsonSerializer.Serialize(new { vision, mision }, JsonOpcionesAuditoria);

    /// <summary>Snapshot de auditoría del array de valores (HU-012, D-D/ADR-003): JSON
    /// {"valores":[...]} con UnsafeRelaxedJsonEscaping (legible sin escapes Unicode).
    /// Usado en valor_anterior/valor_nuevo de UPDATE.</summary>
    private static string SerializarValores(List<string> valores)
        => JsonSerializer.Serialize(new { valores }, JsonOpcionesAuditoria);

    /// <summary>Parseo defensivo del JSONB valores → List&lt;string&gt; (HU-012, D-C/D-H):
    /// JSON inválido o "[]" → lista vacía (nunca null). El orden del array se preserva.</summary>
    private static List<string> DeserializarValores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    /// <summary>Mapeo defensivo (D-H): si no existe fila → Id=Guid.Empty, Vision/Mision="",
    /// Valores=[], UpdatedBy/UpdatedByNombre/UpdatedAt=null (defaults en memoria, sin escritura).
    /// HU-012: Valores = DeserializarValores(e.Valores) (parseo JSONB → List&lt;string&gt;).</summary>
    private static FilosofiaResponse MapToResponse(FilosofiaEntity? e, Guid tenantId, Guid cicloId)
    {
        if (e is null)
            return new FilosofiaResponse { Id = Guid.Empty, CicloId = cicloId, TenantId = tenantId };

        return new FilosofiaResponse
        {
            Id = e.Id,
            CicloId = e.CicloId,
            TenantId = e.TenantId,
            Vision = e.Vision,
            Mision = e.Mision,
            Valores = DeserializarValores(e.Valores),
            UpdatedBy = e.UpdatedBy,
            UpdatedByNombre = e.UpdatedByNombre,
            UpdatedAt = e.UpdatedAt
        };
    }
}
using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Ciclos Anuales — Spec HU-007 § Lógica BLL (pasos 1-7).
/// Implementación real (fase 4 del Loop); los tests de @QA (CicloServiceTests) son la
/// especificación ejecutable.
/// Reglas:
///  · D12: re-validación defensiva de rol en cada escritura (ADM crea/edita/activa/clona;
///    GER cierra — D-A). Lecturas solo validan TenantId no null.
///  · Normalización de nombre (trim + colapso de espacios, mismo helper que TenantService).
///  · Re-validación BLL de mesInicio ∈ 1..12 (espejo del CHECK del DDL L134) → 422.
///  · Unicidad de año fiscal (CA #2): DAL-C6 con/sin excludeId → 422; capa 2 BD: captura
///    23505 del UNIQUE (tenant_id, año_fiscal) del DDL L141 → 422 (patrón ADR-001, D8).
///  · RC-01 (solo un Activo por tenant): DAL-C7 → 422; capa 2 BD: captura 23505 del índice
///    parcial uq_ciclo_unico_activo (ADR-006/V003) en ActivarAsync → 422.
///  · D-C: ActivarAsync consume IPlanService.ValidarLimitesParaTenantAsync (CA #2 HU-002)
///    con el plan_id del tenant vía DAL-C8 (sin acoplar a ITenantRepository).
///  · RC-12/RN-004: solo Borrador editable; Cerrado = solo lectura.
///  · Escrituras (INSERT/UPDATE/estado/umbrales + auditoría) en UNA sola transacción.
///  · Auditoría (ADR-003): Entidad="Ciclo", JSON legible con UnsafeRelaxedJsonEscaping;
///    cerrar se audita como UPDATE y clonar como CREATE con origenId (D11).
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, TenantContext) + overload con
/// ILogger (D13) — patrón UsuarioService/EmpresaService (RNF-023).
/// </summary>
public class CicloService : ICicloService
{
    private const string RolAdminTenant = "AdminTenant";
    private const string RolGerente = "Gerente";
    private const string EntidadAuditoria = "Ciclo";

    /// <summary>Defaults del DDL L150-151 (HU-008 CA #4 "Valores por defecto al crear ciclo").</summary>
    private const decimal UmbralVerdeDefault = 0.90m;
    private const decimal UmbralAmarilloDefault = 0.70m;

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
    private readonly IPlanService _planService;
    private readonly TenantContext _tenantContext; // D17: TenantId nullable; D12: re-validación de rol
    private readonly ILogger<CicloService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public CicloService(ICicloRepository repository, IPlanService planService, TenantContext tenantContext)
        : this(repository, planService, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public CicloService(ICicloRepository repository, IPlanService planService, TenantContext tenantContext, ILogger<CicloService>? logger)
    {
        _repository = repository;
        _planService = planService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ListarAsync: tenantId del contexto (404 si null) → DAL-C3 → List&lt;CicloResponse&gt;.</summary>
    public async Task<List<CicloResponse>> ListarAsync(CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclos = await _repository.ListarAsync(tenantId, ct);
        return ciclos.Select(MapToResponse).ToList();
    }

    /// <summary>Spec §2 · ObtenerPorIdAsync: tenantId (404) → DAL-C2 (404 si null — el filtro por
    /// tenant_id garantiza que un ciclo de otro tenant también devuelve null, sin fuga).</summary>
    public async Task<CicloResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
            ?? throw new NotFoundException($"El ciclo '{id}' no existe");
        return MapToResponse(ciclo);
    }

    /// <summary>Spec §3 · CrearAsync: rol ADM (403) → tenantId (404) → normalizar nombre →
    /// re-validar mesInicio (422) → unicidad año fiscal (422) → tx: INSERT ciclo + 2 umbrales
    /// default + auditoría CREATE → commit → re-lectura → 201. Captura 23505 → 422 (ADR-001).</summary>
    public async Task<CicloResponse> CrearAsync(CicloCreateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM crea).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede crear ciclos");

        var tenantId = ObtenerTenantIdOThrow();

        var nombre = NormalizarNombre(request.Nombre);

        // Re-validación BLL de la forma (fuente de verdad, UX-04) — espejo del CHECK del DDL L134.
        if (request.MesInicio < 1 || request.MesInicio > 12)
            throw new ValidacionException("El mes de inicio debe estar entre 1 y 12");

        // CA #2: unicidad de año fiscal (DAL-C6, capa 1 con mensaje amigable).
        if (await _repository.ExisteAñoFiscalAsync(tenantId, request.AñoFiscal, null, ct))
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");

        var dto = new CicloInsertDto
        {
            TenantId = tenantId,
            Nombre = nombre,
            AñoFiscal = request.AñoFiscal,
            MesInicio = request.MesInicio,
            CreatedBy = _tenantContext.UserId ?? Guid.Empty // wiring D6 HU-004
        };

        // Spec §3 paso 6: INSERT ciclo + 2 umbrales default + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repository.InsertAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el ciclo: id nulo.");

            // HU-008 CA #4: 2 umbrales por defecto (KPI y PlanAccion, 0.90/0.70 — defaults del DDL L150-151).
            await InsertarUmbralDefaultAsync(nuevoId, tenantId, "KPI", tx, ct);
            await InsertarUmbralDefaultAsync(nuevoId, tenantId, "PlanAccion", tx, ct);

            _logger?.LogInformation(
                "Ciclo {CicloId} creado por {UserId}. Modulo=Ciclo, Accion=CREATE, Entidad={Entidad}",
                nuevoId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11): accion=CREATE, valor_anterior=null, JSON legible (ADR-003).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SerializarCiclo(
                    nuevoId, tenantId, nombre, request.AñoFiscal, request.MesInicio,
                    "Borrador", dto.CreatedBy)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var creado = await _repository.ObtenerPorIdAsync(tenantId, nuevoId, ct)
                ?? throw new InvalidOperationException($"El ciclo '{nuevoId}' recién creado no se pudo leer.");
            return MapToResponse(creado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: colisión concurrente del UNIQUE (tenant_id, año_fiscal) del DDL L141.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear ciclo con año fiscal {AñoFiscal}. Modulo=Ciclo", request.AñoFiscal);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");
        }
        catch
        {
            // Rollback para garantizar atomicidad (INSERT + umbrales + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ActualizarAsync: rol ADM (403) → tenantId (404) → original (404) →
    /// RC-12 solo Borrador (422) → normalizar/re-validar mesInicio (422) → unicidad excluyendo
    /// self (422) → tx: UPDATE + auditoría UPDATE con snapshot → commit → re-lectura → 200.</summary>
    public async Task<CicloResponse> ActualizarAsync(Guid id, CicloUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM edita).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede editar ciclos");

        var tenantId = ObtenerTenantIdOThrow();

        var original = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
            ?? throw new NotFoundException($"El ciclo '{id}' no existe");

        // RC-12/RN-004: solo Borrador editable (un ciclo Activo/Cerrado es inmutable en sus parámetros).
        if (!string.Equals(original.Estado, "Borrador", StringComparison.Ordinal))
            throw new ValidacionException("Solo se puede editar un ciclo en estado Borrador");

        var nombre = NormalizarNombre(request.Nombre);

        // Re-validación BLL de la forma (espejo del CHECK del DDL L134).
        if (request.MesInicio < 1 || request.MesInicio > 12)
            throw new ValidacionException("El mes de inicio debe estar entre 1 y 12");

        // CA #2 excluyendo self (DAL-C6 con excludeId = id).
        if (await _repository.ExisteAñoFiscalAsync(tenantId, request.AñoFiscal, id, ct))
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");

        var dto = new CicloUpdateDto
        {
            Id = id,
            TenantId = tenantId,
            Nombre = nombre,
            AñoFiscal = request.AñoFiscal,
            MesInicio = request.MesInicio
        };

        // Spec §4 paso 8: UPDATE + auditoría UPDATE con snapshot previo en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateAsync(dto, tx, ct);

            _logger?.LogInformation(
                "Ciclo {CicloId} actualizado. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = SerializarSnapshot(original),
                ValorNuevo = SerializarCiclo(
                    id, tenantId, nombre, request.AñoFiscal, request.MesInicio,
                    original.Estado, original.CreatedBy)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizado = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
                ?? throw new NotFoundException($"El ciclo '{id}' no existe");
            return MapToResponse(actualizado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el UNIQUE (tenant_id, año_fiscal) también protege el UPDATE.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar ciclo {CicloId}. Modulo=Ciclo", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §5 · ActivarAsync: rol ADM (403) → tenantId (404) → ciclo (404) → ya Activo
    /// (422) → Cerrado (422, RC-12) → RC-01 DAL-C7 (422) → límite del plan vía IPlanService
    /// (422, D-C) → tx: UPDATE estado + auditoría ACTIVATE → commit → re-lectura → 200.
    /// Captura 23505 del índice parcial uq_ciclo_unico_activo (ADR-006/V003) → 422.</summary>
    public async Task<CicloResponse> ActivarAsync(Guid id, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: el ADM activa Borrador → Activo; el GER solo cierra).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede activar ciclos");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
            ?? throw new NotFoundException($"El ciclo '{id}' no existe");

        // Evita log duplicado y auditoría imprecisa.
        if (string.Equals(ciclo.Estado, "Activo", StringComparison.Ordinal))
            throw new ValidacionException("El ciclo ya está activo");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // RC-01: solo un Activo por tenant (DAL-C7; excludeId excluye el ciclo que se activa, aún Borrador).
        if (await _repository.ContarCiclosActivosAsync(tenantId, id, ct) > 0)
            throw new ValidacionException("Ya existe un ciclo activo en este tenant");

        // D-C (CA #2 HU-002): límite max_ciclos_activos del plan vía IPlanService (DAL-C8 evita
        // acoplar a ITenantRepository). Si el plan no está asignado → 404 defensivo.
        var planId = await _repository.ObtenerPlanIdDelTenantAsync(tenantId, ct)
            ?? throw new NotFoundException("El tenant no tiene un plan asignado");
        var resultado = await _planService.ValidarLimitesParaTenantAsync(tenantId, planId, ct);
        if (!resultado.EsValido)
            throw new ValidacionException(string.Join(" ", resultado.Errores));

        var activatedAt = DateTimeOffset.UtcNow;

        // Spec §5 paso 8: UPDATE estado + auditoría ACTIVATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(tenantId, id, "Activo", activatedAt, null, tx, ct);

            _logger?.LogInformation(
                "Ciclo {CicloId} activado. Modulo=Ciclo, Accion=ACTIVATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "ACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = ciclo.Estado }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Activo", activatedAt }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var activo = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
                ?? throw new NotFoundException($"El ciclo '{id}' no existe");
            return MapToResponse(activo);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-006/V003 · Capa 2: el índice parcial uq_ciclo_unico_activo garantiza RC-01 a
            // nivel BD (carrera TOCTOU de activaciones concurrentes) → 422 con rollback explícito.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al activar ciclo {CicloId}. Modulo=Ciclo", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("Ya existe un ciclo activo en este tenant");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE estado + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActivarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §6 · CerrarAsync: rol GER (403, D-A) → tenantId (404) → ciclo (404) →
    /// solo Activo (422) → tx: UPDATE estado (activated_at conservado) + auditoría UPDATE (D11)
    /// → commit → re-lectura → 200.</summary>
    public async Task<CicloResponse> CerrarAsync(Guid id, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el GER cierra Activo → Cerrado; el ADM no cierra).
        if (!string.Equals(_tenantContext.Rol, RolGerente, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el gerente puede cerrar el ciclo activo");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
            ?? throw new NotFoundException($"El ciclo '{id}' no existe");

        // Solo el ciclo activo se cierra; un Borrador no se cierra; un Cerrado ya está cerrado.
        if (!string.Equals(ciclo.Estado, "Activo", StringComparison.Ordinal))
            throw new ValidacionException("Solo se puede cerrar el ciclo activo (estado Activo)");

        var closedAt = DateTimeOffset.UtcNow;

        // Spec §6 paso 4: UPDATE estado + auditoría en UNA sola transacción. activated_at se
        // conserva (DAL-C5); se registra closed_at. D11: el enum accion_auditoria no tiene
        // CLOSE → se audita como UPDATE con el cambio de estado en valor_nuevo.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(tenantId, id, "Cerrado", ciclo.ActivatedAt, closedAt, tx, ct);

            _logger?.LogInformation(
                "Ciclo {CicloId} cerrado. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = ciclo.Estado }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Cerrado", closedAt }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var cerrado = await _repository.ObtenerPorIdAsync(tenantId, id, ct)
                ?? throw new NotFoundException($"El ciclo '{id}' no existe");
            return MapToResponse(cerrado);
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE estado + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CerrarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §7 · ClonarAsync: rol ADM (403) → tenantId (404) → origen (404) →
    /// normalizar/re-validar mesInicio (422) → unicidad año fiscal (422) → umbrales del origen
    /// (DAL-C10) → tx: INSERT ciclo nuevo + umbrales copiados (o 2 defaults si origen sin
    /// umbrales, defensivo) + auditoría CREATE con origenId (D11) → commit → re-lectura → 201.
    /// D-B (CA #5 parcial): áreas/responsables NO se clonan (HU-009/HU-010, Flag #2).</summary>
    public async Task<CicloResponse> ClonarAsync(Guid idOrigen, ClonarCicloRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM clona).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede clonar ciclos");

        var tenantId = ObtenerTenantIdOThrow();

        var origen = await _repository.ObtenerPorIdAsync(tenantId, idOrigen, ct)
            ?? throw new NotFoundException($"El ciclo origen '{idOrigen}' no existe");

        var nombre = NormalizarNombre(request.Nombre);

        // Re-validación BLL de la forma (espejo del CHECK del DDL L134).
        if (request.MesInicio < 1 || request.MesInicio > 12)
            throw new ValidacionException("El mes de inicio debe estar entre 1 y 12");

        // CA #2: unicidad de año fiscal (DAL-C6, capa 1).
        if (await _repository.ExisteAñoFiscalAsync(tenantId, request.AñoFiscal, null, ct))
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");

        // DAL-C10: umbrales del ciclo origen (para copiarlos al nuevo — D-B).
        var umbralesOrigen = (await _repository.ObtenerUmbralesAsync(tenantId, idOrigen, ct)).ToList();

        var dto = new CicloInsertDto
        {
            TenantId = tenantId,
            Nombre = nombre,
            AñoFiscal = request.AñoFiscal,
            MesInicio = request.MesInicio,
            CreatedBy = _tenantContext.UserId ?? Guid.Empty // wiring D6 HU-004
        };

        // Spec §7 paso 7: INSERT ciclo nuevo + umbrales copiados + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repository.InsertAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el ciclo: id nulo.");

            // D-B: copiar umbrales del origen (DAL-C10 → DAL-C9b). Defensivo: si el origen no
            // tiene umbrales (0 filas), insertar los 2 defaults (0.90/0.70) para que el ciclo
            // nuevo nunca quede sin configuración de semáforo.
            if (umbralesOrigen.Count == 0)
            {
                await InsertarUmbralDefaultAsync(nuevoId, tenantId, "KPI", tx, ct);
                await InsertarUmbralDefaultAsync(nuevoId, tenantId, "PlanAccion", tx, ct);
            }
            else
            {
                foreach (var umbral in umbralesOrigen)
                {
                    await _repository.InsertarUmbralAsync(new UmbralSemaforoDto
                    {
                        CicloId = nuevoId,
                        TenantId = tenantId,
                        Tipo = umbral.Tipo,
                        UmbralVerde = umbral.UmbralVerde,
                        UmbralAmarillo = umbral.UmbralAmarillo
                    }, tx, ct);
                }
            }

            _logger?.LogInformation(
                "Ciclo {CicloId} clonado desde {OrigenId} por {UserId}. Modulo=Ciclo, Accion=CREATE, Entidad={Entidad}",
                nuevoId, idOrigen, _tenantContext.UserId, EntidadAuditoria);

            // D11: el clon se audita como CREATE con trazabilidad del origen (origenId en valor_nuevo).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SerializarCiclo(
                    nuevoId, tenantId, nombre, request.AñoFiscal, request.MesInicio,
                    "Borrador", dto.CreatedBy, idOrigen)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var nuevo = await _repository.ObtenerPorIdAsync(tenantId, nuevoId, ct)
                ?? throw new InvalidOperationException($"El ciclo '{nuevoId}' recién clonado no se pudo leer.");
            return MapToResponse(nuevo);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el UNIQUE (tenant_id, año_fiscal) del DDL L141 también protege el clon.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al clonar ciclo con año fiscal {AñoFiscal}. Modulo=Ciclo", request.AñoFiscal);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException($"Ya existe un ciclo con el año fiscal '{request.AñoFiscal}' en este tenant");
        }
        catch
        {
            // Rollback para garantizar atomicidad (INSERT + umbrales + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ClonarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>D17: TenantId null (SuperAdmin sin tenant) → 404 defensivo (los roles autorizados
    /// siempre tienen tenant_id; el SuperAdmin queda fuera por [Authorize(Roles=...)]).</summary>
    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("No se pudo determinar el tenant del usuario autenticado");
        return _tenantContext.TenantId.Value;
    }

    /// <summary>Normalización de nombre: trim + colapso de espacios internos múltiples
    /// (mismo helper que TenantService/UsuarioService, HU-001/HU-003).</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return Regex.Replace(limpio, @"\s+", " ");
    }

    /// <summary>Inserta un umbral de semáforo con los valores por defecto (0.90/0.70 — DDL L150-151).</summary>
    private async Task InsertarUmbralDefaultAsync(Guid cicloId, Guid tenantId, string tipo, IDbTransaction tx, CancellationToken ct)
    {
        await _repository.InsertarUmbralAsync(new UmbralSemaforoDto
        {
            CicloId = cicloId,
            TenantId = tenantId,
            Tipo = tipo,
            UmbralVerde = UmbralVerdeDefault,
            UmbralAmarillo = UmbralAmarilloDefault
        }, tx, ct);
    }

    /// <summary>Serializa el snapshot del ciclo para la auditoría (ADR-003: JSON legible con
    /// UnsafeRelaxedJsonEscaping). origenId solo se incluye al clonar (D11: trazabilidad del origen).</summary>
    private static string SerializarCiclo(
        Guid id, Guid tenantId, string nombre, int añoFiscal, int mesInicio,
        string estado, Guid createdBy, Guid? origenId = null)
        => JsonSerializer.Serialize(new
        {
            id,
            tenantId,
            nombre,
            añoFiscal,
            mesInicio,
            estado,
            createdBy,
            origenId
        }, JsonOpcionesAuditoria);

    /// <summary>Snapshot del estado previo para valor_anterior en ActualizarAsync (ADR-003).</summary>
    private static string SerializarSnapshot(CicloEntity e)
        => JsonSerializer.Serialize(new
        {
            id = e.Id,
            tenantId = e.TenantId,
            nombre = e.Nombre,
            añoFiscal = e.AñoFiscal,
            mesInicio = e.MesInicio,
            estado = e.Estado,
            createdBy = e.CreatedBy
        }, JsonOpcionesAuditoria);

    private static CicloResponse MapToResponse(CicloEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Nombre = e.Nombre,
        AñoFiscal = e.AñoFiscal,
        MesInicio = e.MesInicio,
        Estado = e.Estado,
        CreatedBy = e.CreatedBy,
        ActivatedAt = e.ActivatedAt,
        ClosedAt = e.ClosedAt,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
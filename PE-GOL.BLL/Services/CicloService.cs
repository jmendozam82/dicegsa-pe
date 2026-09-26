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
using PE_GOL.Entity.Estrategia;
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
    private const string EntidadAuditoriaUmbral = "UmbralSemaforo"; // HU-008: auditoría de umbrales (spec §2 paso 8)

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
    /// (422) → Cerrado (422, RC-12) → RC-01 DAL-C7 (422) → HU-011 CA #2: filosofía (Visión y
    /// Misión) registrada (422, DAL-F1) → RN-011: todas las áreas ACTIVAS con responsable (422,
    /// DAL-A6b) → límite del plan vía IPlanService (422, D-C) → tx:
    /// UPDATE estado + auditoría ACTIVATE → commit → re-lectura → 200.
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

        // HU-011 CA #2 (Flag #1 — validación dura): la filosofía (Visión y Misión) debe estar
        // registrada antes de activar el ciclo (DAL-F1 reutilizada). El texto visible tras
        // quitar etiquetas no puede quedar vacío (D-C, StripHtml). SEC-07 NO APLICA (D-E):
        // filosofia es corporativa (sin area_id) → sin filtro por área para ningún rol.
        var filosofia = await _repository.ObtenerFilosofiaAsync(tenantId, id, ct);
        if (filosofia is null ||
            string.IsNullOrWhiteSpace(HtmlSanitizerHelper.StripHtml(filosofia.Vision)) ||
            string.IsNullOrWhiteSpace(HtmlSanitizerHelper.StripHtml(filosofia.Mision)))
            throw new ValidacionException("Debe registrar la Visión y Misión del ciclo antes de activarlo");

        // RN-011 (2026-09-21): todas las áreas ACTIVAS del ciclo deben tener un Jefe de Área
        // responsable asignado antes de activar (DAL-A6b). El área pudo crearse en Borrador sin
        // responsable (ResponsableId opcional en AreaCreate) → este chequeo cierra el requisito
        // "área activa con responsable" en el punto de activación (mismo patrón que HU-011 CA #2).
        var areasSinResponsable = await _repository.ListarAreasSinResponsableAsync(tenantId, id, ct);
        if (areasSinResponsable.Count > 0)
            throw new ValidacionException(
                $"Debe asignar un Jefe de Área como responsable en: {string.Join(", ", areasSinResponsable)} antes de activar el ciclo (RN-011)");

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
    /// umbrales, defensivo) + áreas del origen clonadas (HU-009 §7: DAL-A3 sin filtro → todas,
    /// activas e inactivas; 1 auditoría CREATE por área + sync usuario.area_id si hay responsable)
    /// + auditoría CREATE con origenId y areasClonadas (D11) → commit → re-lectura → 201.</summary>
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

            // HU-009 §7 (CA #5 HU-007): clonar las áreas del origen (DAL-A3 sin filtro → todas,
            // activas e inactivas) dentro de la MISMA transacción, tras copiar umbrales y antes
            // del commit (spec §7 pasos 1-3). Defensivo: 0 áreas origen → sin INSERT de áreas.
            var areasOrigen = (await _repository.ListarAreasAsync(tenantId, idOrigen, null, ct)
                ?? Enumerable.Empty<AreaEntity>()).ToList();
            foreach (var area in areasOrigen)
            {
                var nuevoAreaId = await _repository.InsertarAreaAsync(new AreaInsertDto
                {
                    TenantId = tenantId,
                    CicloId = nuevoId,
                    Codigo = area.Codigo,
                    Nombre = area.Nombre,
                    Comentarios = area.Comentarios,
                    ResponsableId = area.ResponsableId,
                    Orden = area.Orden,
                    Activa = area.Activa
                }, tx, ct) ?? throw new InvalidOperationException("No se pudo insertar el área clonada: id nulo.");

                // Auditoría CREATE por área (ADR-003): 1 log por área clonada (spec §7 paso 3).
                await _repository.InsertLogAsync(new LogAuditoriaInsert
                {
                    TenantId = tenantId,
                    UsuarioId = _tenantContext.UserId,
                    Accion = "CREATE",
                    Entidad = "Area",
                    EntidadId = nuevoAreaId.ToString(),
                    ValorAnterior = null,
                    ValorNuevo = SerializarArea(
                        nuevoAreaId, tenantId, nuevoId, area.Codigo, area.Nombre,
                        area.Comentarios, area.ResponsableId, area.Orden, area.Activa)
                }, tx, ct);

                // Sync SEC-07 (D-J): si el área tiene responsable → usuario.area_id = nuevoAreaId.
                if (area.ResponsableId is not null)
                {
                    await _repository.ActualizarAreaIdUsuarioAsync(area.ResponsableId.Value, nuevoAreaId, tx, ct);
                }
            }

            // D11: el clon se audita como CREATE con trazabilidad del origen (origenId en valor_nuevo)
            // y el conteo de áreas clonadas (areasClonadas — HU-009 §7 paso 3).
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
                    "Borrador", dto.CreatedBy, idOrigen, areasOrigen.Count)
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

    // ─── HU-008 · Umbrales de semáforo (spec §1-§2) ─────────────────────────
    // GET lectura multi-rol (ADM/GER/JEF — RN-007, SEC-07: sin AND area_id) · PUT solo ADM
    // (D12): estado Borrador (RN-039/RC-12/RN-004), rango 0.00–1.00 (CHECKs L156-157),
    // verde > amarillo ESTRICTO (CHECK L155), normalización AwayFromZero 2 decimales (D6),
    // UPSERT conjunto KPI+PlanAccion + auditoría UPDATE 'UmbralSemaforo' en UNA transacción
    // (D1/D4, ADR-003), 23514 → 422 (D7), defaults de HU-007 nunca duplicados (CA #4).

    /// <summary>Spec HU-008 §1 · ObtenerUmbralesAsync: tenantId (404) → DAL-C2 (404 si null) →
    /// DAL-C10 → defaults 0.90/0.70 en memoria si vacío (D5, sin escritura) → UmbralesCicloResponse.</summary>
    public async Task<UmbralesCicloResponse> ObtenerUmbralesAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: el filtro por tenant_id garantiza que un ciclo de otro tenant también devuelve null → 404 sin fuga.
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // DAL-C10: umbrales del ciclo (ambos tipos). Defensivo D5: si no hay filas (no debería
        // ocurrir: HU-007 inserta 2 defaults al crear/clonar), se mapean los defaults 0.90/0.70
        // EN MEMORIA sin escribir en BD — el GET es de solo lectura y el ciclo nunca se presenta
        // sin configuración de semáforo.
        var umbrales = (await _repository.ObtenerUmbralesAsync(tenantId, cicloId, ct)).ToList();
        return MapToUmbralesResponse(cicloId, umbrales);
    }

    /// <summary>Spec HU-008 §2 · ActualizarUmbralesAsync: rol ADM (403, D12) → tenantId (404) →
    /// DAL-C2 (404) → estado Borrador (422, RN-039/RC-12/RN-004) → normalizar 2 decimales
    /// AwayFromZero (D6) → validar rango + verde &gt; amarillo ESTRICTO (422) → snapshot DAL-C10
    /// → tx: 2 UPSERTs (DAL-U2, D4) + auditoría UPDATE 'UmbralSemaforo' (ADR-003) → commit →
    /// 23514 → 422 (D7) → re-lectura post-commit → 200.</summary>
    public async Task<UmbralesCicloResponse> ActualizarUmbralesAsync(Guid cicloId, UmbralesUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (el [Authorize(Roles)] es la primera capa; la BLL es la fuente de verdad).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede configurar los umbrales");

        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: 404 si el ciclo no existe o pertenece a otro tenant (sin fuga de información).
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RN-039/CA #5 + RC-12/RN-004: los umbrales solo se configuran en Borrador. Cubre Activo
        // (no modificables retroactivamente) y Cerrado (solo lectura). La BLL lee el estado del
        // ciclo padre antes de persistir (mismo patrón que ActualizarAsync de HU-007, paso 4).
        if (!string.Equals(ciclo.Estado, "Borrador", StringComparison.Ordinal))
            throw new ValidacionException("Los umbrales solo se pueden configurar mientras el ciclo está en estado Borrador");

        // D6: normalizar a 2 decimales ANTES de validar (espejo de DECIMAL(3,2) del DDL; evita
        // sorpresas de redondeo bancario). Ej: 0.915 → 0.92. Lo que se valida es lo que se persiste.
        var kpiVerde = NormalizarUmbral(request.Kpi.UmbralVerde);
        var kpiAmarillo = NormalizarUmbral(request.Kpi.UmbralAmarillo);
        var planVerde = NormalizarUmbral(request.PlanAccion.UmbralVerde);
        var planAmarillo = NormalizarUmbral(request.PlanAccion.UmbralAmarillo);

        // Validaciones de negocio por categoría (capa 1 con mensajes amigables; espejo de los
        // CHECKs L155-157 del DDL). Rango primero, estricto después (lo que se valida es lo que se persiste).
        ValidarCategoriaUmbral(kpiVerde, kpiAmarillo);
        ValidarCategoriaUmbral(planVerde, planAmarillo);

        // Snapshot de auditoría (DAL-C10): valor_anterior = JSON(umbrales previos por tipo) (ADR-003).
        var previos = (await _repository.ObtenerUmbralesAsync(tenantId, cicloId, ct)).ToList();
        var valorAnterior = SerializarUmbrales(previos);

        // DTOs de los 2 upserts (KPI + PlanAccion) con los valores ya normalizados (CA #1).
        var nuevos = new[]
        {
            new UmbralSemaforoDto
            {
                CicloId = cicloId,
                TenantId = tenantId,
                Tipo = "KPI",
                UmbralVerde = kpiVerde,
                UmbralAmarillo = kpiAmarillo
            },
            new UmbralSemaforoDto
            {
                CicloId = cicloId,
                TenantId = tenantId,
                Tipo = "PlanAccion",
                UmbralVerde = planVerde,
                UmbralAmarillo = planAmarillo
            }
        };
        var valorNuevo = SerializarUmbrales(nuevos);

        // Spec §2 paso 8: 2 UPSERTs + auditoría en UNA sola transacción (D1: atomicidad; el PUT
        // reemplaza el estado completo del recurso /umbrales — semántica REST).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpsertUmbralAsync(nuevos[0], tx, ct);
            await _repository.UpsertUmbralAsync(nuevos[1], tx, ct);

            _logger?.LogInformation(
                "Umbrales del ciclo {CicloId} actualizados por {UserId}. Modulo=Ciclo, Accion=UPDATE, Entidad=UmbralSemaforo",
                cicloId, _tenantContext.UserId);

            // Auditoría (DAL-C11 reutilizada): accion=UPDATE (existe en el enum accion_auditoria,
            // L46), entidad='UmbralSemaforo', entidad_id=cicloId, snapshot previo/nuevo (ADR-003).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoriaUmbral,
                EntidadId = cicloId.ToString(),
                ValorAnterior = valorAnterior,
                ValorNuevo = valorNuevo
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (DAL-C10).
            var actualizados = (await _repository.ObtenerUmbralesAsync(tenantId, cicloId, ct)).ToList();
            return MapToUmbralesResponse(cicloId, actualizados);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            // D7 · Capa 2 BD: los CHECKs de rango (L156-157) y estricto (L155) son la última línea
            // de defensa ante carreras → rollback explícito + 422 con mensaje amigable (patrón ADR-001).
            _logger?.LogWarning(ex, "Violación de CHECK 23514 al actualizar umbrales del ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23514. Modulo=Ciclo"); }
            throw new ValidacionException("Los umbrales deben estar entre 0.00 y 1.00 y el umbral verde debe ser estrictamente mayor que el amarillo");
        }
        catch
        {
            // Rollback para garantizar atomicidad (2 upserts + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarUmbralesAsync. Modulo=Ciclo"); }
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
    /// UnsafeRelaxedJsonEscaping). origenId solo se incluye al clonar (D11: trazabilidad del
    /// origen); areasClonadas (HU-009 §7) solo se incluye al clonar (conteo de áreas copiadas).</summary>
    private static string SerializarCiclo(
        Guid id, Guid tenantId, string nombre, int añoFiscal, int mesInicio,
        string estado, Guid createdBy, Guid? origenId = null, int? areasClonadas = null)
        => JsonSerializer.Serialize(new
        {
            id,
            tenantId,
            nombre,
            añoFiscal,
            mesInicio,
            estado,
            createdBy,
            origenId,
            areasClonadas
        }, JsonOpcionesAuditoria);

    /// <summary>Serializa el snapshot de un área para la auditoría de clonación (ADR-003: JSON
    /// legible con UnsafeRelaxedJsonEscaping). Usado en valor_nuevo del log CREATE por área
    /// (HU-009 §7 paso 3).</summary>
    private static string SerializarArea(
        Guid id, Guid tenantId, Guid cicloId, string codigo, string nombre,
        string? comentarios, Guid? responsableId, int orden, bool activa)
        => JsonSerializer.Serialize(new
        {
            id,
            tenantId,
            cicloId,
            codigo,
            nombre,
            comentarios,
            responsableId,
            orden,
            activa
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

    // ─── Helpers HU-008 · Umbrales de semáforo ──────────────────────────────

    /// <summary>D6: normalización a 2 decimales con AwayFromZero (espejo de DECIMAL(3,2) del DDL;
    /// evita la sorpresa del redondeo bancario ToEven). Ej: 0.915 → 0.92.</summary>
    private static decimal NormalizarUmbral(decimal valor)
        => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    /// <summary>Validaciones de negocio por categoría (capa 1, mensajes amigables — espejo de los
    /// CHECKs L156-157 y L155 del DDL). Rango PRIMERO, estricto después: lo que se valida es lo
    /// que se persiste (los valores ya normalizados). Verde == amarillo se rechaza (CHECK ESTRICTO).</summary>
    private static void ValidarCategoriaUmbral(decimal umbralVerde, decimal umbralAmarillo)
    {
        if (umbralVerde < 0.00m || umbralVerde > 1.00m || umbralAmarillo < 0.00m || umbralAmarillo > 1.00m)
            throw new ValidacionException("Los umbrales deben estar entre 0.00 y 1.00");

        if (umbralVerde <= umbralAmarillo)
            throw new ValidacionException("El umbral verde debe ser estrictamente mayor que el umbral amarillo");
    }

    /// <summary>Snapshot de auditoría (ADR-003): JSON de los umbrales por tipo con
    /// UnsafeRelaxedJsonEscaping (legible sin escapes Unicode). Overload para entidades (previos).</summary>
    private static string SerializarUmbrales(IEnumerable<UmbralSemaforoEntity> umbrales)
        => JsonSerializer.Serialize(
            umbrales.ToDictionary(u => u.Tipo, u => new { u.UmbralVerde, u.UmbralAmarillo }),
            JsonOpcionesAuditoria);

    /// <summary>Snapshot de auditoría (ADR-003): JSON de los umbrales por tipo con
    /// UnsafeRelaxedJsonEscaping. Overload para DTOs (nuevos valores a persistir).</summary>
    private static string SerializarUmbrales(IEnumerable<UmbralSemaforoDto> umbrales)
        => JsonSerializer.Serialize(
            umbrales.ToDictionary(u => u.Tipo, u => new { u.UmbralVerde, u.UmbralAmarillo }),
            JsonOpcionesAuditoria);

    /// <summary>Mapea las filas de umbral_semaforo a UmbralesCicloResponse por tipo (KPI →
    /// kpi, PlanAccion → planAccion). Defensivo D5: si una categoría no tiene fila (o la lista
    /// está vacía), se mapean los defaults 0.90/0.70 EN MEMORIA, sin escribir en BD.</summary>
    private static UmbralesCicloResponse MapToUmbralesResponse(Guid cicloId, IReadOnlyCollection<UmbralSemaforoEntity> umbrales)
    {
        var porTipo = umbrales.ToDictionary(u => u.Tipo, u => u);
        return new UmbralesCicloResponse
        {
            CicloId = cicloId,
            Kpi = MapToCategoriaUmbral(porTipo.GetValueOrDefault("KPI")),
            PlanAccion = MapToCategoriaUmbral(porTipo.GetValueOrDefault("PlanAccion"))
        };
    }

    /// <summary>Mapea una fila a UmbralCategoriaResponse; si es null (defensivo D5) devuelve los
    /// defaults 0.90/0.70 en memoria. El umbral rojo NO se expone (D3): es implícito
    /// (valor &lt; umbralAmarillo).</summary>
    private static UmbralCategoriaResponse MapToCategoriaUmbral(UmbralSemaforoEntity? e)
        => e is null
            ? new UmbralCategoriaResponse
            {
                UmbralVerde = UmbralVerdeDefault,
                UmbralAmarillo = UmbralAmarilloDefault,
                UpdatedAt = DateTimeOffset.UtcNow
            }
            : new UmbralCategoriaResponse
            {
                UmbralVerde = e.UmbralVerde,
                UmbralAmarillo = e.UmbralAmarillo,
                UpdatedAt = e.UpdatedAt
            };
}
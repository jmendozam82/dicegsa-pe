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
using PE_GOL.Entity.Estrategia;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Áreas Estratégicas — Spec HU-009 § Lógica BLL (pasos 1-7).
/// Implementación real (fase IMPLEMENT); los 29 tests de AreaServiceTests + 4 de clonación en
/// CicloServiceTests son la especificación ejecutable.
/// Reglas:
///  · D12: re-validación defensiva de rol en cada escritura (D-A: solo el ADM crea/edita/desactiva).
///  · Normalización de nombre (trim + colapso de espacios, mismo helper que CicloService).
///  · RN-011: área Activa con responsable JefeArea Activo (DAL-A9 + validación BLL, CA #2).
///  · RN-012: un responsable en UNA sola área activa por ciclo (DAL-A8; excludeAreaId en UPDATE).
///  · RN-010: 1..20 áreas activas por ciclo según plan — doble chequeo: (1) DAL-A6 >= MaxAreas
///    vía IPlanService.ObtenerLimitesAsync (chequeo PRECISO por ciclo, D-D); (2) guarda a nivel
///    tenant vía IPlanService.ValidarLimitesParaTenantAsync (CA #2 HU-002).
///  · CA #1/DB-04: código GOL auto-generado en BLL: "GOL" + (MAX(orden)+1) vía DAL-A7 (incl.
///    inactivas → sin reutilizar códigos tras desactivar).
///  · RC-12/RN-004: ciclo Cerrado = solo lectura (422).
///  · Escrituras (INSERT/UPDATE/desactivar + sync usuario.area_id + auditoría) en UNA sola
///    transacción IDbTransaction (patrón HU-001/HU-003); captura SQLSTATE 23505 → rollback
///    explícito + ValidacionException (ADR-001/ADR-007).
///  · Auditoría (ADR-003): Entidad="Area", JSON legible con UnsafeRelaxedJsonEscaping; desactivar
///    se audita como DEACTIVATE (D-E, enum accion_auditoria).
///  · SEC-07: area SÍ es entidad de área → ListarAsync filtra por TenantContext.AreaId si rol
///    JefeArea; ObtenerPorIdAsync lanza 403 si JefeArea pide otra área (D12).
///  · D-J: al cambiar responsable se libera al anterior (DAL-A11 con null) y se sincroniza al
///    nuevo (DAL-A11 con areaId); sin cambio → no se toca usuario.area_id. Desactivar NO limpia
///    usuario.area_id (el JefeArea conserva lectura).
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, TenantContext) + overload con
/// ILogger (D13) — patrón UsuarioService/EmpresaService/CicloService (RNF-023).
/// </summary>
public class AreaService : IAreaService
{
    private const string RolAdminTenant = "AdminTenant";
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "Area";

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
    private readonly ILogger<AreaService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public AreaService(ICicloRepository repository, IPlanService planService, TenantContext tenantContext)
        : this(repository, planService, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public AreaService(ICicloRepository repository, IPlanService planService, TenantContext tenantContext, ILogger<AreaService>? logger)
    {
        _repository = repository;
        _planService = planService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ListarAsync: tenantId (404) → ciclo (404) → areaIdFiltro = TenantContext.AreaId
    /// si rol JefeArea (SEC-07), null en otro caso → DAL-A3 → List&lt;AreaResponse&gt; (ORDER BY orden ASC).</summary>
    public async Task<List<AreaResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: el filtro por tenant_id garantiza que un ciclo de otro tenant también devuelve null → 404 sin fuga.
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // SEC-07: el JefeArea solo ve SU área (AND id = @AreaId en el DAL); ADM/GER ven todas.
        var areaIdFiltro = string.Equals(_tenantContext.Rol, RolJefeArea, StringComparison.Ordinal)
            ? _tenantContext.AreaId
            : null;

        var areas = await _repository.ListarAreasAsync(tenantId, cicloId, areaIdFiltro, ct);
        return areas.Select(MapToResponse).ToList();
    }

    /// <summary>Spec §2 · ObtenerPorIdAsync: tenantId (404) → ciclo (404) → área (404) → 403 si
    /// rol JefeArea y areaId != TenantContext.AreaId (D12, SEC-07) → AreaResponse.</summary>
    public async Task<AreaResponse> ObtenerPorIdAsync(Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var area = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, ct)
            ?? throw new NotFoundException($"El área '{areaId}' no existe");

        // D12 + SEC-07: el JefeArea solo accede a SU área.
        if (string.Equals(_tenantContext.Rol, RolJefeArea, StringComparison.Ordinal)
            && area.Id != _tenantContext.AreaId)
            throw new AccesoDenegadoException("El Jefe de Área solo puede consultar su propia área");

        return MapToResponse(area);
    }

    /// <summary>Spec §3 · CrearAsync: rol ADM (403) → tenantId (404) → ciclo (404) → ciclo Cerrado
    /// (422, RC-12) → normalizar nombre → responsable (RN-011: existe/JefeArea/Activo, 422) →
    /// RN-012 DAL-A8 (422) → RN-010 por ciclo DAL-A6 >= MaxAreas (422) → guarda tenant
    /// ValidarLimitesParaTenantAsync (422) → código GOL (DAL-A7) → tx: INSERT + sync usuario.area_id
    /// (DAL-A11) + auditoría CREATE → commit → re-lectura → 201. Captura 23505 → 422 (ADR-001/007).</summary>
    public async Task<AreaResponse> CrearAsync(Guid cicloId, AreaCreateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM crea).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede crear áreas");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-H).
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var nombre = NormalizarNombre(request.Nombre);

        // RN-011 (CA #2): si se asigna responsable en la creación, debe existir en el tenant,
        // tener rol JefeArea y estado Activo. OPCIONAL desde 2026-09-21: el área puede crearse en
        // Borrador sin responsable (el requisito "área activa con responsable" se valida al activar
        // el ciclo en CicloService.ActivarAsync). Null → no se valida ni se sincroniza usuario.area_id.
        if (request.ResponsableId is { } responsableId)
        {
            var responsable = await _repository.ObtenerResponsableAsync(tenantId, responsableId, ct)
                ?? throw new ValidacionException("El responsable no existe en el tenant");
            if (!string.Equals(responsable.Rol, RolJefeArea, StringComparison.Ordinal))
                throw new ValidacionException("El responsable debe tener rol JefeArea");
            if (!string.Equals(responsable.Estado, "Activo", StringComparison.Ordinal))
                throw new ValidacionException("El responsable debe estar en estado Activo");

            // RN-012: un responsable en UNA sola área ACTIVA por ciclo (DAL-A8 sin excludeAreaId).
            if (await _repository.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, null, ct) > 0)
                throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");
        }

        // RN-010 (D-D): chequeo PRECISO por ciclo — DAL-A6 >= MaxAreas detecta el caso "ciclo al
        // tope" que el MAX-per-ciclo de DAL-P8 (comparación estricta >) NO detecta (Flag #2).
        var planId = await _repository.ObtenerPlanIdDelTenantAsync(tenantId, ct)
            ?? throw new NotFoundException("El tenant no tiene un plan asignado");
        var limites = await _planService.ObtenerLimitesAsync(planId, ct);
        if (await _repository.ContarAreasActivasEnCicloAsync(tenantId, cicloId, ct) >= limites.MaxAreas)
            throw new ValidacionException($"El ciclo ya alcanzó el límite de áreas del plan ({limites.MaxAreas})");

        // CA #2 HU-002: guarda a nivel tenant (los errores del plan se propagan como 422).
        var resultado = await _planService.ValidarLimitesParaTenantAsync(tenantId, planId, ct);
        if (!resultado.EsValido)
            throw new ValidacionException(string.Join(" ", resultado.Errores));

        // CA #1/DB-04: código GOL auto-generado en BLL — "GOL" + (MAX(orden)+1) incl. inactivas.
        var siguiente = await _repository.ObtenerSiguienteOrdenAsync(tenantId, cicloId, ct);
        var codigo = $"GOL{siguiente}";

        var dto = new AreaInsertDto
        {
            TenantId = tenantId,
            CicloId = cicloId,
            Codigo = codigo,
            Nombre = nombre,
            Comentarios = request.Comentarios,
            ResponsableId = request.ResponsableId,
            Orden = siguiente,
            Activa = true
        };

        // Spec §3 paso 6: INSERT + sync usuario.area_id + auditoría CREATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repository.InsertarAreaAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el área: id nulo.");

            // SEC-07 (D-J): si se asignó responsable, queda vinculado al área (usuario.area_id = nuevoId).
            // Si el área se creó SIN responsable, no se toca usuario.area_id de nadie.
            if (request.ResponsableId is { } responsableAsignado)
                await _repository.ActualizarAreaIdUsuarioAsync(responsableAsignado, nuevoId, tx, ct);

            _logger?.LogInformation(
                "Área {AreaId} creada en ciclo {CicloId} por {UserId}. Modulo=Ciclo, Accion=CREATE, Entidad={Entidad}",
                nuevoId, cicloId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=CREATE, valor_anterior=null, JSON legible (ADR-003).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SerializarArea(
                    nuevoId, tenantId, cicloId, codigo, nombre, request.Comentarios,
                    request.ResponsableId, siguiente, true)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var creada = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, nuevoId, ct)
                ?? throw new InvalidOperationException($"El área '{nuevoId}' recién creada no se pudo leer.");
            return MapToResponse(creada);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001/ADR-007 · Capa 2: el UNIQUE (ciclo_id, codigo) del DDL elimina la carrera
            // TOCTOU del código GOL concurrente. Rollback explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear área en ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException($"Ya existe un área con el código '{codigo}' en este ciclo");
        }
        catch
        {
            // Rollback para garantizar atomicidad (INSERT + sync + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ActualizarAsync: rol ADM (403) → tenantId (404) → ciclo (404) → ciclo
    /// Cerrado (422, RC-12) → área (404) → normalizar nombre → responsable (RN-011, 422) →
    /// RN-012 excluyendo self (422) → tx: UPDATE + sync usuario.area_id si cambió el responsable
    /// (D-J: libera anterior con null + sincroniza nuevo) + auditoría UPDATE con snapshot → commit
    /// → re-lectura → 200. Captura 23505 → 422 (ADR-001/007).</summary>
    public async Task<AreaResponse> ActualizarAsync(Guid cicloId, Guid areaId, AreaUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM edita).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede editar áreas");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: ninguna entidad hija se modifica si el ciclo está Cerrado.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var original = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, ct)
            ?? throw new NotFoundException($"El área '{areaId}' no existe");

        var nombre = NormalizarNombre(request.Nombre);

        // RN-011 (CA #2): si el request trae responsable, debe existir en el tenant, tener rol
        // JefeArea y estado Activo. OPCIONAL (2026-09-21): null LIBERA al responsable actual.
        if (request.ResponsableId is { } responsableId)
        {
            var responsable = await _repository.ObtenerResponsableAsync(tenantId, responsableId, ct)
                ?? throw new ValidacionException("El responsable no existe en el tenant");
            if (!string.Equals(responsable.Rol, RolJefeArea, StringComparison.Ordinal))
                throw new ValidacionException("El responsable debe tener rol JefeArea");
            if (!string.Equals(responsable.Estado, "Activo", StringComparison.Ordinal))
                throw new ValidacionException("El responsable debe estar en estado Activo");

            // RN-012 excluyendo self (DAL-A8 con excludeAreaId = areaId): el responsable puede seguir
            // en SU área, pero no puede estar asignado a OTRA área activa del ciclo.
            if (await _repository.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, areaId, ct) > 0)
                throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");
        }

        var dto = new AreaUpdateDto
        {
            Id = areaId,
            TenantId = tenantId,
            CicloId = cicloId,
            Nombre = nombre,
            Comentarios = request.Comentarios,
            ResponsableId = request.ResponsableId
        };

        // Spec §4 paso 8: UPDATE + sync usuario.area_id (si cambió el responsable) + auditoría
        // UPDATE con snapshot previo en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.ActualizarAreaAsync(dto, tx, ct);

            // D-J (SEC-07): si el responsable cambió, libera al anterior (area_id = null) y
            // sincroniza al nuevo (area_id = areaId). Sin cambio → usuario.area_id intacto.
            if (original.ResponsableId != request.ResponsableId)
            {
                if (original.ResponsableId is not null)
                    await _repository.ActualizarAreaIdUsuarioAsync(original.ResponsableId.Value, null, tx, ct);
                if (request.ResponsableId is { } nuevoResponsable)
                    await _repository.ActualizarAreaIdUsuarioAsync(nuevoResponsable, areaId, tx, ct);
            }

            _logger?.LogInformation(
                "Área {AreaId} actualizada. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                areaId, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = areaId.ToString(),
                ValorAnterior = SerializarSnapshot(original),
                ValorNuevo = SerializarArea(
                    areaId, tenantId, cicloId, original.Codigo, nombre, request.Comentarios,
                    request.ResponsableId, original.Orden, original.Activa)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizada = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, ct)
                ?? throw new NotFoundException($"El área '{areaId}' no existe");
            return MapToResponse(actualizada);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001/ADR-007 · Capa 2: el índice parcial uq_area_responsable_unico (ADR-007/V004)
            // también protege el UPDATE (RN-012 a nivel BD). Rollback explícito antes de propagar.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar área {AreaId}. Modulo=Ciclo", areaId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }
            throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + sync + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §5 · DesactivarAsync: rol ADM (403) → tenantId (404) → ciclo (404) → ciclo
    /// Cerrado (422, RC-12) → área (404) → ya inactiva (422, D-E) → tx: UPDATE activa=FALSE
    /// (DAL-A5, sin DELETE — CA #5) + auditoría DEACTIVATE → commit → re-lectura → 200.
    /// NO limpia usuario.area_id (D-J: el JefeArea conserva lectura).</summary>
    public async Task<AreaResponse> DesactivarAsync(Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM desactiva).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede desactivar áreas");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var area = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, ct)
            ?? throw new NotFoundException($"El área '{areaId}' no existe");

        // D-E: no se re-desactiva un área ya inactiva (evita log duplicado y auditoría imprecisa).
        if (!area.Activa)
            throw new ValidacionException("El área ya está desactivada");

        // Spec §5 paso 4: UPDATE activa=FALSE + auditoría DEACTIVATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.DesactivarAreaAsync(tenantId, cicloId, areaId, tx, ct);

            _logger?.LogInformation(
                "Área {AreaId} desactivada. Modulo=Ciclo, Accion=DEACTIVATE, Entidad={Entidad}",
                areaId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=DEACTIVATE (enum accion_auditoria), snapshot previo.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DEACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = areaId.ToString(),
                ValorAnterior = SerializarSnapshot(area),
                ValorNuevo = JsonSerializer.Serialize(new { activa = false }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var desactivada = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, ct)
                ?? throw new NotFoundException($"El área '{areaId}' no existe");
            return MapToResponse(desactivada);
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en DesactivarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §6 · ListarResponsablesCandidatosAsync: tenantId (404) → ciclo (404) →
    /// DAL-A10 (JefeArea Activos del tenant con yaAsignado por RN-012) → mapeo directo.</summary>
    public async Task<List<ResponsableCandidatoResponse>> ListarResponsablesCandidatosAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var candidatos = await _repository.ListarResponsablesCandidatosAsync(tenantId, cicloId, ct);
        return candidatos.ToList();
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
    /// (mismo helper que TenantService/UsuarioService/CicloService, HU-001/HU-003/HU-007).</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return Regex.Replace(limpio, @"\s+", " ");
    }

    /// <summary>Snapshot de auditoría (ADR-003): JSON del área con UnsafeRelaxedJsonEscaping
    /// (legible sin escapes Unicode). Usado en valor_nuevo de CREATE/UPDATE.</summary>
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

    /// <summary>Snapshot del estado previo para valor_anterior en UPDATE/DEACTIVATE (ADR-003).</summary>
    private static string SerializarSnapshot(AreaEntity e)
        => JsonSerializer.Serialize(new
        {
            id = e.Id,
            tenantId = e.TenantId,
            cicloId = e.CicloId,
            codigo = e.Codigo,
            nombre = e.Nombre,
            comentarios = e.Comentarios,
            responsableId = e.ResponsableId,
            orden = e.Orden,
            activa = e.Activa
        }, JsonOpcionesAuditoria);

    private static AreaResponse MapToResponse(AreaEntity e) => new()
    {
        Id = e.Id,
        CicloId = e.CicloId,
        TenantId = e.TenantId,
        Codigo = e.Codigo,
        Nombre = e.Nombre,
        Comentarios = e.Comentarios,
        ResponsableId = e.ResponsableId,
        ResponsableNombre = e.ResponsableNombre,
        ResponsableCorreo = e.ResponsableCorreo,
        Orden = e.Orden,
        Activa = e.Activa,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
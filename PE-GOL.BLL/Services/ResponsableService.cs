using System.Data;
using System.Security.Cryptography;
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
using PE_GOL.Utility.Email;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Responsables — Spec HU-010 § Lógica BLL (pasos 1-5).
/// Implementación real (fase IMPLEMENT); los 35 tests de ResponsableServiceTests son la
/// especificación ejecutable.
/// Reglas:
///  · D12: re-validación defensiva de rol en cada escritura (D-A: solo el ADM crea/reasigna/desactiva).
///  · Normalización de nombre (trim + colapso de espacios) y correo (trim + LOWER).
///  · RC-12/RN-004: ciclo Cerrado = solo lectura (422).
///  · RN-011: área Activa con responsable JefeArea Activo — área destino debe existir, estar
///    activa y sin responsable (422).
///  · RN-012: un responsable en UNA sola área activa por ciclo (DAL-A8; capa 1 BLL + capa 2 BD
///    uq_area_responsable_unico ADR-007 → 23505 → 422).
///  · RN-010: límite de usuarios del plan — doble chequeo: (1) DAL-R5 >= MaxUsuarios vía
///    IPlanService.ObtenerLimitesAsync (chequeo PRECISO por tenant, D-D); (2) guarda a nivel
///    tenant vía IPlanService.ValidarLimitesParaTenantAsync (CA #2 HU-002).
///  · Escrituras (INSERT usuario + sync usuario.area_id + area.responsable_id + auditoría) en
///    UNA sola transacción IDbTransaction (patrón HU-001..HU-009); captura SQLSTATE 23505 →
///    rollback explícito + ValidacionException (ADR-001/ADR-007).
///  · Auditoría (ADR-003): Entidad="Responsable", JSON legible con UnsafeRelaxedJsonEscaping;
///    desactivar se audita como DEACTIVATE (D-K, enum accion_auditoria).
///  · SEC-07: responsable SÍ es entidad de área → ListarAsync filtra por TenantContext.AreaId si
///    rol JefeArea (DAL-R3 con areaIdFiltro); ObtenerPorIdAsync lanza 403 si JefeArea pide otro
///    responsable (D12). Sync usuario.area_id al crear/reasignar (D-J); NO se limpia al desactivar.
///  · CONTRATO #23 (Jorge): Reasignar_MismaArea → no-op SIN auditoría (InsertLogAsync NO se invoca).
///  · CONTRATO #25 (Jorge): ResponsableResponse.Advertencia (string?) — "El área origen quedará
///    sin responsable asignado" si el origen queda sin responsable (spec §4 paso 8).
///  · CONTRATO #10 (Jorge): Crear incluye guarda RN-012 capa 1 vía DAL-A8 (tabla de tests del spec).
///  · D-C: correo de activación fire-and-forget — error de envío NO revierte la creación (logging).
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, IAuthService, IEmailService,
/// TenantContext) + overload con ILogger (D-L, RNF-023).
/// </summary>
public class ResponsableService : IResponsableService
{
    private const string RolAdminTenant = "AdminTenant";
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "Responsable";
    private const int WorkFactorBcrypt = 12; // SEC-02: coste ≥ 12
    private const int LongitudContrasenaTemporal = 12;

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
    private readonly IAuthService _authService; // D-L: reutilización de lógica de hash/validación si se requiere
    private readonly IEmailService _emailService; // D-C: correo de activación fire-and-forget
    private readonly TenantContext _tenantContext; // D17: TenantId nullable; D12: re-validación de rol
    private readonly ILogger<ResponsableService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public ResponsableService(
        ICicloRepository repository, IPlanService planService, IAuthService authService,
        IEmailService emailService, TenantContext tenantContext)
        : this(repository, planService, authService, emailService, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public ResponsableService(
        ICicloRepository repository, IPlanService planService, IAuthService authService,
        IEmailService emailService, TenantContext tenantContext, ILogger<ResponsableService>? logger)
    {
        _repository = repository;
        _planService = planService;
        _authService = authService;
        _emailService = emailService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ListarAsync: tenantId (404) → ciclo (404) → areaIdFiltro =
    /// TenantContext.AreaId si rol JefeArea (SEC-07), null en otro caso → DAL-R3 → List&lt;ResponsableResponse&gt;.</summary>
    public async Task<List<ResponsableResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        // DAL-C2: el filtro por tenant_id garantiza que un ciclo de otro tenant también devuelve null → 404 sin fuga.
        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // SEC-07: el JefeArea solo ve SU responsable (AND a.id = @AreaIdFiltro en el DAL); ADM/GER ven todos.
        var areaIdFiltro = string.Equals(_tenantContext.Rol, RolJefeArea, StringComparison.Ordinal)
            ? _tenantContext.AreaId
            : null;

        var responsables = await _repository.ListarResponsablesAsync(tenantId, cicloId, areaIdFiltro, ct);
        return responsables.Select(r => MapToResponse(r, cicloId)).ToList();
    }

    /// <summary>Spec §2 · ObtenerPorIdAsync: tenantId (404) → ciclo (404) → responsable (404) →
    /// 403 si rol JefeArea y responsable.AreaId != TenantContext.AreaId (D12, SEC-07) → ResponsableResponse.</summary>
    public async Task<ResponsableResponse> ObtenerPorIdAsync(Guid cicloId, Guid responsableId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var responsable = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, ct)
            ?? throw new NotFoundException($"El responsable '{responsableId}' no existe");

        // D12 + SEC-07: el JefeArea solo accede a SU responsable.
        if (string.Equals(_tenantContext.Rol, RolJefeArea, StringComparison.Ordinal)
            && responsable.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("El Jefe de Área solo puede consultar su propio responsable");

        return MapToResponse(responsable, cicloId);
    }

    /// <summary>Spec §3 · CrearAsync: rol ADM (403) → tenantId (404) → ciclo (404) → ciclo Cerrado
    /// (422, RC-12) → normalizar nombre/correo → área destino (RN-011: existe/activa/sin responsable,
    /// 422) → unicidad de correo en tenant (DAL-R4, 422) → RN-012 capa 1 (DAL-A8, 422, CONTRATO #10)
    /// → RN-010 por tenant (DAL-R5 >= MaxUsuarios, 422) → guarda tenant ValidarLimitesParaTenantAsync
    /// (422) → pwd temporal BCrypt 12 → tx: INSERT usuario (DAL-R1) + sync usuario.area_id (DAL-A11)
    /// + area.responsable_id (DAL-R6) + auditoría CREATE → commit → correo de activación fire-and-forget
    /// (D-C) → re-lectura → 201. Captura 23505 → 422 (ADR-001/007).</summary>
    public async Task<ResponsableResponse> CrearAsync(Guid cicloId, ResponsableCreateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM crea).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede crear responsables");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-H).
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        var nombre = NormalizarNombre(request.Nombre);
        var correo = request.Correo.Trim().ToLowerInvariant();

        // Área destino (CA #1, RN-011): debe existir en el ciclo, estar activa y sin responsable.
        var area = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, ct)
            ?? throw new ValidacionException("El área no existe en este ciclo");
        if (!area.Activa)
            throw new ValidacionException("El área debe estar activa para asignar un responsable");
        if (area.ResponsableId is not null)
            throw new ValidacionException("El área ya tiene un responsable asignado");

        // Unicidad de correo en tenant (DAL-R4).
        if (await _repository.ExisteCorreoEnTenantAsync(tenantId, correo, ct))
            throw new ValidacionException($"Ya existe un usuario con el correo '{correo}' en este tenant");

        // RN-012 capa 1 (CONTRATO #10): guarda defensiva exigida por la tabla de tests del spec.
        // El nuevo usuario aún no existe → Guid.Empty (ninguna área puede tenerlo asignado).
        if (await _repository.ContarAreasConResponsableAsync(tenantId, cicloId, Guid.Empty, null, ct) > 0)
            throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");

        // RN-010 (D-D): chequeo PRECISO por tenant — DAL-R5 >= MaxUsuarios detecta el caso "tenant
        // al tope" que el MAX-per-ciclo de DAL-P8 (comparación estricta >) NO detecta.
        var planId = await _repository.ObtenerPlanIdDelTenantAsync(tenantId, ct)
            ?? throw new NotFoundException("El tenant no tiene un plan asignado");
        var limites = await _planService.ObtenerLimitesAsync(planId, ct);
        if (await _repository.ContarUsuariosActivosEnTenantAsync(tenantId, ct) >= limites.MaxUsuarios)
            throw new ValidacionException($"El tenant ya alcanzó el límite de usuarios del plan ({limites.MaxUsuarios})");

        // CA #2 HU-002: guarda a nivel tenant (los errores del plan se propagan como 422).
        var resultado = await _planService.ValidarLimitesParaTenantAsync(tenantId, planId, ct);
        if (!resultado.EsValido)
            throw new ValidacionException(string.Join(" ", resultado.Errores));

        // Contraseña temporal + hash BCrypt (SEC-02, patrón HU-003).
        var pwdTemporal = GenerarContrasenaTemporal();
        var hash = BCrypt.Net.BCrypt.HashPassword(pwdTemporal, workFactor: WorkFactorBcrypt);

        var dto = new ResponsableInsertDto
        {
            TenantId = tenantId,
            Nombre = nombre,
            Correo = correo,
            PasswordHash = hash,
            Rol = RolJefeArea,
            AreaId = request.AreaId,
            Estado = "Activo",
            RequiereCambioPwd = true
        };

        // Spec §3 paso 10: INSERT usuario + sync usuario.area_id + area.responsable_id + auditoría
        // CREATE en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var nuevoUsuarioId = await _repository.InsertarUsuarioAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el usuario: id nulo.");

            // Sync SEC-07 (D-J): usuario.area_id = área asignada (el claim JWT area_id del JefeArea
            // debe coincidir con su asignación).
            await _repository.ActualizarAreaIdUsuarioAsync(nuevoUsuarioId, request.AreaId, tx, ct);

            // Actualizar área con responsable (DAL-R6).
            await _repository.AsignarResponsableAreaAsync(tenantId, cicloId, request.AreaId, nuevoUsuarioId, tx, ct);

            _logger?.LogInformation(
                "Responsable {ResponsableId} creado en ciclo {CicloId} por {UserId}. Modulo=Ciclo, Accion=CREATE, Entidad={Entidad}",
                nuevoUsuarioId, cicloId, _tenantContext.UserId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=CREATE, valor_anterior=null, JSON legible (ADR-003).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoUsuarioId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SerializarResponsable(
                    nuevoUsuarioId, tenantId, cicloId, nombre, correo, RolJefeArea, "Activo",
                    request.AreaId, area.Codigo, area.Nombre, true)
            }, tx, ct);

            tx.Commit();

            // Correo de activación (D-C): fire-and-forget — un error de envío NO revierte la creación.
            try
            {
                await _emailService.EnviarActivacionResponsableAsync(correo, nombre, pwdTemporal, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Fallo al enviar correo de activación. ResponsableId={ResponsableId}, Correo={Correo}",
                    nuevoUsuarioId, correo);
            }

            // Lectura posterior al commit para devolver el estado ya persistido.
            var creado = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, nuevoUsuarioId, ct)
                ?? throw new InvalidOperationException($"El responsable '{nuevoUsuarioId}' recién creado no se pudo leer.");
            return MapToResponse(creado, cicloId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001/ADR-007 · Capa 2: el UNIQUE global (correo) del DDL L85 y el índice parcial
            // uq_area_responsable_unico (ADR-007/V004) eliminan las carreras TOCTOU. Rollback
            // explícito antes de propagar (422).
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear responsable en ciclo {CicloId}. Modulo=Ciclo", cicloId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }

            if (string.Equals(ex.ConstraintName, "uq_usuario_correo", StringComparison.Ordinal))
                throw new ValidacionException($"Ya existe un usuario con el correo '{correo}'");
            if (string.Equals(ex.ConstraintName, "uq_area_responsable_unico", StringComparison.Ordinal))
                throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");
            throw new ValidacionException("Violación de unicidad");
        }
        catch
        {
            // Rollback para garantizar atomicidad (INSERT + sync + área + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ReasignarAsync: rol ADM (403) → tenantId (404) → ciclo (404) →
    /// responsable (404) → CONTRATO #23: misma área → no-op (sin tx/sync/auditoría) → RC-12 ciclo
    /// Cerrado (422) → área destino (RN-011: existe/activa/sin responsable, 422) → RN-012 excluyendo
    /// self (422) → advertencia si el área origen queda sin responsable (CONTRATO #25) → tx: sync
    /// usuario.area_id (DAL-A11) + liberar origen (DAL-R6 null) + asignar destino (DAL-R6) +
    /// auditoría UPDATE con snapshot → commit → re-lectura → 200. Captura 23505 → 422 (ADR-001/007).</summary>
    public async Task<ResponsableResponse> ReasignarAsync(Guid cicloId, Guid responsableId, ResponsableReassignRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM reasigna).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede reasignar responsables");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var responsable = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, ct)
            ?? throw new NotFoundException($"El responsable '{responsableId}' no existe");

        // CONTRATO #23 (Jorge): misma área → no-op. La BLL corta ANTES de validar el área destino
        // (el test no configura ObtenerAreaPorIdAsync) y sin tocar nada (ni tx, ni sync, ni auditoría).
        if (responsable.AreaId == request.AreaId)
            return MapToResponse(responsable, cicloId);

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // Área destino (RN-011): debe existir en el ciclo, estar activa y sin responsable.
        var areaDestino = await _repository.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, ct)
            ?? throw new ValidacionException("El área destino no existe en este ciclo");
        if (!areaDestino.Activa)
            throw new ValidacionException("El área destino debe estar activa");
        if (areaDestino.ResponsableId is not null)
            throw new ValidacionException("El área destino ya tiene un responsable asignado");

        // RN-012 excluyendo self (DAL-A8 con excludeAreaId = request.AreaId): el responsable puede
        // seguir en SU área, pero no puede estar asignado a OTRA área activa del ciclo.
        if (await _repository.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, request.AreaId, ct) > 0)
            throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");

        // Advertencia si el área origen queda sin responsable (spec §4 paso 8, CONTRATO #25):
        // la BLL NO bloquea (el ADM decide), pero el response incluye la advertencia.
        string? advertencia = null;
        var areaOrigenId = responsable.AreaId;
        if (areaOrigenId is not null && areaOrigenId != request.AreaId)
        {
            if (await _repository.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, areaOrigenId.Value, ct) == 0)
                advertencia = "El área origen quedará sin responsable asignado";
        }

        // Spec §4 paso 9: sync usuario.area_id + liberar origen + asignar destino + auditoría
        // UPDATE con snapshot previo en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            // Sync SEC-07 (D-J): nuevo area_id del usuario.
            await _repository.ActualizarAreaIdUsuarioAsync(responsableId, request.AreaId, tx, ct);

            // Liberar área origen (DAL-R6 con ResponsableId = null).
            if (areaOrigenId is not null && areaOrigenId != request.AreaId)
                await _repository.AsignarResponsableAreaAsync(tenantId, cicloId, areaOrigenId.Value, null, tx, ct);

            // Asignar área destino (DAL-R6).
            await _repository.AsignarResponsableAreaAsync(tenantId, cicloId, request.AreaId, responsableId, tx, ct);

            _logger?.LogInformation(
                "Responsable {ResponsableId} reasignado al área {AreaId}. Modulo=Ciclo, Accion=UPDATE, Entidad={Entidad}",
                responsableId, request.AreaId, EntidadAuditoria);

            // Auditoría UPDATE con snapshot previo (ADR-003).
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = responsableId.ToString(),
                ValorAnterior = SerializarSnapshot(responsable),
                ValorNuevo = SerializarResponsable(
                    responsableId, tenantId, cicloId, responsable.Nombre, responsable.Correo,
                    responsable.Rol, responsable.Estado, request.AreaId,
                    areaDestino.Codigo, areaDestino.Nombre, responsable.RequiereCambioPwd)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var reasignado = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, ct)
                ?? throw new InvalidOperationException($"El responsable '{responsableId}' reasignado no se pudo leer.");
            var response = MapToResponse(reasignado, cicloId);
            response.Advertencia = advertencia;
            return response;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001/ADR-007 · Capa 2: uq_area_responsable_unico (ADR-007/V004) ante carrera TOCTOU.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al reasignar responsable {ResponsableId}. Modulo=Ciclo", responsableId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Ciclo"); }

            if (string.Equals(ex.ConstraintName, "uq_area_responsable_unico", StringComparison.Ordinal))
                throw new ValidacionException("El responsable ya está asignado a otra área activa de este ciclo");
            if (string.Equals(ex.ConstraintName, "uq_usuario_correo", StringComparison.Ordinal))
                throw new ValidacionException($"Ya existe un usuario con el correo '{responsable.Correo}'");
            throw new ValidacionException("Violación de unicidad");
        }
        catch
        {
            // Rollback para garantizar atomicidad (sync + liberar + asignar + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ReasignarAsync. Modulo=Ciclo"); }
            throw;
        }
    }

    /// <summary>Spec §5 · DesactivarAsync: rol ADM (403) → tenantId (404) → ciclo (404) →
    /// responsable (404) → RC-12 ciclo Cerrado (422) → ya inactivo (422, D-F) → tx: estado
    /// 'Inactivo' (DAL-R7) + liberar area.responsable_id (DAL-R6 null) + auditoría DEACTIVATE →
    /// commit → re-lectura → 200. NO limpia usuario.area_id (D-J: el JefeArea conserva lectura).</summary>
    public async Task<ResponsableResponse> DesactivarAsync(Guid cicloId, Guid responsableId, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (D-A: solo el ADM desactiva).
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede desactivar responsables");

        var tenantId = ObtenerTenantIdOThrow();

        var ciclo = await _repository.ObtenerPorIdAsync(tenantId, cicloId, ct)
            ?? throw new NotFoundException($"El ciclo '{cicloId}' no existe");

        var responsable = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, ct)
            ?? throw new NotFoundException($"El responsable '{responsableId}' no existe");

        // RC-12/RN-004: un ciclo Cerrado es de solo lectura.
        if (string.Equals(ciclo.Estado, "Cerrado", StringComparison.Ordinal))
            throw new ValidacionException("Un ciclo Cerrado es de solo lectura");

        // D-F: no se re-desactiva un responsable ya inactivo (evita log duplicado y auditoría imprecisa).
        if (string.Equals(responsable.Estado, "Inactivo", StringComparison.Ordinal))
            throw new ValidacionException("El responsable ya está inactivo");

        // Spec §5 paso 7: estado 'Inactivo' + liberar area.responsable_id + auditoría DEACTIVATE
        // en UNA sola transacción. NO se limpia usuario.area_id (D-J, SEC-07).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.DesactivarUsuarioAsync(responsableId, tx, ct);

            // Liberar área asignada (DAL-R6 con ResponsableId = null): el área queda sin responsable
            // (RN-011: solo áreas Activas requieren responsable; puede seguir activa temporalmente).
            if (responsable.AreaId is not null)
                await _repository.AsignarResponsableAreaAsync(tenantId, cicloId, responsable.AreaId.Value, null, tx, ct);

            _logger?.LogInformation(
                "Responsable {ResponsableId} desactivado. Modulo=Ciclo, Accion=DEACTIVATE, Entidad={Entidad}",
                responsableId, EntidadAuditoria);

            // Auditoría (DAL-C11 reutilizada): accion=DEACTIVATE (enum accion_auditoria), snapshot previo.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DEACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = responsableId.ToString(),
                ValorAnterior = SerializarSnapshot(responsable),
                ValorNuevo = SerializarResponsable(
                    responsableId, tenantId, cicloId, responsable.Nombre, responsable.Correo,
                    responsable.Rol, "Inactivo", responsable.AreaId,
                    responsable.AreaCodigo, responsable.AreaNombre, responsable.RequiereCambioPwd)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var desactivado = await _repository.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, ct)
                ?? throw new InvalidOperationException($"El responsable '{responsableId}' desactivado no se pudo leer.");
            return MapToResponse(desactivado, cicloId);
        }
        catch
        {
            // Rollback para garantizar atomicidad (estado + liberar área + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en DesactivarAsync. Modulo=Ciclo"); }
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
    /// (mismo helper que TenantService/UsuarioService/CicloService/AreaService).</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return Regex.Replace(limpio, @"\s+", " ");
    }

    /// <summary>Genera una contraseña temporal segura de 12 chars con al menos una mayúscula,
    /// una minúscula, un dígito y un símbolo (patrón HU-003, spec §3 paso 9).</summary>
    private static string GenerarContrasenaTemporal()
    {
        const string mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string minusculas = "abcdefghijkmnpqrstuvwxyz";
        const string digitos = "23456789";
        const string simbolos = "!@#$%*";
        const string todas = mayusculas + minusculas + digitos + simbolos;

        var buffer = new char[LongitudContrasenaTemporal];

        // Garantiza al menos una clase de cada grupo (mayúsc/minúsc/dígito/símbolo).
        buffer[0] = mayusculas[RandomNumberGenerator.GetInt32(mayusculas.Length)];
        buffer[1] = minusculas[RandomNumberGenerator.GetInt32(minusculas.Length)];
        buffer[2] = digitos[RandomNumberGenerator.GetInt32(digitos.Length)];
        buffer[3] = simbolos[RandomNumberGenerator.GetInt32(simbolos.Length)];

        for (var i = 4; i < buffer.Length; i++)
            buffer[i] = todas[RandomNumberGenerator.GetInt32(todas.Length)];

        return new string(buffer);
    }

    /// <summary>Snapshot de auditoría (ADR-003): JSON del responsable con UnsafeRelaxedJsonEscaping
    /// (legible sin escapes Unicode). Usado en valor_nuevo de CREATE/UPDATE/DEACTIVATE.
    /// NUNCA incluye password_hash (SEC-02).</summary>
    private static string SerializarResponsable(
        Guid id, Guid tenantId, Guid cicloId, string nombre, string correo, string rol,
        string estado, Guid? areaId, string? areaCodigo, string? areaNombre, bool requiereCambioPwd)
        => JsonSerializer.Serialize(new
        {
            id,
            tenantId,
            cicloId,
            nombre,
            correo,
            rol,
            estado,
            areaId,
            areaCodigo,
            areaNombre,
            requiereCambioPwd
        }, JsonOpcionesAuditoria);

    /// <summary>Snapshot del estado previo para valor_anterior en UPDATE/DEACTIVATE (ADR-003).</summary>
    private static string SerializarSnapshot(ResponsableEntity e)
        => JsonSerializer.Serialize(new
        {
            id = e.Id,
            tenantId = e.TenantId,
            cicloId = e.CicloId,
            nombre = e.Nombre,
            correo = e.Correo,
            rol = e.Rol,
            estado = e.Estado,
            areaId = e.AreaId,
            areaCodigo = e.AreaCodigo,
            areaNombre = e.AreaNombre,
            requiereCambioPwd = e.RequiereCambioPwd
        }, JsonOpcionesAuditoria);

    private static ResponsableResponse MapToResponse(ResponsableEntity e, Guid cicloId) => new()
    {
        Id = e.Id,
        CicloId = cicloId,
        TenantId = e.TenantId,
        Nombre = e.Nombre,
        Correo = e.Correo,
        Rol = e.Rol,
        Estado = e.Estado,
        AreaId = e.AreaId,
        AreaCodigo = e.AreaCodigo,
        AreaNombre = e.AreaNombre,
        RequiereCambioPwd = e.RequiereCambioPwd,
        UltimoLogin = e.UltimoLogin,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
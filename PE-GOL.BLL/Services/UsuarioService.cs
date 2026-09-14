using System.Data;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Usuarios Globales — Spec HU-003 § Lógica BLL (pasos 1-5) + § Endpoints (7).
/// Solo SuperAdmin ([Authorize(Roles = "SuperAdmin")] en el controller). La tabla usuario NO está
/// bajo RLS → el aislamiento lo garantiza la capa BLL (D1) más el filtro explícito en Listar.
///
/// Implementación real (fase 4 del Loop); los tests de @QA son la especificación ejecutable.
/// Reglas:
///  · Normalización de nombre (trim + colapso de espacios) y correo (trim + LOWER, D2).
///  · Re-validación BLL de forma: correo (regex) → 422; rol ∈ {AdminTenant, Gerente, JefeArea};
///    SuperAdmin → 422 ("solo seed").
///  · Unicidad de correo GLOBAL case-insensitive (DAL-U2) con y sin excludeId → 422.
///  · Existencia de tenant (DAL-U3) → 422; área pertenece al tenant si rol JefeArea (DAL-U4) → 422.
///  · Límites del plan SIEMPRE vía IPlanService.ValidarLimitesParaTenantAsync (CA #2 HU-002,
///    PROHIBIDO duplicar lógica): en Crear; en Actualizar SOLO si cambia tenant (contra el DESTINO,
///    D4/D6); en Activar (D7 aprobado por Jorge — caso #26). Nunca en Desactivar (D4).
///  · BCrypt cost ≥ 12 (SEC-02) con BCrypt.Net-Next — se usa SIEMPRE el nombre totalmente
///    cualificado BCrypt.Net.BCrypt.* (quirk de namespace del paquete).
///  · Escrituras (INSERT/UPDATE/estado/reset/tokens + auditoría) en UNA sola transacción.
///  · Captura SQLSTATE 23505 (correo duplicado, red de seguridad del UNIQUE del DDL) →
///    ValidacionException + rollback explícito.
///  · Auditoría (ADR-003): Entidad="Usuario", JSON legible con UnsafeRelaxedJsonEscaping,
///    NUNCA incluye password_hash (tests @QA #10/#11/#19/#30). TenantId del log = tenant del
///    usuario gestionado (null solo para SuperAdmin, que se crea por seed).
/// Ctor aprobado por spec: (IUsuarioRepository, IPlanService) + overload con ILogger (RNF-023).
/// </summary>
public class UsuarioService : IUsuarioService
{
    private const string EntidadAuditoria = "Usuario";
    private const int WorkFactorBcrypt = 12; // SEC-02: coste ≥ 12
    private const int LongitudContrasenaTemporal = 12;

    private static readonly HashSet<string> RolesDeTenant = new(StringComparer.Ordinal)
    {
        "AdminTenant", "Gerente", "JefeArea"
    };

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Opciones de serialización para la auditoría (log_auditoria.valor_anterior/valor_nuevo).
    /// UnsafeRelaxedJsonEscaping: NO escapa caracteres no-ASCII (p. ej. "María" se persiste como
    /// "María", no "Mar\u00EDa") → el JSON del log es legible por humanos (RNF-023 / ADR-003).
    /// Implementado por primera vez en un servicio con BCrypt/password (HANDOFF L177/L191).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IUsuarioRepository _repository;
    private readonly IPlanService _planService;
    private readonly ILogger<UsuarioService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public UsuarioService(IUsuarioRepository repository, IPlanService planService)
        : this(repository, planService, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public UsuarioService(IUsuarioRepository repository, IPlanService planService, ILogger<UsuarioService>? logger)
    {
        _repository = repository;
        _planService = planService;
        _logger = logger;
    }

    /// <summary>Spec §1 · CrearAsync: normaliza, re-valida forma/rol/unicidad/tenant/área/límites,
    /// hashea BCrypt 12, INSERT + auditoría CREATE en una sola transacción.</summary>
    public async Task<UsuarioResponse> CrearAsync(UsuarioCreateRequest request, CancellationToken ct = default)
    {
        var nombre = NormalizarNombre(request.Nombre);
        var correo = NormalizarCorreo(request.Correo);

        // Re-validación BLL de la forma (fuente de verdad, UX-04) — el correo malformado → 422.
        if (!EsCorreoValido(correo))
            throw new ValidacionException("El correo ingresado no es válido");

        // Re-validación BLL del rol: prohibido SuperAdmin (se crea solo por seed).
        ValidarRol(request.Rol);

        // Unicidad de correo GLOBAL case-insensitive (DAL-U2, D2).
        if (await _repository.ExisteCorreoAsync(correo, null, ct))
            throw new ValidacionException($"Ya existe un usuario con el correo '{correo}'");

        // El tenant destino debe existir (DAL-U3).
        if (!await _repository.ExisteTenantAsync(request.TenantId, ct))
            throw new ValidacionException("El tenant seleccionado no existe");

        // El área debe pertenecer al tenant destino cuando el rol es JefeArea (DAL-U4, RN-011).
        await ValidarAreaAsync(request.Rol, request.AreaId, request.TenantId, ct);

        // Límites del plan (CA #2 HU-002 → esta HU): SIEMPRE al crear (el usuario nuevo ocupa
        // plaza). La validación vive en IPlanService.ValidarLimitesParaTenantAsync — PROHIBIDO
        // duplicar la lógica (D3).
        await ValidarLimitesTenantAsync(request.TenantId, ct);

        var dto = new UsuarioInsertDto
        {
            TenantId = request.TenantId,
            Nombre = nombre,
            Correo = correo,
            PasswordHash = HashPassword(request.Password), // BCrypt cost ≥ 12 (SEC-02)
            Rol = request.Rol,
            AreaId = request.AreaId,
            Estado = "Activo",
            RequiereCambioPwd = true // D5: SIEMPRE TRUE en creación (CA #3)
        };

        // Spec §1 paso 8-10: INSERT + auditoría en UNA sola transacción (patrón HU-001/HU-002).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repository.InsertAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el usuario: id nulo.");

            _logger?.LogInformation(
                "Usuario {UsuarioId} creado en tenant {TenantId}. Modulo=Saas, Accion=CREATE, Entidad={Entidad}",
                nuevoId, dto.TenantId, EntidadAuditoria);

            // Auditoría (DAL-U5): valor_nuevo con la forma del UsuarioResponse SIN password,
            // serializado con UnsafeRelaxedJsonEscaping (ADR-003) — el JSON NUNCA contiene el hash.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = dto.TenantId, // tenant del usuario creado (filtrable en HU-005)
                UsuarioId = null,        // TenantContext.UserId (SA) se cableará con HU-004
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = SerializarSnapshot(
                    nuevoId, dto.TenantId, dto.Nombre, dto.Correo, dto.Rol,
                    dto.AreaId, dto.Estado, dto.RequiereCambioPwd)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var creado = await _repository.GetByIdAsync(nuevoId, ct)
                ?? throw new InvalidOperationException($"El usuario '{nuevoId}' recién creado no se pudo leer.");
            return MapToResponse(creado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Red de seguridad BD: el UNIQUE de usuario.correo (case-sensitive) captura la
            // carrera TOCTOU de correos EXACTOS. La transacción queda aborted → rollback explícito.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear usuario con correo {Correo}. Modulo=Saas", correo);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un usuario con el correo '{correo}'");
        }
        catch
        {
            // Cualquier error: rollback para garantizar atomicidad (INSERT + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §2 · ActualizarAsync: valida existencia (404), unicidad excluyendo self (422),
    /// rol/área/tenant (422) y —SOLO si cambia tenant— límites del DESTINO (D4/D6). UPDATE +
    /// auditoría UPDATE con valor_anterior (snapshot previo) en una sola transacción.</summary>
    public async Task<UsuarioResponse> ActualizarAsync(Guid id, UsuarioUpdateRequest request, CancellationToken ct = default)
    {
        var original = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El usuario '{id}' no existe");

        var nombre = NormalizarNombre(request.Nombre);
        var correo = NormalizarCorreo(request.Correo);

        // Re-validación BLL de la forma (fuente de verdad, UX-04).
        if (!EsCorreoValido(correo))
            throw new ValidacionException("El correo ingresado no es válido");

        // Re-validación BLL del rol (prohibido SuperAdmin).
        ValidarRol(request.Rol);

        // Unicidad de correo excluyendo self (DAL-U2b).
        if (await _repository.ExisteCorreoAsync(correo, id, ct))
            throw new ValidacionException($"Ya existe un usuario con el correo '{correo}'");

        // El tenant destino debe existir (DAL-U3).
        if (!await _repository.ExisteTenantAsync(request.TenantId, ct))
            throw new ValidacionException("El tenant seleccionado no existe");

        // El área debe pertenecer al tenant destino cuando el rol es JefeArea (DAL-U4, RN-011).
        await ValidarAreaAsync(request.Rol, request.AreaId, request.TenantId, ct);

        // Si cambia tenant_id → re-validar límites contra el DESTINO (D4/D6). Si no cambia,
        // NO se valida (el usuario ya ocupaba plaza en ese tenant — caso #17).
        if (request.TenantId != original.TenantId)
            await ValidarLimitesTenantAsync(request.TenantId, ct);

        var dto = new UsuarioUpdateDto
        {
            Id = id,
            Nombre = nombre,
            Correo = correo,
            Rol = request.Rol,
            TenantId = request.TenantId,
            AreaId = request.AreaId
        };

        // Spec §2 paso 5-6: UPDATE + auditoría en UNA sola transacción. El valor_anterior es el
        // snapshot del estado actual (forma UsuarioResponse SIN password, ADR-003).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateAsync(dto, tx, ct);

            _logger?.LogInformation(
                "Usuario {UsuarioId} actualizado. Modulo=Saas, Accion=UPDATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = dto.TenantId, // tenant del usuario gestionado
                UsuarioId = null,        // TenantContext.UserId (SA) se cableará con HU-004
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = SerializarSnapshot(
                    original.Id, original.TenantId, original.Nombre, original.Correo,
                    original.Rol, original.AreaId, original.Estado, original.RequiereCambioPwd),
                ValorNuevo = SerializarSnapshot(
                    id, dto.TenantId, dto.Nombre, dto.Correo, dto.Rol, dto.AreaId,
                    original.Estado, original.RequiereCambioPwd)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El usuario '{id}' no existe");
            return MapToResponse(actualizado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Red de seguridad BD: el UNIQUE case-sensitive de usuario.correo también protege el UPDATE.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar usuario {UsuarioId}. Modulo=Saas", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un usuario con el correo '{correo}'");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §3 · DesactivarAsync: estado='Inactivo' + revoca refresh tokens (DAL-U9) +
    /// auditoría DEACTIVATE en una sola transacción. SIN validación de límites (D4).</summary>
    public async Task<UsuarioResponse> DesactivarAsync(Guid id, CancellationToken ct = default)
    {
        var actual = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El usuario '{id}' no existe");

        // Si ya está Inactivo no se audita la transición (evita log duplicado).
        if (string.Equals(actual.Estado, "Inactivo", StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException("El usuario ya está inactivo");

        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(id, "Inactivo", tx, ct);

            // CA: al desactivar se revocan los refresh tokens → el usuario pierde la sesión inmediata.
            var revocados = await _repository.RevocarRefreshTokensAsync(id, tx, ct);

            _logger?.LogInformation(
                "Usuario {UsuarioId} desactivado. RefreshTokensRevocados={Revocados}. Modulo=Saas, Accion=DEACTIVATE, Entidad={Entidad}",
                id, revocados, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = actual.TenantId, // tenant del usuario gestionado
                UsuarioId = null,           // TenantContext.UserId (SA) se cableará con HU-004
                Accion = "DEACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = actual.Estado }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Inactivo" }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var desactivado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El usuario '{id}' no existe");
            return MapToResponse(desactivado);
        }
        catch
        {
            // Rollback: UPDATE estado + revocación + auditoría no se persisten parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en DesactivarAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ActivarAsync: valida límites del plan al ACTIVAR (D7 aprobado por Jorge —
    /// tenant al tope → 422, caso #26) y transiciona a 'Activo'. Auditoría ACTIVATE en la misma tx.</summary>
    public async Task<UsuarioResponse> ActivarAsync(Guid id, CancellationToken ct = default)
    {
        var actual = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El usuario '{id}' no existe");

        if (string.Equals(actual.Estado, "Activo", StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException("El usuario ya está activo");

        // D7 (decisión conservadora aprobada por Jorge): aunque activar no incrementa el conteo
        // (el Inactivo/Bloqueado ya ocupa plaza — D5 HU-002 cuenta TODOS), se valida igual para
        // no permitir estados inconsistentes → tenant al tope = 422.
        if (actual.TenantId is not null)
            await ValidarLimitesTenantAsync(actual.TenantId.Value, ct);

        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(id, "Activo", tx, ct);

            _logger?.LogInformation(
                "Usuario {UsuarioId} activado. Modulo=Saas, Accion=ACTIVATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = actual.TenantId, // tenant del usuario gestionado
                UsuarioId = null,           // TenantContext.UserId (SA) se cableará con HU-004
                Accion = "ACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = actual.Estado }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Activo" }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var activo = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El usuario '{id}' no existe");
            return MapToResponse(activo);
        }
        catch
        {
            // Rollback: UPDATE estado + auditoría no se persisten parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActivarAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §5 · ResetearContrasenaAsync: genera contraseña temporal (12 chars, 4 clases),
    /// hash BCrypt cost 12, requiere_cambio_pwd=TRUE, resetea intentos/bloqueo (DAL-U10) y revoca
    /// refresh tokens (DAL-U9). Auditoría UPDATE sin exponer el hash (ADR-003). La contraseña
    /// temporal NO se devuelve por HTTP (llega por correo en la HU de notificaciones, Sprint 2).</summary>
    public async Task<UsuarioResponse> ResetearContrasenaAsync(Guid id, CancellationToken ct = default)
    {
        var actual = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El usuario '{id}' no existe");

        // Generación de contraseña temporal segura + hash BCrypt cost ≥ 12 (SEC-02).
        var temporal = GenerarContrasenaTemporal();
        var hash = HashPassword(temporal);

        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.ResetContrasenaAsync(id, hash, tx, ct);

            // Al resetear se revocan los refresh tokens activos → las sesiones actuales mueren.
            var revocados = await _repository.RevocarRefreshTokensAsync(id, tx, ct);

            _logger?.LogInformation(
                "Contraseña del usuario {UsuarioId} reseteada. RefreshTokensRevocados={Revocados}. Modulo=Saas, Accion=UPDATE, Entidad={Entidad}",
                id, revocados, EntidadAuditoria);

            // Auditoría: valor_anterior marca el flag previo; valor_nuevo refleja
            // requiere_cambio_pwd=true y pwd_reseteado=true. NUNCA el hash ni el password literal.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = actual.TenantId, // tenant del usuario gestionado
                UsuarioId = null,           // TenantContext.UserId (SA) se cableará con HU-004
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(
                    new { requiere_cambio_pwd = actual.RequiereCambioPwd }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(
                    new { requiere_cambio_pwd = true, pwd_reseteado = true }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var reseteado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El usuario '{id}' no existe");
            return MapToResponse(reseteado);
        }
        catch
        {
            // Rollback: reset + revocación + auditoría no se persisten parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ResetearContrasenaAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec § Endpoints · ListarAsync: listado paginado con filtros opcionales
    /// (tenantId/rol/estado). page ≥ 1; pageSize 1..100 (trunca a 100). Página vacía con total==0.</summary>
    public async Task<PagedResult<UsuarioResponse>> ListarAsync(int page, int pageSize, Guid? tenantId, string? rol, string? estado, CancellationToken ct = default)
    {
        // Sanear paginación: page >= 1; pageSize 1..100 (>100 trunca a 100).
        var pagina = page < 1 ? 1 : page;
        var tamanio = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

        var total = await _repository.CountAsync(tenantId, rol, estado, ct);

        if (total == 0)
        {
            return new PagedResult<UsuarioResponse>
            {
                Items = [],
                Page = pagina,
                PageSize = tamanio,
                Total = 0,
                TotalPages = 0
            };
        }

        var items = await _repository.GetPagedAsync(pagina, tamanio, tenantId, rol, estado, ct);
        var totalPages = (total + tamanio - 1) / tamanio;

        return new PagedResult<UsuarioResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            Page = pagina,
            PageSize = tamanio,
            Total = total,
            TotalPages = totalPages
        };
    }

    /// <summary>Spec § Endpoints · ObtenerPorIdAsync: 404 si no existe.</summary>
    public async Task<UsuarioResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var entidad = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El usuario '{id}' no existe");
        return MapToResponse(entidad);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Normalización de nombre: trim + colapso de espacios internos múltiples
    /// (mismo helper que TenantService/PlanService, HU-001/HU-002).</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return Regex.Replace(limpio, @"\s+", " ");
    }

    /// <summary>Normalización de correo: trim + ToLowerInvariant (D2 — unicidad GLOBAL
    /// case-insensitive por patrón ADR-001/002).</summary>
    private static string NormalizarCorreo(string? correo)
        => (correo?.Trim() ?? string.Empty).ToLowerInvariant();

    /// <summary>Re-validación BLL de la forma del correo (la BLL es la fuente de verdad, UX-04).</summary>
    private static bool EsCorreoValido(string correo)
        => correo.Length <= 200 && EmailRegex.IsMatch(correo);

    /// <summary>Re-validación BLL del rol: prohibido SuperAdmin (solo seed); permitidos
    /// AdminTenant/Gerente/JefeArea. → 422.</summary>
    private static void ValidarRol(string rol)
    {
        if (string.Equals(rol, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException("El SuperAdmin se crea solo mediante seed");

        if (!RolesDeTenant.Contains(rol))
            throw new ValidacionException($"El rol '{rol}' no es válido. Los roles permitidos son AdminTenant, Gerente y JefeArea");
    }

    /// <summary>Validación de área para rol JefeArea (RN-011): requerida y debe pertenecer al
    /// tenant destino (DAL-U4). → 422.</summary>
    private async Task ValidarAreaAsync(string rol, Guid? areaId, Guid tenantId, CancellationToken ct)
    {
        if (!string.Equals(rol, "JefeArea", StringComparison.Ordinal))
            return;

        if (areaId is null)
            throw new ValidacionException("El rol JefeArea requiere un área asignada");

        if (!await _repository.PerteneceAreaAlTenantAsync(areaId.Value, tenantId, ct))
            throw new ValidacionException("El área seleccionada no pertenece al tenant");
    }

    /// <summary>Validación centralizada de límites del plan (CA #2 HU-002). Obtiene el plan_id del
    /// tenant (U3b) y delega en IPlanService.ValidarLimitesParaTenantAsync — la ÚNICA fuente de
    /// verdad (D3): no se duplica ninguna lógica de conteo. → 422 con los errores del validador.</summary>
    private async Task ValidarLimitesTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var planId = await _repository.ObtenerPlanIdTenantAsync(tenantId, ct);
        var resultado = await _planService.ValidarLimitesParaTenantAsync(tenantId, planId ?? Guid.Empty, ct);
        if (!resultado.EsValido)
            throw new ValidacionException(string.Join(" ", resultado.Errores));
    }

    /// <summary>Hash BCrypt con work factor ≥ 12 (SEC-02). Se usa el nombre totalmente cualificado
    /// BCrypt.Net.BCrypt.* (quirk de namespace del paquete BCrypt.Net-Next). Si el hash falla se
    /// propaga → el middleware la traduce a 500.</summary>
    private static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactorBcrypt);

    /// <summary>Genera una contraseña temporal segura de 12 chars con al menos una mayúscula,
    /// una minúscula, un dígito y un símbolo (spec §5 paso 2).</summary>
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

    /// <summary>Serializa el snapshot de auditoría con la forma del UsuarioResponse (SEC-02/ADR-003):
    /// NUNCA incluye password_hash nia contraseñas; usa UnsafeRelaxedJsonEscaping para que el JSON
    /// sea legible (RNF-023).</summary>
    private static string SerializarSnapshot(
        Guid id, Guid? tenantId, string nombre, string correo,
        string rol, Guid? areaId, string estado, bool requiereCambioPwd)
        => JsonSerializer.Serialize(new
        {
            id,
            tenantId,
            nombre,
            correo,
            rol,
            areaId,
            estado,
            requiereCambioPwd
        }, JsonOpcionesAuditoria);

    private static UsuarioResponse MapToResponse(UsuarioEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Nombre = e.Nombre,
        Correo = e.Correo,
        Rol = e.Rol,
        AreaId = e.AreaId,
        Estado = e.Estado,
        RequiereCambioPwd = e.RequiereCambioPwd,
        UltimoLogin = e.UltimoLogin,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
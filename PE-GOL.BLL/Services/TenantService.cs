using System.Data;
using System.Text.Json;
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
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Tenants — Spec HU-001 § "Lógica BLL".
/// Implementación real (fase 4 del Loop); los tests de @QA son la especificación ejecutable.
/// Reglas: unicidad de nombre (DAL-8) → ValidacionException(422); existencia de plan (DAL-9)
/// → 422; tenant inexistente → NotFoundException(404); transiciones de estado validas.
/// Captura SQLSTATE 23505 (unique_violation de uq_tenant_nombre, ADR-001) y traduce a
/// ValidacionException con rollback explícito de la transacción.
/// </summary>
public class TenantService : ITenantService
{
    public const string ZonaHorariaDefault = "America/Managua";
    private const string EntidadAuditoria = "Tenant";

    private readonly ITenantRepository _repository;
    private readonly IPlanService _planService;
    private readonly TenantContext? _tenantContext; // D6 (HU-004): actor en log_auditoria.usuario_id
    private readonly ILogger<TenantService>? _logger;

    /// <summary>
    /// Ctor con la nueva dependencia IPlanService (Spec HU-002 §9.1, decisión D3/D4).
    /// El DI (IOC) inyecta IPlanService; @QA pasa un Mock&lt;IPlanService&gt; en Arrange
    /// (los 20 tests de HU-001 se ajustan SOLO en el ctor, sin cambiar asserts ni nombres).
    /// ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).
    /// </summary>
    public TenantService(ITenantRepository repository, IPlanService planService, ILogger<TenantService>? logger = null)
    {
        _repository = repository;
        _planService = planService;
        _logger = logger;
    }

    /// <summary>
    /// Ctor con TenantContext (D6 — HU-004): overload opcional que permite cablear
    /// TenantContext.UserId en log_auditoria.usuario_id (el SuperAdmin autenticado).
    /// </summary>
    public TenantService(ITenantRepository repository, IPlanService planService, TenantContext tenantContext, ILogger<TenantService>? logger = null)
    {
        _repository = repository;
        _planService = planService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<TenantResponse> CrearAsync(TenantCreateRequest request, CancellationToken ct = default)
    {
        var nombre = NormalizarNombre(request.Nombre);

        // Capa 1 de unicidad (DAL-8): mensaje amigable sin depender de la BD (ADR-001).
        if (await _repository.ExisteNombreAsync(nombre, null, ct))
            throw new ValidacionException($"Ya existe un tenant con el nombre '{nombre}'");

        if (!await _repository.ExistePlanAsync(request.PlanId, ct))
            throw new ValidacionException("El plan seleccionado no existe");

        var dto = new TenantInsertDto
        {
            Nombre = nombre,
            Descripcion = request.Descripcion,
            PlanId = request.PlanId,
            LogoUrl = request.LogoUrl,
            Eslogan = request.Eslogan,
            ZonaHoraria = string.IsNullOrWhiteSpace(request.ZonaHoraria) ? ZonaHorariaDefault : request.ZonaHoraria,
            Estado = "Activo"
        };

        // Spec HU-001 § Lógica BLL: INSERT + auditoría en UNA sola transacción (IDbTransaction).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var id = await _repository.InsertAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el tenant: id nulo.");

            _logger?.LogInformation(
                "Tenant {TenantId} creado. Modulo=Saas, Accion=CREATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = id,
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = null,
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    nombre = dto.Nombre,
                    descripcion = dto.Descripcion,
                    planId = dto.PlanId,
                    logoUrl = dto.LogoUrl,
                    eslogan = dto.Eslogan,
                    zonaHoraria = dto.ZonaHoraria,
                    estado = dto.Estado
                })
            }, tx, ct);

            tx.Commit();

            var creado = await _repository.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"El tenant '{id}' recién creado no se pudo leer.");
            return MapToResponse(creado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: colisión concurrente capturada a nivel BD. La transacción
            // PostgreSQL queda aborted; se hace rollback explícito antes de propagar la excepción.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear tenant con nombre {Nombre}. Modulo=Saas", nombre);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un tenant con el nombre '{nombre}'");
        }
        catch
        {
            // Cualquier error: rollback para garantizar atomicidad (INSERT + auditoría no se
            // persisten parcialmente).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CrearAsync. Modulo=Saas"); }
            throw;
        }
    }

    public async Task<TenantResponse> ActualizarAsync(Guid id, TenantUpdateRequest request, CancellationToken ct = default)
    {
        var original = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El tenant '{id}' no existe");

        var nombre = NormalizarNombre(request.Nombre);

        // Unicidad excluyendo self (DAL-8 con excludeId = id).
        if (await _repository.ExisteNombreAsync(nombre, id, ct))
            throw new ValidacionException($"Ya existe un tenant con el nombre '{nombre}'");

        if (!await _repository.ExistePlanAsync(request.PlanId, ct))
            throw new ValidacionException("El plan seleccionado no existe");

        // HU-002 §9.1 (CA #3): al CAMBIAR de plan se valida centralizadamente (D4) que el tenant
        // no exceda los límites del plan destino (PlanLimitValidator vía IPlanService).
        // Si el plan no cambia, NO se valida (compatibilidad total con los tests de HU-001).
        if (request.PlanId != original.PlanId)
        {
            var resultado = await _planService.ValidarLimitesParaTenantAsync(id, request.PlanId, ct);
            if (!resultado.EsValido)
                throw new ValidacionException(string.Join(" ", resultado.Errores));
        }

        var dto = new TenantUpdateDto
        {
            Id = id,
            Nombre = nombre,
            Descripcion = request.Descripcion,
            PlanId = request.PlanId,
            LogoUrl = request.LogoUrl,
            Eslogan = request.Eslogan,
            ZonaHoraria = string.IsNullOrWhiteSpace(request.ZonaHoraria)
                ? (original.ZonaHoraria ?? ZonaHorariaDefault)
                : request.ZonaHoraria
        };

        // Spec HU-001 § Lógica BLL: UPDATE + auditoría en UNA sola transacción (IDbTransaction).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateAsync(dto, tx, ct);

            _logger?.LogInformation(
                "Tenant {TenantId} actualizado. Modulo=Saas, Accion=UPDATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = id,
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(original),
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    nombre = dto.Nombre,
                    descripcion = dto.Descripcion,
                    planId = dto.PlanId,
                    logoUrl = dto.LogoUrl,
                    eslogan = dto.Eslogan,
                    zonaHoraria = dto.ZonaHoraria
                })
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El tenant '{id}' no existe");

            return MapToResponse(actualizado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el índice único uq_tenant_nombre también protege el UPDATE.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar tenant {TenantId}. Modulo=Saas", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un tenant con el nombre '{nombre}'");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría no se persisten parcialmente).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Saas"); }
            throw;
        }
    }

    public async Task<TenantResponse> DesactivarAsync(Guid id, CancellationToken ct = default)
    {
        var actual = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El tenant '{id}' no existe");

        // Si ya está Inactivo no se audita la transición (evita log duplicado).
        if (string.Equals(actual.Estado, "Inactivo", StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException("El tenant ya está desactivado");

        // Spec HU-001 § Lógica BLL paso 3: UPDATE estado + revocación refresh tokens + auditoría
        // en UNA sola transacción (si la auditoría falla, el tenant NO queda Inactivo sin log;
        // si la revocación falla, el tenant NO queda Inactivo con tokens vigentes).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(id, "Inactivo", tx, ct);

            // CA #3: al desactivar se revocan los refresh tokens de los usuarios del tenant.
            var revocados = await _repository.RevocarRefreshTokensAsync(id, tx, ct);

            _logger?.LogInformation(
                "Tenant {TenantId} desactivado. RefreshTokensRevocados={Revocados}. Modulo=Saas, Accion=DEACTIVATE, Entidad={Entidad}",
                id, revocados, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = id,
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "DEACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = actual.Estado }),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Inactivo" })
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var desactivado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El tenant '{id}' no existe");

            return MapToResponse(desactivado);
        }
        catch
        {
            // Rollback: ninguna de las tres operaciones (UPDATE, revocación, auditoría) se
            // persiste parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en DesactivarAsync. Modulo=Saas"); }
            throw;
        }
    }

    public async Task<TenantResponse> ActivarAsync(Guid id, CancellationToken ct = default)
    {
        var actual = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El tenant '{id}' no existe");

        if (string.Equals(actual.Estado, "Activo", StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException("El tenant ya está activo");

        // Spec HU-001 § Lógica BLL paso 4: UPDATE estado + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateEstadoAsync(id, "Activo", tx, ct);

            _logger?.LogInformation(
                "Tenant {TenantId} activado. Modulo=Saas, Accion=ACTIVATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = id,
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "ACTIVATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { estado = actual.Estado }),
                ValorNuevo = JsonSerializer.Serialize(new { estado = "Activo" })
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var activo = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El tenant '{id}' no existe");

            return MapToResponse(activo);
        }
        catch
        {
            // Rollback: UPDATE + auditoría no se persisten parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActivarAsync. Modulo=Saas"); }
            throw;
        }
    }

    public async Task<PagedResult<TenantResponse>> ListarAsync(int page, int pageSize, string? estado, Guid? planId, CancellationToken ct = default)
    {
        // Sanear paginación: page >= 1; pageSize 1..100 (>100 trunca a 100).
        var pagina = page < 1 ? 1 : page;
        var tamanio = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

        var total = await _repository.CountAsync(estado, planId, ct);

        // total == 0 → página vacía sin ejecutar el SELECT de datos (spec § ListarAsync paso 2).
        if (total == 0)
        {
            return new PagedResult<TenantResponse>
            {
                Items = [],
                Page = pagina,
                PageSize = tamanio,
                Total = 0,
                TotalPages = 0
            };
        }

        var items = await _repository.GetPagedAsync(pagina, tamanio, estado, planId, ct);
        var totalPages = (total + tamanio - 1) / tamanio;

        return new PagedResult<TenantResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            Page = pagina,
            PageSize = tamanio,
            Total = total,
            TotalPages = totalPages
        };
    }

    public async Task<TenantResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var entidad = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El tenant '{id}' no existe");
        return MapToResponse(entidad);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Normalización de nombre: trim + colapso de espacios internos múltiples.</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(limpio, @"\s+", " ");
    }

    private static TenantResponse MapToResponse(TenantEntity e) => new()
    {
        Id = e.Id,
        Nombre = e.Nombre,
        Descripcion = e.Descripcion,
        PlanId = e.PlanId,
        PlanNombre = e.PlanNombre,
        LogoUrl = e.LogoUrl,
        Eslogan = e.Eslogan,
        ZonaHoraria = e.ZonaHoraria ?? ZonaHorariaDefault,
        Estado = e.Estado,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de gestión de Planes de Suscripción — Spec HU-002 § Lógica BLL (pasos 1-7) + §9
/// integración con TenantService. Solo SuperAdmin.
/// Implementación real (fase 4 del Loop); los tests de @QA son la especificación ejecutable.
/// Reglas:
///  · Normalización de nombre (trim + colapso de espacios) — mismo helper que TenantService.
///  · Re-validación BLL de rangos (D9): maxAreas 1..20, maxUsuarios 1..1000, maxCiclosActivos 1..10 → 422.
///  · Unicidad de nombre case-insensitive (DAL-P4) con y sin excludeId → 422.
///  · Regla crítica: bajar límites no puede dejar a ningún tenant existente sobre el tope →
///    ValidarLimitesParaTenantsDelPlanAsync (PlanLimitValidator centralizado, D4) → 422.
///  · DELETE físico protegido por ContarTenantsUsoAsync > 0 → 422; red de seguridad FK
///    ON DELETE RESTRICT → SQLSTATE 23503 → ValidacionException.
///  · Escriburas (INSERT/UPDATE/DELETE + auditoría) en UNA sola transacción IDbTransaction.
///  · ADR-002: captura SQLSTATE 23505 (uq_plan_nombre) → ValidacionException + rollback explícito.
/// Ctor requerido por spec: (IPlanRepository, PlanLimitValidator) + overload con ILogger (RNF-023).
/// </summary>
public class PlanService : IPlanService
{
    private const string EntidadAuditoria = "Plan";
    private const int MaxMensajesTenants = 5;

    /// <summary>
    /// Opciones de serialización para la auditoría (log_auditoria.valor_anterior/valor_nuevo).
    /// UnsafeRelaxedJsonEscaping: NO escapa caracteres no-ASCII (p. ej. "Estándar" se persiste
    /// como "Estándar", no "Est\u00E1ndar") → los valores JSON del log son legibles por humanos
    /// (RNF-023 / trazabilidad de auditoría). Verificados contra los tests de @QA (PlanServiceTests
    /// caso 17: el JSON auditado debe contener el literal "Estándar XL").
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPlanRepository _repository;
    private readonly PlanLimitValidator _validator;
    private readonly TenantContext? _tenantContext; // D6 (HU-004): actor en log_auditoria.usuario_id
    private readonly ILogger<PlanService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public PlanService(IPlanRepository repository, PlanLimitValidator validator)
        : this(repository, validator, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public PlanService(IPlanRepository repository, PlanLimitValidator validator, ILogger<PlanService>? logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Ctor con TenantContext (D6 — HU-004): overload opcional que permite cablear
    /// TenantContext.UserId en log_auditoria.usuario_id (el SuperAdmin autenticado).
    /// </summary>
    public PlanService(IPlanRepository repository, PlanLimitValidator validator, TenantContext tenantContext, ILogger<PlanService>? logger = null)
    {
        _repository = repository;
        _validator = validator;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · CrearAsync: normaliza, re-valida rangos, valida unicidad (DAL-P4),
    /// INSERT + auditoría CREATE en una sola transacción.</summary>
    public async Task<PlanResponse> CrearAsync(PlanCreateRequest request, CancellationToken ct = default)
    {
        var nombre = NormalizarNombre(request.Nombre);

        // Re-validación BLL de rangos (D9) — la BLL es la fuente de verdad (UX-04).
        ValidarRangos(request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos);

        // Capa 1 de unicidad (DAL-P4 sin exclude): mensaje amigable sin depender de la BD (ADR-002).
        if (await _repository.ExisteNombreAsync(nombre, null, ct))
            throw new ValidacionException($"Ya existe un plan con el nombre '{nombre}'");

        var dto = new PlanInsertDto
        {
            Nombre = nombre,
            Descripcion = request.Descripcion,
            MaxAreas = request.MaxAreas,
            MaxUsuarios = request.MaxUsuarios,
            MaxCiclosActivos = request.MaxCiclosActivos
        };

        // Spec §1 paso 4-7: INSERT + auditoría en UNA sola transacción (IDbTransaction).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            var id = await _repository.InsertAsync(dto, tx, ct)
                ?? throw new InvalidOperationException("No se pudo insertar el plan: id nulo.");

            _logger?.LogInformation(
                "Plan {PlanId} creado. Modulo=Saas, Accion=CREATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = null, // acción GLOBAL del SuperAdmin: log_auditoria.tenant_id = NULL (DAL-P9)
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = null,
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    nombre = dto.Nombre,
                    descripcion = dto.Descripcion,
                    maxAreas = dto.MaxAreas,
                    maxUsuarios = dto.MaxUsuarios,
                    maxCiclosActivos = dto.MaxCiclosActivos
                }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido (incl. createdAt).
            var creado = await _repository.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"El plan '{id}' recién creado no se pudo leer.");
            return MapToResponse(creado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-002 · Capa 2: el índice único uq_plan_nombre elimina la carrera TOCTOU.
            // La transacción PostgreSQL queda aborted; rollback explícito antes de propagar.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al crear plan con nombre {Nombre}. Modulo=Saas", nombre);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un plan con el nombre '{nombre}'");
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
    /// rangos (422), y —si los límites cambian— que ningún tenant existente exceda los NUEVOS
    /// límites (422). UPDATE + auditoría UPDATE en una sola transacción.</summary>
    public async Task<PlanResponse> ActualizarAsync(Guid id, PlanUpdateRequest request, CancellationToken ct = default)
    {
        var original = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El plan '{id}' no existe");

        var nombre = NormalizarNombre(request.Nombre);

        // Unicidad excluyendo self (DAL-P4 con excludeId = id).
        if (await _repository.ExisteNombreAsync(nombre, id, ct))
            throw new ValidacionException($"Ya existe un plan con el nombre '{nombre}'");

        // Re-validación BLL de rangos (D9).
        ValidarRangos(request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos);

        // Regla crítica (spec §2 paso 4): si los límites cambian, ningún tenant existente del
        // plan puede quedar sobre el tope. Se reportan TODOS los tenants infractores (acumulados).
        var nuevosLimites = PlanLimits.Desde(request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos);
        var limitesActuales = PlanLimits.Desde(original.MaxAreas, original.MaxUsuarios, original.MaxCiclosActivos);
        if (nuevosLimites != limitesActuales)
        {
            var validacion = await ValidarLimitesParaTenantsDelPlanAsync(id, nuevosLimites, ct);
            if (!validacion.EsValido)
                throw new ValidacionException(string.Join(" ", validacion.Errores));
        }

        var dto = new PlanUpdateDto
        {
            Id = id,
            Nombre = nombre,
            Descripcion = request.Descripcion,
            MaxAreas = request.MaxAreas,
            MaxUsuarios = request.MaxUsuarios,
            MaxCiclosActivos = request.MaxCiclosActivos
        };

        // Spec §2 paso 5-8: UPDATE + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.UpdateAsync(dto, tx, ct);

            _logger?.LogInformation(
                "Plan {PlanId} actualizado. Modulo=Saas, Accion=UPDATE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = null, // acción GLOBAL del SuperAdmin (DAL-P9)
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(original, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    nombre = dto.Nombre,
                    descripcion = dto.Descripcion,
                    maxAreas = dto.MaxAreas,
                    maxUsuarios = dto.MaxUsuarios,
                    maxCiclosActivos = dto.MaxCiclosActivos
                }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizado = await _repository.GetByIdAsync(id, ct)
                ?? throw new NotFoundException($"El plan '{id}' no existe");

            return MapToResponse(actualizado);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-002 · Capa 2: el índice único también protege el UPDATE.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar plan {PlanId}. Modulo=Saas", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Saas"); }
            throw new ValidacionException($"Ya existe un plan con el nombre '{nombre}'");
        }
        catch
        {
            // Rollback para garantizar atomicidad (UPDATE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en ActualizarAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §3 · EliminarAsync: DELETE físico (D2) protegido por BLL (plan en uso → 422)
    /// y por la FK ON DELETE RESTRICT (red de seguridad 23503). Auditoría DELETE en la misma
    /// transacción. Retorna true si eliminó.</summary>
    public async Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var entidad = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El plan '{id}' no existe");

        var enUso = await _repository.ContarTenantsUsoAsync(id, ct);
        if (enUso > 0)
            throw new ValidacionException($"No se puede eliminar el plan '{entidad.Nombre}' porque lo usan {enUso} tenant(s)");

        // Spec §3 paso 3-5: DELETE + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.DeleteAsync(id, tx, ct);

            _logger?.LogInformation(
                "Plan {PlanId} eliminado. Modulo=Saas, Accion=DELETE, Entidad={Entidad}",
                id, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = null, // acción GLOBAL del SuperAdmin (DAL-P9)
                UsuarioId = _tenantContext?.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "DELETE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(entidad, JsonOpcionesAuditoria),
                ValorNuevo = null
            }, tx, ct);

            tx.Commit();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            // Red de seguridad BD (carrera concurrente): tenant.plan_id ON DELETE RESTRICT.
            _logger?.LogWarning(ex, "Violación de FK 23503 al eliminar plan {PlanId}. Modulo=Saas", id);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23503. Modulo=Saas"); }
            throw new ValidacionException($"No se puede eliminar el plan '{entidad.Nombre}' porque lo usan tenant(s)");
        }
        catch
        {
            // Rollback para garantizar atomicidad (DELETE + auditoría).
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en EliminarAsync. Modulo=Saas"); }
            throw;
        }
    }

    /// <summary>Spec §4 · ListarAsync: listado completo ordenado por nombre ASC (sin paginación, D1).</summary>
    public async Task<List<PlanResponse>> ListarAsync(CancellationToken ct = default)
    {
        var planes = await _repository.GetAllAsync(ct);
        return planes.Select(MapToResponse).ToList();
    }

    /// <summary>Spec §5 · ObtenerPorIdAsync: 404 si no existe.</summary>
    public async Task<PlanResponse?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var entidad = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"El plan '{id}' no existe");
        return MapToResponse(entidad);
    }

    /// <summary>Spec §6 · ValidarLimitesParaTenantAsync: validación reutilizable (consumida por
    /// HU-009/HU-003 y por TenantService.ActualizarAsync). NO lanza por límites excedidos:
    /// el llamador decide cómo responder (422 con los Errores). 404 solo si el plan no existe.</summary>
    public async Task<ResultadoValidacionLimites> ValidarLimitesParaTenantAsync(Guid tenantId, Guid planId, CancellationToken ct = default)
    {
        var plan = await _repository.GetByIdAsync(planId, ct)
            ?? throw new NotFoundException($"El plan '{planId}' no existe");

        var conteos = await _repository.ObtenerConteosUsoAsync(tenantId, ct);
        return _validator.Validar(
            PlanLimits.Desde(plan.MaxAreas, plan.MaxUsuarios, plan.MaxCiclosActivos),
            conteos);
    }

    /// <summary>Spec §7 · ValidarLimitesParaTenantsDelPlanAsync: acumula por tenant (con nombre)
    /// los límites excedidos del plan para los nuevos límites. Reporta todos los infractores;
    /// si hay más de 5 mensajes, limita a 5 y agrega "…y N más" (decisión de UX del spec).</summary>
    public async Task<ResultadoValidacionLimites> ValidarLimitesParaTenantsDelPlanAsync(Guid planId, PlanLimits nuevosLimites, CancellationToken ct = default)
    {
        var tenants = await _repository.ObtenerTenantsPorPlanAsync(planId, ct);
        var errores = new List<string>();

        foreach (var tenant in tenants)
        {
            var conteos = await _repository.ObtenerConteosUsoAsync(tenant.TenantId, ct);
            var resultado = _validator.Validar(nuevosLimites, conteos);

            foreach (var error in resultado.Errores)
            {
                // Los mensajes del validador comienzan con el prefijo "El tenant"; se reemplaza
                // el prefijo por "El tenant '{Nombre}'" para identificar al infractor (D4:
                // el validador puro sigue siendo la única fuente de verdad de QUÉ se excede).
                errores.Add($"El tenant '{tenant.Nombre}'{error[PlanLimitValidator.PrefijoMensaje.Length..]}");
            }
        }

        if (errores.Count == 0)
            return ResultadoValidacionLimites.Ok();

        if (errores.Count > MaxMensajesTenants)
        {
            var recortados = errores.Take(MaxMensajesTenants).ToList();
            recortados.Add($"…y {errores.Count - MaxMensajesTenants} más");
            return ResultadoValidacionLimites.Fallo(recortados);
        }

        return ResultadoValidacionLimites.Fallo(errores);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Normalización de nombre: trim + colapso de espacios internos múltiples
    /// (mismo helper que TenantService, HU-001).</summary>
    private static string NormalizarNombre(string? nombre)
    {
        var limpio = nombre?.Trim() ?? string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(limpio, @"\s+", " ");
    }

    /// <summary>Re-validación BLL de rangos (D9) — fuente de verdad (UX-04: la validación
    /// del servidor manda). Mensajes claros con el nombre del campo → 422.</summary>
    private static void ValidarRangos(int maxAreas, int maxUsuarios, int maxCiclosActivos)
    {
        if (maxAreas is < 1 or > 20)
            throw new ValidacionException("maxAreas debe estar entre 1 y 20");

        if (maxUsuarios is < 1 or > 1000)
            throw new ValidacionException("maxUsuarios debe estar entre 1 y 1000");

        if (maxCiclosActivos is < 1 or > 10)
            throw new ValidacionException("maxCiclosActivos debe estar entre 1 y 10");
    }

    private static PlanResponse MapToResponse(PlanEntity e) => new()
    {
        Id = e.Id,
        Nombre = e.Nombre,
        Descripcion = e.Descripcion,
        MaxAreas = e.MaxAreas,
        MaxUsuarios = e.MaxUsuarios,
        MaxCiclosActivos = e.MaxCiclosActivos,
        CreatedAt = e.CreatedAt
    };
}
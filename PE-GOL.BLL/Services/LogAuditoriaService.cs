using Microsoft.Extensions.Logging;
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
/// Servicio de consulta del Log de Auditoría — Spec HU-005 § Lógica BLL.
/// ⚠️ STUB TDD (fase roja, TEST-01): los métodos lanzan NotImplementedException a propósito.
/// Los tests de @QA (LogAuditoriaServiceTests, 19 casos) son la especificación ejecutable.
/// @BackendDev reemplaza los cuerpos en fase 4 con la lógica del spec:
///   1. ListarAsync: D12 (rol SuperAdmin → 403) → saneo de paginación (page≥1, pageSize 1..100)
///      → re-validación de filtros (accion ∈ enum → 422; desde≤hasta → 422; hasta≤NOW → 422)
///      → CountAsync (total==0 → página vacía sin SELECT) → GetPagedAsync → totalPages=ceil(total/pageSize)
///      → mapeo a LogAuditoriaResponse SIN JSONB (D3) → Serilog (RNF-023).
///   2. ObtenerPorIdAsync: D12 (403) → GetByIdAsync (null → NotFoundException 404) → mapeo a
///      LogAuditoriaDetalleResponse con valorAnterior/valorNuevo como string (ADR-003).
/// Ctor aprobado por spec: (ILogAuditoriaRepository, TenantContext) + overload con ILogger (D11).
/// </summary>
public class LogAuditoriaService : ILogAuditoriaService
{
    private const string RolSuperAdmin = "SuperAdmin";

    /// <summary>Enum accion_auditoria del DDL (06_MODELO_DATOS.md L46) — re-validación BLL (paso 3).</summary>
    private static readonly string[] AccionesValidas =
        ["CREATE", "UPDATE", "DELETE", "LOGIN", "LOGOUT", "ACTIVATE", "DEACTIVATE"];

    private readonly ILogAuditoriaRepository _repository;
    private readonly TenantContext _tenantContext; // D12: re-validación de rol; UserId para Serilog
    private readonly ILogger<LogAuditoriaService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public LogAuditoriaService(ILogAuditoriaRepository repository, TenantContext tenantContext)
        : this(repository, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public LogAuditoriaService(ILogAuditoriaRepository repository, TenantContext tenantContext, ILogger<LogAuditoriaService>? logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec §1 · ListarAsync: D12 (rol SuperAdmin → 403) → saneo de paginación
    /// (page ≥ 1, pageSize 1..100) → re-validación de filtros (accion ∈ enum → 422;
    /// desde ≤ hasta → 422; hasta ≤ NOW → 422) → CountAsync (total==0 → página vacía sin
    /// SELECT) → GetPagedAsync → totalPages = ceil(total/pageSize) → mapeo SIN JSONB (D3)
    /// → Serilog (RNF-023).</summary>
    public async Task<PagedResult<LogAuditoriaResponse>> ListarAsync(LogAuditoriaFiltrosRequest filtros, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol (el [Authorize(Roles)] del controller es la
        // primera capa; la BLL es la fuente de verdad — patrón HU-001..008).
        if (!string.Equals(_tenantContext.Rol, RolSuperAdmin, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el SuperAdmin puede consultar el log de auditoría");

        // Saneo de paginación (patrón HU-001 ListarAsync): page ≥ 1; pageSize 1..100 (>100 trunca a 100).
        var pagina = (filtros.Page ?? 1) < 1 ? 1 : (filtros.Page ?? 1);
        var tamanio = filtros.PageSize ?? 10;
        if (tamanio < 1) tamanio = 10;
        if (tamanio > 100) tamanio = 100;

        // Re-validación de filtros (fuente de verdad BLL, UX-04).
        if (filtros.Accion is not null && !AccionesValidas.Contains(filtros.Accion, StringComparer.Ordinal))
            throw new ValidacionException($"La acción '{filtros.Accion}' no es válida");

        if (filtros.Desde is not null && filtros.Hasta is not null && filtros.Desde > filtros.Hasta)
            throw new ValidacionException("La fecha inicial debe ser anterior o igual a la fecha final");

        if (filtros.Hasta is not null && filtros.Hasta > DateTimeOffset.UtcNow)
            throw new ValidacionException("La fecha final no puede estar en el futuro");

        var dto = new LogAuditoriaFiltrosDto
        {
            Page = pagina,
            PageSize = tamanio,
            TenantId = filtros.TenantId,
            UsuarioId = filtros.UsuarioId,
            Accion = filtros.Accion,
            Desde = filtros.Desde,
            Hasta = filtros.Hasta
        };

        // COUNT con filtros (DAL-L2). total == 0 → página vacía sin ejecutar el SELECT (patrón HU-001 paso 2).
        var total = await _repository.CountAsync(dto, ct);
        if (total == 0)
        {
            _logger?.LogInformation(
                "Log de auditoría consultado por {UserId}. Filtros: tenant={TenantId}, usuario={UsuarioId}, accion={Accion}, desde={Desde}, hasta={Hasta}. Resultado=0",
                _tenantContext.UserId, filtros.TenantId, filtros.UsuarioId, filtros.Accion, filtros.Desde, filtros.Hasta);

            return new PagedResult<LogAuditoriaResponse>
            {
                Items = [],
                Page = pagina,
                PageSize = tamanio,
                Total = 0,
                TotalPages = 0
            };
        }

        // SELECT paginado (DAL-L1) con offset = (page-1)*pageSize, ORDER BY created_at DESC.
        var items = await _repository.GetPagedAsync(dto, ct);
        var totalPages = (total + tamanio - 1) / tamanio; // ceil(total / pageSize)

        _logger?.LogInformation(
            "Log de auditoría consultado por {UserId}. Filtros: tenant={TenantId}, usuario={UsuarioId}, accion={Accion}, desde={Desde}, hasta={Hasta}. Total={Total}",
            _tenantContext.UserId, filtros.TenantId, filtros.UsuarioId, filtros.Accion, filtros.Desde, filtros.Hasta, total);

        // Mapeo a LogAuditoriaResponse SIN valor_anterior/valor_nuevo (D3).
        return new PagedResult<LogAuditoriaResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            Page = pagina,
            PageSize = tamanio,
            Total = total,
            TotalPages = totalPages
        };
    }

    /// <summary>Spec §2 · ObtenerPorIdAsync: D12 (403) → GetByIdAsync (null → NotFoundException
    /// 404) → mapeo a LogAuditoriaDetalleResponse con valorAnterior/valorNuevo como string (ADR-003).</summary>
    public async Task<LogAuditoriaDetalleResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol.
        if (!string.Equals(_tenantContext.Rol, RolSuperAdmin, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el SuperAdmin puede consultar el log de auditoría");

        var entrada = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"La entrada de auditoría '{id}' no existe");

        return MapToDetalleResponse(entrada);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Mapeo del listado (D3): SIN valor_anterior/valor_nuevo (los JSONB pueden ser
    /// grandes; el detalle GET /{id} los expone para inspección forense).</summary>
    private static LogAuditoriaResponse MapToResponse(LogAuditoriaEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        TenantNombre = e.TenantNombre,
        UsuarioId = e.UsuarioId,
        UsuarioNombre = e.UsuarioNombre,
        Accion = e.Accion,
        Entidad = e.Entidad,
        EntidadId = e.EntidadId,
        CreatedAt = e.CreatedAt
    };

    /// <summary>Mapeo del detalle (D3/ADR-003): valorAnterior/valorNuevo como JSON crudo (string).</summary>
    private static LogAuditoriaDetalleResponse MapToDetalleResponse(LogAuditoriaEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        TenantNombre = e.TenantNombre,
        UsuarioId = e.UsuarioId,
        UsuarioNombre = e.UsuarioNombre,
        Accion = e.Accion,
        Entidad = e.Entidad,
        EntidadId = e.EntidadId,
        ValorAnterior = e.ValorAnterior,
        ValorNuevo = e.ValorNuevo,
        CreatedAt = e.CreatedAt
    };
}
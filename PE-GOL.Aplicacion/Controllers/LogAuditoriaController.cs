using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de consulta del Log de Auditoría (HU-005 § UI).
/// Solo SuperAdmin (doble capa MVC + API). SOLO LECTURA (CA #3): sin acciones de
/// escritura. SEC-06: tenantId/usuarioId viajan como query params de filtro, nunca en body.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class LogAuditoriaController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<LogAuditoriaController> _logger;

    public LogAuditoriaController(IApiClient apiClient, ILogger<LogAuditoriaController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado paginado con filtros ────────────────────────────────────────

    public async Task<IActionResult> Index(int page = 1, int pageSize = 10,
        string? accion = null, string? desde = null, string? hasta = null, Guid? tenantId = null)
    {
        var tenants = await CargarTenantsAsync();

        // Saneo espejo BLL HU-005: page ≥ 1 · pageSize 1..100 (default 10).
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(accion))
            query["accion"] = accion;
        if (tenantId.HasValue)
            query["tenantId"] = tenantId.Value.ToString();
        if (DateTimeOffset.TryParse(desde, out var desdeOffset))
            query["desde"] = desdeOffset.ToString("o");
        if (DateTimeOffset.TryParse(hasta, out var hastaOffset))
            query["hasta"] = hastaOffset.ToString("o");

        try
        {
            var resultado = await _apiClient.GetAsync<PagedResult<LogAuditoriaResponse>>("/api/v1/log-auditoria", query);
            var modelo = new LogAuditoriaIndexViewModel
            {
                Items = resultado.Items,
                Page = resultado.Page,
                PageSize = resultado.PageSize,
                Total = resultado.Total,
                TotalPages = resultado.TotalPages,
                AccionFiltro = accion,
                DesdeFiltro = desdeOffset,
                HastaFiltro = hastaOffset,
                TenantIdFiltro = tenantId,
                Tenants = tenants
            };
            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            var modelo = new LogAuditoriaIndexViewModel
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 0,
                AccionFiltro = accion,
                DesdeFiltro = desdeOffset,
                HastaFiltro = hastaOffset,
                TenantIdFiltro = tenantId,
                Tenants = tenants
            };
            return View(modelo);
        }
    }

    // ─── Detalle (con JSON crudo) ────────────────────────────────────────────

    public async Task<IActionResult> Detalle(Guid id)
    {
        try
        {
            var detalle = await _apiClient.GetAsync<LogAuditoriaDetalleResponse>($"/api/v1/log-auditoria/{id}");
            return View(new LogAuditoriaDetalleViewModel { Detalle = detalle });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Catálogo de tenants para el filtro del SA (primeros 100).</summary>
    private async Task<List<TenantResponse>> CargarTenantsAsync()
    {
        try
        {
            var query = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = "100" };
            var resultado = await _apiClient.GetAsync<PagedResult<TenantResponse>>("/api/v1/tenants", query);
            return resultado.Items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de tenants para el filtro de auditoría.");
            return [];
        }
    }
}
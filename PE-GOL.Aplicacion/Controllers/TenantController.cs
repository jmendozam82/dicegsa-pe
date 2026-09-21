using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Gestión de Tenants (Spec HU-045 § Lógica del controlador MVC).
/// Solo SuperAdmin (doble capa: [Authorize(Roles="SuperAdmin")] MVC + re-validación por la API
/// vía JWT — Flag 3). SEC-06: el {id} de la ruta identifica al tenant gestionado; tenant_id
/// nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class TenantController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<TenantController> _logger;

    public TenantController(IApiClient apiClient, ILogger<TenantController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index(int page = 1, int pageSize = 10, string? estado = null, Guid? planId = null)
    {
        var planes = await CargarPlanesAsync();

        // Saneo (espejo BLL HU-001): page ≥ 1 · pageSize 1..100 (default 10).
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(estado))
            query["estado"] = estado;
        if (planId.HasValue)
            query["planId"] = planId.Value.ToString();

        try
        {
            var resultado = await _apiClient.GetAsync<PagedResult<TenantResponse>>("/api/v1/tenants", query);
            var modelo = new TenantsIndexViewModel
            {
                Items = resultado.Items,
                Page = resultado.Page,
                PageSize = resultado.PageSize,
                Total = resultado.Total,
                TotalPages = resultado.TotalPages,
                EstadoFiltro = estado,
                PlanIdFiltro = planId,
                Planes = planes
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
            var modelo = new TenantsIndexViewModel
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 0,
                EstadoFiltro = estado,
                PlanIdFiltro = planId,
                Planes = planes
            };
            return View(modelo);
        }
    }

    // ─── Crear ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Create()
    {
        var planes = await CargarPlanesAsync();
        return View(new TenantFormViewModel { Planes = planes });
    }

    [HttpPost]
    public async Task<IActionResult> Create(TenantCreateRequest request)
    {
        try
        {
            var tenant = await _apiClient.PostAsync<TenantCreateRequest, TenantResponse>("/api/v1/tenants", request);
            TempData["Success"] = $"Tenant '{tenant.Nombre}' creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 400/422: errores del servidor visibles (CA #5) → ModelState + re-render con catálogo.
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var planes = await CargarPlanesAsync();
            return View(new TenantFormViewModel
            {
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                PlanId = request.PlanId,
                LogoUrl = request.LogoUrl,
                Eslogan = request.Eslogan,
                ZonaHoraria = request.ZonaHoraria,
                Planes = planes
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(Guid id)
    {
        try
        {
            var tenant = await _apiClient.GetAsync<TenantResponse>($"/api/v1/tenants/{id}");
            var planes = await CargarPlanesAsync();
            return View(new TenantFormViewModel
            {
                Id = tenant.Id,
                Nombre = tenant.Nombre,
                Descripcion = tenant.Descripcion,
                PlanId = tenant.PlanId,
                LogoUrl = tenant.LogoUrl,
                Eslogan = tenant.Eslogan,
                ZonaHoraria = tenant.ZonaHoraria,
                Estado = tenant.Estado,
                Planes = planes
            });
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

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, TenantUpdateRequest request)
    {
        try
        {
            var tenant = await _apiClient.PutAsync<TenantUpdateRequest, TenantResponse>($"/api/v1/tenants/{id}", request);
            TempData["Success"] = $"Tenant '{tenant.Nombre}' actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var planes = await CargarPlanesAsync();
            return View(new TenantFormViewModel
            {
                Id = id,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                PlanId = request.PlanId,
                LogoUrl = request.LogoUrl,
                Eslogan = request.Eslogan,
                ZonaHoraria = request.ZonaHoraria,
                Planes = planes
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Activar / Desactivar ────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Activar(Guid id)
    {
        try
        {
            await _apiClient.PostAsync<TenantResponse>($"/api/v1/tenants/{id}/activar");
            TempData["Success"] = "Tenant activado.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 422 (ya activo) → flash de error con el mensaje del servidor.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Desactivar(Guid id)
    {
        try
        {
            await _apiClient.PostAsync<TenantResponse>($"/api/v1/tenants/{id}/desactivar");
            TempData["Success"] = "Tenant desactivado. Sus usuarios no podrán iniciar sesión.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 422 (ya inactivo) → flash de error con el mensaje del servidor.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Catálogo de planes vía GET /api/v1/planes (HU-002, sin paginación — D1).
    /// Si falla → log warning + catálogo vacío (el listado sigue funcionando).
    /// </summary>
    private async Task<List<PlanResponse>> CargarPlanesAsync()
    {
        try
        {
            return await _apiClient.GetAsync<List<PlanResponse>>("/api/v1/planes");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de planes (GET /api/v1/planes).");
            return [];
        }
    }
}
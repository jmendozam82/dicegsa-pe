using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Gestión de Planes de Suscripción (HU-002 § UI).
/// Solo SuperAdmin (doble capa: [Authorize(Roles="SuperAdmin")] MVC + re-validación
/// por la API vía JWT). SEC-06: sin parámetros tenant en body/query.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class PlanesController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<PlanesController> _logger;

    public PlanesController(IApiClient apiClient, ILogger<PlanesController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado (sin paginación, D1) ────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        try
        {
            var planes = await _apiClient.GetAsync<List<PlanResponse>>("/api/v1/planes");
            return View(new PlanesIndexViewModel { Items = planes, Total = planes.Count });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new PlanesIndexViewModel());
        }
    }

    // ─── Crear ───────────────────────────────────────────────────────────────

    public IActionResult Create()
        => View(new PlanFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(PlanCreateRequest request)
    {
        try
        {
            var plan = await _apiClient.PostAsync<PlanCreateRequest, PlanResponse>("/api/v1/planes", request);
            TempData["Success"] = $"Plan '{plan.Nombre}' creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new PlanFormViewModel
            {
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                MaxAreas = request.MaxAreas,
                MaxUsuarios = request.MaxUsuarios,
                MaxCiclosActivos = request.MaxCiclosActivos
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
            var plan = await _apiClient.GetAsync<PlanResponse>($"/api/v1/planes/{id}");
            return View(new PlanFormViewModel
            {
                Id = plan.Id,
                Nombre = plan.Nombre,
                Descripcion = plan.Descripcion,
                MaxAreas = plan.MaxAreas,
                MaxUsuarios = plan.MaxUsuarios,
                MaxCiclosActivos = plan.MaxCiclosActivos
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
    public async Task<IActionResult> Edit(Guid id, PlanUpdateRequest request)
    {
        try
        {
            var plan = await _apiClient.PutAsync<PlanUpdateRequest, PlanResponse>($"/api/v1/planes/{id}", request);
            TempData["Success"] = $"Plan '{plan.Nombre}' actualizado correctamente.";
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
            return View(new PlanFormViewModel
            {
                Id = id,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                MaxAreas = request.MaxAreas,
                MaxUsuarios = request.MaxUsuarios,
                MaxCiclosActivos = request.MaxCiclosActivos
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (DELETE físico, D2) ────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid id)
    {
        try
        {
            await _apiClient.DeleteAsync<object>($"/api/v1/planes/{id}");
            TempData["Success"] = "Plan eliminado.";
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        return RedirectToAction(nameof(Index));
    }
}
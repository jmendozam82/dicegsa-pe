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
/// UI de Gestión de Ciclos (Spec HU-007 § UI) + Umbrales de semáforo (Spec HU-008 § UI).
/// Roles (RN-007): AdminTenant → crear/editar/activar/clonar/umbrales; Gerente → cerrar;
/// JefeArea → solo lectura. Formularios de ciclo respetan el estado (RC-12/RN-004):
/// Borrador editable, Activo/Cerrado solo lectura con badge.
/// SEC-06: {id}/{cicloId} vienen de la ruta; tenant_id nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class CicloController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<CicloController> _logger;

    public CicloController(IApiClient apiClient, ILogger<CicloController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado (GET /Ciclos) ───────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            return View(new CiclosIndexViewModel
            {
                Items = ciclos,
                EsAdmin = User.IsInRole("AdminTenant"),
                EsGerente = User.IsInRole("Gerente")
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new CiclosIndexViewModel
            {
                EsAdmin = User.IsInRole("AdminTenant"),
                EsGerente = User.IsInRole("Gerente")
            });
        }
    }

    // ─── Crear (GET/POST /Ciclos/Crear) — solo AdminTenant ───────────────────

    [Authorize(Roles = "AdminTenant")]
    public IActionResult Crear()
        => View(new CicloFormViewModel { AñoFiscal = DateTime.Now.Year, MesInicio = 1 });

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Crear(CicloCreateRequest request)
    {
        try
        {
            var ciclo = await _apiClient.PostAsync<CicloCreateRequest, CicloResponse>("/api/v1/ciclos", request);
            TempData["Success"] = $"Ciclo '{ciclo.Nombre}' ({ciclo.AñoFiscal}) creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new CicloFormViewModel
            {
                Nombre = request.Nombre,
                AñoFiscal = request.AñoFiscal,
                MesInicio = request.MesInicio
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /Ciclos/Editar/{id}) — solo AdminTenant ────────────

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Editar(Guid id)
    {
        try
        {
            var ciclo = await _apiClient.GetAsync<CicloResponse>($"/api/v1/ciclos/{id}");
            return View(new CicloFormViewModel
            {
                Id = ciclo.Id,
                Nombre = ciclo.Nombre,
                AñoFiscal = ciclo.AñoFiscal,
                MesInicio = ciclo.MesInicio,
                Estado = ciclo.Estado
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

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Editar(Guid id, CicloUpdateRequest request)
    {
        try
        {
            var ciclo = await _apiClient.PutAsync<CicloUpdateRequest, CicloResponse>($"/api/v1/ciclos/{id}", request);
            TempData["Success"] = $"Ciclo '{ciclo.Nombre}' actualizado correctamente.";
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
            return View(new CicloFormViewModel
            {
                Id = id,
                Nombre = request.Nombre,
                AñoFiscal = request.AñoFiscal,
                MesInicio = request.MesInicio
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Activar (POST /Ciclos/Activar/{id}) — solo AdminTenant ──────────────

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Activar(Guid id)
    {
        try
        {
            var ciclo = await _apiClient.PostAsync<CicloResponse>($"/api/v1/ciclos/{id}/activar");
            TempData["Success"] = $"Ciclo '{ciclo.Nombre}' activado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 422 (ya activo / RC-01 / tope del plan) → flash con el mensaje del servidor.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Cerrar (POST /Ciclos/Cerrar/{id}) — solo Gerente ────────────────────

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Cerrar(Guid id)
    {
        try
        {
            var ciclo = await _apiClient.PostAsync<CicloResponse>($"/api/v1/ciclos/{id}/cerrar");
            TempData["Success"] = $"Ciclo '{ciclo.Nombre}' cerrado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Clonar (GET/POST /Ciclos/Clonar/{id}) — solo AdminTenant ────────────

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Clonar(Guid id)
    {
        try
        {
            var origen = await _apiClient.GetAsync<CicloResponse>($"/api/v1/ciclos/{id}");
            return View(new CicloFormViewModel
            {
                Id = origen.Id,
                OrigenNombre = origen.Nombre,
                AñoFiscal = origen.AñoFiscal + 1,
                MesInicio = origen.MesInicio
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

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Clonar(Guid id, ClonarCicloRequest request)
    {
        try
        {
            var ciclo = await _apiClient.PostAsync<ClonarCicloRequest, CicloResponse>($"/api/v1/ciclos/{id}/clonar", request);
            TempData["Success"] = $"Ciclo '{ciclo.Nombre}' clonado correctamente (umbrales copiados del origen).";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new CicloFormViewModel
            {
                Id = id,
                Nombre = request.Nombre,
                AñoFiscal = request.AñoFiscal,
                MesInicio = request.MesInicio
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Umbrales (GET/POST /Ciclos/Umbrales/{id}) — HU-008 ──────────────────

    public async Task<IActionResult> Umbrales(Guid id)
    {
        try
        {
            var ciclo = await _apiClient.GetAsync<CicloResponse>($"/api/v1/ciclos/{id}");
            var umbrales = await _apiClient.GetAsync<UmbralesCicloResponse>($"/api/v1/ciclos/{id}/umbrales");
            return View(new UmbralesViewModel
            {
                CicloId = ciclo.Id,
                CicloNombre = ciclo.Nombre,
                CicloEstado = ciclo.Estado,
                KpiVerde = umbrales.Kpi.UmbralVerde,
                KpiAmarillo = umbrales.Kpi.UmbralAmarillo,
                PlanVerde = umbrales.PlanAccion.UmbralVerde,
                PlanAmarillo = umbrales.PlanAccion.UmbralAmarillo,
                EsEditable = User.IsInRole("AdminTenant") && ciclo.Estado == "Borrador"
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

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Umbrales(Guid id, UmbralesUpdateRequest request)
    {
        try
        {
            var umbrales = await _apiClient.PutAsync<UmbralesUpdateRequest, UmbralesCicloResponse>(
                $"/api/v1/ciclos/{id}/umbrales", request);
            TempData["Success"] = "Umbrales de semáforo actualizados correctamente.";
            return RedirectToAction(nameof(Umbrales), new { id });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new UmbralesViewModel
            {
                CicloId = id,
                KpiVerde = request.Kpi.UmbralVerde,
                KpiAmarillo = request.Kpi.UmbralAmarillo,
                PlanVerde = request.PlanAccion.UmbralVerde,
                PlanAmarillo = request.PlanAccion.UmbralAmarillo,
                EsEditable = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }
}
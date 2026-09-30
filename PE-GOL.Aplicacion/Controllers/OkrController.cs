using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de OKRs del área (Spec HU-024). JefeArea-only (SEC-07).
/// Consume la API interna en /api/v1/okrs (SEC-06: tenant_id/area_id/ciclo_id nunca en query/body).
/// </summary>
[Authorize]
public class OkrController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<OkrController> _logger;

    public OkrController(IApiClient apiClient, ILogger<OkrController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado JEF (GET /Okr) — solo JefeArea ─────────────────────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Index()
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
            var modelo = new OkrIndexViewModel
            {
                HayCicloActivo = cicloActivo is not null,
                CicloNombre = cicloActivo?.Nombre
            };

            if (cicloActivo is not null)
            {
                modelo.Items = await _apiClient.GetAsync<List<OkrResponse>>("/api/v1/okrs");
            }

            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new OkrIndexViewModel());
        }
    }

    // ─── Crear (GET/POST /Okr/Crear) — solo JefeArea ─────────────────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Crear()
    {
        try
        {
            var pilares = await ObtenerPilaresCicloActivoAsync();
            if (pilares is null)
            {
                TempData["Error"] = "No hay un ciclo activo para el tenant.";
                return RedirectToAction(nameof(Index));
            }

            return View(new OkrFormViewModel { Pilares = pilares });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Crear(OkrCreateRequest request)
    {
        try
        {
            var okr = await _apiClient.PostAsync<OkrCreateRequest, OkrResponse>("/api/v1/okrs", request);
            TempData["Success"] = $"OKR '{okr.Codigo}' creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var pilares = await ObtenerPilaresCicloActivoAsync() ?? [];
            return View(new OkrFormViewModel
            {
                Descripcion = request.Descripcion,
                PilarId = request.PilarId,
                Pilares = pilares
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /Okr/Editar/{id}) — solo JefeArea ──────────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Editar(Guid id)
    {
        try
        {
            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{id}");
            var pilares = await ObtenerPilaresCicloActivoAsync() ?? [];
            return View(new OkrFormViewModel
            {
                OkrId = okr.Id,
                Codigo = okr.Codigo,
                Descripcion = okr.Descripcion,
                PilarId = okr.PilarId,
                Pilares = pilares,
                EsEdicion = true
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

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Editar(Guid id, OkrUpdateRequest request)
    {
        try
        {
            var okr = await _apiClient.PutAsync<OkrUpdateRequest, OkrResponse>($"/api/v1/okrs/{id}", request);
            TempData["Success"] = $"OKR '{okr.Codigo}' actualizado correctamente.";
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
            var pilares = await ObtenerPilaresCicloActivoAsync() ?? [];
            return View(new OkrFormViewModel
            {
                OkrId = id,
                Codigo = null,
                Descripcion = request.Descripcion,
                PilarId = request.PilarId,
                Pilares = pilares,
                EsEdicion = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (POST /Okr/Eliminar/{id}) — solo JefeArea ──────────────────

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid id)
    {
        try
        {
            var okr = await _apiClient.DeleteAsync<OkrResponse>($"/api/v1/okrs/{id}");
            TempData["Success"] = $"OKR '{okr.Codigo}' eliminado correctamente.";
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

    // ─── Helper: pilares del ciclo activo (RF-017 — select de pilares en el formulario) ──

    private async Task<List<PilarResponse>?> ObtenerPilaresCicloActivoAsync()
    {
        var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
        var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
        if (cicloActivo is null)
            return null;

        return await _apiClient.GetAsync<List<PilarResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/pilares");
    }
}

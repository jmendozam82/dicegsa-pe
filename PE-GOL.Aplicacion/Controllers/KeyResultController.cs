using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Key Results de un OKR (Spec HU-025). JefeArea-only (SEC-07).
/// Consume la API interna en /api/v1/okrs/{okrId}/key-results (SEC-06: tenant_id/area_id/ciclo_id/okr_id nunca en query/body).
/// </summary>
[Authorize]
public class KeyResultController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<KeyResultController> _logger;

    public KeyResultController(IApiClient apiClient, ILogger<KeyResultController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado JEF (GET /KeyResult/Index/{okrId}) — solo JefeArea ──────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Index(Guid okrId)
    {
        try
        {
            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");
            var krs = await _apiClient.GetAsync<List<KeyResultResponse>>($"/api/v1/okrs/{okrId}/key-results");

            var modelo = new KeyResultIndexViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                OkrDescripcion = okr.Descripcion,
                PilarNombre = okr.PilarNombre,
                Items = krs,
                SumaPesos = krs.Sum(kr => kr.Peso),
                Cantidad = krs.Count
            };

            return View(modelo);
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            var modelo = new KeyResultIndexViewModel
            {
                OkrId = okrId,
                OkrNoExiste = true
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
            return View(new KeyResultIndexViewModel { OkrId = okrId });
        }
    }

    // ─── Crear (GET/POST /KeyResult/Crear/{okrId}) — solo JefeArea ───────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Crear(Guid okrId)
    {
        try
        {
            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");
            var krs = await _apiClient.GetAsync<List<KeyResultResponse>>($"/api/v1/okrs/{okrId}/key-results");

            var sumaPesos = krs.Sum(kr => kr.Peso);
            var pesoRestante = 1.000m - sumaPesos;

            return View(new KeyResultFormViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                Peso = pesoRestante > 0 ? pesoRestante : 0.001m,
                SumaPesosActual = sumaPesos,
                PesoRestanteSugerido = pesoRestante > 0 ? pesoRestante : 0.001m
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
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { okrId });
        }
    }

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Crear(Guid okrId, KeyResultCreateRequest request)
    {
        try
        {
            var kr = await _apiClient.PostAsync<KeyResultCreateRequest, KeyResultResponse>(
                $"/api/v1/okrs/{okrId}/key-results", request);
            TempData["Success"] = $"Key Result '{kr.Codigo}' creado correctamente.";
            return RedirectToAction(nameof(Index), new { okrId });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");
            var krs = await _apiClient.GetAsync<List<KeyResultResponse>>($"/api/v1/okrs/{okrId}/key-results");
            var sumaPesos = krs.Sum(kr => kr.Peso);

            return View(new KeyResultFormViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                Descripcion = request.Descripcion,
                Peso = request.Peso,
                SumaPesosActual = sumaPesos,
                PesoRestanteSugerido = 1.000m - sumaPesos
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /KeyResult/Editar/{okrId}/{id}) — solo JefeArea ────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Editar(Guid okrId, Guid id)
    {
        try
        {
            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");
            var kr = await _apiClient.GetAsync<KeyResultResponse>($"/api/v1/okrs/{okrId}/key-results/{id}");

            return View(new KeyResultFormViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                KeyResultId = kr.Id,
                Codigo = kr.Codigo,
                Descripcion = kr.Descripcion,
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
    public async Task<IActionResult> Editar(Guid okrId, Guid id, KeyResultUpdateRequest request)
    {
        try
        {
            var kr = await _apiClient.PutAsync<KeyResultUpdateRequest, KeyResultResponse>(
                $"/api/v1/okrs/{okrId}/key-results/{id}", request);
            TempData["Success"] = $"Key Result '{kr.Codigo}' actualizado correctamente.";
            return RedirectToAction(nameof(Index), new { okrId });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");

            return View(new KeyResultFormViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                KeyResultId = id,
                Descripcion = request.Descripcion,
                EsEdicion = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (POST /KeyResult/Eliminar/{okrId}/{id}) — solo JefeArea ────

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid okrId, Guid id)
    {
        try
        {
            var kr = await _apiClient.DeleteAsync<KeyResultResponse>(
                $"/api/v1/okrs/{okrId}/key-results/{id}");
            TempData["Success"] = $"Key Result '{kr.Codigo}' eliminado correctamente.";
            return RedirectToAction(nameof(Index), new { okrId });
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { okrId });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── ActualizarPesos (POST /KeyResult/ActualizarPesos/{okrId}) — solo JefeArea ──

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> ActualizarPesos(Guid okrId, KeyResultPesosUpdateRequest request)
    {
        try
        {
            await _apiClient.PutAsync<KeyResultPesosUpdateRequest, object>(
                $"/api/v1/okrs/{okrId}/key-results/pesos", request);
            TempData["Success"] = "Reparto de pesos actualizado correctamente.";
            return RedirectToAction(nameof(Index), new { okrId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            var okr = await _apiClient.GetAsync<OkrResponse>($"/api/v1/okrs/{okrId}");
            var krs = await _apiClient.GetAsync<List<KeyResultResponse>>($"/api/v1/okrs/{okrId}/key-results");

            var modelo = new KeyResultIndexViewModel
            {
                OkrId = okrId,
                OkrCodigo = okr.Codigo,
                OkrDescripcion = okr.Descripcion,
                PilarNombre = okr.PilarNombre,
                Items = krs,
                SumaPesos = krs.Sum(kr => kr.Peso),
                Cantidad = krs.Count
            };

            return View(nameof(Index), modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }
}

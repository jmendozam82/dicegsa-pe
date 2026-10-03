using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de valores mensuales de KRs de un OKR (Spec HU-026). JefeArea-only (SEC-07).
/// Consume la API interna en /api/v1/okrs/{okrId}/valores-mensuales (ADR-016: el navegador
/// nunca ve una ruta /api/v1 ni el JWT; tenant_id/area_id/ciclo_id nunca en query/body).
/// </summary>
[Authorize]
public class ValorMensualKrController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<ValorMensualKrController> _logger;

    public ValorMensualKrController(IApiClient apiClient, ILogger<ValorMensualKrController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Grilla JEF (GET /ValorMensualKr/Index/{okrId}) — solo JefeArea ──────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Index(Guid okrId)
    {
        try
        {
            var grilla = await _apiClient.GetAsync<ValorMensualKrGrillaResponse>(
                $"/api/v1/okrs/{okrId}/valores-mensuales");

            var modelo = new ValorMensualKrIndexViewModel
            {
                OkrId = grilla.OkrId,
                OkrCodigo = grilla.OkrCodigo,
                OkrDescripcion = grilla.OkrDescripcion,
                PilarNombre = grilla.PilarNombre,
                KRs = grilla.KRs,
                UmbralVerde = grilla.UmbralVerde,
                UmbralAmarillo = grilla.UmbralAmarillo,
                MesActual = grilla.MesActual,
                MesMinEditable = grilla.MesMinEditable,
                MesMaxEditable = grilla.MesMaxEditable,
                FechaNegocio = grilla.FechaNegocio,
                MesesEditables = grilla.MesesEditables,
                CalculoOkr = grilla.CalculoOkr,
                OkrNoExiste = false
            };

            return View(modelo);
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            var modelo = new ValorMensualKrIndexViewModel
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
            _logger.LogError(ex, "Error al obtener la grilla de valores mensuales del OKR {OkrId}.", okrId);
            TempData["Error"] = ex.Message;
            return View(new ValorMensualKrIndexViewModel { OkrId = okrId });
        }
    }

    // ─── Guardar (POST /ValorMensualKr/Guardar/{okrId}/{keyResultId}) — solo JefeArea ──

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Guardar(Guid okrId, Guid keyResultId, ValorMensualKrUpdateRequest request)
    {
        try
        {
            var resultado = await _apiClient.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                $"/api/v1/okrs/{okrId}/key-results/{keyResultId}/valores", request);
            return Json(resultado);
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex) when (ex.StatusCode == 422)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return Json(new { success = false, message = ex.Message, errores = ex.Errors });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Error al guardar valores mensuales del KR {KeyResultId}.", keyResultId);
            TempData["Error"] = ex.Message;
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─── Eliminar valor (POST /ValorMensualKr/EliminarValor/{okrId}/{keyResultId}) — solo JefeArea ──

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> EliminarValor(Guid okrId, Guid keyResultId, int mes)
    {
        try
        {
            var resultado = await _apiClient.DeleteAsync<ValorMensualKrGuardarResponse>(
                $"/api/v1/okrs/{okrId}/key-results/{keyResultId}/valores/{mes}");
            return Json(resultado);
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Error al eliminar el valor del mes {mes} del KR {KeyResultId}.", mes, keyResultId);
            TempData["Error"] = ex.Message;
            return Json(new { success = false, message = ex.Message });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Exportar Excel (GET /ValorMensualKr/Exportar/{okrId}) — solo JefeArea ──

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Exportar(Guid okrId)
    {
        try
        {
            var bytes = await _apiClient.GetBytesAsync(
                $"/api/v1/okrs/{okrId}/valores-mensuales/exportar");
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"valores-mensuales-okr-{okrId}.xlsx");
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
            _logger.LogError(ex, "Error al exportar valores mensuales del OKR {OkrId}.", okrId);
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { okrId });
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.Utility.Exceptions;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de la Vista Consolidada del Plan (Spec HU-023 § UI — Consolidado.shtml).
/// Solo lectura, solo Gerente (RN-006). La vista consume la API interna vía ApiClient.
/// SEC-06: sin query params — ni tenant_id, ni ciclo_id; la API los deduce del JWT.
/// </summary>
[Authorize(Roles = "Gerente")]
public class PlanController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly SesionService _sesionService;
    private readonly ILogger<PlanController> _logger;

    public PlanController(IApiClient apiClient, SesionService sesionService, ILogger<PlanController> logger)
    {
        _apiClient = apiClient;
        _sesionService = sesionService;
        _logger = logger;
    }

    /// <summary>
    /// Vista Consolidada del Plan de Acción (HU-023).
    /// GET /Plan/Consolidado
    /// Muestra la tabla consolidada de todas las áreas con filtros, resumen por status
    /// y exportación a Excel. Solo lectura, solo Gerente.
    /// </summary>
    public async Task<IActionResult> Consolidado()
    {
        try
        {
            // Cargar áreas y CGs para los filtros (el Gerente ve todas las áreas)
            var areas = await _apiClient.GetAsync<List<AreaResponse>>("/api/v1/areas");
            var objetivosCg = await _apiClient.GetAsync<List<ObjetivoCgResponse>>("/api/v1/objetivos-cg");

            // Cargar el consolidado (sin filtros, página 1)
            var consolidado = await _apiClient.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado");

            var token = _sesionService.ObtenerAccessToken();

            return View(new PlanConsolidadoViewModel
            {
                HayCicloActivo = true,
                HayAcciones = consolidado.Items.Count > 0,
                CicloNombre = consolidado.Items.FirstOrDefault()?.CicloNombre,
                Resumen = consolidado.Resumen,
                Items = consolidado.Items,
                Paginacion = consolidado.Paginacion,
                AccessToken = token ?? string.Empty,
                Areas = areas ?? new List<AreaResponse>(),
                ObjetivosCg = objetivosCg ?? new List<ObjetivoCgResponse>()
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            // Sin ciclo activo: la vista se RENDERIZA con su empty state (UX-05)
            _logger.LogInformation("Vista Consolidado sin ciclo activo: {Mensaje}", ex.Message);
            return View(new PlanConsolidadoViewModel { HayCicloActivo = false, HayAcciones = false });
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Error al cargar la vista Consolidado");
            TempData["Error"] = ex.Message;
            return View(new PlanConsolidadoViewModel { HayCicloActivo = false, HayAcciones = false });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }
}

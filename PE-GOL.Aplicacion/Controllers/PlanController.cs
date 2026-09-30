using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.Utility.Exceptions;
using PE_GOL.DTO.Requests.PlanOperativo;
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
    ///
    /// Contrato de llamadas (spec v2 § UI § 3):
    ///   1) GET /api/v1/ciclos → FirstOrDefault(Estado == "Activo") determina HayCicloActivo.
    ///   2) Solo si hay ciclo activo: áreas, CGs consolidados y datos del plan.
    ///   Cualquier ApiClientException (incluido 404 de catálogo) es error de carga, no "sin ciclo activo".
    /// </summary>
    public async Task<IActionResult> Consolidado()
    {
        var modelo = new PlanConsolidadoViewModel();

        try
        {
            // Paso 1: resolver ciclo activo (RC-01). Único determinante de HayCicloActivo.
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos?.FirstOrDefault(c => c.Estado == "Activo");
            modelo.HayCicloActivo = cicloActivo is not null;
            modelo.CicloNombre = cicloActivo?.Nombre;

            if (cicloActivo is null)
            {
                // Sin ciclo activo: empty state de ciclo (UX-05), sin error, sin llamadas 2-4.
                return View(modelo);
            }

            // Pasos 2-4: catálogos y consolidado (solo cuando hay ciclo activo).
            var areas = await _apiClient.GetAsync<List<AreaResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/areas");
            var objetivosCg = await _apiClient.GetAsync<List<ObjetivoCgConsolidadoResponse>>("/api/v1/objetivos-cg/consolidado");
            var consolidado = await _apiClient.GetAsync<ConsolidadoResponse>("/api/v1/planes/consolidado");

            // ADR-016: el JWT no se inyecta en la vista; el proxy server-side lo adjunta
            // desde la sesión al llamar a la API interna.
            modelo.HayAcciones = consolidado.Items.Count > 0;
            modelo.Resumen = consolidado.Resumen;
            modelo.Items = consolidado.Items;
            modelo.Paginacion = consolidado.Paginacion;
            modelo.Areas = areas ?? new List<AreaResponse>();
            modelo.ObjetivosCg = objetivosCg ?? new List<ObjetivoCgConsolidadoResponse>();

            return View(modelo);
        }
        catch (ApiClientException ex)
        {
            // Spec v2 § UI § 7: cualquier ApiClientException es error de carga genuino.
            // Ningún 404 se etiqueta como "sin ciclo activo"; HayCicloActivo conserva su valor.
            _logger.LogError(ex, "Error al cargar la vista Consolidado");
            TempData["Error"] = ex.Message;
            modelo.ErrorCarga = true;
            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    /// <summary>
    /// Proxy MVC para el endpoint de datos consolidados (Defecto E, ADR-016).
    /// GET /Plan/ConsolidadoDatos
    /// Reenvía los filtros a GET /api/v1/planes/consolidado y devuelve el payload JSON íntegro.
    /// El JWT viaja server-side desde la sesión; el navegador no lo envía (SEC-01/ADR-016).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ConsolidadoDatos([FromQuery] FiltrosConsolidadoRequest filtros)
    {
        try
        {
            var query = ConstruirQueryDeFiltros(filtros);
            var payload = await _apiClient.GetAsync<ConsolidadoResponse>("/api/v1/planes/consolidado", query);
            return new ObjectResult(payload) { StatusCode = 200 };
        }
        catch (ApiClientException ex)
        {
            // ADR-016: los detalles internos (URL, JWT, status crudo) van solo al log del servidor.
            // Al navegador se devuelve el mismo código HTTP con un mensaje genérico.
            _logger.LogError(ex, "Error al consultar el plan consolidado (proxy MVC)");
            return new ObjectResult("Error al consultar el plan consolidado") { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            // Endpoint consumido por fetch: un 302 se seguiría de forma transparente y el JS
            // recibiría el HTML del login como 200. Devolvemos 401 para que el JS redirija.
            return Unauthorized();
        }
    }

    /// <summary>
    /// Proxy MVC para la exportación a Excel (Defecto E, ADR-016).
    /// GET /Plan/ConsolidadoExportar
    /// Reenvía los filtros a GET /api/v1/planes/consolidado/exportar y devuelve el archivo XLSX.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ConsolidadoExportar([FromQuery] FiltrosConsolidadoRequest filtros)
    {
        try
        {
            var query = ConstruirQueryDeFiltros(filtros);
            var bytes = await _apiClient.GetBytesAsync("/api/v1/planes/consolidado/exportar", query);
            return new FileContentResult(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = "PlanConsolidado_Export.xlsx"
            };
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Error al exportar el plan consolidado (proxy MVC)");
            return new ObjectResult("Error al exportar el plan consolidado") { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    /// <summary>
    /// Construye el query string con whitelist SEC-06 para reenviar a la API.
    /// Solo los 9 parámetros del contrato viajan; nunca tenant_id/tenantId.
    /// </summary>
    private static Dictionary<string, string?> ConstruirQueryDeFiltros(FiltrosConsolidadoRequest filtros)
    {
        var query = new Dictionary<string, string?>();

        if (filtros.AreaId.HasValue)
            query["areaId"] = filtros.AreaId.Value.ToString();

        if (filtros.CgId.HasValue)
            query["cgId"] = filtros.CgId.Value.ToString();

        if (!string.IsNullOrEmpty(filtros.Status))
            query["status"] = filtros.Status;

        if (!string.IsNullOrEmpty(filtros.Clasificacion))
            query["clasificacion"] = filtros.Clasificacion;

        if (!string.IsNullOrEmpty(filtros.Tipo))
            query["tipo"] = filtros.Tipo;

        if (filtros.FechaDesde.HasValue)
            query["fechaDesde"] = filtros.FechaDesde.Value.ToString("yyyy-MM-dd");

        if (filtros.FechaHasta.HasValue)
            query["fechaHasta"] = filtros.FechaHasta.Value.ToString("yyyy-MM-dd");

        // El JS siempre envía page/pageSize; los reenviamos para mantener la paginación server-side.
        query["page"] = filtros.Page.ToString();
        query["pageSize"] = filtros.PageSize.ToString();

        return query;
    }
}

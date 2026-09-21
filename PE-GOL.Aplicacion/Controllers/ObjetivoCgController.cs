using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Objetivos CG (Spec HU-018 § UI — Consolidado.cshtml para Gerente; HU-017 § UI —
/// Index/Crear/Editar.cshtml para JefeArea).
/// Gerente: GET /api/v1/objetivos-cg/consolidado (ruta standalone, D-B — visibilidad global).
/// JefeArea: CRUD de CGs de SU área (SEC-07 — la API filtra por AreaId del JWT; SEC-06:
/// tenant_id/area_id nunca viajan en body/query). CA #3 (HU-017): la pantalla de creación
/// muestra los ObjetivoQ1..Q4 del pilar seleccionado como referencia contextual (GET /pilares — HU-014).
/// </summary>
[Authorize]
public class ObjetivoCgController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<ObjetivoCgController> _logger;

    public ObjetivoCgController(IApiClient apiClient, ILogger<ObjetivoCgController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Consolidado (GET /ObjetivoCg/Consolidado?AreaId=&PilarId=&Trimestre=&Semaforo=) ──

    [Authorize(Roles = "Gerente")]
    public async Task<IActionResult> Consolidado(ObjetivoCgFilterRequest filtros)
    {
        try
        {
            var modelo = new ObjetivoCgConsolidadoViewModel { Filtros = filtros };

            // Ciclo activo del tenant (para catálogos de filtro y contexto del encabezado).
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
            modelo.HayCicloActivo = cicloActivo is not null;
            modelo.CicloNombre = cicloActivo?.Nombre;

            if (cicloActivo is not null)
            {
                var areas = await _apiClient.GetAsync<List<AreaResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/areas");
                var pilares = await _apiClient.GetAsync<List<PilarResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/pilares");
                modelo.Areas = areas;
                modelo.Pilares = pilares;

                var query = new Dictionary<string, string?>
                {
                    ["AreaId"] = filtros.AreaId?.ToString(),
                    ["PilarId"] = filtros.PilarId?.ToString(),
                    ["Trimestre"] = filtros.Trimestre,
                    ["Semaforo"] = filtros.Semaforo
                };
                modelo.Items = await _apiClient.GetAsync<List<ObjetivoCgConsolidadoResponse>>(
                    "/api/v1/objetivos-cg/consolidado", query);
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
            return View(new ObjetivoCgConsolidadoViewModel { Filtros = filtros });
        }
    }

    // ─── Listado JEF (GET /ObjetivoCg) — solo JefeArea ───────────────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Index()
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
            var modelo = new ObjetivoCgIndexViewModel
            {
                HayCicloActivo = cicloActivo is not null,
                CicloNombre = cicloActivo?.Nombre
            };

            if (cicloActivo is not null)
            {
                // SEC-07: la API filtra por AreaId del JWT — la UI solo muestra lo que devuelve.
                modelo.Items = await _apiClient.GetAsync<List<ObjetivoCgResponse>>("/api/v1/objetivos-cg");
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
            return View(new ObjetivoCgIndexViewModel());
        }
    }

    // ─── Crear (GET/POST /ObjetivoCg/Crear) — solo JefeArea ──────────────────

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

            return View(new ObjetivoCgFormViewModel { Pilares = pilares });
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
    public async Task<IActionResult> Crear(ObjetivoCgCreateRequest request)
    {
        try
        {
            var cg = await _apiClient.PostAsync<ObjetivoCgCreateRequest, ObjetivoCgResponse>(
                "/api/v1/objetivos-cg", request);
            TempData["Success"] = $"Objetivo CG '{cg.Codigo}' creado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var pilares = await ObtenerPilaresCicloActivoAsync() ?? [];
            return View(new ObjetivoCgFormViewModel
            {
                Descripcion = request.Descripcion,
                PilarId = request.PilarId,
                TrimestreObjetivo = request.TrimestreObjetivo,
                Pilares = pilares
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /ObjetivoCg/Editar/{id}) — solo JefeArea ───────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Editar(Guid id)
    {
        try
        {
            var cg = await _apiClient.GetAsync<ObjetivoCgResponse>($"/api/v1/objetivos-cg/{id}");
            var pilares = await ObtenerPilaresCicloActivoAsync() ?? [];
            return View(new ObjetivoCgFormViewModel
            {
                ObjetivoCgId = cg.Id,
                Codigo = cg.Codigo,
                Descripcion = cg.Descripcion,
                PilarId = cg.PilarId,
                TrimestreObjetivo = cg.TrimestreObjetivo,
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
    public async Task<IActionResult> Editar(Guid id, ObjetivoCgUpdateRequest request)
    {
        try
        {
            var cg = await _apiClient.PutAsync<ObjetivoCgUpdateRequest, ObjetivoCgResponse>(
                $"/api/v1/objetivos-cg/{id}", request);
            TempData["Success"] = $"Objetivo CG '{cg.Codigo}' actualizado correctamente.";
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
            return View(new ObjetivoCgFormViewModel
            {
                ObjetivoCgId = id,
                Descripcion = request.Descripcion,
                PilarId = request.PilarId,
                TrimestreObjetivo = request.TrimestreObjetivo,
                Pilares = pilares,
                EsEdicion = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (POST /ObjetivoCg/Eliminar/{id}) — solo JefeArea ───────────

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid id)
    {
        try
        {
            var cg = await _apiClient.DeleteAsync<ObjetivoCgResponse>($"/api/v1/objetivos-cg/{id}");
            TempData["Success"] = $"Objetivo CG '{cg.Codigo}' eliminado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 422 (tiene acciones asociadas — F3 HU-017, o ciclo Cerrado — RC-12) → flash.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helper: pilares del ciclo activo (CA #3 — referencia contextual ObjetivoQ1..Q4) ──

    /// <summary>
    /// Catálogo de pilares del ciclo activo con ObjetivoQ1..Q4 (HU-014). Null si no hay ciclo activo.
    /// </summary>
    private async Task<List<PilarResponse>?> ObtenerPilaresCicloActivoAsync()
    {
        var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
        var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
        if (cicloActivo is null)
            return null;

        return await _apiClient.GetAsync<List<PilarResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/pilares");
    }
}
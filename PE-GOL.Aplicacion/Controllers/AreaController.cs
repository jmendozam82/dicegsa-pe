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
/// UI de Gestión de Áreas Estratégicas (Spec HU-009 § UI — AreaController).
/// GET multi-rol (AdminTenant/Gerente/JefeArea — JEF solo su área, SEC-07: la API filtra);
/// crear/editar/desactivar SOLO AdminTenant (doble capa MVC + API).
/// Desactivar = PUT .../areas/{id}/desactivar (sin DELETE — CA #5: datos en solo lectura).
/// SEC-06: {cicloId}/{areaId} vienen de la ruta; tenant_id nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class AreaController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<AreaController> _logger;

    public AreaController(IApiClient apiClient, ILogger<AreaController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado (GET /Areas?cicloId=...) ────────────────────────────────────

    public async Task<IActionResult> Index(Guid? cicloId)
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var modelo = new AreasIndexViewModel
            {
                Ciclos = ciclos,
                CicloId = cicloId,
                EsAdmin = User.IsInRole("AdminTenant")
            };

            if (cicloId.HasValue)
            {
                var areas = await _apiClient.GetAsync<List<AreaResponse>>($"/api/v1/ciclos/{cicloId}/areas");
                modelo.Items = areas;
                var ciclo = ciclos.FirstOrDefault(c => c.Id == cicloId.Value);
                modelo.CicloNombre = ciclo?.Nombre;
                modelo.CicloEstado = ciclo?.Estado;
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
            return View(new AreasIndexViewModel
            {
                CicloId = cicloId,
                EsAdmin = User.IsInRole("AdminTenant")
            });
        }
    }

    // ─── Crear (GET/POST /Areas/Crear?cicloId=...) — solo AdminTenant ────────

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Crear(Guid cicloId)
    {
        var candidatos = await CargarCandidatosAsync(cicloId);
        return View(new AreaFormViewModel { CicloId = cicloId, Candidatos = candidatos });
    }

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Crear(Guid cicloId, AreaCreateRequest request)
    {
        try
        {
            var area = await _apiClient.PostAsync<AreaCreateRequest, AreaResponse>(
                $"/api/v1/ciclos/{cicloId}/areas", request);
            TempData["Success"] = $"Área '{area.Nombre}' ({area.Codigo}) creada correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var candidatos = await CargarCandidatosAsync(cicloId);
            return View(new AreaFormViewModel
            {
                CicloId = cicloId,
                Nombre = request.Nombre,
                Comentarios = request.Comentarios,
                ResponsableId = request.ResponsableId,
                Candidatos = candidatos
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /Areas/Editar/{id}?cicloId=...) — solo AdminTenant ─

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Editar(Guid cicloId, Guid id)
    {
        try
        {
            var area = await _apiClient.GetAsync<AreaResponse>($"/api/v1/ciclos/{cicloId}/areas/{id}");
            var candidatos = await CargarCandidatosAsync(cicloId);
            return View(new AreaFormViewModel
            {
                CicloId = cicloId,
                AreaId = area.Id,
                Nombre = area.Nombre,
                Comentarios = area.Comentarios,
                ResponsableId = area.ResponsableId ?? Guid.Empty,
                EsEdicion = true,
                Codigo = area.Codigo,
                Activa = area.Activa,
                Candidatos = candidatos
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
    public async Task<IActionResult> Editar(Guid cicloId, Guid id, AreaUpdateRequest request)
    {
        try
        {
            var area = await _apiClient.PutAsync<AreaUpdateRequest, AreaResponse>(
                $"/api/v1/ciclos/{cicloId}/areas/{id}", request);
            TempData["Success"] = $"Área '{area.Nombre}' ({area.Codigo}) actualizada correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var candidatos = await CargarCandidatosAsync(cicloId);
            return View(new AreaFormViewModel
            {
                CicloId = cicloId,
                AreaId = id,
                Nombre = request.Nombre,
                Comentarios = request.Comentarios,
                ResponsableId = request.ResponsableId,
                EsEdicion = true,
                Candidatos = candidatos
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Desactivar (POST /Areas/Desactivar/{id}?cicloId=...) — solo AdminTenant

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Desactivar(Guid cicloId, Guid id)
    {
        try
        {
            var area = await _apiClient.PutAsync<AreaResponse>($"/api/v1/ciclos/{cicloId}/areas/{id}/desactivar");
            TempData["Success"] = $"Área '{area.Nombre}' ({area.Codigo}) desactivada. Sus datos permanecen en solo lectura.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            // 422 (ya inactiva / ciclo Cerrado) → flash con el mensaje del servidor.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Candidatos a responsable (CA #2, puente HU-010): GET /api/v1/ciclos/{cicloId}/areas/responsables.
    /// Si falla → log warning + catálogo vacío (el formulario sigue funcionando).
    /// </summary>
    private async Task<List<ResponsableCandidatoResponse>> CargarCandidatosAsync(Guid cicloId)
    {
        try
        {
            return await _apiClient.GetAsync<List<ResponsableCandidatoResponse>>(
                $"/api/v1/ciclos/{cicloId}/areas/responsables");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de candidatos (GET /api/v1/ciclos/{CicloId}/areas/responsables).", cicloId);
            return [];
        }
    }
}
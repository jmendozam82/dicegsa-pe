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
/// UI de Gestión de Responsables (Spec HU-010 § UI — ResponsableController).
/// GET multi-rol (AdminTenant/Gerente/JefeArea — JEF solo su responsable, SEC-07: la API filtra);
/// crear/reasignar/desactivar SOLO AdminTenant (doble capa MVC + API).
/// Editar = SOLO reasignación de área (PUT .../responsables/{id} con ResponsableReassignRequest);
/// desactivar = PUT .../responsables/{id}/desactivar (usuario.estado = 'Inactivo', sin DELETE).
/// SEC-06: {cicloId}/{responsableId} vienen de la ruta; tenant_id nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class ResponsableController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<ResponsableController> _logger;

    public ResponsableController(IApiClient apiClient, ILogger<ResponsableController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado (GET /Responsables?cicloId=...) ─────────────────────────────

    public async Task<IActionResult> Index(Guid? cicloId)
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var modelo = new ResponsablesIndexViewModel
            {
                Ciclos = ciclos,
                CicloId = cicloId,
                EsAdmin = User.IsInRole("AdminTenant")
            };

            if (cicloId.HasValue)
            {
                var responsables = await _apiClient.GetAsync<List<ResponsableResponse>>(
                    $"/api/v1/ciclos/{cicloId}/responsables");
                modelo.Items = responsables;
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
            return View(new ResponsablesIndexViewModel
            {
                CicloId = cicloId,
                EsAdmin = User.IsInRole("AdminTenant")
            });
        }
    }

    // ─── Crear (GET/POST /Responsables/Crear?cicloId=...) — solo AdminTenant ─

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Crear(Guid cicloId)
    {
        var areas = await CargarAreasAsync(cicloId);
        return View(new ResponsableFormViewModel { CicloId = cicloId, Areas = areas });
    }

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Crear(Guid cicloId, ResponsableCreateRequest request)
    {
        try
        {
            var responsable = await _apiClient.PostAsync<ResponsableCreateRequest, ResponsableResponse>(
                $"/api/v1/ciclos/{cicloId}/responsables", request);
            TempData["Success"] = $"Responsable '{responsable.Nombre}' creado correctamente. Se envió una contraseña temporal por correo.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var areas = await CargarAreasAsync(cicloId);
            return View(new ResponsableFormViewModel
            {
                CicloId = cicloId,
                Nombre = request.Nombre,
                Correo = request.Correo,
                AreaId = request.AreaId,
                Areas = areas
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar/Reasignar (GET/POST /Responsables/Editar/{id}?cicloId=...) ───

    [Authorize(Roles = "AdminTenant")]
    public async Task<IActionResult> Editar(Guid cicloId, Guid id)
    {
        try
        {
            var responsable = await _apiClient.GetAsync<ResponsableResponse>(
                $"/api/v1/ciclos/{cicloId}/responsables/{id}");
            var areas = await CargarAreasAsync(cicloId);
            return View(new ResponsableFormViewModel
            {
                CicloId = cicloId,
                ResponsableId = responsable.Id,
                Nombre = responsable.Nombre,
                Correo = responsable.Correo,
                AreaId = responsable.AreaId ?? Guid.Empty,
                EsEdicion = true,
                Estado = responsable.Estado,
                Areas = areas
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
    public async Task<IActionResult> Editar(Guid cicloId, Guid id, ResponsableReassignRequest request)
    {
        try
        {
            var responsable = await _apiClient.PutAsync<ResponsableReassignRequest, ResponsableResponse>(
                $"/api/v1/ciclos/{cicloId}/responsables/{id}", request);
            TempData["Success"] = $"Responsable '{responsable.Nombre}' reasignado correctamente.";
            if (!string.IsNullOrWhiteSpace(responsable.Advertencia))
                TempData["Error"] = responsable.Advertencia;
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
            var areas = await CargarAreasAsync(cicloId);
            return View(new ResponsableFormViewModel
            {
                CicloId = cicloId,
                ResponsableId = id,
                AreaId = request.AreaId,
                EsEdicion = true,
                Areas = areas
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Desactivar (POST /Responsables/Desactivar/{id}?cicloId=...) ─────────

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Desactivar(Guid cicloId, Guid id)
    {
        try
        {
            var responsable = await _apiClient.PutAsync<ResponsableResponse>(
                $"/api/v1/ciclos/{cicloId}/responsables/{id}/desactivar");
            TempData["Success"] = $"Responsable '{responsable.Nombre}' desactivado. El área queda liberada.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            // 422 (ya inactivo / ciclo Cerrado) → flash con el mensaje del servidor.
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
    /// Catálogo de áreas del ciclo (para el select de área destino).
    /// Si falla → log warning + catálogo vacío (el formulario sigue funcionando).
    /// </summary>
    private async Task<List<AreaResponse>> CargarAreasAsync(Guid cicloId)
    {
        try
        {
            return await _apiClient.GetAsync<List<AreaResponse>>($"/api/v1/ciclos/{cicloId}/areas");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de áreas (GET /api/v1/ciclos/{CicloId}/areas).", cicloId);
            return [];
        }
    }
}
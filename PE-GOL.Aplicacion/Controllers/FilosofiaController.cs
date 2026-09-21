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
/// UI de Filosofía Corporativa (Spec HU-011 § UI — Visión y Misión; HU-012 § UI — Valores).
/// GET multi-rol (AdminTenant/Gerente/JefeArea — SEC-07 NO APLICA, D-E: filosofía corporativa);
/// PUT solo Gerente (RN-006; ADM/JEF solo lectura — RN-007).
/// Vision/Mision viajan como HTML sanitizado por la BLL (allowlist HU-011 D-C) → render seguro.
/// Valores: lista ordenada (HU-012 D-C) — el frontend envía la lista final ya ordenada.
/// SEC-06: {cicloId} viene de la ruta; tenant_id nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class FilosofiaController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<FilosofiaController> _logger;

    public FilosofiaController(IApiClient apiClient, ILogger<FilosofiaController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Visión y Misión (GET /Filosofia?cicloId=...) ────────────────────────

    public async Task<IActionResult> Index(Guid? cicloId)
    {
        try
        {
            var modelo = await CargarModeloAsync(cicloId);
            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new FilosofiaViewModel
            {
                CicloId = cicloId,
                EsGerente = User.IsInRole("Gerente")
            });
        }
    }

    // ─── Guardar Visión y Misión (POST /Filosofia/Guardar?cicloId=...) — solo Gerente ──

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Guardar(Guid cicloId, FilosofiaUpdateRequest request)
    {
        try
        {
            var filosofia = await _apiClient.PutAsync<FilosofiaUpdateRequest, FilosofiaResponse>(
                $"/api/v1/ciclos/{cicloId}/filosofia", request);
            TempData["Success"] = "Visión y Misión guardadas correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var modelo = await CargarModeloAsync(cicloId);
            modelo.Vision = request.Vision;
            modelo.Mision = request.Mision;
            return View(nameof(Index), modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Valores Corporativos (GET /Filosofia/Valores?cicloId=...) ────────────

    public async Task<IActionResult> Valores(Guid? cicloId)
    {
        try
        {
            var modelo = await CargarModeloAsync(cicloId);
            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new FilosofiaViewModel
            {
                CicloId = cicloId,
                EsGerente = User.IsInRole("Gerente")
            });
        }
    }

    // ─── Guardar Valores (POST /Filosofia/GuardarValores?cicloId=...) — solo Gerente ──

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> GuardarValores(Guid cicloId, ValoresUpdateRequest request)
    {
        try
        {
            var filosofia = await _apiClient.PutAsync<ValoresUpdateRequest, FilosofiaResponse>(
                $"/api/v1/ciclos/{cicloId}/filosofia/valores", request);
            TempData["Success"] = "Valores corporativos guardados correctamente.";
            return RedirectToAction(nameof(Valores), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var modelo = await CargarModeloAsync(cicloId);
            modelo.Valores = request.Valores;
            return View(nameof(Valores), modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Carga ciclos + filosofía del ciclo seleccionado (GET /api/v1/ciclos/{cicloId}/filosofia).
    /// Sin ciclo seleccionado → modelo con catálogo y estado vacío (UX-05).
    /// </summary>
    private async Task<FilosofiaViewModel> CargarModeloAsync(Guid? cicloId)
    {
        var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
        var modelo = new FilosofiaViewModel
        {
            Ciclos = ciclos,
            CicloId = cicloId,
            EsGerente = User.IsInRole("Gerente")
        };

        if (cicloId.HasValue)
        {
            var filosofia = await _apiClient.GetAsync<FilosofiaResponse>($"/api/v1/ciclos/{cicloId}/filosofia");
            modelo.Vision = filosofia.Vision;
            modelo.Mision = filosofia.Mision;
            modelo.Valores = filosofia.Valores;
            modelo.UpdatedByNombre = filosofia.UpdatedByNombre;
            modelo.UpdatedAt = filosofia.UpdatedAt;
            var ciclo = ciclos.FirstOrDefault(c => c.Id == cicloId.Value);
            modelo.CicloNombre = ciclo?.Nombre;
            modelo.CicloEstado = ciclo?.Estado;
        }

        return modelo;
    }
}
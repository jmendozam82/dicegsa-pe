using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Constants;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Configuración de la Empresa (Spec HU-006 § UI — EmpresaController).
/// GET multi-rol (AdminTenant/Gerente/JefeArea — RN-007); PUT y logo SOLO AdminTenant
/// (doble capa: [Authorize(Roles)] MVC + re-validación por la API vía JWT).
/// SEC-06: tenant_id nunca viaja en body/query (la API lo resuelve del JWT).
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class EmpresaController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<EmpresaController> _logger;

    public EmpresaController(IApiClient apiClient, ILogger<EmpresaController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Configuración (GET /Empresa) ────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        try
        {
            var empresa = await _apiClient.GetAsync<EmpresaResponse>("/api/v1/empresa");
            return View("Configuracion", MapearViewModel(empresa));
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View("Configuracion", new EmpresaViewModel { EsAdmin = User.IsInRole("AdminTenant") });
        }
    }

    // ─── Guardar (POST /Empresa) — solo AdminTenant ──────────────────────────

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Guardar(EmpresaUpdateRequest request)
    {
        try
        {
            var empresa = await _apiClient.PutAsync<EmpresaUpdateRequest, EmpresaResponse>("/api/v1/empresa", request);
            TempData["Success"] = "Configuración de la empresa actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 400/422: errores del servidor visibles (CA #5) → ModelState + re-render.
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View("Configuracion", new EmpresaViewModel
            {
                Nombre = request.Nombre,
                Eslogan = request.Eslogan,
                ZonaHoraria = request.ZonaHoraria,
                EsAdmin = true,
                Zonas = ObtenerZonas()
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Logo (POST /Empresa/Logo) — solo AdminTenant ────────────────────────

    [Authorize(Roles = "AdminTenant")]
    [HttpPost]
    public async Task<IActionResult> Logo(IFormFile archivo)
    {
        try
        {
            var empresa = await _apiClient.PostMultipartAsync<EmpresaResponse>("/api/v1/empresa/logo", archivo, "archivo");
            TempData["Success"] = "Logo actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            // 422 (formato/tamaño inválido) → errores del servidor visibles.
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return await ReRenderizarConfiguracionAsync();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<IActionResult> ReRenderizarConfiguracionAsync()
    {
        try
        {
            var empresa = await _apiClient.GetAsync<EmpresaResponse>("/api/v1/empresa");
            return View("Configuracion", MapearViewModel(empresa));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo recargar la configuración tras un error de logo.");
            return View("Configuracion", new EmpresaViewModel { EsAdmin = true, Zonas = ObtenerZonas() });
        }
    }

    private EmpresaViewModel MapearViewModel(EmpresaResponse empresa) => new()
    {
        TenantId = empresa.TenantId,
        Nombre = empresa.Nombre,
        Eslogan = empresa.Eslogan,
        LogoUrl = empresa.LogoUrl,
        ZonaHoraria = empresa.ZonaHoraria,
        Descripcion = empresa.Descripcion,
        UpdatedAt = empresa.UpdatedAt,
        EsAdmin = User.IsInRole("AdminTenant"),
        Zonas = ObtenerZonas()
    };

    /// <summary>
    /// Catálogo IANA (ZonasIANA) ordenado con America/* primero (spec HU-006 § UI).
    /// </summary>
    private static List<string> ObtenerZonas()
        => ZonasIANA.Zonas
            .OrderBy(z => z.StartsWith("America/", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(z => z, StringComparer.Ordinal)
            .ToList();
}
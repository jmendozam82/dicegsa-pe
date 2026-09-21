using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses.Dashboard;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de los Tableros de Inicio (Spec HU-016 § UI — Gerente.cshtml; HU-015 § UI — JefeArea.cshtml).
/// Gerente: GET /api/v1/dashboard/gerente (RN-006; SEC-07 NO APLICA, D-Q: el GER ve TODAS las áreas).
/// JefeArea: GET /api/v1/dashboard/jefe-area (SEC-07: solo SU área — la API filtra por AreaId del JWT).
/// Ambos sin query params ni body (SEC-06; el ciclo activo se resuelve server-side, D-G).
/// Renderizan .kpi-card (DS § 5.1) — CA #1/#2/#4.
/// </summary>
[Authorize]
public class DashboardController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IApiClient apiClient, ILogger<DashboardController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Tablero Gerente (GET /Dashboard/Gerente) ────────────────────────────

    [Authorize(Roles = "Gerente")]
    public async Task<IActionResult> Gerente()
    {
        try
        {
            var tablero = await _apiClient.GetAsync<TableroGerenteResponse>("/api/v1/dashboard/gerente");
            return View(new DashboardGerenteViewModel { Tablero = tablero });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            // 404 (sin ciclo activo) → flash con el mensaje del servidor + estado vacío.
            TempData["Error"] = ex.Message;
            return View(new DashboardGerenteViewModel());
        }
    }

    // ─── Tablero Jefe de Área (GET /Dashboard/JefeArea) ──────────────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> JefeArea()
    {
        try
        {
            var tablero = await _apiClient.GetAsync<TableroJefeAreaResponse>("/api/v1/dashboard/jefe-area");
            return View(new DashboardJefeAreaViewModel { Tablero = tablero });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            // 404 (sin ciclo activo / sin área asignada) → flash + estado vacío (UX-05).
            TempData["Error"] = ex.Message;
            return View(new DashboardJefeAreaViewModel());
        }
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Responses.Dashboard;

namespace PE_GOL.API.Controllers;

/// <summary>
/// Tablero de Inicio del Jefe de Área (Spec HU-015 § Endpoints) — completa 04_ARQUITECTURA.md
/// L113 (decisión D-J). Ruta standalone GET /api/v1/dashboard/jefe-area (F6 de Jorge): el ciclo
/// activo se resuelve internamente en BLL vía DAL-D1 (CA #4, D-G) — el frontend no necesita
/// conocer cicloId. Sin query params ni body (SEC-06): tenant_id/area_id/ciclo_id provienen
/// SIEMPRE del TenantContext (claims del JWT), nunca del cliente.
/// Roles (D-F): solo JefeArea en esta HU (el tablero del GER es HU-016, Sprint 3; el controller
/// puede albergar ambos endpoints sin romper contratos).
/// SEC-07: DAL-D3/D4/D5 filtran por TenantContext.AreaId (el JEF solo ve SU área).
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07). Códigos: 200 · 401 · 403 · 404 · 500.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/dashboard/jefe-area — Tablero resumen del área del JEF para el ciclo
    /// activo del tenant (CA #1/#2/#4): tarjetas (Total OKRs · OKRs alcanzados · % avance plan ·
    /// atrasadas · días al vencimiento) + semáforo global (promedio OKRs + plan, D-E).
    /// 403 rol ≠ JefeArea (D12/D-F) · 404 sin tenant / sin ciclo activo / JEF sin área / área
    /// inexistente. Sin auditoría (D-H: solo lectura).</summary>
    [HttpGet("jefe-area")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<TableroJefeAreaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ObtenerTableroJefeArea(CancellationToken ct = default)
    {
        var data = await _service.ObtenerTableroJefeAreaAsync(ct);
        return Ok(data);
    }

    /// <summary>GET /api/v1/dashboard/gerente — Tablero consolidado del Gerente para el ciclo
    /// activo del tenant (CA #1/#2/#4): paneles por área, totales consolidados y alertas.
    /// 403 rol ≠ Gerente · 404 sin tenant / sin ciclo activo. SEC-07 NO APLICA.</summary>
    [HttpGet("gerente")]
    [Authorize(Roles = "Gerente")]
    [ProducesResponseType(typeof(ApiResponse<TableroGerenteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ObtenerTableroGerente(CancellationToken ct = default)
    {
        var data = await _service.ObtenerTableroGerenteAsync(ct);
        return Ok(data);
    }
}
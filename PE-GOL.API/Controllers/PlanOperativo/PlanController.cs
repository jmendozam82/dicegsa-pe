using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.API.Controllers.PlanOperativo;

/// <summary>
/// Controller de solo lectura para la vista consolidada del Plan (Spec HU-023).
/// Solo rol Gerente (RN-006). Sin POST/PUT/DELETE.
/// </summary>
[ApiController]
[Route("api/v1/planes")]
[Authorize(Roles = "Gerente")]
public sealed class PlanController : ControllerBase
{
    private readonly IPlanConsolidadoService _service;
    private readonly TenantContext _tenantContext;

    public PlanController(IPlanConsolidadoService service, TenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/v1/planes/consolidado — Tabla consolidada paginada con filtros + resumen.
    /// 200 → ConsolidadoResponse con items, resumen y paginación.
    /// 401 → sin JWT · 403 → rol ≠ Gerente · 404 → sin tenant o sin ciclo activo · 422 → filtros inválidos.
    /// </summary>
    [HttpGet("consolidado")]
    [ProducesResponseType(typeof(ApiResponse<ConsolidadoResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Consolidado(
        [FromQuery] FiltrosConsolidadoRequest filtros, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var result = await _service.ObtenerConsolidadoAsync(filtros, ct);
        return Ok(new ApiResponse<ConsolidadoResponse> { Success = true, Data = result });
    }

    /// <summary>
    /// GET /api/v1/planes/consolidado/exportar — Exportación a Excel (ClosedXML).
    /// Excepción a ARCH-07: devuelve FileContentResult, no ApiResponse&lt;T&gt;.
    /// 200 → archivo .xlsx · 401 → sin JWT · 403 → rol ≠ Gerente · 404 → sin tenant o sin ciclo activo · 422 → filtros inválidos.
    /// </summary>
    [HttpGet("consolidado/exportar")]
    [ProducesResponseType(typeof(FileContentResult), 200, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Exportar(
        [FromQuery] FiltrosConsolidadoRequest filtros, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var bytes = await _service.ExportarConsolidadoAsync(filtros, ct);
        var fileName = $"PlanConsolidado_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}

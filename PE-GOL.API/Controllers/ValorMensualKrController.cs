using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.API.Controllers;

/// <summary>
/// Registro mensual de valores reales de KRs (Spec HU-026). Actor JefeArea (SEC-07, F1).
/// Todos los endpoints resuelven tenant_id, area_id y ciclo_id desde el JWT/TenantContext (SEC-06);
/// el okr_id viaja SOLO por la ruta.
/// </summary>
[ApiController]
[Authorize(Roles = "JefeArea")]
public sealed class ValorMensualKrController : ControllerBase
{
    private readonly IValorMensualKrService _service;
    private readonly TenantContext _tenantContext;

    public ValorMensualKrController(IValorMensualKrService service, TenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/v1/okrs/{okrId}/valores-mensuales — Grilla completa del OKR: 12 celdas por KR +
    /// flags de editabilidad + ventana editable + umbrales + agregados de cálculo.
    /// 200 → grilla · 401 · 403 · 404 (sin tenant/área, sin ciclo activo, OKR de otra área).
    /// </summary>
    [HttpGet("api/v1/okrs/{okrId:guid}/valores-mensuales")]
    [ProducesResponseType(typeof(ApiResponse<ValorMensualKrGrillaResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ObtenerGrilla(Guid okrId, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var grilla = await _service.ObtenerGrillaAsync(okrId, ct);
        return Ok(new ApiResponse<ValorMensualKrGrillaResponse> { Success = true, Data = grilla });
    }

    /// <summary>
    /// PUT /api/v1/okrs/{okrId}/key-results/{keyResultId}/valores — UPSERT batch de 1..12 meses
    /// (RC-07/DB-06) + recálculo completo de la cadena (CA #4, CA #5).
    /// 200 → fila recalculada + agregado del OKR · 400 · 401 · 403 · 404 · 422.
    /// </summary>
    [HttpPut("api/v1/okrs/{okrId:guid}/key-results/{keyResultId:guid}/valores")]
    [ProducesResponseType(typeof(ApiResponse<ValorMensualKrGuardarResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> GuardarValores(Guid okrId, Guid keyResultId, [FromBody] ValorMensualKrUpdateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var resultado = await _service.GuardarValoresAsync(okrId, keyResultId, request, ct);
        return Ok(new ApiResponse<ValorMensualKrGuardarResponse> { Success = true, Data = resultado, Message = "Valores mensuales registrados exitosamente." });
    }

    /// <summary>
    /// DELETE /api/v1/okrs/{okrId}/key-results/{keyResultId}/valores/{mes} — Borra el valor de un
    /// mes + recálculo. Es el desbloqueo de KeyResultService.EliminarAsync (HU-025 CA #4).
    /// 200 → fila recalculada + agregado del OKR · 400 · 401 · 403 · 404 · 422.
    /// </summary>
    [HttpDelete("api/v1/okrs/{okrId:guid}/key-results/{keyResultId:guid}/valores/{mes:int}")]
    [ProducesResponseType(typeof(ApiResponse<ValorMensualKrGuardarResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> EliminarValor(Guid okrId, Guid keyResultId, int mes, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var resultado = await _service.EliminarValorAsync(okrId, keyResultId, mes, ct);
        return Ok(new ApiResponse<ValorMensualKrGuardarResponse> { Success = true, Data = resultado, Message = "Valor mensual eliminado exitosamente." });
    }

    /// <summary>
    /// GET /api/v1/okrs/{okrId}/valores-mensuales/exportar — Genera el XLSX del OKR (ADR-014).
    /// 200 → archivo Excel · 401 · 403 · 404.
    /// </summary>
    [HttpGet("api/v1/okrs/{okrId:guid}/valores-mensuales/exportar")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Exportar(Guid okrId, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var bytes = await _service.ExportarAsync(okrId, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"valores-mensuales-okr-{okrId}.xlsx");
    }
}

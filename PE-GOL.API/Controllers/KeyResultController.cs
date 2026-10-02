using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// CRUD de Key Results de un OKR (Spec HU-025). Actor JefeArea (SEC-07, F1).
/// Todos los endpoints resuelven tenant_id, area_id y ciclo_id desde el JWT/TenantContext (SEC-06);
/// el okr_id viaja SOLO por la ruta.
/// </summary>
[ApiController]
[Route("api/v1/okrs/{okrId:guid}/key-results")]
[Authorize(Roles = "JefeArea")]
public sealed class KeyResultController : ControllerBase
{
    private readonly IKeyResultService _service;
    private readonly TenantContext _tenantContext;

    public KeyResultController(IKeyResultService service, TenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/v1/okrs/{okrId}/key-results — Lista los KRs del OKR (CA #1).
    /// 200 ? lista (vacía válida) · 401 · 403 · 404 (sin tenant/área, sin ciclo activo, OKR de otra área o de otro ciclo).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<KeyResultResponse>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Listar(Guid okrId, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var krs = await _service.ListarAsync(okrId, ct);
        return Ok(new ApiResponse<IEnumerable<KeyResultResponse>> { Success = true, Data = krs });
    }

    /// <summary>
    /// GET /api/v1/okrs/{okrId}/key-results/{id} — Detalle de un KR del OKR.
    /// 200 ? KeyResultResponse · 401 · 403 · 404 (sin tenant/área/ciclo, OKR ajeno o id inexistente/ajeno).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<KeyResultResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Obtener(Guid okrId, Guid id, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var kr = await _service.ObtenerPorIdAsync(okrId, id, ct);
        return Ok(new ApiResponse<KeyResultResponse> { Success = true, Data = kr });
    }

    /// <summary>
    /// POST /api/v1/okrs/{okrId}/key-results — Crea un KR con código KR.N auto-generado (CA #1, CA #2).
    /// 201 ? KeyResultResponse · 400 · 401 · 403 · 404 · 422 (descripción/peso inválidos, máx 5 KRs, ciclo Cerrado, colisión 23505).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<KeyResultResponse>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Crear(Guid okrId, [FromBody] KeyResultCreateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var creado = await _service.CrearAsync(okrId, request, ct);
        return CreatedAtAction(nameof(Obtener), new { okrId, id = creado.Id },
            new ApiResponse<KeyResultResponse> { Success = true, Data = creado, Message = "Key Result creado exitosamente." });
    }

    /// <summary>
    /// PUT /api/v1/okrs/{okrId}/key-results/{id} — Edita descripción y peso. No toca código, orden ni puntuaciones (CA #1, DB-04).
    /// 200 ? KeyResultResponse · 400 · 401 · 403 · 404 · 422.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<KeyResultResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Actualizar(Guid okrId, Guid id, [FromBody] KeyResultUpdateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var actualizado = await _service.ActualizarAsync(okrId, id, request, ct);
        return Ok(new ApiResponse<KeyResultResponse> { Success = true, Data = actualizado, Message = "Key Result actualizado exitosamente." });
    }

    /// <summary>
    /// PUT /api/v1/okrs/{okrId}/key-results/pesos — Reparto masivo atómico de pesos con S = 1.000 (CA #3, F0).
    /// 200 ? { okrId, cantidadKrs, mensaje } · 400 · 401 · 403 · 404 · 422 (vector inválido, S ? 1.000, ciclo Cerrado).
    /// </summary>
    [HttpPut("pesos")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> ActualizarPesos(Guid okrId, [FromBody] KeyResultPesosUpdateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        await _service.ActualizarPesosAsync(okrId, request, ct);
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { okrId, cantidadKrs = request.Pesos.Count, mensaje = "Reparto de pesos actualizado." }
        });
    }

    /// <summary>
    /// DELETE /api/v1/okrs/{okrId}/key-results/{id} — Elimina físicamente el KR si no tiene valores reales (CA #4, F3).
    /// 200 ? true · 401 · 403 · 404 · 422 (valores reales o ciclo Cerrado).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Eliminar(Guid okrId, Guid id, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        await _service.EliminarAsync(okrId, id, ct);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "Key Result eliminado exitosamente." });
    }
}

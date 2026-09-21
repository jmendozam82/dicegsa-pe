using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.API.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize] // Default es usuario autenticado
public class AccionPlanController : ControllerBase
{
    private readonly IAccionPlanService _service;

    public AccionPlanController(IAccionPlanService service)
    {
        _service = service;
    }

    [HttpGet("objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<AccionPlanResponse>>), 200)]
    public async Task<IActionResult> Listar(Guid objetivoCgId, CancellationToken ct)
    {
        var acciones = await _service.ListarPorObjetivoCgAsync(objetivoCgId, ct);
        return Ok(new ApiResponse<IEnumerable<AccionPlanResponse>> { Success = true, Data = acciones });
    }

    [HttpGet("acciones/{id:guid}")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        var accion = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion });
    }

    [HttpPost("objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 201)]
    public async Task<IActionResult> Crear(Guid objetivoCgId, [FromBody] AccionPlanCreateRequest request, CancellationToken ct)
    {
        var accion = await _service.CrearAsync(objetivoCgId, request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = accion.Id }, new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción creada exitosamente." });
    }

    [HttpPut("acciones/{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] AccionPlanUpdateRequest request, CancellationToken ct)
    {
        var accion = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción actualizada exitosamente." });
    }

    [HttpDelete("acciones/{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await _service.EliminarAsync(id, ct);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "Acción eliminada exitosamente." });
    }

    // ─── HU-020 — Progreso e Historial ───────────────────────────────────────

    /// <summary>
    /// Actualiza el % de progreso de una acción.
    /// Recalcula status (RN-017), puntuación ponderada, historial y semáforo del CG (RN-018, F2).
    /// Idempotente: si el progreso no cambia retorna 200 OK sin efectos secundarios (F3).
    /// </summary>
    [HttpPatch("acciones/{id:guid}/progreso")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ActualizarProgreso(
        Guid id, [FromBody] ActualizarProgresoRequest request, CancellationToken ct)
    {
        var accion = await _service.ActualizarProgresoAsync(id, request, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Progreso actualizado exitosamente." });
    }

    /// <summary>Lista el historial de cambios de progreso de una acción (F1: JefeArea + Gerente).</summary>
    [HttpGet("acciones/{id:guid}/historial")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<HistorialProgresoResponse>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ObtenerHistorial(Guid id, CancellationToken ct)
    {
        var historial = await _service.ListarHistorialAsync(id, ct);
        return Ok(new ApiResponse<IEnumerable<HistorialProgresoResponse>> { Success = true, Data = historial });
    }
}
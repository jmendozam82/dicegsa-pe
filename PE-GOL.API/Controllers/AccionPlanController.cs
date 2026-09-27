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
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.API.Controllers;

[ApiController]
[Route("api/v1/acciones")]
[Authorize] // Default es usuario autenticado
public class AccionPlanController : ControllerBase
{
    private readonly IAccionPlanService _service;

    public AccionPlanController(IAccionPlanService service)
    {
        _service = service;
    }

    [HttpGet("~/api/v1/objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<AccionPlanResponse>>), 200)]
    public async Task<IActionResult> Listar(Guid objetivoCgId, CancellationToken ct)
    {
        var acciones = await _service.ListarPorObjetivoCgAsync(objetivoCgId, ct);
        return Ok(new ApiResponse<IEnumerable<AccionPlanResponse>> { Success = true, Data = acciones });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        var accion = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion });
    }

    [HttpPost("~/api/v1/objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 201)]
    public async Task<IActionResult> Crear(Guid objetivoCgId, [FromBody] AccionPlanCreateRequest request, CancellationToken ct)
    {
        var accion = await _service.CrearAsync(objetivoCgId, request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = accion.Id }, new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción creada exitosamente." });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] AccionPlanUpdateRequest request, CancellationToken ct)
    {
        var accion = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción actualizada exitosamente." });
    }

    [HttpDelete("{id:guid}")]
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
    [HttpPatch("{id:guid}/progreso")]
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
    [HttpGet("{id:guid}/historial")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<HistorialProgresoResponse>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ObtenerHistorial(Guid id, CancellationToken ct)
    {
        var historial = await _service.ListarHistorialAsync(id, ct);
        return Ok(new ApiResponse<IEnumerable<HistorialProgresoResponse>> { Success = true, Data = historial });
    }

    // ─── HU-021 — Vista Gantt del Plan de Acción ─────────────────────────────

    /// <summary>
    /// Datos del Gantt del plan de acción del CICLO ACTIVO del tenant (HU-021, solo lectura).
    /// Sin body y sin query string: el tenant sale del JWT (SEC-06) y el ciclo se resuelve en la
    /// BLL (RC-01). La BLL devuelve el DTO plano; el wrapper ApiResponse&lt;T&gt; se arma aquí
    /// (ARCH-07, ADR-010 Decisión 1).
    /// 200 → GanttPlanResponse con la escala de 12 meses, los grupos (Objetivos CG con acciones),
    ///       las acciones planas y los 4 conteos por status. Un ciclo sin acciones devuelve 200 con
    ///       payload vacío (no 404): la vista muestra el .empty-state (UX-05).
    /// 401 → sin JWT válido · 403 → rol ∉ {JefeArea, Gerente}, o JefeArea sin área en el token.
    /// 404 → sin tenant en el contexto, o no hay ciclo Activo para el tenant.
    /// 500 → error inesperado del servidor.
    /// </summary>
    [HttpGet("gantt")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<GanttPlanResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 500)]
    public async Task<IActionResult> ObtenerGantt(CancellationToken ct)
    {
        var gantt = await _service.ObtenerGanttAsync(ct);
        return Ok(new ApiResponse<GanttPlanResponse> { Success = true, Data = gantt });
    }
}
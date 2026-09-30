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
/// CRUD de OKRs del área (Spec HU-024). Actor JefeArea (SEC-07, F1).
/// Todos los endpoints resuelven tenant_id, ciclo_id y area_id desde el JWT/TenantContext (SEC-06).
/// </summary>
[ApiController]
[Route("api/v1/okrs")]
[Authorize(Roles = "JefeArea")]
public sealed class OkrController : ControllerBase
{
    private readonly IOkrService _service;
    private readonly TenantContext _tenantContext;

    public OkrController(IOkrService service, TenantContext tenantContext)
    {
        _service = service;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// GET /api/v1/okrs — Lista los OKRs del área en el ciclo activo (CA #4).
    /// 200 → lista con PuntuacionFinal, Semaforo y PilarNombre · 401 · 403 · 404 (sin tenant/área o sin ciclo activo).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<OkrResponse>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var okrs = await _service.ListarAsync(ct);
        return Ok(new ApiResponse<IEnumerable<OkrResponse>> { Success = true, Data = okrs });
    }

    /// <summary>
    /// GET /api/v1/okrs/{id} — Detalle de un OKR del área.
    /// 200 → OkrResponse · 401 · 403 · 404 (sin tenant/área, sin ciclo activo o id inexistente/de otra área).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OkrResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var okr = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<OkrResponse> { Success = true, Data = okr });
    }

    /// <summary>
    /// POST /api/v1/okrs — Crea un OKR del área. Código OKR.N auto-generado (CA #1), puntuación 0.000 y semáforo Rojo (F3).
    /// 201 → OkrResponse · 400 · 401 · 403 · 404 · 422 (máx 9 OKRs, ciclo Cerrado, pilar inexistente, colisión 23505).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OkrResponse>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Crear([FromBody] OkrCreateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var creado = await _service.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id },
            new ApiResponse<OkrResponse> { Success = true, Data = creado, Message = "OKR creado exitosamente." });
    }

    /// <summary>
    /// PUT /api/v1/okrs/{id} — Edita descripción y pilar. No modifica código, puntuación, semáforo ni orden (CA #1, DB-04).
    /// 200 → OkrResponse · 400 · 401 · 403 · 404 · 422.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OkrResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] OkrUpdateRequest request, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        var actualizado = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<OkrResponse> { Success = true, Data = actualizado, Message = "OKR actualizado exitosamente." });
    }

    /// <summary>
    /// DELETE /api/v1/okrs/{id} — Elimina físicamente el OKR si ningún KR tiene valores reales (CA #3 / RN-028 / F2).
    /// 200 → true · 401 · 403 · 404 · 422 (KRs con valores reales o ciclo Cerrado).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        _ = _tenantContext.TenantId
            ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

        await _service.EliminarAsync(id, ct);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "OKR eliminado exitosamente." });
    }
}

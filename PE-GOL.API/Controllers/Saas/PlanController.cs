using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers.Saas;

/// <summary>
/// Endpoints de Gestión de Planes de Suscripción (Spec HU-002 § Endpoints).
/// Todos bajo /api/v1/planes, protegidos con [Authorize(Roles = "SuperAdmin")].
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07). Catálogo acotado:
/// GET listado SIN paginación (D1). Códigos HTTP: 200, 201, 400, 401, 403, 404, 422, 500.
/// </summary>
[ApiController]
[Route("api/v1/planes")]
[Authorize(Roles = "SuperAdmin")]
public class PlanController : ControllerBase
{
    private readonly IPlanService _service;

    public PlanController(IPlanService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/planes — Listado COMPLETO de planes ordenado por nombre ASC (sin paginación, D1).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<PlanResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct = default)
    {
        var data = await _service.ListarAsync(ct);
        return Ok(new ApiResponse<List<PlanResponse>> { Success = true, Message = null, Data = data, Errors = null });
    }

    /// <summary>GET /api/v1/planes/{id} — Obtiene un plan por id (404 si no existe).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPorId(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<PlanResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/planes — Crea un plan (201). 422 si el nombre ya existe o límites fuera de rango.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear([FromBody] PlanCreateRequest request, CancellationToken ct = default)
    {
        var data = await _service.CrearAsync(request, ct);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = data.Id },
            new ApiResponse<PlanResponse> { Success = true, Message = "Plan creado", Data = data });
    }

    /// <summary>PUT /api/v1/planes/{id} — Actualiza un plan. 404 si no existe; 422 si el nombre
    /// está duplicado o los nuevos límites dejan a algún tenant existente sobre el tope.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] PlanUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<PlanResponse> { Success = true, Message = "Plan actualizado", Data = data });
    }

    /// <summary>DELETE /api/v1/planes/{id} — Elimina FÍSICAMENTE un plan (D2). 422 si está en uso
    /// por uno o más tenants; 404 si no existe.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct = default)
    {
        await _service.EliminarAsync(id, ct);
        return Ok(new ApiResponse<object> { Success = true, Message = "Plan eliminado", Data = null });
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.API.Controllers;

/// <summary>
/// Controlador de Objetivos Corporativos por Área (HU-017/HU-018).
/// GET listado/detalle/CRUD → solo JefeArea (SEC-07: solo ve su área, TenantContext.AreaId).
/// GET consolidado → Gerente (vista consolidada HU-018).
/// ARCH-07: todas las respuestas envueltas en ApiResponse<T>.
/// </summary>
[ApiController]
[Route("api/v1/objetivos-cg")]
[Authorize]
public class ObjetivoCgController : ControllerBase
{
    private readonly IObjetivoCgService _objetivoCgService;

    public ObjetivoCgController(IObjetivoCgService objetivoCgService)
    {
        _objetivoCgService = objetivoCgService;
    }

    [HttpGet]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ObjetivoCgResponse>>), 200)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Listar()
    {
        var result = await _objetivoCgService.ListarAsync();
        return Ok(new ApiResponse<IEnumerable<ObjetivoCgResponse>> { Success = true, Data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<ObjetivoCgResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ObtenerPorId(Guid id)
    {
        var result = await _objetivoCgService.ObtenerPorIdAsync(id);
        return Ok(new ApiResponse<ObjetivoCgResponse> { Success = true, Data = result });
    }

    [HttpPost]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<ObjetivoCgResponse>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Crear([FromBody] ObjetivoCgCreateRequest request)
    {
        var result = await _objetivoCgService.CrearAsync(request);
        return StatusCode(201, new ApiResponse<ObjetivoCgResponse> { Success = true, Data = result, Message = "Objetivo corporativo creado exitosamente." });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<ObjetivoCgResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] ObjetivoCgUpdateRequest request)
    {
        var result = await _objetivoCgService.ActualizarAsync(id, request);
        return Ok(new ApiResponse<ObjetivoCgResponse> { Success = true, Data = result, Message = "Objetivo corporativo actualizado exitosamente." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Eliminar(Guid id)
    {
        await _objetivoCgService.EliminarAsync(id);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "Objetivo corporativo eliminado exitosamente." });
    }

    /// <summary>GET /api/v1/objetivos-cg/consolidado — Vista consolidada del Gerente (HU-018).
    /// Requiere rol Gerente. ARCH-07: respuesta envuelta en ApiResponse<T>.</summary>
    [HttpGet("consolidado")]
    [Authorize(Roles = "Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ObjetivoCgConsolidadoResponse>>), 200)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarConsolidadoGerente([FromQuery] ObjetivoCgFilterRequest filtros)
    {
        var result = await _objetivoCgService.ListarConsolidadoGerenteAsync(filtros);
        return Ok(new ApiResponse<IEnumerable<ObjetivoCgConsolidadoResponse>> { Success = true, Data = result });
    }
}

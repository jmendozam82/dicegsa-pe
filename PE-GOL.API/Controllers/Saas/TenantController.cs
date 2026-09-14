using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers.Saas;

/// <summary>
/// Endpoints de Gestión de Tenants (Spec HU-001 § Endpoints).
/// Todos bajo /api/v1/tenants, protegidos con [Authorize(Roles = "SuperAdmin")].
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07). No se acepta
/// tenant_id en body/query (SEC-06): el tenant gestionado se identifica por {id} de ruta.
/// </summary>
[ApiController]
[Route("api/v1/tenants")]
[Authorize(Roles = "SuperAdmin")]
public class TenantController : ControllerBase
{
    private readonly ITenantService _service;

    public TenantController(ITenantService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/tenants — Listado paginado con filtros opcionales (estado, planId).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TenantResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? estado = null,
        [FromQuery] Guid? planId = null,
        CancellationToken ct = default)
    {
        var data = await _service.ListarAsync(page, pageSize, estado, planId, ct);
        return Ok(new ApiResponse<PagedResult<TenantResponse>> { Success = true, Message = null, Data = data, Errors = null });
    }

    /// <summary>GET /api/v1/tenants/{id} — Obtiene un tenant por id (404 si no existe).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPorId(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<TenantResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/tenants — Crea un tenant (201).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear([FromBody] TenantCreateRequest request, CancellationToken ct = default)
    {
        var data = await _service.CrearAsync(request, ct);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = data.Id },
            new ApiResponse<TenantResponse> { Success = true, Message = "Tenant creado", Data = data });
    }

    /// <summary>PUT /api/v1/tenants/{id} — Actualiza datos generales (estado NO se modifica aquí).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] TenantUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<TenantResponse> { Success = true, Message = "Tenant actualizado", Data = data });
    }

    /// <summary>POST /api/v1/tenants/{id}/activar — Inactivo → Activo (422 si ya está Activo).</summary>
    [HttpPost("{id:guid}/activar")]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ActivarAsync(id, ct);
        return Ok(new ApiResponse<TenantResponse> { Success = true, Message = "Tenant activado", Data = data });
    }

    /// <summary>POST /api/v1/tenants/{id}/desactivar — Activo → Inactivo + revocación de refresh tokens.</summary>
    [HttpPost("{id:guid}/desactivar")]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.DesactivarAsync(id, ct);
        return Ok(new ApiResponse<TenantResponse> { Success = true, Message = "Tenant desactivado", Data = data });
    }
}
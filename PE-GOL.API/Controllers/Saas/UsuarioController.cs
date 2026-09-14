using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers.Saas;

/// <summary>
/// Endpoints de Gestión de Usuarios Globales (Spec HU-003 § Endpoints).
/// Todos bajo /api/v1/usuarios, protegidos con [Authorize(Roles = "SuperAdmin")] — el SA
/// gestiona usuarios de CUALQUIER tenant mediante {id} de ruta / tenantId de query.
/// SEC-06: el SA es GLOBAL y por tanto declara tenantId/areaId explícitamente en el request
/// (única excepción documentada; el resto de roles jamás llama a estos endpoints — 403).
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07). No existe DELETE físico (D1):
/// los estados se gestionan con activar/desactivar (soft-state con estado_usuario).
/// </summary>
[ApiController]
[Route("api/v1/usuarios")]
[Authorize(Roles = "SuperAdmin")]
public class UsuarioController : ControllerBase
{
    private readonly IUsuarioService _service;

    public UsuarioController(IUsuarioService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/usuarios — Listado paginado con filtros opcionales
    /// (tenantId/rol/estado). page ≥ 1; pageSize 1..100 (trunca a 100).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UsuarioResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? rol = null,
        [FromQuery] string? estado = null,
        CancellationToken ct = default)
    {
        var data = await _service.ListarAsync(page, pageSize, tenantId, rol, estado, ct);
        return Ok(new ApiResponse<PagedResult<UsuarioResponse>> { Success = true, Message = null, Data = data, Errors = null });
    }

    /// <summary>GET /api/v1/usuarios/{id} — Obtiene un usuario por id (404 si no existe).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPorId(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<UsuarioResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/usuarios — Crea un usuario de tenant (201). 422 si: correo ya
    /// existe, tenant inexistente, área no pertenece al tenant, rol JefeArea sin área, o el
    /// tenant supera max_usuarios (CA #2 HU-002).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UsuarioResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear([FromBody] UsuarioCreateRequest request, CancellationToken ct = default)
    {
        var data = await _service.CrearAsync(request, ct);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = data.Id },
            new ApiResponse<UsuarioResponse> { Success = true, Message = "Usuario creado", Data = data });
    }

    /// <summary>PUT /api/v1/usuarios/{id} — Actualiza nombre/correo/rol/área/tenant (200).
    /// 404 si no existe · 422 con las mismas reglas que POST (re-valida límites SOLO si cambia
    /// tenant_id — D4/D6). El estado NO se modifica aquí (activar/desactivar).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] UsuarioUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<UsuarioResponse> { Success = true, Message = "Usuario actualizado", Data = data });
    }

    /// <summary>POST /api/v1/usuarios/{id}/activar — Bloqueado/Inactivo/Activo → Activo (200).
    /// 404 · 422 si ya está Activo o si el tenant supera max_usuarios al activar (D7 aprobado).</summary>
    [HttpPost("{id:guid}/activar")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ActivarAsync(id, ct);
        return Ok(new ApiResponse<UsuarioResponse> { Success = true, Message = "Usuario activado", Data = data });
    }

    /// <summary>POST /api/v1/usuarios/{id}/desactivar — Activo → Inactivo + revocación de refresh
    /// tokens (200). 404 · 422 si ya está Inactivo. Sin validación de límites (D4).</summary>
    [HttpPost("{id:guid}/desactivar")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.DesactivarAsync(id, ct);
        return Ok(new ApiResponse<UsuarioResponse> { Success = true, Message = "Usuario desactivado", Data = data });
    }

    /// <summary>POST /api/v1/usuarios/{id}/reset-contrasena — Genera nueva contraseña temporal
    /// (BCrypt cost 12), requiere_cambio_pwd=TRUE y revoca los refresh tokens activos (200).
    /// 404 · 500 (generación/BD). La contraseña temporal NO se devuelve por HTTP (llega por
    /// correo en la HU de Notificaciones, Sprint 2) → Data null.</summary>
    [HttpPost("{id:guid}/reset-contrasena")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetearContrasena(Guid id, CancellationToken ct = default)
    {
        await _service.ResetearContrasenaAsync(id, ct);
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Contraseña temporal generada. El usuario deberá cambiarla en su primer inicio de sesión.",
            Data = null
        });
    }
}
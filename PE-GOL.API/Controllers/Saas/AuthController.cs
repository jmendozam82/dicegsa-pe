using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.API.Controllers.Saas;

/// <summary>
/// Endpoints de Autenticación (Spec HU-004 § Endpoints). Todos bajo /api/v1/auth.
/// login/refresh/logout son [AllowAnonymous] (el access token puede estar expirado o no
/// existir aún); cambiar-contrasena es [Authorize] (cualquier rol autenticado — incl.
/// SuperAdmin, que también tiene requiere_cambio_pwd=TRUE en el seed).
/// SEC-06: el usuarioId de cambiar-contrasena sale del claim user_id del JWT, NUNCA del body.
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _service;

    public AuthController(IAuthService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/auth/login — Login correo+contraseña. 200 con LoginResponse
    /// (access token 60 min + refresh token 7 días + usuario) · 401 credenciales inválidas /
    /// cuenta bloqueada / usuario inactivo / tenant inactivo · 422 forma inválida.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct = default)
    {
        var data = await _service.LoginAsync(request, ct);
        return Ok(new ApiResponse<LoginResponse> { Success = true, Message = "Login exitoso", Data = data });
    }

    /// <summary>POST /api/v1/auth/refresh — Renueva el access token con el refresh token
    /// (rotación D1). 200 con RefreshResponse · 401 token inválido/revocado/expirado/usuario no activo.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RefreshResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct = default)
    {
        var data = await _service.RefreshAsync(request, ct);
        return Ok(new ApiResponse<RefreshResponse> { Success = true, Message = "Sesión renovada", Data = data });
    }

    /// <summary>POST /api/v1/auth/logout — Revoca el refresh token presentado (idempotente D10).
    /// 200 con ApiResponse&lt;object&gt; (Data null).</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct = default)
    {
        var data = await _service.LogoutAsync(request, ct);
        return Ok(data);
    }

    /// <summary>POST /api/v1/auth/cambiar-contrasena — Cambio de contraseña (primer login y
    /// autoservicio). El usuarioId sale del claim user_id del JWT (SEC-06), NUNCA del body.
    /// 200 · 401 (sin JWT) · 422 (actual incorrecta / política débil / igual a la actual) · 404.</summary>
    [HttpPost("cambiar-contrasena")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CambiarContrasena([FromBody] CambiarContrasenaRequest request, CancellationToken ct = default)
    {
        // SEC-06: usuarioId del claim user_id del JWT (poblado por el middleware JwtBearer).
        var usuarioIdClaim = User.FindFirstValue("user_id");
        if (!Guid.TryParse(usuarioIdClaim, out var usuarioId))
            throw new UnauthorizedException("No se pudo identificar al usuario autenticado.");

        var data = await _service.CambiarContrasenaAsync(usuarioId, request, ct);
        return Ok(data);
    }
}
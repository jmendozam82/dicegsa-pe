using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de autenticación (Spec HU-004 § Lógica BLL). Endpoints: login/refresh/logout/
/// cambiar-contrasena. Lanza UnauthorizedException (401), ValidacionException (422) y
/// NotFoundException (404) desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (IAuthRepository, JwtOptions) + overload (..., ILogger&lt;AuthService&gt;?).
/// </summary>
public interface IAuthService
{
    /// <summary>POST /api/v1/auth/login — 200 con LoginResponse · 401 credenciales/bloqueo/inactivo/tenant inactivo.</summary>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/auth/refresh — 200 con RefreshResponse (rotación D1) · 401 token inválido/revocado/expirado/usuario no activo.</summary>
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/auth/logout — revoca el refresh token presentado (idempotente D10).
    /// ARCH-02 (auditoría 2026-09-21): la BLL NO construye el wrapper HTTP — el controller lo hace
    /// (ARCH-07). Retorna true siempre (200 idempotente).</summary>
    Task<bool> LogoutAsync(LogoutRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/auth/cambiar-contrasena — 200 · 401 · 422 (actual incorrecta/política débil/igual a la actual) · 404.
    /// ARCH-02 (auditoría 2026-09-21): la BLL NO construye el wrapper HTTP — el controller lo hace (ARCH-07).</summary>
    Task CambiarContrasenaAsync(Guid usuarioId, CambiarContrasenaRequest request, CancellationToken ct = default);
}
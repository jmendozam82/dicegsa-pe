using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Services;

/// <summary>
/// Sesión MVC (cookie de sesión ASP.NET Core, sin librería externa) — Spec HU-045 § Cimiento.
/// Guarda/lee AccessToken, RefreshToken y UsuarioJson (JSON de UsuarioResponse de HU-003/HU-004).
/// El JWT se guarda en sesión (no en el cookie de autenticación) y lo usa el ApiClient (D-4).
/// </summary>
public class SesionService
{
    private const string AccessTokenKey = "AccessToken";
    private const string RefreshTokenKey = "RefreshToken";
    private const string UsuarioJsonKey = "UsuarioJson";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public SesionService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession? Sesion => _httpContextAccessor.HttpContext?.Session;

    public void GuardarSesion(string accessToken, string refreshToken, UsuarioResponse usuario)
    {
        var sesion = Sesion;
        if (sesion is null)
            return;

        sesion.SetString(AccessTokenKey, accessToken);
        sesion.SetString(RefreshTokenKey, refreshToken);
        sesion.SetString(UsuarioJsonKey, JsonSerializer.Serialize(usuario));
    }

    /// <summary>Actualiza tokens tras un refresh 401 (conserva el usuario).</summary>
    public void ActualizarTokens(string accessToken, string refreshToken)
    {
        var sesion = Sesion;
        if (sesion is null)
            return;

        sesion.SetString(AccessTokenKey, accessToken);
        sesion.SetString(RefreshTokenKey, refreshToken);
    }

    public void CerrarSesion()
    {
        var sesion = Sesion;
        if (sesion is null)
            return;

        sesion.Remove(AccessTokenKey);
        sesion.Remove(RefreshTokenKey);
        sesion.Remove(UsuarioJsonKey);
    }

    public string? ObtenerAccessToken() => Sesion?.GetString(AccessTokenKey);

    public string? ObtenerRefreshToken() => Sesion?.GetString(RefreshTokenKey);

    public UsuarioResponse? ObtenerUsuario()
    {
        var json = Sesion?.GetString(UsuarioJsonKey);
        return string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<UsuarioResponse>(json);
    }
}
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// Autenticación MVC (Spec HU-045 § Cimiento de frontend / AuthController).
/// Login → POST /api/v1/auth/login → tokens en sesión + cookie MVC con claims →
/// redirect a CambiarContrasena si requiereCambioPwd (seed SA) o a /Tenants.
/// Logout → POST /api/v1/auth/logout (best-effort, idempotente D10 HU-004) → CerrarSesion +
/// SignOutAsync. CambiarContrasena → POST /api/v1/auth/cambiar-contrasena (SEC-06: usuarioId
/// del JWT, nunca del body).
/// </summary>
[AllowAnonymous]
public class AuthController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly SesionService _sesionService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IApiClient apiClient, SesionService sesionService, ILogger<AuthController> logger)
    {
        _apiClient = apiClient;
        _sesionService = sesionService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request, string? returnUrl = null)
    {
        try
        {
            var respuesta = await _apiClient.PostAsync<LoginRequest, LoginResponse>("/api/v1/auth/login", request);

            _sesionService.GuardarSesion(respuesta.AccessToken, respuesta.RefreshToken, respuesta.Usuario);

            // Cookie MVC con claims derivados del UsuarioResponse del JWT (D-4).
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, respuesta.Usuario.Id.ToString()),
                new(ClaimTypes.Role, respuesta.Usuario.Rol),
                new(ClaimTypes.Name, respuesta.Usuario.Nombre)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Flag 2: el SA seed requiere cambio de contraseña en el primer login.
            if (respuesta.RequiereCambioPwd)
                return new RedirectToActionResult("CambiarContrasena", "Auth", null);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return new RedirectToActionResult("Index", "Tenants", null);
        }
        catch (UnauthorizedException ex)
        {
            // 401: credenciales inválidas / bloqueado / inactivo / tenant inactivo (mensajes HU-004).
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(request);
        }
        catch (ApiClientException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(request);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var refreshToken = _sesionService.ObtenerRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                // Best-effort: si la API no responde, el logout local igual procede (D10 HU-004).
                await _apiClient.PostAsync<LogoutRequest, object>(
                    "/api/v1/auth/logout",
                    new LogoutRequest { RefreshToken = refreshToken });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout best-effort: fallo al revocar el refresh token en la API.");
        }

        _sesionService.CerrarSesion();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return new RedirectToActionResult("Login", "Auth", null);
    }

    [HttpGet]
    public IActionResult CambiarContrasena()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CambiarContrasena(CambiarContrasenaRequest request)
    {
        try
        {
            await _apiClient.PostAsync<CambiarContrasenaRequest, object>("/api/v1/auth/cambiar-contrasena", request);
            return new RedirectToActionResult("Index", "Tenants", null);
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(request);
        }
        catch (UnauthorizedException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(request);
        }
    }
}
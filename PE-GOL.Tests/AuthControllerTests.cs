using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Tests.Helpers;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para AuthController (MVC) — Spec HU-045 § "Cimiento de frontend" / AuthController
/// (5 casos, tabla #23..#27). TDD fase red (TEST-01): AuthController NO existe aún en
/// PE-GOL.Aplicacion → esta clase NO compila hasta que @FrontendDev lo implemente (rojo esperado).
/// Contrato fijado (spec L388-399):
///   [AllowAnonymous] public class AuthController : Controller
///   AuthController(IApiClient apiClient, SesionService sesionService, ILogger&lt;AuthController&gt; logger)
///   IActionResult Login()  ·  [HttpPost] IActionResult Login(LoginRequest request, string? returnUrl = null)
///   [HttpPost] IActionResult Logout()
///   IActionResult CambiarContrasena()  ·  [HttpPost] IActionResult CambiarContrasena(CambiarContrasenaRequest request)
/// Comportamientos: Login POST → POST /api/v1/auth/login → GuardarSesion(tokens, usuario) →
/// SignInAsync con claims (NameIdentifier=usuario.Id, Role=usuario.Rol, Name=usuario.Nombre) →
/// si RequiereCambioPwd → RedirectToAction("CambiarContrasena","Auth"); si no → RedirectToAction("Index","Tenant");
/// 401 → re-render Login con ModelState error del servidor. Logout POST → POST /api/v1/auth/logout
/// (best-effort) → CerrarSesion() + SignOutAsync → RedirectToAction("Login","Auth").
/// CambiarContrasena POST → POST /api/v1/auth/cambiar-contrasena → RedirectToAction("Index","Tenant").
/// Moq sobre IApiClient + SesionService real con FakeSession (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class AuthControllerTests
{
    private readonly Mock<IApiClient> _apiClient;
    private readonly SesionService _sesion;
    private readonly Mock<IAuthenticationService> _authService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _apiClient = new Mock<IApiClient>();
        _sesion = CrearSesionService();
        _authService = new Mock<IAuthenticationService>();
        _authService
            .Setup(a => a.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        _authService
            .Setup(a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAuthenticationService))).Returns(_authService.Object);
        httpContext.RequestServices = services.Object;

        _controller = new AuthController(_apiClient.Object, _sesion, NullLogger<AuthController>.Instance);
        var tempDataProvider = new Mock<ITempDataProvider>();
        _controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static SesionService CrearSesionService()
    {
        var httpContext = new DefaultHttpContext { Session = new FakeSession() };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return new SesionService(accessor.Object);
    }

    private static UsuarioResponse CrearUsuario()
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Nombre = "Super Admin",
            Correo = "superadmin@pegol.app",
            Rol = "SuperAdmin",
            AreaId = null,
            Estado = "Activo"
        };

    private static LoginResponse CrearLoginResponse(bool requiereCambioPwd = false)
        => new()
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiraEn = DateTimeOffset.UtcNow.AddMinutes(60),
            RefreshExpiraEn = DateTimeOffset.UtcNow.AddDays(7),
            RequiereCambioPwd = requiereCambioPwd,
            Usuario = CrearUsuario()
        };

    private static LoginRequest CrearLoginRequest()
        => new() { Correo = "superadmin@pegol.app", Password = "Password#123" };

    // ─── 23. Login_PostExitoso_GuardaSesionYRedirigeATenants ────────────────

    [Fact]
    public async Task Login_PostExitoso_GuardaSesionYRedirigeATenants()
    {
        // Arrange
        var respuesta = CrearLoginResponse(requiereCambioPwd: false);
        _apiClient
            .Setup(c => c.PostAsync<LoginRequest, LoginResponse>(
                "/api/v1/auth/login",
                It.IsAny<LoginRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(respuesta);

        // Act
        var resultado = await _controller.Login(CrearLoginRequest());

        // Assert: tokens en sesión + cookie emitida con claims + redirect a /Tenants
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Tenant", redirect.ControllerName);

        Assert.Equal("access-1", _sesion.ObtenerAccessToken());
        Assert.Equal("refresh-1", _sesion.ObtenerRefreshToken());
        Assert.Equal(respuesta.Usuario.Id, _sesion.ObtenerUsuario()?.Id);

        _authService.Verify(
            a => a.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.Is<ClaimsPrincipal>(p =>
                    p.FindFirstValue(ClaimTypes.NameIdentifier) == respuesta.Usuario.Id.ToString() &&
                    p.FindFirstValue(ClaimTypes.Role) == respuesta.Usuario.Rol &&
                    p.FindFirstValue(ClaimTypes.Name) == respuesta.Usuario.Nombre),
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    // ─── 24. Login_PostRequiereCambioPwd_RedirigeACambiarContrasena ─────────

    [Fact]
    public async Task Login_PostRequiereCambioPwd_RedirigeACambiarContrasena()
    {
        // Arrange: flag requiereCambioPwd=true (seed SA) → redirect al cambio de contraseña
        var respuesta = CrearLoginResponse(requiereCambioPwd: true);
        _apiClient
            .Setup(c => c.PostAsync<LoginRequest, LoginResponse>(
                "/api/v1/auth/login",
                It.IsAny<LoginRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(respuesta);

        // Act
        var resultado = await _controller.Login(CrearLoginRequest());

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("CambiarContrasena", redirect.ActionName);
        Assert.Equal("Auth", redirect.ControllerName);
    }

    // ─── 25. Login_Post401_RerenderizaConError ──────────────────────────────

    [Fact]
    public async Task Login_Post401_RerenderizaConError()
    {
        // Arrange: credenciales inválidas → UnauthorizedException → re-render con error del servidor
        _apiClient
            .Setup(c => c.PostAsync<LoginRequest, LoginResponse>(
                "/api/v1/auth/login",
                It.IsAny<LoginRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("Credenciales inválidas"));

        // Act
        var resultado = await _controller.Login(CrearLoginRequest());

        // Assert: vista Login re-renderizada con el mensaje del servidor en ModelState
        Assert.IsType<ViewResult>(resultado);
        Assert.True(_controller.ModelState.ErrorCount > 0);
        var errores = _controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        Assert.Contains(errores, e => e.Contains("Credenciales inválidas"));
    }

    // ─── 26. Logout_Post_LimpiaSesionYRedirigeALogin ────────────────────────

    [Fact]
    public async Task Logout_Post_LimpiaSesionYRedirigeALogin()
    {
        // Arrange: sesión activa + POST /api/v1/auth/logout (best-effort, idempotente D10)
        _sesion.GuardarSesion("access-1", "refresh-1", CrearUsuario());
        _apiClient
            .Setup(c => c.PostAsync<LogoutRequest, object>(
                "/api/v1/auth/logout",
                It.IsAny<LogoutRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null!); // ApiResponse<object> con Data null (HU-004)

        // Act
        var resultado = await _controller.Logout();

        // Assert: CerrarSesion() + SignOutAsync + redirect a /Auth/Login
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Auth", redirect.ControllerName);

        Assert.Null(_sesion.ObtenerAccessToken());
        Assert.Null(_sesion.ObtenerRefreshToken());
        Assert.Null(_sesion.ObtenerUsuario());

        _authService.Verify(
            a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    // ─── 27. CambiarContrasena_PostExitoso_RedirigeATenants ─────────────────

    [Fact]
    public async Task CambiarContrasena_PostExitoso_RedirigeATenants()
    {
        // Arrange: POST /api/v1/auth/cambiar-contrasena → éxito → redirect a /Tenants
        _apiClient
            .Setup(c => c.PostAsync<CambiarContrasenaRequest, object>(
                "/api/v1/auth/cambiar-contrasena",
                It.IsAny<CambiarContrasenaRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null!); // ApiResponse<object> con Data null (HU-004)

        // Act
        var resultado = await _controller.CambiarContrasena(new CambiarContrasenaRequest
        {
            ContrasenaActual = "Password#123",
            NuevaContrasena = "Nueva#Password1"
        });

        // Assert: limpia flag requiereCambioPwd (server-side) y redirige a /Tenants
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Tenant", redirect.ControllerName);
    }
}
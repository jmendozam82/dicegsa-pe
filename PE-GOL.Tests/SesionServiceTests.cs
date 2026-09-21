using Microsoft.AspNetCore.Http;
using Moq;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para SesionService — Spec HU-045 § "Cimiento de frontend" (3 casos, tabla #9..#11).
/// TDD fase red (TEST-01): SesionService NO existe aún en PE-GOL.Aplicacion → esta clase NO compila
/// hasta que @FrontendDev lo implemente con la firma exacta documentada en el reporte de @QA.
/// Contrato fijado (spec § Sesión: guarda/lee AccessToken, RefreshToken, UsuarioJson):
///   SesionService(IHttpContextAccessor httpContextAccessor)   — scoped, usa ISession
///   void GuardarSesion(string accessToken, string refreshToken, UsuarioResponse usuario)
///   void ActualizarTokens(string accessToken, string refreshToken)  — refresh 401 (conserva usuario)
///   void CerrarSesion()
///   string? ObtenerAccessToken()
///   string? ObtenerRefreshToken()
///   UsuarioResponse? ObtenerUsuario()   — deserializado de UsuarioJson
/// Claves de sesión: "AccessToken", "RefreshToken", "UsuarioJson" (JSON de UsuarioResponse).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-05).
/// </summary>
public class SesionServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (SesionService service, FakeSession session) CrearServicio()
    {
        var session = new FakeSession();
        var httpContext = new DefaultHttpContext { Session = session };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return (new SesionService(accessor.Object), session);
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

    // ─── 9. GuardarSesion_AlmacenaTokensYUsuario ────────────────────────────

    [Fact]
    public void GuardarSesion_AlmacenaTokensYUsuario()
    {
        // Arrange
        var (service, _) = CrearServicio();
        var usuario = CrearUsuario();

        // Act
        service.GuardarSesion("access-1", "refresh-1", usuario);

        // Assert: access/refresh/usuario recuperables desde la sesión
        Assert.Equal("access-1", service.ObtenerAccessToken());
        Assert.Equal("refresh-1", service.ObtenerRefreshToken());
        var recuperado = service.ObtenerUsuario();
        Assert.NotNull(recuperado);
        Assert.Equal(usuario.Id, recuperado.Id);
        Assert.Equal(usuario.Nombre, recuperado.Nombre);
        Assert.Equal(usuario.Correo, recuperado.Correo);
        Assert.Equal(usuario.Rol, recuperado.Rol);
    }

    // ─── 10. CerrarSesion_LimpiaTokensYUsuario ──────────────────────────────

    [Fact]
    public void CerrarSesion_LimpiaTokensYUsuario()
    {
        // Arrange
        var (service, _) = CrearServicio();
        service.GuardarSesion("access-1", "refresh-1", CrearUsuario());

        // Act
        service.CerrarSesion();

        // Assert: claves eliminadas
        Assert.Null(service.ObtenerAccessToken());
        Assert.Null(service.ObtenerRefreshToken());
        Assert.Null(service.ObtenerUsuario());
    }

    // ─── 11. ObtenerAccessToken_SinSesion_RetornaNull ───────────────────────

    [Fact]
    public void ObtenerAccessToken_SinSesion_RetornaNull()
    {
        // Arrange: servicio recién creado, sin GuardarSesion previo
        var (service, _) = CrearServicio();

        // Act & Assert
        Assert.Null(service.ObtenerAccessToken());
        Assert.Null(service.ObtenerRefreshToken());
        Assert.Null(service.ObtenerUsuario());
    }
}
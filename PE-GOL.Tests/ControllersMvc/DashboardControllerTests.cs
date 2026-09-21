using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses.Dashboard;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para DashboardController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#40-41). Tanda T2 (implementación completa): happy path GET de las 2 acciones +
/// autorización por reflexión (4 tests). Contrato real (HU-015/016 § UI): clase [Authorize] +
/// acción Gerente [Authorize(Roles = "Gerente")] (RN-006) y acción JefeArea
/// [Authorize(Roles = "JefeArea")] (SEC-07 — la API filtra por AreaId del JWT).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class DashboardControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (DashboardController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new DashboardController(apiClient.Object, NullLogger<DashboardController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static TableroGerenteResponse CrearTableroGerente() => new()
    {
        CicloId = Guid.NewGuid(),
        CicloNombre = "PE 2026",
        AñoFiscal = 2026,
        Paneles = [],
        Totales = new TotalesConsolidadosResponse(),
        AreasConAlertaActiva = 0
    };

    private static TableroJefeAreaResponse CrearTableroJefeArea() => new()
    {
        CicloId = Guid.NewGuid(),
        CicloNombre = "PE 2026",
        AñoFiscal = 2026,
        AreaId = Guid.NewGuid(),
        AreaCodigo = "GOL1",
        AreaNombre = "CEDIS FARMA",
        Tarjetas = new TarjetasResumenResponse(),
        Semaforo = new SemaforoAreaResponse()
    };

    // ─── 40a. Gerente_ConDatos_RetornaVistaConModelo ────────────────────────

    [Fact]
    public async Task Gerente_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: GET /api/v1/dashboard/gerente devuelve el tablero consolidado
        var (controller, apiClient) = CrearController();
        var tablero = CrearTableroGerente();
        apiClient
            .Setup(c => c.GetAsync<TableroGerenteResponse>(
                "/api/v1/dashboard/gerente",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tablero);

        // Act
        var resultado = await controller.Gerente();

        // Assert: DashboardGerenteViewModel con el tablero mapeado
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<DashboardGerenteViewModel>(view.Model);
        Assert.NotNull(modelo.Tablero);
        Assert.Equal("PE 2026", modelo.Tablero.CicloNombre);
        Assert.Equal(2026, modelo.Tablero.AñoFiscal);
    }

    // ─── 40b. JefeArea_ConDatos_RetornaVistaConModelo ───────────────────────

    [Fact]
    public async Task JefeArea_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: GET /api/v1/dashboard/jefe-area devuelve el tablero del área del JEF
        var (controller, apiClient) = CrearController();
        var tablero = CrearTableroJefeArea();
        apiClient
            .Setup(c => c.GetAsync<TableroJefeAreaResponse>(
                "/api/v1/dashboard/jefe-area",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tablero);

        // Act
        var resultado = await controller.JefeArea();

        // Assert: DashboardJefeAreaViewModel con el tablero mapeado
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<DashboardJefeAreaViewModel>(view.Model);
        Assert.NotNull(modelo.Tablero);
        Assert.Equal("GOL1", modelo.Tablero.AreaCodigo);
        Assert.Equal("CEDIS FARMA", modelo.Tablero.AreaNombre);
    }

    // ─── 41a. Gerente_TieneAuthorizeGerente ─────────────────────────────────

    [Fact]
    public void Gerente_TieneAuthorizeGerente()
    {
        // Arrange/Act: clase [Authorize] (autenticado) + acción Gerente [Authorize(Roles = "Gerente")]
        Assert.True(AuthorizeHelper.TieneAuthorizeClase(typeof(DashboardController)));
        var roles = AuthorizeHelper.RolesDeAccion(typeof(DashboardController), nameof(DashboardController.Gerente));

        // Assert: solo Gerente (RN-006 — HU-016 § UI, doble capa MVC + API)
        Assert.Contains("Gerente", roles);
    }

    // ─── 41b. JefeArea_TieneAuthorizeJefeArea ───────────────────────────────

    [Fact]
    public void JefeArea_TieneAuthorizeJefeArea()
    {
        // Arrange/Act: clase [Authorize] (autenticado) + acción JefeArea [Authorize(Roles = "JefeArea")]
        Assert.True(AuthorizeHelper.TieneAuthorizeClase(typeof(DashboardController)));
        var roles = AuthorizeHelper.RolesDeAccion(typeof(DashboardController), nameof(DashboardController.JefeArea));

        // Assert: solo JefeArea (SEC-07 — HU-015 § UI, doble capa MVC + API)
        Assert.Contains("JefeArea", roles);
    }
}
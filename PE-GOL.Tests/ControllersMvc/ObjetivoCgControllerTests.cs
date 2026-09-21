using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para ObjetivoCgController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#42-43). Tanda T2 (implementación completa): happy path GET de las 2 acciones principales
/// (Index JEF + Consolidado GER) + autorización por reflexión (4 tests). Contrato real (HU-017/018
/// § UI): clase [Authorize] + acción Index [Authorize(Roles = "JefeArea")] (SEC-07) y acción
/// Consolidado [Authorize(Roles = "Gerente")] (D-B — visibilidad global).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class ObjetivoCgControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (ObjetivoCgController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new ObjetivoCgController(apiClient.Object, NullLogger<ObjetivoCgController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static CicloResponse CrearCiclo(Guid id, string nombre = "PE 2026", string estado = "Activo")
        => new()
        {
            Id = id,
            TenantId = Guid.NewGuid(),
            Nombre = nombre,
            AñoFiscal = 2026,
            MesInicio = 1,
            Estado = estado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AreaResponse CrearArea(Guid cicloId) => new()
    {
        Id = Guid.NewGuid(),
        CicloId = cicloId,
        TenantId = Guid.NewGuid(),
        Codigo = "GOL1",
        Nombre = "CEDIS FARMA",
        Orden = 1,
        Activa = true,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-20),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static PilarResponse CrearPilar(Guid cicloId) => new()
    {
        Id = Guid.NewGuid(),
        CicloId = cicloId,
        TenantId = Guid.NewGuid(),
        Codigo = "PEC-1",
        Nombre = "Crecimiento",
        Orden = 1,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ObjetivoCgResponse CrearCg() => new()
    {
        Id = Guid.NewGuid(),
        CicloId = Guid.NewGuid(),
        PilarId = Guid.NewGuid(),
        PilarNombre = "Crecimiento",
        Codigo = "GOL1.CG1",
        Descripcion = "Incrementar ventas 15%",
        TrimestreObjetivo = "Q1",
        Progreso = 50m,
        Semaforo = "Amarillo",
        AreaId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddDays(-10),
        UpdatedAt = DateTime.UtcNow
    };

    // ─── 42a. Index_ConDatos_RetornaVistaConModelo ──────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: ciclo activo + CGs del área del JEF (SEC-07 — la API filtra)
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        var cgs = new List<ObjetivoCgResponse> { CrearCg(), CrearCg() };
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgResponse>>(
                "/api/v1/objetivos-cg",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cgs);

        // Act
        var resultado = await controller.Index();

        // Assert: ObjetivoCgIndexViewModel con ciclo activo + CGs mapeados
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<ObjetivoCgIndexViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("GOL1.CG1", modelo.Items[0].Codigo);
    }

    // ─── 42b. Consolidado_ConDatos_RetornaVistaConModelo ────────────────────

    [Fact]
    public async Task Consolidado_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: ciclo activo + catálogos (áreas/pilares) + CGs consolidados
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CrearArea(cicloActivo.Id)]);
        apiClient
            .Setup(c => c.GetAsync<List<PilarResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/pilares",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CrearPilar(cicloActivo.Id)]);
        var consolidados = new List<ObjetivoCgConsolidadoResponse>
        {
            new() { Id = Guid.NewGuid(), AreaId = Guid.NewGuid(), AreaNombre = "CEDIS FARMA", Codigo = "GOL1.CG1", Progreso = 50m, Semaforo = "Amarillo" }
        };
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgConsolidadoResponse>>(
                "/api/v1/objetivos-cg/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(consolidados);

        // Act
        var resultado = await controller.Consolidado(new ObjetivoCgFilterRequest());

        // Assert: ObjetivoCgConsolidadoViewModel con ciclo activo + catálogos + items
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<ObjetivoCgConsolidadoViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Single(modelo.Areas);
        Assert.Single(modelo.Pilares);
        Assert.Single(modelo.Items);
        Assert.Equal("GOL1.CG1", modelo.Items[0].Codigo);
    }

    // ─── 43a. Index_TieneAuthorizeJefeArea ──────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeJefeArea()
    {
        // Arrange/Act: clase [Authorize] (autenticado) + acción Index [Authorize(Roles = "JefeArea")]
        Assert.True(AuthorizeHelper.TieneAuthorizeClase(typeof(ObjetivoCgController)));
        var roles = AuthorizeHelper.RolesDeAccion(typeof(ObjetivoCgController), nameof(ObjetivoCgController.Index));

        // Assert: solo JefeArea (SEC-07 — HU-017 § UI, doble capa MVC + API)
        Assert.Contains("JefeArea", roles);
    }

    // ─── 43b. Consolidado_TieneAuthorizeGerente ─────────────────────────────

    [Fact]
    public void Consolidado_TieneAuthorizeGerente()
    {
        // Arrange/Act: clase [Authorize] (autenticado) + acción Consolidado [Authorize(Roles = "Gerente")]
        Assert.True(AuthorizeHelper.TieneAuthorizeClase(typeof(ObjetivoCgController)));
        var roles = AuthorizeHelper.RolesDeAccion(typeof(ObjetivoCgController), nameof(ObjetivoCgController.Consolidado));

        // Assert: solo Gerente (D-B — HU-018 § UI, doble capa MVC + API)
        Assert.Contains("Gerente", roles);
    }
}
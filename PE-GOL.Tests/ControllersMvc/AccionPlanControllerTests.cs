using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para AccionPlanController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#44-45). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-019/020 § UI): clase [Authorize(Roles = "JefeArea,Gerente")] (HU-019 nota de
/// acceso); acción Index [Authorize(Roles = "JefeArea")] (escrituras solo JEF — RN-007; el Gerente
/// es solo lectura). Index(Guid? objetivoCgId) → View(AccionPlanIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class AccionPlanControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (AccionPlanController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new AccionPlanController(apiClient.Object, NullLogger<AccionPlanController>.Instance);

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

    private static AccionPlanResponse CrearAccion(Guid objetivoCgId) => new()
    {
        Id = Guid.NewGuid(),
        ObjetivoCgId = objetivoCgId,
        Codigo = "GOL1.CG1.A1",
        Descripcion = "Lanzar campaña de marketing",
        ResponsableId = Guid.NewGuid(),
        ResponsableNombre = "Juan Pérez",
        FechaInicio = DateTime.Today.AddDays(-10),
        FechaVencimiento = DateTime.Today.AddDays(20),
        Clasificacion = "Estrategica",
        TipoPresupuesto = "CAPEX",
        Peso = 0.5m,
        Progreso = 40m,
        PuntuacionPonderada = 20m,
        Status = "EnProgreso",
        Orden = 1,
        CreatedAt = DateTime.UtcNow.AddDays(-10),
        UpdatedAt = DateTime.UtcNow
    };

    // ─── 44. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: ciclo activo + CGs del área + acciones del CG seleccionado
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        var cg = CrearCg();
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgResponse>>(
                "/api/v1/objetivos-cg",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cg]);
        var acciones = new List<AccionPlanResponse> { CrearAccion(cg.Id), CrearAccion(cg.Id) };
        apiClient
            .Setup(c => c.GetAsync<List<AccionPlanResponse>>(
                $"/api/v1/objetivos-cg/{cg.Id}/acciones",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(acciones);

        // Act
        var resultado = await controller.Index(cg.Id);

        // Assert: AccionPlanIndexViewModel con ciclo activo + CG + acciones mapeadas
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AccionPlanIndexViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(cg.Id, modelo.ObjetivoCgId);
        Assert.Equal("GOL1.CG1", modelo.ObjetivoCgCodigo);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("GOL1.CG1.A1", modelo.Items[0].Codigo);
    }

    // ─── 45. Index_TieneAuthorizeJefeArea ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeJefeArea()
    {
        // Arrange/Act: clase [Authorize(Roles = "JefeArea,Gerente")] (HU-019 nota de acceso) +
        // acción Index [Authorize(Roles = "JefeArea")] (escrituras solo JEF — RN-007)
        var rolesClase = AuthorizeHelper.RolesDeClase(typeof(AccionPlanController));
        Assert.Contains("JefeArea", rolesClase);
        Assert.Contains("Gerente", rolesClase);

        var rolesAccion = AuthorizeHelper.RolesDeAccion(typeof(AccionPlanController), nameof(AccionPlanController.Index));

        // Assert: la acción Index (listado con selector de CG) es solo JefeArea
        Assert.Contains("JefeArea", rolesAccion);
    }
}
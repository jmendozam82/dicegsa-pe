using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para AreaController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#32-33). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-009 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a nivel de clase
/// (GET multi-rol — JEF solo su área, SEC-07); crear/editar/desactivar solo AdminTenant.
/// Index(Guid? cicloId) → View(AreasIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class AreaControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (AreaController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new AreaController(apiClient.Object, NullLogger<AreaController>.Instance);

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

    private static AreaResponse CrearArea(Guid cicloId, string codigo = "GOL1", string nombre = "CEDIS FARMA")
        => new()
        {
            Id = Guid.NewGuid(),
            CicloId = cicloId,
            TenantId = Guid.NewGuid(),
            Codigo = codigo,
            Nombre = nombre,
            ResponsableId = Guid.NewGuid(),
            ResponsableNombre = "Juan Pérez",
            Orden = 1,
            Activa = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-20),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ─── 32. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: catálogo de ciclos + áreas del ciclo seleccionado
        var (controller, apiClient) = CrearController();
        var cicloId = Guid.NewGuid();
        var ciclos = new List<CicloResponse> { CrearCiclo(cicloId, "PE 2026", "Activo") };
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclos);
        var areas = new List<AreaResponse> { CrearArea(cicloId, "GOL1", "CEDIS FARMA"), CrearArea(cicloId, "GOL2", "VENTAS") };
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                $"/api/v1/ciclos/{cicloId}/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(areas);

        // Act
        var resultado = await controller.Index(cicloId);

        // Assert: AreasIndexViewModel con ciclo + áreas mapeadas
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AreasIndexViewModel>(view.Model);
        Assert.Equal(cicloId, modelo.CicloId);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal("Activo", modelo.CicloEstado);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("GOL1", modelo.Items[0].Codigo);
    }

    // ─── 33. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(AreaController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-009 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
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
/// Tests de humo para CicloController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#30-31). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-007/008 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a nivel de
/// clase (RN-007); crear/editar/activar/clonar/umbrales solo AdminTenant; cerrar solo Gerente.
/// Index() → View(CiclosIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class CicloControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (CicloController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new CicloController(apiClient.Object, NullLogger<CicloController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static CicloResponse CrearCiclo(string nombre = "PE 2026", string estado = "Activo")
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Nombre = nombre,
            AñoFiscal = 2026,
            MesInicio = 1,
            Estado = estado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ─── 30. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: GET /api/v1/ciclos devuelve el listado del tenant
        var (controller, apiClient) = CrearController();
        var ciclos = new List<CicloResponse> { CrearCiclo("PE 2026", "Activo"), CrearCiclo("PE 2025", "Cerrado") };
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclos);

        // Act
        var resultado = await controller.Index();

        // Assert: CiclosIndexViewModel con los ciclos mapeados
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<CiclosIndexViewModel>(view.Model);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("PE 2026", modelo.Items[0].Nombre);
        Assert.Equal("Activo", modelo.Items[0].Estado);
    }

    // ─── 31. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(CicloController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-007 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
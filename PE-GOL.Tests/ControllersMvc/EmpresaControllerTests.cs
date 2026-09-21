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
/// Tests de humo para EmpresaController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#28-29). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-006 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a nivel de clase
/// (GET multi-rol RN-007); PUT/logo solo AdminTenant. Index() → View("Configuracion", EmpresaViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class EmpresaControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (EmpresaController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new EmpresaController(apiClient.Object, NullLogger<EmpresaController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static EmpresaResponse CrearEmpresa() => new()
    {
        TenantId = Guid.NewGuid(),
        Nombre = "Dicegsa",
        Eslogan = "Excelencia que transforma",
        ZonaHoraria = "America/Managua",
        Descripcion = "Empresa demo",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ─── 28. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: GET /api/v1/empresa devuelve la configuración del tenant
        var (controller, apiClient) = CrearController();
        var empresa = CrearEmpresa();
        apiClient
            .Setup(c => c.GetAsync<EmpresaResponse>(
                "/api/v1/empresa",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(empresa);

        // Act
        var resultado = await controller.Index();

        // Assert: vista "Configuracion" con EmpresaViewModel mapeado
        var view = Assert.IsType<ViewResult>(resultado);
        Assert.Equal("Configuracion", view.ViewName);
        var modelo = Assert.IsType<EmpresaViewModel>(view.Model);
        Assert.Equal(empresa.Nombre, modelo.Nombre);
        Assert.Equal(empresa.Eslogan, modelo.Eslogan);
        Assert.Equal(empresa.ZonaHoraria, modelo.ZonaHoraria);
        Assert.NotEmpty(modelo.Zonas); // catálogo IANA cargado para el select
    }

    // ─── 29. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (GET multi-rol RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(EmpresaController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-006 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
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
/// Tests de humo para FilosofiaController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#36-37). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-011/012 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a nivel de
/// clase (GET multi-rol — SEC-07 NO APLICA, D-E); PUT solo Gerente (RN-006).
/// Index(Guid? cicloId) → View(FilosofiaViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class FilosofiaControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (FilosofiaController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new FilosofiaController(apiClient.Object, NullLogger<FilosofiaController>.Instance);

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

    private static FilosofiaResponse CrearFilosofia(Guid cicloId) => new()
    {
        Id = Guid.NewGuid(),
        CicloId = cicloId,
        TenantId = Guid.NewGuid(),
        Vision = "<p>Ser líderes en el mercado</p>",
        Mision = "<p>Innovar cada día</p>",
        Valores = ["Integridad", "Excelencia", "Trabajo en equipo"],
        UpdatedBy = Guid.NewGuid(),
        UpdatedByNombre = "Gerente Demo",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ─── 36. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: catálogo de ciclos + filosofía del ciclo seleccionado
        var (controller, apiClient) = CrearController();
        var cicloId = Guid.NewGuid();
        var ciclos = new List<CicloResponse> { CrearCiclo(cicloId, "PE 2026", "Activo") };
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclos);
        var filosofia = CrearFilosofia(cicloId);
        apiClient
            .Setup(c => c.GetAsync<FilosofiaResponse>(
                $"/api/v1/ciclos/{cicloId}/filosofia",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(filosofia);

        // Act
        var resultado = await controller.Index(cicloId);

        // Assert: FilosofiaViewModel con visión/misión/valores mapeados
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<FilosofiaViewModel>(view.Model);
        Assert.Equal(cicloId, modelo.CicloId);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(filosofia.Vision, modelo.Vision);
        Assert.Equal(filosofia.Mision, modelo.Mision);
        Assert.Equal(3, modelo.Valores.Count);
        Assert.Equal("Gerente Demo", modelo.UpdatedByNombre);
    }

    // ─── 37. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(FilosofiaController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-011 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
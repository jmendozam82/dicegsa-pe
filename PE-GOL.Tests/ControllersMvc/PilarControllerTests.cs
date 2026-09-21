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
/// Tests de humo para PilarController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#38-39). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-013/014 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a nivel de
/// clase (GET multi-rol — SEC-07 NO APLICA, D-E); POST/PUT/DELETE solo Gerente (RN-006).
/// Index(Guid? cicloId) → View(PilaresIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class PilarControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (PilarController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new PilarController(apiClient.Object, NullLogger<PilarController>.Instance);

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

    private static PilarResponse CrearPilar(Guid cicloId, string codigo = "PEC-1", string nombre = "Crecimiento")
        => new()
        {
            Id = Guid.NewGuid(),
            CicloId = cicloId,
            TenantId = Guid.NewGuid(),
            Codigo = codigo,
            Nombre = nombre,
            EstrategiaVictoria = "Ganar participación de mercado",
            Orden = 1,
            TotalObjetivosCg = 2,
            TotalOkrs = 4,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ─── 38. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: catálogo de ciclos + pilares del ciclo seleccionado
        var (controller, apiClient) = CrearController();
        var cicloId = Guid.NewGuid();
        var ciclos = new List<CicloResponse> { CrearCiclo(cicloId, "PE 2026", "Activo") };
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclos);
        var pilares = new List<PilarResponse>
        {
            CrearPilar(cicloId, "PEC-1", "Crecimiento"),
            CrearPilar(cicloId, "PEC-2", "Eficiencia")
        };
        apiClient
            .Setup(c => c.GetAsync<List<PilarResponse>>(
                $"/api/v1/ciclos/{cicloId}/pilares",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pilares);

        // Act
        var resultado = await controller.Index(cicloId);

        // Assert: PilaresIndexViewModel con ciclo + pilares mapeados
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PilaresIndexViewModel>(view.Model);
        Assert.Equal(cicloId, modelo.CicloId);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("PEC-1", modelo.Items[0].Codigo);
        Assert.Equal("Crecimiento", modelo.Items[0].Nombre);
    }

    // ─── 39. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(PilarController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-013 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
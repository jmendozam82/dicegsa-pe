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
/// Tests de humo para ResponsableController (MVC) — Spec HU-045 § "Tests de humo por controller
/// MVC nuevo" (#34-35). Tanda T2 (implementación completa): happy path GET + autorización por
/// reflexión. Contrato real (HU-010 § UI): [Authorize(Roles = "AdminTenant,Gerente,JefeArea")] a
/// nivel de clase (GET multi-rol — JEF solo su responsable, SEC-07); crear/reasignar/desactivar
/// solo AdminTenant. Index(Guid? cicloId) → View(ResponsablesIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class ResponsableControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (ResponsableController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new ResponsableController(apiClient.Object, NullLogger<ResponsableController>.Instance);

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

    private static ResponsableResponse CrearResponsable(Guid cicloId, string nombre = "Juan Pérez")
        => new()
        {
            Id = Guid.NewGuid(),
            CicloId = cicloId,
            TenantId = Guid.NewGuid(),
            Nombre = nombre,
            Correo = "juan.perez@empresa.com",
            Rol = "JefeArea",
            Estado = "Activo",
            AreaId = Guid.NewGuid(),
            AreaCodigo = "GOL1",
            AreaNombre = "CEDIS FARMA",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-20),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // ─── 34. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: catálogo de ciclos + responsables del ciclo seleccionado
        var (controller, apiClient) = CrearController();
        var cicloId = Guid.NewGuid();
        var ciclos = new List<CicloResponse> { CrearCiclo(cicloId, "PE 2026", "Activo") };
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclos);
        var responsables = new List<ResponsableResponse>
        {
            CrearResponsable(cicloId, "Juan Pérez"),
            CrearResponsable(cicloId, "María López")
        };
        apiClient
            .Setup(c => c.GetAsync<List<ResponsableResponse>>(
                $"/api/v1/ciclos/{cicloId}/responsables",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsables);

        // Act
        var resultado = await controller.Index(cicloId);

        // Assert: ResponsablesIndexViewModel con ciclo + responsables mapeados
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<ResponsablesIndexViewModel>(view.Model);
        Assert.Equal(cicloId, modelo.CicloId);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("Juan Pérez", modelo.Items[0].Nombre);
        Assert.Equal("GOL1", modelo.Items[0].AreaCodigo);
    }

    // ─── 35. Index_TieneAuthorizeMultiRol ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeMultiRol()
    {
        // Arrange/Act: atributo [Authorize(Roles)] a nivel de clase (RN-007)
        var roles = AuthorizeHelper.RolesDeClase(typeof(ResponsableController));

        // Assert: AdminTenant/Gerente/JefeArea (HU-010 § UI — doble capa MVC + API)
        Assert.Contains("AdminTenant", roles);
        Assert.Contains("Gerente", roles);
        Assert.Contains("JefeArea", roles);
    }
}
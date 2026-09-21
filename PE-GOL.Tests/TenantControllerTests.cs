using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para TenantController (MVC) — Spec HU-045 § "Lógica del controlador MVC"
/// (11 casos, tabla #12..#22). TDD fase red (TEST-01): TenantController NO existe aún en
/// PE-GOL.Aplicacion → esta clase NO compila hasta que @FrontendDev lo implemente (rojo esperado).
/// Contrato fijado (spec L342-386):
///   [Authorize(Roles = "SuperAdmin")] public class TenantController : Controller
///   TenantController(IApiClient apiClient, ILogger&lt;TenantController&gt; logger)
///   IActionResult Index(int page = 1, int pageSize = 10, string? estado = null, Guid? planId = null)
///   IActionResult Create()  ·  [HttpPost] IActionResult Create(TenantCreateRequest request)
///   IActionResult Edit(Guid id)  ·  [HttpPost] IActionResult Edit(Guid id, TenantUpdateRequest request)
///   [HttpPost] IActionResult Activar(Guid id)  ·  [HttpPost] IActionResult Desactivar(Guid id)
/// ViewModels (PE_GOL.Aplicacion.Models):
///   TenantsIndexViewModel { Items, Page, PageSize, Total, TotalPages, EstadoFiltro, PlanIdFiltro, Planes }
///   TenantFormViewModel { Id, Nombre, Descripcion, PlanId, LogoUrl, Eslogan, ZonaHoraria, Estado, Planes }
/// Comportamientos: catálogo de planes vía GET /api/v1/planes (fallo → warning + catálogo vacío);
/// saneo page≥1 y pageSize 1..100; query params page/pageSize/estado/planId a GET /api/v1/tenants;
/// éxito → TempData["Success"] + RedirectToAction("Index"); 400/422 → ModelState con Errors del
/// servidor + re-render; 404 → NotFound(); 422 en activar/desactivar → TempData["Error"] + redirect.
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
/// </summary>
public class TenantControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (TenantController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new TenantController(apiClient.Object, NullLogger<TenantController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static TenantResponse CrearTenant(string nombre = "Acme Corp", string estado = "Activo")
        => new()
        {
            Id = Guid.NewGuid(),
            Nombre = nombre,
            Descripcion = "Empresa demo",
            PlanId = Guid.NewGuid(),
            PlanNombre = "Básico",
            ZonaHoraria = "America/Managua",
            Estado = estado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PlanResponse CrearPlan(string nombre = "Básico")
        => new()
        {
            Id = Guid.NewGuid(),
            Nombre = nombre,
            Descripcion = null,
            MaxAreas = 5,
            MaxUsuarios = 10,
            MaxCiclosActivos = 1,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60)
        };

    private static void ConfigurarCatalogoPlanes(Mock<IApiClient> apiClient, int cantidad = 2)
    {
        var planes = Enumerable.Range(0, cantidad).Select(i => CrearPlan($"Plan {i + 1}")).ToList();
        apiClient
            .Setup(c => c.GetAsync<List<PlanResponse>>(
                "/api/v1/planes",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(planes);
    }

    // ─── 12. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange
        var (controller, apiClient) = CrearController();
        ConfigurarCatalogoPlanes(apiClient, cantidad: 2);
        var items = new List<TenantResponse> { CrearTenant("Acme Corp"), CrearTenant("Beta SA") };
        apiClient
            .Setup(c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TenantResponse>
            {
                Items = items,
                Page = 1,
                PageSize = 10,
                Total = 2,
                TotalPages = 1
            });

        // Act
        var resultado = await controller.Index();

        // Assert: PagedResult mapeado a TenantsIndexViewModel
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<TenantsIndexViewModel>(view.Model);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal(1, modelo.Page);
        Assert.Equal(10, modelo.PageSize);
        Assert.Equal(2, modelo.Total);
        Assert.Equal(1, modelo.TotalPages);
        Assert.Equal(2, modelo.Planes.Count); // catálogo de planes cargado para el filtro
    }

    // ─── 13. Index_SinDatos_RetornaVistaConItemsVacios ──────────────────────

    [Fact]
    public async Task Index_SinDatos_RetornaVistaConItemsVacios()
    {
        // Arrange: Items.Count==0 → la vista renderiza .empty-state (UX-05)
        var (controller, apiClient) = CrearController();
        ConfigurarCatalogoPlanes(apiClient);
        apiClient
            .Setup(c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TenantResponse> { Items = [], Page = 1, PageSize = 10, Total = 0, TotalPages = 0 });

        // Act
        var resultado = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<TenantsIndexViewModel>(view.Model);
        Assert.Empty(modelo.Items);
        Assert.Equal(0, modelo.Total);
    }

    // ─── 14. Index_ConFiltros_EnviaQueryParams ──────────────────────────────

    [Fact]
    public async Task Index_ConFiltros_EnviaQueryParams()
    {
        // Arrange
        var (controller, apiClient) = CrearController();
        ConfigurarCatalogoPlanes(apiClient);
        var planId = Guid.NewGuid();
        apiClient
            .Setup(c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TenantResponse> { Items = [], Page = 1, PageSize = 10, Total = 0, TotalPages = 0 });

        // Act
        await controller.Index(page: 1, pageSize: 10, estado: "Activo", planId: planId);

        // Assert: estado/planId propagados como query params al ApiClient
        apiClient.Verify(
            c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.Is<IDictionary<string, string?>?>(q =>
                    q != null &&
                    q["estado"] == "Activo" &&
                    q["planId"] == planId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 15. Index_PageSizeMayor100_TruncaA100 ──────────────────────────────

    [Fact]
    public async Task Index_PageSizeMayor100_TruncaA100()
    {
        // Arrange: saneo del límite (espejo BLL HU-001: pageSize 1..100, default 10)
        var (controller, apiClient) = CrearController();
        ConfigurarCatalogoPlanes(apiClient);
        apiClient
            .Setup(c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<TenantResponse> { Items = [], Page = 1, PageSize = 100, Total = 0, TotalPages = 0 });

        // Act
        await controller.Index(page: 1, pageSize: 500);

        // Assert: pageSize truncado a 100 antes de llegar al ApiClient
        apiClient.Verify(
            c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.Is<IDictionary<string, string?>?>(q => q != null && q["pageSize"] == "100"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        apiClient.Verify(
            c => c.GetAsync<PagedResult<TenantResponse>>(
                "/api/v1/tenants",
                It.Is<IDictionary<string, string?>?>(q => q != null && q["pageSize"] == "500"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 16. Create_PostExitoso_RedirigeAIndexConMensaje ────────────────────

    [Fact]
    public async Task Create_PostExitoso_RedirigeAIndexConMensaje()
    {
        // Arrange: 201 → TempData["Success"] + redirect a Index
        var (controller, apiClient) = CrearController();
        var tenant = CrearTenant("Acme Corp");
        apiClient
            .Setup(c => c.PostAsync<TenantCreateRequest, TenantResponse>(
                "/api/v1/tenants",
                It.IsAny<TenantCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        var request = new TenantCreateRequest { Nombre = "Acme Corp", PlanId = tenant.PlanId };

        // Act
        var resultado = await controller.Create(request);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("creado", controller.TempData["Success"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ─── 17. Create_Post422_RerenderizaConErroresDelServidor ────────────────

    [Fact]
    public async Task Create_Post422_RerenderizaConErroresDelServidor()
    {
        // Arrange: ApiClientException 422 → ModelState con Errors del servidor (CA #5)
        var (controller, apiClient) = CrearController();
        ConfigurarCatalogoPlanes(apiClient); // el re-render recarga el catálogo para el select
        apiClient
            .Setup(c => c.PostAsync<TenantCreateRequest, TenantResponse>(
                "/api/v1/tenants",
                It.IsAny<TenantCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(
                422,
                "Validación fallida",
                ["El nombre ya existe en la plataforma", "El plan no existe"]));
        var request = new TenantCreateRequest { Nombre = "Acme Corp", PlanId = Guid.NewGuid() };

        // Act
        var resultado = await controller.Create(request);

        // Assert: re-render de la vista con los errores del servidor en ModelState
        Assert.IsType<ViewResult>(resultado);
        Assert.True(controller.ModelState.ErrorCount > 0);
        var errores = controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        Assert.Contains(errores, e => e.Contains("El nombre ya existe en la plataforma"));
        Assert.Contains(errores, e => e.Contains("El plan no existe"));
    }

    // ─── 18. Edit_GetTenantInexistente_RetornaNotFound ──────────────────────

    [Fact]
    public async Task Edit_GetTenantInexistente_RetornaNotFound()
    {
        // Arrange: ApiClientException 404 → NotFound() (página 404 estándar)
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        apiClient
            .Setup(c => c.GetAsync<TenantResponse>(
                $"/api/v1/tenants/{id}",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "Tenant no encontrado", ["Tenant no encontrado"]));

        // Act
        var resultado = await controller.Edit(id);

        // Assert
        Assert.IsType<NotFoundResult>(resultado);
    }

    // ─── 19. Edit_PostExitoso_RedirigeAIndex ────────────────────────────────

    [Fact]
    public async Task Edit_PostExitoso_RedirigeAIndex()
    {
        // Arrange: 200 → TempData["Success"] + redirect a Index
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        var tenant = CrearTenant("Nuevo Nombre");
        apiClient
            .Setup(c => c.PutAsync<TenantUpdateRequest, TenantResponse>(
                $"/api/v1/tenants/{id}",
                It.IsAny<TenantUpdateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        var request = new TenantUpdateRequest { Nombre = "Nuevo Nombre", PlanId = tenant.PlanId };

        // Act
        var resultado = await controller.Edit(id, request);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(controller.TempData["Success"]);
    }

    // ─── 20. Activar_Exitoso_RedirigeConMensaje ─────────────────────────────

    [Fact]
    public async Task Activar_Exitoso_RedirigeConMensaje()
    {
        // Arrange: POST /api/v1/tenants/{id}/activar → TempData["Success"] + redirect
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        apiClient
            .Setup(c => c.PostAsync<TenantResponse>(
                $"/api/v1/tenants/{id}/activar",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearTenant("Acme Corp", "Activo"));

        // Act
        var resultado = await controller.Activar(id);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("activado", controller.TempData["Success"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ─── 21. Desactivar_Exitoso_RedirigeConMensaje ──────────────────────────

    [Fact]
    public async Task Desactivar_Exitoso_RedirigeConMensaje()
    {
        // Arrange: POST /api/v1/tenants/{id}/desactivar → TempData["Success"] + redirect
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        apiClient
            .Setup(c => c.PostAsync<TenantResponse>(
                $"/api/v1/tenants/{id}/desactivar",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearTenant("Acme Corp", "Inactivo"));

        // Act
        var resultado = await controller.Desactivar(id);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("desactivado", controller.TempData["Success"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ─── 22. Desactivar_422_MuestraErrorFlash ───────────────────────────────

    [Fact]
    public async Task Desactivar_422_MuestraErrorFlash()
    {
        // Arrange: 422 (ya inactivo) → TempData["Error"] con el mensaje del servidor + redirect
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        apiClient
            .Setup(c => c.PostAsync<TenantResponse>(
                $"/api/v1/tenants/{id}/desactivar",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "El tenant ya está desactivado", ["El tenant ya está desactivado"]));

        // Act
        var resultado = await controller.Desactivar(id);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("desactivado", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
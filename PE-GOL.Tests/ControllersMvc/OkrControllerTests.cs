using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-024 — CRUD de OKRs (Spec § "Tests requeridos" casos MVC 49-53,
/// 5 métodos de humo). Escritos ANTES de la implementación (TEST-01): el rojo legítimo es el FALLO
/// DE COMPILACIÓN del proyecto de tests porque <c>PE-GOL.Aplicacion.Controllers.OkrController</c>
/// y <c>PE_GOL.Aplicacion.Models.OkrIndexViewModel</c> AÚN NO existen.
/// <para>
/// Patrón verbatim de <c>ControllersMvc/ObjetivoCgControllerTests.cs</c> (HU-017): Moq sobre
/// <c>IApiClient</c>, <c>TempData</c> con <c>Mock&lt;ITempDataProvider&gt;</c>, autorización por
/// reflexión con <c>AuthorizeHelper</c>. La vista <c>Index</c> consume <c>GET /api/v1/ciclos</c>
/// para resolver el ciclo activo + <c>GET /api/v1/okrs</c> para listar (BUC-01, SEC-06/07).
/// </para>
/// <para>
/// <b>Contrato que @FrontendDev debe implementar</b> (sin esto el archivo no compila):
/// <list type="bullet">
///   <item><c>PE-GOL.Aplicacion/Controllers/OkrController.cs</c> →
///     namespace <c>PE_GOL.Aplicacion.Controllers</c>, clase <c>[Authorize]</c> +
///     <c>[Authorize(Roles = "JefeArea")]</c> por acción; ctor
///     <c>(IApiClient, ILogger&lt;OkrController&gt;)</c> (2 args).</item>
///   <item>Acciones MVC: <c>Index()</c>, <c>Crear()</c> GET/POST, <c>Editar(Guid id)</c>
///     GET/POST, <c>Eliminar(Guid id)</c> POST — patrón verbatim del
///     <c>ObjetivoCgController</c>.</item>
///   <item>ViewModels en <c>PE-GOL.Aplicacion/Models</c>: <c>OkrIndexViewModel</c>
///     (HayCicloActivo, CicloNombre, Items) y <c>OkrFormViewModel</c>.</item>
///   <item>Endpoint API: <c>GET /api/v1/okrs</c> (sin params — SEC-06/07).</item>
/// </list>
/// </para>
/// </summary>
public class OkrControllerTests
{
    // ─── Helpers ───────────────────────────────────────────────────────────

    private static (OkrController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var controller = new OkrController(apiClient.Object, NullLogger<OkrController>.Instance);

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

    private static OkrResponse CrearOkr(string codigo = "OKR.1", string descripcion = "Incrementar ventas 15%",
        decimal puntuacionFinal = 0.500m, string semaforo = "Amarillo", string pilarNombre = "Crecimiento")
        => new()
        {
            Id = Guid.NewGuid(),
            CicloId = Guid.NewGuid(),
            PilarId = Guid.NewGuid(),
            PilarNombre = pilarNombre,
            Codigo = codigo,
            Descripcion = descripcion,
            PuntuacionFinal = puntuacionFinal,
            Semaforo = semaforo,
            AreaId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow
        };

    // ─── 49 · Index_SinCicloActivo_RetornaVistaConEmptyState ─────────────────────────

    /// <summary>49 · Sin ciclo activo (API /ciclos devuelve vacío) → vista con HayCicloActivo=false.</summary>
    [Fact]
    public async Task Index_SinCicloActivo_RetornaVistaConEmptyState()
    {
        // Arrange: la API devuelve lista vacía de ciclos (no hay ciclo activo)
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CicloResponse>());

        // Act
        var resultado = await controller.Index();

        // Assert: ViewResult con OkrIndexViewModel y HayCicloActivo=false
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<OkrIndexViewModel>(view.Model);
        Assert.False(modelo.HayCicloActivo);
        Assert.Null(modelo.CicloNombre);
        Assert.Empty(modelo.Items);

        // La API /okrs NO se invoca (porque no hay ciclo activo)
        apiClient.Verify(c => c.GetAsync<List<OkrResponse>>(
            "/api/v1/okrs",
            It.IsAny<IDictionary<string, string?>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── 50 · Index_ConOkrs_RetornaVistaConDatos ─────────────────────────────────────

    /// <summary>50 · Con ciclo activo + 3 OKRs → vista con HayCicloActivo=true y 3 items.</summary>
    [Fact]
    public async Task Index_ConOkrs_RetornaVistaConDatos()
    {
        // Arrange: ciclo activo + 3 OKRs del JEF
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CicloResponse> { cicloActivo });
        var okrs = new List<OkrResponse>
        {
            CrearOkr("OKR.1", "Incrementar ventas 15%",      0.500m, "Amarillo", "Crecimiento"),
            CrearOkr("OKR.2", "Reducir mermas 5%",            0.250m, "Rojo",     "Eficiencia"),
            CrearOkr("OKR.3", "Implementar CRM Q1",           0.900m, "Verde",    "Calidad")
        };
        apiClient
            .Setup(c => c.GetAsync<List<OkrResponse>>(
                "/api/v1/okrs",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(okrs);

        // Act
        var resultado = await controller.Index();

        // Assert
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<OkrIndexViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(3, modelo.Items.Count);
        Assert.Equal("OKR.1", modelo.Items[0].Codigo);
        Assert.Equal("OKR.2", modelo.Items[1].Codigo);
        Assert.Equal("OKR.3", modelo.Items[2].Codigo);
        Assert.Equal("Crecimiento", modelo.Items[0].PilarNombre);
        Assert.Equal(0.500m, modelo.Items[0].PuntuacionFinal);
        Assert.Equal("Amarillo", modelo.Items[0].Semaforo);
    }

    // ─── 51 · Index_Autorizacion_SoloJefeArea ────────────────────────────────────────

    /// <summary>51 · Index tiene [Authorize(Roles = "JefeArea")] (REFUERZO MVC, ya está en la API).</summary>
    [Fact]
    public void Index_Autorizacion_SoloJefeArea()
    {
        // Assert: clase [Authorize] (autenticado) + acción Index [Authorize(Roles = "JefeArea")]
        Assert.True(AuthorizeHelper.TieneAuthorizeClase(typeof(OkrController)));
        var roles = AuthorizeHelper.RolesDeAccion(typeof(OkrController), nameof(OkrController.Index));

        // Solo JefeArea (SEC-07 + F1 del spec — JEF-only en esta HU)
        Assert.Contains("JefeArea", roles);
    }

    // ─── 52 · Crear_Post_ConError422_RetornaVistaConModelState ───────────────────────

    /// <summary>52 · POST Crear con error 422 (FluentValidation + BLL) → ModelState poblado + pilares re-poblados.</summary>
    [Fact]
    public async Task Crear_Post_ConError422_RetornaVistaConModelState()
    {
        // Arrange: API lanza ApiClientException con errors (422 = máx 9 OKRs / desc >500)
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CicloResponse> { cicloActivo });
        var pilares = new List<PilarResponse>
        {
            new() { Id = Guid.NewGuid(), CicloId = cicloActivo.Id, TenantId = Guid.NewGuid(),
                    Codigo = "PEC-1", Nombre = "Crecimiento", Orden = 1,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), UpdatedAt = DateTimeOffset.UtcNow }
        };
        apiClient
            .Setup(c => c.GetAsync<List<PilarResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/pilares",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pilares);

        // POST Crear lanza 422 con errores
        apiClient
            .Setup(c => c.PostAsync<OkrCreateRequest, OkrResponse>(
                "/api/v1/okrs",
                It.IsAny<OkrCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(
                "Error de validación",
                statusCode: 422,
                errors: new List<string> { "La descripción del objetivo es obligatoria." }));

        var request = new OkrCreateRequest { PilarId = pilares[0].Id, Descripcion = "" };

        // Act
        var resultado = await controller.Crear(request);

        // Assert: ViewResult con ModelState poblado + pilares re-poblados
        var view = Assert.IsType<ViewResult>(resultado);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.True(view.ViewData.ModelState.ErrorCount > 0);

        var modelo = Assert.IsType<OkrFormViewModel>(view.Model);
        Assert.Single(modelo.Pilares);
        Assert.Equal("PEC-1", modelo.Pilares[0].Codigo);
    }

    // ─── 53 · Eliminar_Post_ConError422_RedirigeAIndexConFlash ───────────────────────

    /// <summary>53 · POST Eliminar con 422 (KRs con valores reales) → redirect a Index + TempData["Error"] poblado.</summary>
    [Fact]
    public async Task Eliminar_Post_ConError422_RedirigeAIndexConFlash()
    {
        // Arrange: API lanza 422 (CA #3: el OKR tiene KRs con valores reales)
        var (controller, apiClient) = CrearController();
        var id = Guid.NewGuid();
        apiClient
            .Setup(c => c.DeleteAsync<OkrResponse>(
                $"/api/v1/okrs/{id}",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(
                "No se puede eliminar el OKR porque tiene KRs con valores reales registrados.",
                statusCode: 422));

        // Act
        var resultado = await controller.Eliminar(id);

        // Assert: RedirectToAction("Index") + TempData["Error"] poblado (patrón de la vista)
        var redirect = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redirect.ActionName);

        Assert.True(controller.TempData.ContainsKey("Error"));
        Assert.Equal(
            "No se puede eliminar el OKR porque tiene KRs con valores reales registrados.",
            controller.TempData["Error"]);
    }
}
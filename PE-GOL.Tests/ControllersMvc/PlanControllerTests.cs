using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para PlanController (MVC) — Spec HU-023 § "Tests requeridos" (casos 33-36).
/// TDD en FASE ROJA (TEST-01): la acción MVC `Consolidado()` y su ViewModel todavía NO existen →
/// el rojo de esta fase es el fallo de compilación del proyecto de tests (CS0246 / CS0117 / CS1061).
///
/// Contrato que @BackendDev debe implementar:
///   · Acción:  public async Task&lt;IActionResult&gt; Consolidado()  → View(PlanConsolidadoViewModel)
///              [Authorize(Roles = "Gerente")] en la acción (solo Gerente — RN-006)
///   · Endpoint: GET /api/v1/planes/consolidado vía IApiClient.GetAsync&lt;ConsolidadoResponse&gt;,
///              SIN query params (SEC-06: ni tenant_id ni ciclo_id del cliente).
///              404 → estado vacío "No hay un ciclo activo" (UX-05); otro ApiClientException →
///              TempData["Error"] + vista (patrón de AccionPlanController).
///   · ViewModel `PlanConsolidadoViewModel` (PE-GOL.Aplicacion/Models, namespace
///     PE_GOL.Aplicacion.Models): HayCicloActivo (bool) · HayAcciones (bool) · CicloNombre
///     (string?) · Resumen (ResumenConsolidado?) · Items (List&lt;ConsolidadoItemResponse&gt;)
///     · Paginacion (PaginacionInfo?) · AccessToken (string) · Areas (List&lt;AreaResponse&gt;)
///     · ObjetivosCg (List&lt;ObjetivoCgResponse&gt;)
/// </summary>
public class PlanControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (PlanController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var sesionService = new SesionService(new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } });
        var controller = new PlanController(apiClient.Object, sesionService, NullLogger<PlanController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static ConsolidadoItemResponse CrearItem(string areaCodigo, string cgCodigo, DateTime fechaInicio)
        => new()
        {
            AccionId = Guid.NewGuid(),
            AccionCodigo = $"{areaCodigo}.{cgCodigo}.A1",
            AccionDescripcion = "Acción de prueba",
            FechaInicio = fechaInicio,
            FechaVencimiento = fechaInicio.AddMonths(3),
            Clasificacion = "Proyecto",
            TipoPresupuesto = "OPEX",
            Progreso = 50m,
            Status = "EnProgreso",
            Peso = 0.5m,
            ResponsableNombre = "Juan Pérez",
            ObjetivoCgId = Guid.NewGuid(),
            ObjetivoCodigo = cgCodigo,
            ObjetivoDescripcion = "Objetivo de prueba",
            ObjetivoProgreso = 50m,
            ObjetivoSemaforo = "Amarillo",
            PilarCodigo = "PEC-1",
            PilarNombre = "Crecimiento",
            AreaId = Guid.NewGuid(),
            AreaCodigo = areaCodigo,
            AreaNombre = "Área de prueba",
            CicloId = Guid.NewGuid(),
            CicloNombre = "PE 2026",
            CicloAnioFiscal = 2026
        };

    private static ConsolidadoResponse CrearResponseConItems(int cantidad)
    {
        var items = new List<ConsolidadoItemResponse>();
        for (var i = 0; i < cantidad; i++)
            items.Add(CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15).AddDays(i)));

        return new ConsolidadoResponse
        {
            Items = items,
            Resumen = new ResumenConsolidado { Total = cantidad, EnProgreso = cantidad },
            Paginacion = new PaginacionInfo { Page = 1, PageSize = 25, TotalItems = cantidad, TotalPages = 1 }
        };
    }

    private static void ConfigurarApi(Mock<IApiClient> apiClient, ConsolidadoResponse response)
    {
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                "/api/v1/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AreaResponse>());

        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgResponse>>(
                "/api/v1/objetivos-cg",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ObjetivoCgResponse>());

        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    // ─── 33. Consolidado_SinCicloActivo_RetornaVistaConEmptyState ────────────

    [Fact]
    public async Task Consolidado_SinCicloActivo_RetornaVistaConEmptyState()
    {
        // Arrange: la API responde 404 (no hay ciclo Activo — RC-01) vía ApiClientException
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "No hay un ciclo activo para el tenant"));

        // Act
        var resultado = await controller.Consolidado();

        // Assert: estado vacío (UX-05) — la vista se RENDERIZA, no es una redirección ni un 404
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.False(modelo.HayCicloActivo);
        Assert.False(modelo.HayAcciones);

        // El 404 es un estado de UI, no un error: NO se muestra el banner TempData["Error"]
        Assert.False(controller.TempData.ContainsKey("Error"));
    }

    // ─── 34. Consolidado_SinAcciones_RetornaVistaConEmptyState ───────────────

    [Fact]
    public async Task Consolidado_SinAcciones_RetornaVistaConEmptyState()
    {
        // Arrange: 200 con payload válido pero sin acciones (D-C: no es 404)
        var (controller, apiClient) = CrearController();
        var response = new ConsolidadoResponse
        {
            Items = new List<ConsolidadoItemResponse>(),
            Resumen = new ResumenConsolidado { Total = 0 },
            Paginacion = new PaginacionInfo { Page = 1, PageSize = 25, TotalItems = 0, TotalPages = 0 }
        };
        ConfigurarApi(apiClient, response);

        // Act
        var resultado = await controller.Consolidado();

        // Assert: hay ciclo activo, pero no hay acciones
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.False(modelo.HayAcciones);
    }

    // ─── 35. Consolidado_ConAcciones_RetornaVistaConDatos ───────────────────

    [Fact]
    public async Task Consolidado_ConAcciones_RetornaVistaConDatos()
    {
        // Arrange
        var (controller, apiClient) = CrearController();
        var response = CrearResponseConItems(3);
        ConfigurarApi(apiClient, response);

        // Act
        var resultado = await controller.Consolidado();

        // Assert
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.True(modelo.HayAcciones);
        Assert.Equal(3, modelo.Items.Count);
        Assert.Equal("GOL1.CG1.A1", modelo.Items[0].AccionCodigo);
    }

    // ─── 36. Consolidado_Autorizacion_SoloGerente ───────────────────────────

    [Fact]
    public void Consolidado_Autorizacion_SoloGerente()
    {
        // Arrange: reflexión sobre la CLASE PlanController
        var authorizeAttr = typeof(PlanController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        // Assert: la clase declara [Authorize(Roles = "Gerente")] — exactamente
        // ese rol (ni JefeArea ni AdminTenant) — RN-006
        Assert.NotNull(authorizeAttr);
        Assert.Equal("Gerente", authorizeAttr.Roles);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Tests.Helpers;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para PlanController (MVC) — Spec HU-023 § "Tests requeridos" (Revisión v2 — hotfix).
/// TDD en FASE ROJA (TEST-01):
///   · Casos 33 (reescrito), 37, 38, 39, 40 deben fallar contra el código actual
///     (defectos A y B del spec v2 § Revisión).
///   · Casos 41 y 42 son reflexión: deben fallar porque el ViewModel no tiene
///     `ErrorCarga` (bool) ni `ObjetivosCg: List&lt;ObjetivoCgConsolidadoResponse&gt;`.
///
/// Defectos que el hotfix debe corregir (spec v2 § "Revisión v2"):
///   · Defecto A: el MVC llama a GET /api/v1/areas (ruta inexistente → 404). El catch 404 lo
///     etiqueta como "sin ciclo activo". El fix debe llamar primero a GET /api/v1/ciclos y resolver
///     el ciclo activo desde la lista (RC-01).
///   · Defecto B: el MVC llama a GET /api/v1/objetivos-cg (rol JefeArea → 403 para Gerente). El fix
///     debe llamar a GET /api/v1/objetivos-cg/consolidado.
///   · Defecto C: el DAL no declara DbType.Date para los parámetros de fecha → 42P08.
///     (No testeado aquí, ver PlanConsolidadoRepositoryTests.cs casos R8/R9).
///
/// Contrato que @BackendDev debe implementar (spec v2 § UI § 3 — tabla de contrato cerrada):
///   · Acción `Consolidado()` llama EXACTAMENTE estas 4 rutas en este orden:
///       1. GET /api/v1/ciclos                              → List&lt;CicloResponse&gt;
///       2. GET /api/v1/ciclos/{cicloActivo.Id}/areas        → List&lt;AreaResponse&gt;
///       3. GET /api/v1/objetivos-cg/consolidado             → List&lt;ObjetivoCgConsolidadoResponse&gt;
///       4. GET /api/v1/planes/consolidado                   → ConsolidadoResponse
///   · Si la lista de ciclos no tiene Activo (RC-01) → HayCicloActivo=false, ErrorCarga=false,
///     sin TempData["Error"], sin llamadas a las rutas 2-4.
///   · Cualquier ApiClientException de las rutas 1-4 → TempData["Error"] + ErrorCarga=true
///     (preserva HayCicloActivo si ya se resolvió).
///   · ViewModel.ObjetivosCg: List&lt;ObjetivoCgConsolidadoResponse&gt; (antes: List&lt;ObjetivoCgResponse&gt;).
///   · ViewModel.ErrorCarga: bool (default false; true solo si hubo error de carga).
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

    /// <summary>
    /// Variante con `MockBehavior.Strict` para los casos del proxy MVC (HU-023 Revisión v3, ADR-016).
    /// En Strict, cualquier método del mock que el código invoque sin Setup lanza
    /// `MockException` con mensaje legible ("invocation was not set up") — a diferencia del
    /// Loose default, que devuelve `null`/`default` y permite que el test pase silenciosamente
    /// o falle con `NullReferenceException` lejos del defecto real. Lo usan los casos 45 y 47,
    /// donde el corazón del caso es verificar QUÉ llamada hace el proxy (no solo el resultado).
    /// </summary>
    private static (PlanController controller, Mock<IApiClient> apiClient) CrearControllerEstricto()
    {
        var apiClient = new Mock<IApiClient>(MockBehavior.Strict);
        var sesionService = new SesionService(new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } });
        var controller = new PlanController(apiClient.Object, sesionService, NullLogger<PlanController>.Instance);

        var httpContext = new DefaultHttpContext();
        var tempDataProvider = new Mock<ITempDataProvider>();
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, apiClient);
    }

    private static CicloResponse CrearCicloActivo() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Nombre = "PE 2026",
        AñoFiscal = 2026,
        MesInicio = 1,
        Estado = "Activo",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CicloResponse CrearCiclo(string nombre, string estado) => new()
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

    /// <summary>
    /// Helper reescrito (v2) que mockea las 4 rutas reales del contrato (spec v2 § UI § 3).
    /// El helper v1 mockeaba `/api/v1/areas` y `/api/v1/objetivos-cg` — eso REPLICABA los defectos A y B.
    /// Lección documentada en el spec: "un mock que confirma la ruta que le dictan no puede detectar
    /// que la ruta no existe o que el rol no tiene acceso".
    /// </summary>
    private static void ConfigurarApi(Mock<IApiClient> apiClient, ConsolidadoResponse response)
    {
        var cicloActivo = CrearCicloActivo();
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgConsolidadoResponse>>(
                "/api/v1/objetivos-cg/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    // ══════════════════════════════ CASOS 33-36 (v1 con helper v2) ══════════════════════════════

    // ─── 33 (reescrito v2). Sin ciclo activo (lista sin "Activo") → empty state SIN llamada al consolidado ─

    [Fact]
    public async Task Consolidado_SinCicloActivo_ListaSinActivo_RetornaEmptyStateSinLlamarAlConsolidado()
    {
        // Arrange: /api/v1/ciclos responde 200 con lista SIN ciclo "Activo" (solo "Borrador").
        // La condición "sin ciclo activo" es de DATOS, no de HTTP — el código v1 etiquetaba
        // cualquier 404 como "sin ciclo activo" (Defecto A). El fix debe distinguir:
        // sin ciclo (lista sin Activo, RC-01) vs. error de carga (cualquier ApiClientException).
        var (controller, apiClient) = CrearController();
        var cicloBorrador = CrearCiclo("PE 2027", "Borrador");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloBorrador]);

        // Mock defensivo: /api/v1/planes/consolidado se mockea para que el código actual NO lance
        // NullReferenceException si la llama (consolidado.Items.Count sobre consolidado == null).
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponseConItems(0));

        // Act
        var resultado = await controller.Consolidado();

        // Assert: empty state de ciclo, sin error de carga, sin TempData["Error"]
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.False(modelo.HayCicloActivo);
        Assert.False(modelo.HayAcciones);

        // ErrorCarga debe existir Y ser false. Reflexión porque la propiedad aún no existe en el
        // código actual (TEST-01 — fase roja, sin error de compilación).
        var propErrorCarga = typeof(PlanConsolidadoViewModel).GetProperty("ErrorCarga");
        Assert.NotNull(propErrorCarga);  // Falla hoy: la propiedad no existe
        Assert.False((bool)propErrorCarga!.GetValue(modelo)!);

        Assert.False(controller.TempData.ContainsKey("Error"));

        // /api/v1/planes/consolidado NO debe invocarse cuando no hay ciclo activo
        apiClient.Verify(
            c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 34 (v1; helper v2). Sin acciones → empty state de acciones ──────────────

    [Fact]
    public async Task Consolidado_SinAcciones_RetornaVistaConEmptyState()
    {
        // Arrange: 200 con payload válido pero sin acciones (no es 404)
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

    // ─── 35 (v1; helper v2). Con acciones → vista con datos ──────────────

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

    // ─── 36 (v1, sin cambios). [Authorize(Roles = "Gerente")] en la clase ──────────────

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

    // ══════════════════════════════ CASOS 37-40 (nuevos v2 — fase roja) ══════════════════════════════

    // ─── 37 (nuevo v2 — falla hoy: A+B). Usa las 4 rutas reales del contrato ──────────

    [Fact]
    public async Task Consolidado_CargaCatalogos_UsaLasRutasRealesDeLaAPI()
    {
        // Arrange: las 4 rutas del contrato responden 200
        var (controller, apiClient) = CrearController();
        ConfigurarApi(apiClient, CrearResponseConItems(1));

        // Act
        await controller.Consolidado();

        // Assert: las 4 rutas del fix se llaman exactamente una vez
        apiClient.Verify(
            c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Para la ruta de áreas, el Guid del ciclo activo se genera dentro de CrearCicloActivo()
        // y no se expone. Verificamos el patrón /api/v1/ciclos/{guid}/areas con un matcher flexible.
        apiClient.Verify(
            c => c.GetAsync<List<AreaResponse>>(
                It.Is<string>(s => s != null && s.StartsWith("/api/v1/ciclos/") && s.EndsWith("/areas")),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        apiClient.Verify(
            c => c.GetAsync<List<ObjetivoCgConsolidadoResponse>>(
                "/api/v1/objetivos-cg/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        apiClient.Verify(
            c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Y las rutas PROHIBIDAS (Defectos A y B) NO se llaman — match EXACTO, no StartsWith
        apiClient.Verify(
            c => c.GetAsync<List<AreaResponse>>(
                "/api/v1/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        apiClient.Verify(
            c => c.GetAsync<List<ObjetivoCgResponse>>(
                "/api/v1/objetivos-cg",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 38 (nuevo v2 — falla hoy: A). 404 en catálogo de área → banner, NO empty state de ciclo ─

    [Fact]
    public async Task Consolidado_Error404DeCatalogo_NoEsSinCicloActivo()
    {
        // Arrange: el fix llama primero a /api/v1/ciclos → ciclo Activo (RC-01 OK),
        // luego a /api/v1/ciclos/{id}/areas → 404 (error de catálogo).
        // El comportamiento esperado: HayCicloActivo=true (el ciclo SÍ se resolvió) +
        // ErrorCarga=true + TempData["Error"]. NUNCA el empty state de ciclo (Defecto A).
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCicloActivo();
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "Not Found"));

        // Mock defensivo: el código actual SÍ llama a /api/v1/planes/consolidado (su ruta, no la del fix).
        // Sin este mock, Moq devuelve null y el código actual NRE al hacer `consolidado.Items.Count`.
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponseConItems(0));

        // Act
        var resultado = await controller.Consolidado();

        // Assert: el ciclo activo se resolvió (HayCicloActivo=true), pero hay error de carga
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);  // El ciclo SÍ está activo (la ruta 1 resolvió OK)

        // ErrorCarga: reflexión porque la propiedad aún no existe en el código actual (TEST-01).
        // Si la propiedad no existe → Assert.NotNull falla → rojo correcto.
        var propErrorCarga = typeof(PlanConsolidadoViewModel).GetProperty("ErrorCarga");
        Assert.NotNull(propErrorCarga);
        Assert.True((bool)propErrorCarga!.GetValue(modelo)!);

        Assert.True(controller.TempData.ContainsKey("Error"));
    }

    // ─── 39 (nuevo v2 — falla hoy: B). 403 en el consolidado → banner, NO empty state de ciclo ─

    [Fact]
    public async Task Consolidado_Error403DeConsolidado_MuestraErrorNoEmptyState()
    {
        // Arrange: ciclo OK, catálogo de áreas OK, /api/v1/objetivos-cg/consolidado → 403.
        // El código actual llama a /api/v1/objetivos-cg (rol JEF) → 403 → catch genérico → vacío.
        // El fix llama a /api/v1/objetivos-cg/consolidado (rol GER) y debe mostrar el banner.
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCicloActivo();
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        apiClient
            .Setup(c => c.GetAsync<List<AreaResponse>>(
                $"/api/v1/ciclos/{cicloActivo.Id}/areas",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgConsolidadoResponse>>(
                "/api/v1/objetivos-cg/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(403, "Forbidden"));

        // Mock defensivo: /api/v1/planes/consolidado con respuesta válida para evitar NRE
        // en el código actual si llega a esa llamada.
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponseConItems(0));

        // Act
        var resultado = await controller.Consolidado();

        // Assert: error de carga, HayCicloActivo=true (el ciclo se resolvió OK antes del 403)
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);

        var propErrorCarga = typeof(PlanConsolidadoViewModel).GetProperty("ErrorCarga");
        Assert.NotNull(propErrorCarga);
        Assert.True((bool)propErrorCarga!.GetValue(modelo)!);

        Assert.True(controller.TempData.ContainsKey("Error"));
    }

    // ─── 40 (nuevo v2). 500 en /api/v1/ciclos → banner, HayCicloActivo=false ──────────

    [Fact]
    public async Task Consolidado_Error500DeCiclos_MuestraErrorNoEmptyState()
    {
        // Arrange: /api/v1/ciclos → 500. El código actual ni siquiera llama a esta ruta (Defecto A),
        // por lo que la vista resultante dice "HayCicloActivo=true" sin banner de error.
        // El fix debe detectar el 500 y mostrar el banner, con HayCicloActivo=false (no se resolvió).
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(500, "Internal Server Error"));

        // Mock defensivo: el código actual SÍ llama a /api/v1/planes/consolidado.
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponseConItems(0));

        // Act
        var resultado = await controller.Consolidado();

        // Assert: error de carga, HayCicloActivo=false (el 500 en /api/v1/ciclos impide resolver)
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<PlanConsolidadoViewModel>(view.Model);
        Assert.False(modelo.HayCicloActivo);  // Falla hoy: el código actual nunca llama a /api/v1/ciclos

        var propErrorCarga = typeof(PlanConsolidadoViewModel).GetProperty("ErrorCarga");
        Assert.NotNull(propErrorCarga);
        Assert.True((bool)propErrorCarga!.GetValue(modelo)!);

        Assert.True(controller.TempData.ContainsKey("Error"));
    }

    // ══════════════════════════════ CASOS 41-42 (reflexión — fase roja) ══════════════════════════════

    // ─── 41 (nuevo v2 — reflexión). ViewModel.ObjetivosCg es List<ObjetivoCgConsolidadoResponse> ──

    [Fact]
    public void Consolidado_Contrato_ObjetoCgEsConsolidadoResponse()
    {
        // Arrange: reflexión sobre PlanConsolidadoViewModel.ObjetivosCg.
        // El código actual tiene `List<ObjetivoCgResponse>`; el fix debe ser
        // `List<ObjetivoCgConsolidadoResponse>` (spec v2 § Desviación de contrato).
        // Se accede por nombre de cadena y se compara PropertyType.FullName string-contra-string
        // para que el proyecto de tests COMPITE antes de que el tipo cambie (TEST-01 — fase roja).
        var prop = typeof(PlanConsolidadoViewModel).GetProperty("ObjetivosCg");

        // Assert: la propiedad existe
        Assert.NotNull(prop);

        // Assert: el tipo es List<ObjetivoCgConsolidadoResponse>.
        // PropertyType.FullName para List<T> tiene la forma:
        //   "System.Collections.Generic.List`1[[PE_GOL.DTO.Responses.Objetivos.ObjetivoCgConsolidadoResponse, PE-GOL.DTO, Version=..., Culture=..., PublicKeyToken=null]]"
        // Usamos Contains (no equality exacta) porque la parte de versión/assembly puede variar.
        var fullName = prop!.PropertyType.FullName;
        Assert.NotNull(fullName);
        Assert.Contains("List`1", fullName!);
        Assert.Contains("ObjetivoCgConsolidadoResponse", fullName!);

        // Y específicamente NO debe ser List<ObjetivoCgResponse> (el tipo anterior).
        // Verificación negativa: si el código actual expone el tipo viejo, esto fallaría porque
        // `ObjetivoCgResponse` aparece y `ObjetivoCgConsolidadoResponse` NO aparece.
        // (Validación cruzada: Assert.Contains(ObjetivoCgConsolidadoResponse) ya cubre esto arriba.)
    }

    // ─── 42 (nuevo v2 — reflexión). ViewModel expone bool ErrorCarga ──────────

    [Fact]
    public void Consolidado_Contrato_ViewModelConErrorCarga()
    {
        // Arrange: reflexión sobre PlanConsolidadoViewModel.ErrorCarga.
        // La propiedad no existe en el código actual; el fix debe agregarla (bool, default false).
        // Se accede por nombre de cadena para que el proyecto COMPITE antes de que exista (TEST-01).
        var prop = typeof(PlanConsolidadoViewModel).GetProperty("ErrorCarga");

        // Assert: la propiedad existe
        Assert.NotNull(prop);  // Falla hoy: la propiedad no existe en el ViewModel

        // Assert: la propiedad es de tipo bool
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    // ══════════════════════════════ CASOS 43-49 (Defecto E · proxy MVC · Revisión v3) ══════════════════════════════
    //
    // El navegador NUNCA consume la API interna (ADR-016): el MVC hace de proxy con IApiClient.
    // Las acciones `ConsolidadoDatos` y `ConsolidadoExportar` (GET /Plan/ConsolidadoDatos y
    // /Plan/ConsolidadoExportar) reenvían los filtros como query string a la API y devuelven
    // el resultado al JS (ConsolidadoDatos → JSON; ConsolidadoExportar → FileResult).
    //
    // FASE ROJA TDD (TEST-01): estas acciones NO existen en el código actual. El proyecto de
    // tests DEBE compilar (igual que en la fase roja anterior) → reflexión por nombre de cadena
    // para encontrar las acciones y construir sus argumentos por nombre de parámetro. Si la
    // acción no existe, Assert.NotNull en el helper falla con mensaje claro (no compilación).
    //
    // Contrato que @BackendDev debe implementar (ADR-016, spec v3 § "Contrato de las 2 acciones
    // MVC proxy"):
    //   · `ConsolidadoDatos`: recibe los 9 filtros de FiltrosConsolidadoRequest (por
    //     [FromQuery] FiltrosConsolidadoRequest o por parámetros individuales), construye
    //     un query dictionary con whitelist SEC-06 (nunca tenant_id), llama a
    //     `_apiClient.GetAsync<ConsolidadoResponse>("/api/v1/planes/consolidado", query)` y
    //     devuelve Ok(payload) — el JSON íntegro al JS.
    //   · `ConsolidadoExportar`: igual pero llama a `GetAsync<byte[]>("/api/v1/planes/consolidado/exportar", query)`
    //     y devuelve FileContentResult con contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    //     y FileDownloadName = "PlanConsolidado_*.xlsx".
    //   · ApiClientException → mismo código HTTP al navegador, mensaje genérico (nunca ex.Message
    //     crudo: el mensaje puede contener la URL interna de la API o el JWT).
    //   · UnauthorizedException → HTTP 401, NO RedirectToAction("Login","Auth") — el endpoint
    //     lo consume un `fetch` y un 302 se seguiría transparente recibiendo el HTML del login.
    //   · Ambas acciones bajo [Authorize(Roles = "Gerente")] (heredado de la clase).

    /// <summary>
    /// Busca una acción del PlanController (MVC) por nombre (case-insensitive). Falla con
    /// mensaje claro si la acción no existe — fase roja del hotfix v3 (TEST-01).
    /// </summary>
    private static MethodInfo ObtenerAccion(string nombreAccion)
    {
        var metodo = typeof(PlanController).GetMethod(
            nombreAccion,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        Assert.NotNull(metodo);  // FASE ROJA: la acción proxy aún no existe.
        return metodo!;
    }

    /// <summary>
    /// Construye el array de argumentos para una acción del proxy a partir del nombre de cada
    /// parámetro. Soporta dos firmas probables:
    ///   (a) parámetros individuales: Guid? areaId, Guid? cgId, string? status, …
    ///   (b) FiltrosConsolidadoRequest filtros (DTOs anidados)
    /// Los nombres se matchean case-insensitive. Parámetros no reconocidos reciben su default.
    /// </summary>
    private static object?[] ConstruirArgumentos(
        MethodInfo metodo,
        Guid? areaId,
        Guid? cgId,
        string? status,
        string? clasificacion,
        string? tipo,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        int? page,
        int? pageSize)
    {
        var parametros = metodo.GetParameters();
        var valores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["areaId"] = areaId,
            ["cgId"] = cgId,
            ["status"] = status,
            ["clasificacion"] = clasificacion,
            ["tipo"] = tipo,
            ["fechaDesde"] = fechaDesde,
            ["fechaHasta"] = fechaHasta,
            ["page"] = page,
            ["pageSize"] = pageSize,
        };

        var args = new object?[parametros.Length];
        for (var i = 0; i < parametros.Length; i++)
        {
            var p = parametros[i];

            // Caso (b): el parámetro es el DTO FiltrosConsolidadoRequest → construimos uno.
            if (p.ParameterType == typeof(FiltrosConsolidadoRequest))
            {
                args[i] = new FiltrosConsolidadoRequest
                {
                    AreaId = areaId,
                    CgId = cgId,
                    Status = status,
                    Clasificacion = clasificacion,
                    Tipo = tipo,
                    FechaDesde = fechaDesde,
                    FechaHasta = fechaHasta,
                    Page = page ?? 1,
                    PageSize = pageSize ?? 25,
                };
                continue;
            }

            // Caso (a): parámetro por nombre. Si no se reconoce, default.
            if (p.Name != null && valores.TryGetValue(p.Name, out var v))
                args[i] = v;
            else if (p.HasDefaultValue)
                args[i] = p.DefaultValue;
            else if (p.ParameterType.IsValueType)
                args[i] = Activator.CreateInstance(p.ParameterType);
            else
                args[i] = null;
        }
        return args;
    }

    /// <summary>
    /// Invoca una acción proxy por reflexión y devuelve el IActionResult.
    /// Acepta firma con parámetros (caso a) o con FiltrosConsolidadoRequest (caso b).
    /// </summary>
    private static async Task<IActionResult> InvocarAccionProxyAsync(
        PlanController controller,
        string nombreAccion,
        Guid? areaId = null,
        Guid? cgId = null,
        string? status = null,
        string? clasificacion = null,
        string? tipo = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int? page = null,
        int? pageSize = null)
    {
        var metodo = ObtenerAccion(nombreAccion);
        var args = ConstruirArgumentos(
            metodo, areaId, cgId, status, clasificacion, tipo,
            fechaDesde, fechaHasta, page, pageSize);

        var resultado = metodo.Invoke(controller, args);
        if (resultado is Task task)
        {
            await task.ConfigureAwait(false);
            var propResultado = task.GetType().GetProperty("Result");
            Assert.NotNull(propResultado);  // La acción proxy debe devolver Task<IActionResult>
            return (IActionResult)propResultado!.GetValue(task)!;
        }
        return (IActionResult)resultado!;
    }

    // ─── 43 (nuevo v3 — falla hoy). Proxy reenvía filtros a /api/v1/planes/consolidado y devuelve JSON ─

    [Fact]
    public async Task ConsolidadoDatos_ConFiltros_RenviaFiltrosComoQueryStringYDevuelvePayload()
    {
        // Arrange: mock IApiClient → 200 con ConsolidadoResponse; filtros conocidos.
        var (controller, apiClient) = CrearController();
        var payload = CrearResponseConItems(2);
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        var areaId = Guid.NewGuid();
        var cgId = Guid.NewGuid();
        var fechaDesde = new DateTime(2026, 1, 1);
        var fechaHasta = new DateTime(2026, 12, 31);

        // Act
        var resultado = await InvocarAccionProxyAsync(
            controller, "ConsolidadoDatos",
            areaId: areaId,
            cgId: cgId,
            status: "Atrasado",
            clasificacion: "Proyecto",
            tipo: "OPEX",
            fechaDesde: fechaDesde,
            fechaHasta: fechaHasta,
            page: 2,
            pageSize: 10);

        // Assert 1: la API fue llamada EXACTAMENTE una vez con la ruta correcta y los filtros
        // reenviados como query string. La whitelist SEC-06 excluye tenant_id (no se acepta en
        // ningún parámetro del request — sale del JWT).
        apiClient.Verify(
            c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.Is<IDictionary<string, string?>?>(q => QueryContieneFiltrosCompletos(
                    q, areaId, cgId, "Atrasado", "Proyecto", "OPEX", fechaDesde, fechaHasta)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert 2: la acción devuelve el payload de la API íntegro al JS (JSON 200).
        // Aceptamos OkObjectResult con el payload o cualquier IActionResult cuyo StatusCode==200
        // y cuyo cuerpo sea el payload — la forma exacta (Ok(...) vs Json(...)) la decide @BackendDev.
        Assert.NotNull(resultado);
        Assert.IsType<ObjectResult>(resultado);  // El spec dice "JSON 200" → OkObjectResult hereda de ObjectResult.
        var objectResult = (ObjectResult)resultado;
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.Same(payload, objectResult.Value);
    }

    /// <summary>
    /// Predicado (no inlineable en árbol de expresión): el query dictionary contiene los 7
    /// filtros esperados, NO contiene tenant_id/tenantId (whitelist SEC-06), y los valores
    /// coinciden con los delArrange.
    /// </summary>
    private static bool QueryContieneFiltrosCompletos(
        IDictionary<string, string?>? q,
        Guid areaId, Guid cgId, string status, string clasificacion, string tipo,
        DateTime fechaDesde, DateTime fechaHasta)
    {
        if (q is null) return false;
        if (q.ContainsKey("tenantId") || q.ContainsKey("tenant_id")) return false;

        return q.TryGetValue("areaId", out var av) && av == areaId.ToString()
            && q.TryGetValue("cgId", out var cv) && cv == cgId.ToString()
            && q.TryGetValue("status", out var sv) && sv == status
            && q.TryGetValue("clasificacion", out var clv) && clv == clasificacion
            && q.TryGetValue("tipo", out var tv) && tv == tipo
            && q.TryGetValue("fechaDesde", out var fdv) && fdv == fechaDesde.ToString("yyyy-MM-dd")
            && q.TryGetValue("fechaHasta", out var fhv) && fhv == fechaHasta.ToString("yyyy-MM-dd");
    }

    // ─── 44 (nuevo v3 — falla hoy). Sin filtros → la API se llama SIN los 7 filtros en el query ─

    [Fact]
    public async Task ConsolidadoDatos_SinFiltros_LlamaALaApiYDevuelvePayload()
    {
        // Arrange: mock → 200; sin query string de filtros en el request.
        // La paginación por defecto del JS viaja tal cual (page=1, pageSize=25).
        var (controller, apiClient) = CrearController();
        var payload = CrearResponseConItems(0);
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        // Act: invocamos sin filtros. El proxy debe construir un query dictionary vacío
        // (sin los 7 filtros). page/pageSize pueden estar o no — el contrato v3 no lo fija.
        var resultado = await InvocarAccionProxyAsync(controller, "ConsolidadoDatos");

        // Assert 1: la API fue llamada una vez con la ruta correcta y SIN los 7 filtros en el query.
        apiClient.Verify(
            c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.Is<IDictionary<string, string?>?>(q => QuerySinFiltros(q)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert 2: payload devuelto (JSON 200).
        Assert.IsType<ObjectResult>(resultado);
        var objectResult = (ObjectResult)resultado;
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.Same(payload, objectResult.Value);
    }

    /// <summary>
    /// Predicado: el query dictionary NO contiene ninguno de los 7 filtros ni tenant_id.
    /// page/pageSize pueden estar o no (el contrato v3 no los fija explícitamente).
    /// </summary>
    private static bool QuerySinFiltros(IDictionary<string, string?>? q)
    {
        if (q is null) return false;
        return !q.ContainsKey("areaId")
            && !q.ContainsKey("cgId")
            && !q.ContainsKey("status")
            && !q.ContainsKey("clasificacion")
            && !q.ContainsKey("tipo")
            && !q.ContainsKey("fechaDesde")
            && !q.ContainsKey("fechaHasta")
            && !q.ContainsKey("tenantId") && !q.ContainsKey("tenant_id");
    }

    /// <summary>
    /// Predicado: el query dictionary contiene la clave "status" con el valor esperado.
    /// Usado por el caso 45 (exportar con filtros) — verificación mínima, otros filtros pueden
    /// o no estar.
    /// </summary>
    private static bool QueryContieneStatus(IDictionary<string, string?>? q, string status)
        => q is not null && q.TryGetValue("status", out var sv) && sv == status;

    // ─── 45 (nuevo v3 — falla hoy). Proxy de exportar devuelve FileResult con content-type XLSX ─
    //
    // LECCIÓN DE ARQUITECTURA (incidente 2026-09-30): la implementación real usa `GetBytesAsync`
    // (no `GetAsync<byte[]>`) — `IApiClient.GetAsync<T>` va por `EnviarAsync<T>` que DESERIALIZA
    // JSON, y un XLSX NO es JSON. Por eso la firma `GetBytesAsync` existe en `IApiClient.cs:19`:
    // lee el stream binario en crudo. El endpoint API real
    // (`PE-GOL.API/Controllers/PlanOperativo/PlanController.cs:59-73`) hace `File(bytes,
    // "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName)` — excepción
    // explícita a ARCH-07 documentada en el propio endpoint.
    //
    // Por qué `MockBehavior.Strict` aquí: el test verifica que el proxy llama a `GetBytesAsync`
    // con la URL correcta y los filtros correctos. Si el código cambiara a otro método (p. ej.
    // regresión a `GetAsync<byte[]>`), queremos un fallo legible ("invocation was not set up") en
    // lugar de un default que pase el test silenciosamente.

    [Fact]
    public async Task ConsolidadoExportar_ConFiltros_DevuelveFileResultConContentTypeXlsx()
    {
        // Arrange: mock IApiClient (Strict) → byte[] con magic bytes de XLSX (ZIP: PK\x03\x04).
        var (controller, apiClient) = CrearControllerEstricto();
        // Magic bytes de un XLSX válido: ZIP local file header (0x50 0x4B 0x03 0x04).
        var xlsxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };
        apiClient
            .Setup(c => c.GetBytesAsync(
                "/api/v1/planes/consolidado/exportar",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(xlsxBytes);

        var status = "Atrasado";

        // Act
        var resultado = await InvocarAccionProxyAsync(
            controller, "ConsolidadoExportar", status: status);

        // Assert 1: la API fue llamada con la ruta de exportar y los filtros reenviados.
        // ESTE ES EL CORAZÓN DEL CASO 45: el proxy projea la URL interna, no la inventa.
        apiClient.Verify(
            c => c.GetBytesAsync(
                "/api/v1/planes/consolidado/exportar",
                It.Is<IDictionary<string, string?>?>(q => QueryContieneStatus(q, status)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert 2: la acción devuelve un FileContentResult con el content-type XLSX y un
        // FileDownloadName tipo "PlanConsolidado_*.xlsx". El spec fija el content-type exacto
        // y el prefijo del nombre — el sufijo (timestamp) puede variar.
        var fileResult = Assert.IsType<FileContentResult>(resultado);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileResult.ContentType);
        Assert.NotNull(fileResult.FileDownloadName);
        Assert.StartsWith("PlanConsolidado_", fileResult.FileDownloadName);
        Assert.EndsWith(".xlsx", fileResult.FileDownloadName);
        Assert.Same(xlsxBytes, fileResult.FileContents);
    }

    // ─── 46 (nuevo v3 — falla hoy). ApiClientException → mensaje genérico, sin URL interna ni JWT ─

    [Fact]
    public async Task ConsolidadoDatos_ApiClientException_DevuelveErrorGenericoSinJwtNiUrlInterna()
    {
        // Arrange: la API lanza 500 con un mensaje que contiene la URL INTERNA (Api:BaseUrl)
        // y un JWT simulado. El proxy NO debe filtrar estos detalles al navegador (ADR-016:
        // el cuerpo de la respuesta nunca contiene el JWT ni la URL interna de la API).
        var (controller, apiClient) = CrearController();
        var jwtFalso = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.fake.fake";
        var urlInterna = "https://localhost:7269/api/v1/planes/consolidado";
        var mensajeInterno = $"Error en {urlInterna} con token {jwtFalso}";
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(500, mensajeInterno));

        // Act
        var resultado = await InvocarAccionProxyAsync(controller, "ConsolidadoDatos");

        // Assert 1: el código HTTP devuelto al navegador es el MISMO (500) que devolvió la API.
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(500, objectResult.StatusCode);

        // Assert 2: el cuerpo de la respuesta NO contiene la URL interna, ni "Bearer", ni el JWT.
        // Reflejamos el mensaje a string y verificamos ausencia — el spec exige mensaje genérico,
        // nunca `ex.Message` crudo.
        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.DoesNotContain(urlInterna, cuerpo);
        Assert.DoesNotContain("localhost:7269", cuerpo);
        Assert.DoesNotContain("Bearer", cuerpo);
        Assert.DoesNotContain(jwtFalso, cuerpo);
        Assert.DoesNotContain("eyJ", cuerpo);  // Prefijo típico de JWT

        // Assert 3: NO es un RedirectToAction (un endpoint consumido por fetch no debe redirigir).
        Assert.IsNotType<RedirectToActionResult>(resultado);
        Assert.IsNotType<RedirectResult>(resultado);
        Assert.IsNotType<ViewResult>(resultado);
    }

    // ─── 47 (nuevo v3 — falla hoy). Idem 46 sobre ConsolidadoExportar ──────────
    //
    // LECCIÓN DE ARQUITECTURA (incidente 2026-09-30): mismo motivo que el caso 45 — la API
    // devuelve bytes binarios crudos, así que la firma del mock DEBE ser `GetBytesAsync` (no
    // `GetAsync<byte[]>`). Sin este mock, Moq (Loose) devuelve `null` por defecto y el código
    // entra al `try` con éxito — el caso 47 nunca ejercita la rama de excepción y el test pasa
    // por accidente o falla en `Assert.Equal(500, statusCode)` con `Actual: -1`.
    //
    // Por qué `MockBehavior.Strict`: el proxy debe llamar SOLO a `GetBytesAsync`. Si por regresión
    // se cambiara a otro método (o se añadiera una llamada extra sin mockear), queremos un fallo
    // legible — no un default silencioso.

    [Fact]
    public async Task ConsolidadoExportar_ApiClientException_DevuelveErrorGenericoSinJwtNiUrlInterna()
    {
        // Arrange: misma idea que 46 — la API lanza 500 con URL interna y JWT simulado.
        var (controller, apiClient) = CrearControllerEstricto();
        var jwtFalso = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.fake.fake";
        var urlInterna = "https://localhost:7269/api/v1/planes/consolidado/exportar";
        var mensajeInterno = $"Error en {urlInterna} con token {jwtFalso}";
        apiClient
            .Setup(c => c.GetBytesAsync(
                "/api/v1/planes/consolidado/exportar",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(500, mensajeInterno));

        // Act
        var resultado = await InvocarAccionProxyAsync(controller, "ConsolidadoExportar");

        // Assert: misma idea que 46 — código 500, mensaje genérico sin filtrar URL ni JWT.
        // Aceptamos StatusCodeResult u ObjectResult (la forma exacta la decide @BackendDev;
        // lo que NO puede ser es RedirectToAction ni RedirectResult ni ViewResult).
        Assert.IsNotType<RedirectToActionResult>(resultado);
        Assert.IsNotType<RedirectResult>(resultado);
        Assert.IsNotType<ViewResult>(resultado);

        var statusCode = resultado switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => -1,
        };
        Assert.Equal(500, statusCode);

        // Si es ObjectResult, verificamos el cuerpo. Si es StatusCodeResult puro (sin cuerpo),
        // no hay nada que filtrar.
        if (resultado is ObjectResult objectResult)
        {
            var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
            Assert.DoesNotContain(urlInterna, cuerpo);
            Assert.DoesNotContain("localhost:7269", cuerpo);
            Assert.DoesNotContain("Bearer", cuerpo);
            Assert.DoesNotContain(jwtFalso, cuerpo);
            Assert.DoesNotContain("eyJ", cuerpo);
        }
    }

    // ─── 48 (nuevo v3 — reflexión). Ambas acciones bajo [Authorize(Roles = "Gerente")] ──────────

    [Fact]
    public void ConsolidadoProxy_Autorizacion_SoloGerente()
    {
        // Arrange: reflexión sobre las acciones `ConsolidadoDatos` y `ConsolidadoExportar`
        // del PlanController (MVC). El contrato v3 exige que estén bajo [Authorize(Roles =
        // "Gerente")] — explícito o heredado de la clase — para no ampliar la superficie
        // de acceso del endpoint subyacente (que también es solo Gerente).
        var rolesClase = AuthorizeHelper.RolesDeClase(typeof(PlanController));

        foreach (var nombreAccion in new[] { "ConsolidadoDatos", "ConsolidadoExportar" })
        {
            // La acción DEBE existir (el contrato v3 la define; sin ella, el proxy no existe).
            var metodo = typeof(PlanController).GetMethod(
                nombreAccion,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            Assert.NotNull(metodo);  // FASE ROJA: la acción proxy aún no existe.

            // Si la acción tiene [Authorize] propio, sus roles mandan. Si no, hereda de la clase.
            var rolesAccion = AuthorizeHelper.RolesDeAccion(typeof(PlanController), nombreAccion);
            var rolesEfectivos = rolesAccion.Length > 0 ? rolesAccion : rolesClase;

            // Assert: el rol "Gerente" está presente (exactamente, sin ampliar a otros roles).
            // El endpoint subyacente es solo Gerente (RN-006) — el proxy no puede ampliar el
            // acceso (p. ej. no se admite "JefeArea").
            Assert.Contains("Gerente", rolesEfectivos);

            // Y específicamente NO está "JefeArea" ni "AdminTenant" ni "SuperAdmin" —
            // el proxy no añade nuevos roles al endpoint subyacente.
            Assert.DoesNotContain("JefeArea", rolesEfectivos);
            Assert.DoesNotContain("AdminTenant", rolesEfectivos);
            Assert.DoesNotContain("SuperAdmin", rolesEfectivos);
        }
    }

    // ─── 49 (nuevo v3 — falla hoy). UnauthorizedException → 401, NO redirect a login ──────────

    [Fact]
    public async Task ConsolidadoDatos_UnauthorizedException_Devuelve401NoRedirect()
    {
        // Arrange: el ApiClient lanza UnauthorizedException (refresh agotado / sesión expirada).
        // El proxy NO debe redirigir al login: un endpoint consumido por `fetch` recibiría el
        // HTML del login como 200 si sigue un 302 transparente → sesión inválida indetectable.
        // El contrato v3 exige HTTP 401 — el JS trata el 401 como sesión expirada y redirige.
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<ConsolidadoResponse>(
                "/api/v1/planes/consolidado",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("La sesión expiró. Vuelva a iniciar sesión."));

        // Act
        var resultado = await InvocarAccionProxyAsync(controller, "ConsolidadoDatos");

        // Assert 1: el código HTTP es 401 — el mismo que la API produciría sin proxy.
        var statusCode = resultado switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => -1,
        };
        Assert.Equal(401, statusCode);

        // Assert 2: NO es un RedirectToAction a Auth/Login — el fetch lo seguiría transparente
        // y el JS recibiría el HTML del login como 200. El contrato v3 es terminante.
        Assert.IsNotType<RedirectToActionResult>(resultado);
        Assert.IsNotType<RedirectResult>(resultado);
        Assert.IsNotType<ViewResult>(resultado);

        // Assert 3 (doble seguridad): si por error fuera un Redirect, no debería apuntar a Auth/Login.
        if (resultado is RedirectToActionResult redirect)
        {
            Assert.NotEqual("Auth", redirect.ControllerName);
            Assert.NotEqual("Login", redirect.ActionName);
        }
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.Tests.Helpers;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Tests.Helpers;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests de humo para AccionPlanController (MVC) — Spec HU-045 § "Tests de humo por controller MVC
/// nuevo" (#44-45). Tanda T2 (implementación completa): happy path GET + autorización por reflexión.
/// Contrato real (HU-019/020 § UI): clase [Authorize(Roles = "JefeArea,Gerente")] (HU-019 nota de
/// acceso); acción Index [Authorize(Roles = "JefeArea")] (escrituras solo JEF — RN-007; el Gerente
/// es solo lectura). Index(Guid? objetivoCgId) → View(AccionPlanIndexViewModel).
/// Moq sobre IApiClient (TEST-03). Patrón Arrange/Act/Assert (TEST-05).
///
/// ── HU-021 · Vista Gantt del Plan de Acción (casos 24-28 del spec, L644-650) ───────────────
/// Tests TDD en FASE ROJA (TEST-01): la acción MVC `Gantt()` y su ViewModel todavía NO existen →
/// el rojo de esta fase es el fallo de compilación del proyecto de tests (CS0246 / CS0117 / CS1061),
/// declarado por @Orquestador. El archivo se EXTIENDE (no se crea otro) para no duplicar la
/// fixture de TempData/HttpContext ni partir la regresión HU-019/HU-020 en dos clases.
/// Contrato que @BackendDev debe implementar (firmas exactas, sin adivinar):
///   · Acción:  public async Task&lt;IActionResult&gt; Gantt()   → View(AccionPlanGanttViewModel)
///              [Authorize(Roles = "JefeArea,Gerente")] en la acción (F7) — la clase ya lo tiene,
///              pero `Index` lo estrecha a JefeArea, así que `Gantt` necesita el suyo.
///   · Endpoint: GET /api/v1/acciones/gantt vía IApiClient.GetAsync&lt;GanttPlanResponse&gt;,
///              SIN query params (SEC-06: ni tenant_id ni ciclo_id ni area_id del cliente).
///              404 → estado vacío "No hay un ciclo activo" (UX-05); otro ApiClientException →
///              TempData["Error"] + vista (patrón de Index).
///   · ViewModel `AccionPlanGanttViewModel` (PE-GOL.Aplicacion/Models, namespace
///     PE_GOL.Aplicacion.Models): HayCicloActivo (bool) · HayAcciones (bool) · CicloNombre
///     (string?) · Escala (List&lt;GanttMesResponse&gt;) · ConteoPorStatus
///     (Dictionary&lt;string,int&gt;) · JsonPlan (string, payload serializado con
///     JavaScriptEncoder.Default y embebido en &lt;script type="application/json"&gt;).
/// </summary>
public class AccionPlanControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (AccionPlanController controller, Mock<IApiClient> apiClient) CrearController()
    {
        var apiClient = new Mock<IApiClient>();
        var sesionService = new SesionService(new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } });
        var controller = new AccionPlanController(apiClient.Object, sesionService, NullLogger<AccionPlanController>.Instance);

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

    private static ObjetivoCgResponse CrearCg() => new()
    {
        Id = Guid.NewGuid(),
        CicloId = Guid.NewGuid(),
        PilarId = Guid.NewGuid(),
        PilarNombre = "Crecimiento",
        Codigo = "GOL1.CG1",
        Descripcion = "Incrementar ventas 15%",
        TrimestreObjetivo = "Q1",
        Progreso = 50m,
        Semaforo = "Amarillo",
        AreaId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddDays(-10),
        UpdatedAt = DateTime.UtcNow
    };

    private static AccionPlanResponse CrearAccion(Guid objetivoCgId) => new()
    {
        Id = Guid.NewGuid(),
        ObjetivoCgId = objetivoCgId,
        Codigo = "GOL1.CG1.A1",
        Descripcion = "Lanzar campaña de marketing",
        ResponsableId = Guid.NewGuid(),
        ResponsableNombre = "Juan Pérez",
        FechaInicio = DateTime.Today.AddDays(-10),
        FechaVencimiento = DateTime.Today.AddDays(20),
        Clasificacion = "Estrategica",
        TipoPresupuesto = "CAPEX",
        Peso = 0.5m,
        Progreso = 40m,
        PuntuacionPonderada = 20m,
        Status = "EnProgreso",
        Orden = 1,
        CreatedAt = DateTime.UtcNow.AddDays(-10),
        UpdatedAt = DateTime.UtcNow
    };

    // ─── 44. Index_ConDatos_RetornaVistaConModelo ───────────────────────────

    [Fact]
    public async Task Index_ConDatos_RetornaVistaConModelo()
    {
        // Arrange: ciclo activo + CGs del área + acciones del CG seleccionado
        var (controller, apiClient) = CrearController();
        var cicloActivo = CrearCiclo(Guid.NewGuid(), "PE 2026", "Activo");
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cicloActivo]);
        var cg = CrearCg();
        apiClient
            .Setup(c => c.GetAsync<List<ObjetivoCgResponse>>(
                "/api/v1/objetivos-cg",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([cg]);
        var acciones = new List<AccionPlanResponse> { CrearAccion(cg.Id), CrearAccion(cg.Id) };
        apiClient
            .Setup(c => c.GetAsync<List<AccionPlanResponse>>(
                $"/api/v1/objetivos-cg/{cg.Id}/acciones",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(acciones);

        // Act
        var resultado = await controller.Index(cg.Id);

        // Assert: AccionPlanIndexViewModel con ciclo activo + CG + acciones mapeadas
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AccionPlanIndexViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(cg.Id, modelo.ObjetivoCgId);
        Assert.Equal("GOL1.CG1", modelo.ObjetivoCgCodigo);
        Assert.Equal(2, modelo.Items.Count);
        Assert.Equal("GOL1.CG1.A1", modelo.Items[0].Codigo);
    }

    // ─── 45. Index_TieneAuthorizeJefeArea ───────────────────────────────────

    [Fact]
    public void Index_TieneAuthorizeJefeArea()
    {
        // Arrange/Act: clase [Authorize(Roles = "JefeArea,Gerente")] (HU-019 nota de acceso) +
        // acción Index [Authorize(Roles = "JefeArea")] (escrituras solo JEF — RN-007)
        var rolesClase = AuthorizeHelper.RolesDeClase(typeof(AccionPlanController));
        Assert.Contains("JefeArea", rolesClase);
        Assert.Contains("Gerente", rolesClase);

        var rolesAccion = AuthorizeHelper.RolesDeAccion(typeof(AccionPlanController), nameof(AccionPlanController.Index));

        // Assert: la acción Index (listado con selector de CG) es solo JefeArea
        Assert.Contains("JefeArea", rolesAccion);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HU-021 — Vista Gantt del Plan de Acción (casos 24-28, spec L644-650)
    // ═══════════════════════════════════════════════════════════════════════

    private const string RutaGantt = "/api/v1/acciones/gantt";

    private static readonly string[] AbreviaturasMeses =
        ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    /// <summary>Payload de ejemplo devuelto por GET /api/v1/acciones/gantt (ApiResponse desenvuelto).
    /// Sin TenantId en ningún nivel (SEC-06) y con un status/fecha que exige escaping HTML
    /// (SEC-05) para verificar que JsonPlan se serializa con JavaScriptEncoder.Default.</summary>
    private static GanttPlanResponse CrearPayloadGantt(bool conAcciones = true)
    {
        var cicloId = Guid.NewGuid();
        var cgId = Guid.NewGuid();
        var areaId = Guid.NewGuid();

        var payload = new GanttPlanResponse
        {
            CicloId = cicloId,
            CicloNombre = "PE 2026",
            AñoFiscal = 2026,
            MesInicio = 6,
            FechaInicioCiclo = new DateTime(2026, 6, 1),
            FechaFinCiclo = new DateTime(2027, 5, 31),
            ConteoPorStatus = conAcciones
                ? new Dictionary<string, int>
                {
                    ["NoIniciado"] = 1,
                    ["EnProgreso"] = 1,
                    ["Terminado"] = 0,
                    ["Atrasado"] = 0
                }
                : new Dictionary<string, int>
                {
                    ["NoIniciado"] = 0,
                    ["EnProgreso"] = 0,
                    ["Terminado"] = 0,
                    ["Atrasado"] = 0
                }
        };

        // CA #1: 12 meses desde mes_inicio, con cruce de año (mes_inicio = 6)
        var baseEscala = new DateTime(2026, 6, 1);
        for (var i = 0; i < 12; i++)
        {
            var mes = baseEscala.AddMonths(i);
            payload.Escala.Add(new GanttMesResponse
            {
                Anio = mes.Year,
                Mes = mes.Month,
                Etiqueta = $"{AbreviaturasMeses[mes.Month - 1]} {mes.Year}"
            });
        }

        if (conAcciones)
        {
            payload.Grupos.Add(new GanttGrupoResponse
            {
                ObjetivoCgId = cgId,
                Codigo = "GOL1.CG1",
                Descripcion = "Incrementar ventas 15%",
                AreaId = areaId,
                AreaCodigo = "GOL1",
                AreaNombre = "CEDIS FARMA",
                Progreso = 0.5m,
                Semaforo = "Amarillo",
                TotalAcciones = 2
            });

            payload.Acciones.Add(new GanttAccionResponse
            {
                Id = Guid.NewGuid(),
                ObjetivoCgId = cgId,
                AreaId = areaId,
                Codigo = "GOL1.CG1.01",
                Descripcion = "Lanzar campaña <script>alert(1)</script>",
                FechaInicio = new DateTime(2026, 6, 1),
                FechaVencimiento = new DateTime(2026, 8, 31),
                Progreso = 40m,
                Status = "EnProgreso",
                Clasificacion = "Proyecto",
                TipoPresupuesto = "OPEX",
                Peso = 0.6m,
                ResponsableNombre = "Juan Pérez",
                Orden = 1
            });

            payload.Acciones.Add(new GanttAccionResponse
            {
                Id = Guid.NewGuid(),
                ObjetivoCgId = cgId,
                AreaId = areaId,
                Codigo = "GOL1.CG1.02",
                Descripcion = "Abrir 3 tiendas nuevas",
                FechaInicio = new DateTime(2026, 9, 1),
                FechaVencimiento = new DateTime(2026, 11, 30),
                Progreso = 0m,
                Status = "NoIniciado",
                Clasificacion = "Iniciativa",
                TipoPresupuesto = "CAPEX",
                Peso = 0.4m,
                ResponsableNombre = null,
                Orden = 2
            });
        }

        return payload;
    }

    private static void ConfigurarGantt(Mock<IApiClient> apiClient, GanttPlanResponse payload)
        => apiClient
            .Setup(c => c.GetAsync<GanttPlanResponse>(
                RutaGantt,
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

    // ─── 24. Gantt_SinCicloActivo_RetornaVistaConEmptyState ─────────────────

    [Fact]
    public async Task Gantt_SinCicloActivo_RetornaVistaConEmptyState()
    {
        // Arrange: la API responde 404 (no hay ciclo Activo — RC-01) vía ApiClientException
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<GanttPlanResponse>(
                RutaGantt,
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "No hay un ciclo activo para el tenant"));

        // Act
        var resultado = await controller.Gantt();

        // Assert: estado vacío (UX-05) — la vista se RENDERIZA, no es una redirección ni un 404
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AccionPlanGanttViewModel>(view.Model);
        Assert.False(modelo.HayCicloActivo);
        Assert.False(modelo.HayAcciones);

        // El 404 es un estado de UI, no un error: NO se muestra el banner TempData["Error"]
        // (a diferencia del caso 27, que sí es un fallo de la API).
        Assert.False(controller.TempData.ContainsKey("Error"));
    }

    // ─── 25. Gantt_SinAcciones_RetornaVistaConEmptyState ───────────────────

    [Fact]
    public async Task Gantt_SinAcciones_RetornaVistaConEmptyState()
    {
        // Arrange: 200 con payload válido pero sin grupos ni acciones (D-C: no es 404)
        var (controller, apiClient) = CrearController();
        var payload = CrearPayloadGantt(conAcciones: false);
        ConfigurarGantt(apiClient, payload);

        // Act
        var resultado = await controller.Gantt();

        // Assert: hay ciclo activo, pero no hay nada que dibujar
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AccionPlanGanttViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.False(modelo.HayAcciones);

        // La escala viaja igual (12 meses) aunque no haya acciones: es la base de la vista
        Assert.Equal(12, modelo.Escala.Count);
        Assert.Equal("JUN 2026", modelo.Escala[0].Etiqueta);
        Assert.Equal("MAY 2027", modelo.Escala[11].Etiqueta);
    }

    // ─── 26. Gantt_ConAcciones_RetornaVistaConDatosSerializados ────────────

    [Fact]
    public async Task Gantt_ConAcciones_RetornaVistaConDatosSerializados()
    {
        // Arrange
        var (controller, apiClient) = CrearController();
        var payload = CrearPayloadGantt();
        ConfigurarGantt(apiClient, payload);

        // Act
        var resultado = await controller.Gantt();

        // Assert
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<AccionPlanGanttViewModel>(view.Model);
        Assert.True(modelo.HayCicloActivo);
        Assert.True(modelo.HayAcciones);
        Assert.Equal("PE 2026", modelo.CicloNombre);
        Assert.Equal(12, modelo.Escala.Count);
        Assert.Equal(4, modelo.ConteoPorStatus.Count);
        Assert.Equal(1, modelo.ConteoPorStatus["EnProgreso"]);
        Assert.Equal(1, modelo.ConteoPorStatus["NoIniciado"]);

        // JsonPlan: JSON VÁLIDO (lo embebe gantt-plan.js en <script type="application/json">)
        Assert.False(string.IsNullOrWhiteSpace(modelo.JsonPlan));
        using (var documento = JsonDocument.Parse(modelo.JsonPlan))
            Assert.Equal(JsonValueKind.Object, documento.RootElement.ValueKind);

        // Contiene los id de las acciones (el front construye las tareas desde el payload)
        foreach (var accion in payload.Acciones)
            Assert.Contains(accion.Id.ToString(), modelo.JsonPlan);

        // SEC-06: el payload serializado NUNCA expone TenantId
        Assert.DoesNotContain("tenantid", modelo.JsonPlan, StringComparison.OrdinalIgnoreCase);

        // SEC-05: la descripción trae HTML y se serializa con JavaScriptEncoder.Default
        // (NO UnsafeRelaxedJsonEscaping) → el '<' viaja como < y nunca crudo.
        Assert.DoesNotContain("<script>", modelo.JsonPlan, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 27. Gantt_ApiClientFalla_MuestraErrorYNoRompe ────────────────────

    [Fact]
    public async Task Gantt_ApiClientFalla_MuestraErrorYNoRompe()
    {
        // Arrange: la API responde 500 → ApiClientException (mismo patrón que Index)
        var (controller, apiClient) = CrearController();
        apiClient
            .Setup(c => c.GetAsync<GanttPlanResponse>(
                RutaGantt,
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(500, "Error interno del servidor"));

        // Act
        var resultado = await controller.Gantt();

        // Assert: la vista se renderiza con el mensaje de error (la vista no se rompe)
        var view = Assert.IsType<ViewResult>(resultado);
        Assert.IsType<AccionPlanGanttViewModel>(view.Model);
        Assert.True(controller.TempData.ContainsKey("Error"));
        Assert.Equal("Error interno del servidor", controller.TempData["Error"]);
    }

    // ─── 28. Gantt_Autorizacion_SoloJefeAreaYGerente ───────────────────────

    [Fact]
    public void Gantt_Autorizacion_SoloJefeAreaYGerente()
    {
        // Arrange/Act: reflexión sobre el atributo [Authorize] de la acción Gantt
        var rolesAccion = AuthorizeHelper.RolesDeAccion(
            typeof(AccionPlanController), nameof(AccionPlanController.Gantt));

        // Assert: la acción Gantt declara [Authorize(Roles = "JefeArea,Gerente")] — exactamente
        // esos 2 roles (ni AdminTenant ni SuperAdmin) y en ese conjunto, sea cual sea el orden.
        Assert.Equal(2, rolesAccion.Length);
        Assert.Contains("JefeArea", rolesAccion);
        Assert.Contains("Gerente", rolesAccion);

        // La clase conserva [Authorize(Roles = "JefeArea,Gerente")] (F7: el Gantt lo ven JEF y
        // Gerente; el Gerente es el principal beneficiario del Gantt multi-área).
        var rolesClase = AuthorizeHelper.RolesDeClase(typeof(AccionPlanController));
        Assert.Contains("JefeArea", rolesClase);
        Assert.Contains("Gerente", rolesClase);
    }
}
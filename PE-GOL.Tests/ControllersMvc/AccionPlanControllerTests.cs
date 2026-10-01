using System.Reflection;
using System.Security.Claims;
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
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Tests.Helpers;
using PE_GOL.Utility.Exceptions;

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

    // ═══════════════════════════════════════════════════════════════════════
    //  HU-022-hotfix — Proxies MVC + permisos UI + JWT fuera del DOM
    //  Spec: specs/sprint-04/HU-022-hotfix.spec.md (Aprobado 2026-10-01).
    //  ADR-016 (proxy MVC) · SEC-01/SEC-05 (JWT fuera del navegador).
    //
    //  FASE ROJA TDD (TEST-01): las 4 acciones proxy (EntregablesDatos /
    //  EntregablesSubir / EntregablesDescarga / EntregablesEliminar) y la
    //  propiedad PuedeSubir en el ViewModel TODAVÍA NO EXISTEN en el código
    //  actual. Para evitar fallo de compilación del proyecto de tests (CS0117/
    //  CS1061 si se invocan directamente), se invocan por REFLEXIÓN por nombre
    //  (patrón espejo del hotfix v3 de HU-023 — PlanControllerTests.cs casos
    //  43-49). El rojo legítimo es Assert.NotNull(metodo) que falla con mensaje
    //  claro en tiempo de test (no de compilación).
    //
    //  No se mockea el overload multi-archivo de IApiClient.PostMultipartAsync
    //  (no existe aún — Spec § B.4): mockearlo no compilaría. El contrato se
    //  verifica por la FORMA del IActionResult devuelto (status 200 + payload
    //  IEnumerable<EntregableAdjuntoResponse>).
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Helpers para el hotfix de HU-022 ──────────────────────────────

    /// <summary>
    /// Busca una acción del AccionPlanController (MVC) por nombre (case-insensitive).
    /// Falla con mensaje claro si la acción no existe — fase roja del hotfix (TEST-01).
    /// Espejo del helper homónimo de PlanControllerTests.
    /// </summary>
    private static MethodInfo ObtenerAccionEntregables(string nombreAccion)
    {
        var metodo = typeof(AccionPlanController).GetMethod(
            nombreAccion,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.NotNull(metodo);  // FASE ROJA: la acción proxy aún no existe
        return metodo!;
    }

    /// <summary>
    /// Invoca una acción proxy del hotfix por reflexión. Construye los argumentos
    /// por nombre de parámetro. Acepta los nombres del contrato del spec § B.2:
    ///   · id (Guid)             → ruta convencional
    ///   · entregableId (Guid?)  → [FromQuery]
    ///   · archivos (object?, List&lt;IFormFile&gt; esperado) → [FromForm]
    /// </summary>
    private static async Task<IActionResult> InvocarAccionProxyEntregablesAsync(
        AccionPlanController controller,
        string nombreAccion,
        Guid id,
        Guid? entregableId = null,
        object? archivos = null)
    {
        var metodo = ObtenerAccionEntregables(nombreAccion);
        var parametros = metodo.GetParameters();
        var args = new object?[parametros.Length];
        for (var i = 0; i < parametros.Length; i++)
        {
            var p = parametros[i];
            if (string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase))
                args[i] = id;
            else if (string.Equals(p.Name, "entregableId", StringComparison.OrdinalIgnoreCase))
                args[i] = entregableId;
            else if (string.Equals(p.Name, "archivos", StringComparison.OrdinalIgnoreCase))
                args[i] = archivos;
            else if (p.HasDefaultValue)
                args[i] = p.DefaultValue;
            else if (p.ParameterType.IsValueType)
                args[i] = Activator.CreateInstance(p.ParameterType);
            else
                args[i] = null;
        }

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

    /// <summary>Construye un IFormFile mockeado (Moq) para tests de multipart.</summary>
    private static IFormFile CrearFormFileMock(string nombre, long tamano = 1024, string contentType = "application/pdf")
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(nombre);
        mock.Setup(f => f.Length).Returns(tamano);
        mock.Setup(f => f.ContentType).Returns(contentType);
        return mock.Object;
    }

    /// <summary>Construye una lista de IFormFile mockeados para tests de multipart multi-archivo.</summary>
    private static List<IFormFile> CrearArchivosDePrueba(int cantidad)
    {
        var lista = new List<IFormFile>();
        for (var i = 0; i < cantidad; i++)
            lista.Add(CrearFormFileMock($"archivo-{i}.pdf", tamano: 2048));
        return lista;
    }

    /// <summary>
    /// Helper para los casos 15 y 16: configura los 3 mocks que la acción
    /// Entregables realiza hoy (acción, adjuntos, ciclos) y asigna el rol
    /// indicado al HttpContext.User para que User.IsInRole() devuelva true/false.
    /// </summary>
    private static void ConfigurarEntregablesConRol(
        AccionPlanController controller,
        Mock<IApiClient> apiClient,
        Guid accionId,
        string rol)
    {
        var cgId = Guid.NewGuid();
        var accion = new AccionPlanResponse
        {
            Id = accionId,
            ObjetivoCgId = cgId,
            Codigo = "GOL1.CG1.A1",
            Descripcion = "Lanzar campaña de marketing",
            FechaInicio = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddMonths(2),
            Clasificacion = "Proyecto",
            TipoPresupuesto = "OPEX",
            Peso = 0.5m,
            Progreso = 0m,
            Status = "NoIniciado",
            Orden = 1
        };
        apiClient
            .Setup(c => c.GetAsync<AccionPlanResponse>(
                $"/api/v1/acciones/{accionId}",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accion);
        apiClient
            .Setup(c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                $"/api/v1/acciones/{accionId}/entregables",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoResponse>());
        apiClient
            .Setup(c => c.GetAsync<List<CicloResponse>>(
                "/api/v1/ciclos",
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CicloResponse>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.NewGuid(),
                    Nombre = "PE 2026",
                    AñoFiscal = 2026,
                    MesInicio = 1,
                    Estado = "Activo"
                }
            });

        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, rol) }, "Cookie"));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Bloque A — Proxies (12 casos)
    // ═══════════════════════════════════════════════════════════════════

    private const string RutaDatos = "/api/v1/acciones/{0}/entregables";
    private const string RutaSubir = "/api/v1/acciones/{0}/entregables";
    private const string RutaDescarga = "/api/v1/acciones/{0}/entregables/{1}/descarga";
    private const string RutaEliminar = "/api/v1/acciones/{0}/entregables/{1}";

    private static string D(string patron, params object[] args) => string.Format(patron, args);

    // ─── 1. EntregablesDatos_ConAdjuntos_DevuelvePayloadJson200 ────────

    [Fact]
    public async Task EntregablesDatos_ConAdjuntos_DevuelvePayloadJson200()
    {
        // Arrange: mock del listado de adjuntos con 2 elementos
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var adjuntos = new List<EntregableAdjuntoResponse>
        {
            new()
            {
                Id = Guid.NewGuid(), AccionId = accionId, NombreArchivo = "doc1.pdf",
                TamanoBytes = 1024, TamanoLegible = "1,0 KB", TipoMime = "application/pdf",
                Extension = "pdf", PuedeEliminar = true, CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(), AccionId = accionId, NombreArchivo = "doc2.pdf",
                TamanoBytes = 2048, TamanoLegible = "2,0 KB", TipoMime = "application/pdf",
                Extension = "pdf", PuedeEliminar = false, CreatedAt = DateTime.UtcNow
            }
        };
        apiClient
            .Setup(c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                D(RutaDatos, accionId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(adjuntos);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDatos", accionId);

        // Assert: 200 con la lista íntegra (D-5: payload directo, sin wrapper ApiResponse<T>)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.Same(adjuntos, objectResult.Value);

        apiClient.Verify(
            c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                D(RutaDatos, accionId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 2. EntregablesDatos_ListaVacia_Devuelve200ConArrayVacio ────────

    [Fact]
    public async Task EntregablesDatos_ListaVacia_Devuelve200ConArrayVacio()
    {
        // Arrange: la API devuelve 200 con lista vacía (acción sin adjuntos)
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var listaVacia = new List<EntregableAdjuntoResponse>();
        apiClient
            .Setup(c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                D(RutaDatos, accionId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(listaVacia);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDatos", accionId);

        // Assert: 200 con array vacío (D-C: nunca 404 — la lista vacía es estado de UI, no error)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        var payload = Assert.IsAssignableFrom<IEnumerable<EntregableAdjuntoResponse>>(objectResult.Value);
        Assert.Empty(payload);
    }

    // ─── 3. EntregablesDatos_ApiClientException_DevuelveMismoStatusSinDetallesInternos ────────

    [Fact]
    public async Task EntregablesDatos_ApiClientException_DevuelveMismoStatusSinDetallesInternos()
    {
        // Arrange: la API lanza 403 con un mensaje que contiene URL INTERNA y JWT falso.
        // El proxy NO debe filtrar estos detalles al navegador (ADR-016, regla dura).
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var jwtFalso = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.fake.fake";
        var urlInterna = D("https://localhost:7269{0}", D(RutaDatos, accionId));
        var mensajeInterno = $"Error en {urlInterna} con token {jwtFalso}";
        apiClient
            .Setup(c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                D(RutaDatos, accionId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(403, mensajeInterno));

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDatos", accionId);

        // Assert 1: el código HTTP devuelto al navegador es el MISMO que devolvió la API (403)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(403, objectResult.StatusCode);

        // Assert 2 (regla dura ADR-016): el cuerpo de la respuesta NO contiene URL interna, ni Bearer, ni JWT
        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.DoesNotContain(urlInterna, cuerpo);
        Assert.DoesNotContain("localhost:7269", cuerpo);
        Assert.DoesNotContain("Bearer", cuerpo);
        Assert.DoesNotContain(jwtFalso, cuerpo);
        Assert.DoesNotContain("eyJ", cuerpo);  // Prefijo típico de JWT

        // Assert 3: el cuerpo expone {message, errors} para que el JS muestre mensajes user-facing (D-4)
        Assert.Contains("message", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("errors", cuerpo, StringComparison.OrdinalIgnoreCase);

        // Assert 4: NO es un RedirectToAction (endpoint consumido por fetch)
        Assert.IsNotType<RedirectToActionResult>(resultado);
    }

    // ─── 4. EntregablesDatos_UnauthorizedException_Devuelve401NoRedirect ────────

    [Fact]
    public async Task EntregablesDatos_UnauthorizedException_Devuelve401NoRedirect()
    {
        // Arrange: el ApiClient lanza UnauthorizedException (refresh agotado / sesión expirada).
        // El proxy NO debe redirigir al login: un endpoint consumido por fetch recibiría el
        // HTML del login como 200 si sigue un 302 transparente → sesión inválida indetectable.
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        apiClient
            .Setup(c => c.GetAsync<List<EntregableAdjuntoResponse>>(
                D(RutaDatos, accionId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("La sesión expiró. Vuelva a iniciar sesión."));

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDatos", accionId);

        // Assert 1: el código HTTP es 401 (mismo que la API produciría sin proxy)
        var statusCode = resultado switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => -1
        };
        Assert.Equal(401, statusCode);

        // Assert 2: NO es RedirectToAction a Auth/Login (precedente caso 49 HU-023)
        Assert.IsNotType<RedirectToActionResult>(resultado);
        Assert.IsNotType<RedirectResult>(resultado);
        Assert.IsNotType<ViewResult>(resultado);
    }

    // ─── 5. EntregablesSubir_ConArchivos_ProxeaMultipartYDevuelveCreados ────────

    [Fact]
    public async Task EntregablesSubir_ConArchivos_ProxeaMultipartYDevuelveCreados()
    {
        // Arrange: 2 IFormFile de prueba + mock del overload multi-archivo de IApiClient.
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var archivos = CrearArchivosDePrueba(2);
        var creados = new List<EntregableAdjuntoResponse>
        {
            new()
            {
                Id = Guid.NewGuid(), AccionId = accionId, NombreArchivo = "archivo-0.pdf",
                TamanoBytes = 2048, TamanoLegible = "2,0 KB", TipoMime = "application/pdf",
                Extension = "pdf", PuedeEliminar = true, CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(), AccionId = accionId, NombreArchivo = "archivo-1.pdf",
                TamanoBytes = 2048, TamanoLegible = "2,0 KB", TipoMime = "application/pdf",
                Extension = "pdf", PuedeEliminar = true, CreatedAt = DateTime.UtcNow
            }
        };
        apiClient
            .Setup(c => c.PostMultipartAsync<List<EntregableAdjuntoResponse>>(
                D(RutaSubir, accionId),
                It.IsAny<IReadOnlyList<IFormFile>>(),
                "archivos",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(creados);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesSubir", accionId, archivos: archivos);

        // Assert: 200 con los creados. La forma exacta (List, Array) la decide @BackendDev;
        // lo que no puede ser es Redirect ni View (es un endpoint consumido por fetch).
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.IsAssignableFrom<IEnumerable<EntregableAdjuntoResponse>>(objectResult.Value);

        apiClient.Verify(
            c => c.PostMultipartAsync<List<EntregableAdjuntoResponse>>(
                D(RutaSubir, accionId),
                It.IsAny<IReadOnlyList<IFormFile>>(),
                "archivos",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 6. EntregablesSubir_SinArchivos_Devuelve400SinLlamarALaApi ────────

    [Fact]
    public async Task EntregablesSubir_SinArchivos_Devuelve400SinLlamarALaApi()
    {
        // Arrange: lista de archivos vacía → el guard del proxy debe responder 400
        // SIN invocar a la API (defensa en profundidad; el JS ya lo impide — entregables.js L195-198).
        var (controller, _) = CrearController();
        var accionId = Guid.NewGuid();
        var archivosVacios = new List<IFormFile>();

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesSubir", accionId, archivos: archivosVacios);

        // Assert 1: 400 con un mensaje user-facing
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(400, objectResult.StatusCode);

        // Assert 2: el cuerpo contiene un message (la UI lo muestra como toast)
        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.Contains("message", cuerpo, StringComparison.OrdinalIgnoreCase);

        // Assert 3: no es Redirect ni View (consumido por fetch, igual que los demás proxies)
        Assert.IsNotType<RedirectToActionResult>(resultado);
        Assert.IsNotType<ViewResult>(resultado);
    }

    // ─── 7. EntregablesSubir_ApiClientException422_Devuelve422ConErroresDeValidacion ────────

    [Fact]
    public async Task EntregablesSubir_ApiClientException422_Devuelve422ConErroresDeValidacion()
    {
        // Arrange: la API devuelve 422 con errores de validación de forma (CA #5).
        // Los mensajes de validación DEBEN viajar al usuario (D-4): sin esta política,
        // un "máx. 5 archivos" llegaría como "Error al subir" y no sabría qué corregir.
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var archivos = CrearArchivosDePrueba(3);
        apiClient
            .Setup(c => c.PostMultipartAsync<List<EntregableAdjuntoResponse>>(
                D(RutaSubir, accionId),
                It.IsAny<IReadOnlyList<IFormFile>>(),
                "archivos",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(
                422,
                "Los archivos adjuntos no son válidos.",
                new List<string> { "No se pueden subir más de 5 archivos por acción." }));

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesSubir", accionId, archivos: archivos);

        // Assert: el catch del ApiClientException(422) debe devolver 422 con cuerpo que contenga
        // los `errors` de validación (viajan al usuario, D-4) y SIN detalles internos
        // (URL interna, JWT, Bearer — ADR-016).
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(422, objectResult.StatusCode);

        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.Contains("errors", cuerpo, StringComparison.OrdinalIgnoreCase);

        // El cuerpo NO debe filtrar URL interna ni JWT (regla dura ADR-016).
        Assert.DoesNotContain("localhost:7269", cuerpo);
        Assert.DoesNotContain("Bearer", cuerpo);
        Assert.DoesNotContain("eyJ", cuerpo);  // Prefijo típico de JWT

        Assert.IsNotType<RedirectToActionResult>(resultado);
    }

    // ─── 8. EntregablesDescarga_ConIds_ProxeaApiYDevuelveUrlFirmada ────────

    [Fact]
    public async Task EntregablesDescarga_ConIds_ProxeaApiYDevuelveUrlFirmada()
    {
        // Arrange: mock → EntregableDescargaResponse con Url firmada (24 h, ARCH-06)
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var entregableId = Guid.NewGuid();
        var urlFirmada = "https://supabase.example/storage/v1/object/sign/entregables/abc?token=xyz";
        var payload = new EntregableDescargaResponse
        {
            Id = entregableId,
            AccionId = accionId,
            NombreArchivo = "informe.pdf",
            TamanoBytes = 102400,
            TipoMime = "application/pdf",
            Url = urlFirmada,
            ExpiraEn = DateTimeOffset.UtcNow.AddHours(24)
        };
        apiClient
            .Setup(c => c.GetAsync<EntregableDescargaResponse>(
                D(RutaDescarga, accionId, entregableId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDescarga", accionId, entregableId: entregableId);

        // Assert: 200 con el payload íntegro (D-5)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.Same(payload, objectResult.Value);

        apiClient.Verify(
            c => c.GetAsync<EntregableDescargaResponse>(
                D(RutaDescarga, accionId, entregableId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 9. EntregablesDescarga_UrlNull_Devuelve200ConUrlNull ────────

    [Fact]
    public async Task EntregablesDescarga_UrlNull_Devuelve200ConUrlNull()
    {
        // Arrange: degradación elegante (D-G, RNF-014): si el Storage falla, la BLL devuelve
        // 200 con Url=null y la UI muestra un aviso — NUNCA un 500 (mejor UX).
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var entregableId = Guid.NewGuid();
        var payload = new EntregableDescargaResponse
        {
            Id = entregableId,
            AccionId = accionId,
            NombreArchivo = "informe.pdf",
            TamanoBytes = 102400,
            TipoMime = "application/pdf",
            Url = null,  // ← degradación: Storage falló al firmar
            ExpiraEn = DateTimeOffset.UtcNow.AddHours(24)
        };
        apiClient
            .Setup(c => c.GetAsync<EntregableDescargaResponse>(
                D(RutaDescarga, accionId, entregableId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDescarga", accionId, entregableId: entregableId);

        // Assert: 200 con Url=null (no 500)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        var descarga = Assert.IsType<EntregableDescargaResponse>(objectResult.Value);
        Assert.Null(descarga.Url);
    }

    // ─── 10. EntregablesDescarga_ApiClientException404_Devuelve404 ────────

    [Fact]
    public async Task EntregablesDescarga_ApiClientException404_Devuelve404()
    {
        // Arrange: la API responde 404 (entregable inexistente / Guid.Empty)
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var entregableId = Guid.NewGuid();
        apiClient
            .Setup(c => c.GetAsync<EntregableDescargaResponse>(
                D(RutaDescarga, accionId, entregableId),
                It.IsAny<IDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "Entregable no encontrado"));

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesDescarga", accionId, entregableId: entregableId);

        // Assert: 404 con {message, errors} — sin filtrar detalles internos (mismo cuerpo que D-4)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(404, objectResult.StatusCode);

        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.Contains("message", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("errors", cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 11. EntregablesEliminar_ConIds_ProxeaDeleteYDevuelveTrue ────────

    [Fact]
    public async Task EntregablesEliminar_ConIds_ProxeaDeleteYDevuelveTrue()
    {
        // Arrange: mock del DeleteAsync<bool> → true (DELETE /api/v1/acciones/{id}/entregables/{entregableId})
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var entregableId = Guid.NewGuid();
        apiClient
            .Setup(c => c.DeleteAsync<bool>(
                D(RutaEliminar, accionId, entregableId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesEliminar", accionId, entregableId: entregableId);

        // Assert: 200 con bool=true (D-5: payload directo)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(200, objectResult.StatusCode ?? 200);
        Assert.Equal(true, objectResult.Value);

        apiClient.Verify(
            c => c.DeleteAsync<bool>(
                D(RutaEliminar, accionId, entregableId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 12. EntregablesEliminar_ApiClientException_DevuelveMismoStatusSinDetallesInternos ────────

    [Fact]
    public async Task EntregablesEliminar_ApiClientException_DevuelveMismoStatusSinDetallesInternos()
    {
        // Arrange: la API lanza 403 con URL interna y JWT falso en el mensaje.
        // Misma regla dura de ADR-016 que el caso 3 (Defecto B).
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        var entregableId = Guid.NewGuid();
        var jwtFalso = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.fake.fake";
        var urlInterna = D("https://localhost:7269{0}", D(RutaEliminar, accionId, entregableId));
        var mensajeInterno = $"Error en {urlInterna} con token {jwtFalso}";
        apiClient
            .Setup(c => c.DeleteAsync<bool>(
                D(RutaEliminar, accionId, entregableId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(403, mensajeInterno));

        // Act
        var resultado = await InvocarAccionProxyEntregablesAsync(
            controller, "EntregablesEliminar", accionId, entregableId: entregableId);

        // Assert 1: código HTTP 403 (mismo que la API)
        var objectResult = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(403, objectResult.StatusCode);

        // Assert 2: cuerpo sin URL interna, sin Bearer, sin JWT
        var cuerpo = objectResult.Value?.ToString() ?? string.Empty;
        Assert.DoesNotContain(urlInterna, cuerpo);
        Assert.DoesNotContain("localhost:7269", cuerpo);
        Assert.DoesNotContain("Bearer", cuerpo);
        Assert.DoesNotContain(jwtFalso, cuerpo);
        Assert.DoesNotContain("eyJ", cuerpo);

        // Assert 3: expone {message, errors}
        Assert.Contains("message", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("errors", cuerpo, StringComparison.OrdinalIgnoreCase);

        // Assert 4: NO es Redirect (fetch)
        Assert.IsNotType<RedirectToActionResult>(resultado);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Bloque B — Autorización por reflexión (2 casos)
    // ═══════════════════════════════════════════════════════════════════

    // ─── 13. EntregablesSubir_Autorizacion_SoloJefeArea ────────

    [Fact]
    public void EntregablesSubir_Autorizacion_SoloJefeArea()
    {
        // Arrange/Act: reflexión sobre el atributo [Authorize] de la acción EntregablesSubir.
        // FASE ROJA: la acción no existe → RolesDeAccion devuelve [] → Assert.Contains falla.
        var rolesAccion = AuthorizeHelper.RolesDeAccion(
            typeof(AccionPlanController), "EntregablesSubir");

        // Assert: la acción declara [Authorize(Roles = "JefeArea")] EXPLÍCITO (no heredado).
        // La clase admite JEF+GER, pero el proxy de subida DEBE estrecharla a JEF porque
        // la API subyacente es JEF-only (PE-GOL.API/Controllers/AccionPlanController.cs:174).
        // Espejo del caso 48 de HU-023.
        Assert.Contains("JefeArea", rolesAccion);

        // Y NO admite Gerente (la API no lo admite — SEC-07, RN-007).
        Assert.DoesNotContain("Gerente", rolesAccion);
    }

    // ─── 14. EntregablesProxy_DatosDescargaEliminar_Autorizacion_JefeAreaGerente ────────

    [Fact]
    public void EntregablesProxy_DatosDescargaEliminar_Autorizacion_JefeAreaGerente()
    {
        // Arrange: reflexión sobre las 3 acciones del proxy que la clase admite por defecto
        // (JEF+GER). El proxy NO amplía la superficie de acceso del endpoint subyacente.
        var rolesClase = AuthorizeHelper.RolesDeClase(typeof(AccionPlanController));
        Assert.Contains("JefeArea", rolesClase);
        Assert.Contains("Gerente", rolesClase);

        // Act + Assert: para cada acción del bloque de lectura/borrado
        foreach (var nombreAccion in new[] { "EntregablesDatos", "EntregablesDescarga", "EntregablesEliminar" })
        {
            // La acción DEBE existir (FASE ROJA: Assert.NotNull falla si no existe).
            var metodo = typeof(AccionPlanController).GetMethod(
                nombreAccion,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            Assert.NotNull(metodo);

            // Si la acción tiene [Authorize] propio, sus roles mandan; si no, hereda de la clase.
            var rolesAccion = AuthorizeHelper.RolesDeAccion(typeof(AccionPlanController), nombreAccion);
            var rolesEfectivos = rolesAccion.Length > 0 ? rolesAccion : rolesClase;

            // El proxy admite JEF y GER (espejo del endpoint subyacente: el listado es
            // JEF+GER, la descarga es JEF+GER, el borrado es JEF+GER — SEC-07).
            Assert.Contains("JefeArea", rolesEfectivos);
            Assert.Contains("Gerente", rolesEfectivos);

            // Y NO añade nuevos roles (AdminTenant/SuperAdmin no tienen acceso a Entregables).
            Assert.DoesNotContain("AdminTenant", rolesEfectivos);
            Assert.DoesNotContain("SuperAdmin", rolesEfectivos);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Bloque C — Action Entregables + ViewModel (3 casos)
    // ═══════════════════════════════════════════════════════════════════

    // ─── 15. Entregables_RolJefeArea_PuedeSubirTrue ────────

    [Fact]
    public async Task Entregables_RolJefeArea_PuedeSubirTrue()
    {
        // Arrange: mocks de los 3 endpoints que la acción Entregables consume hoy + rol JEF.
        // El controller actual NO setea PuedeSubir en el ViewModel → FASE ROJA en la
        // reflexión de la propiedad (Assert.NotNull(propPuedeSubir) falla).
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        ConfigurarEntregablesConRol(controller, apiClient, accionId, "JefeArea");

        // Act
        var resultado = await controller.Entregables(accionId);

        // Assert 1: la acción devuelve un ViewResult con el ViewModel correcto.
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<EntregablesViewModel>(view.Model);

        // Assert 2: reflexión sobre PuedeSubir (la propiedad NO existe aún → FASE ROJA).
        // El fix debe calcularla server-side como User.IsInRole("JefeArea") (D-7).
        var propPuedeSubir = typeof(EntregablesViewModel).GetProperty("PuedeSubir");
        Assert.NotNull(propPuedeSubir);  // FASE ROJA: PuedeSubir no existe actualmente
        Assert.Equal(typeof(bool), propPuedeSubir!.PropertyType);
        Assert.True((bool)propPuedeSubir!.GetValue(modelo)!);  // JEF → true
    }

    // ─── 16. Entregables_RolGerente_PuedeSubirFalse ────────

    [Fact]
    public async Task Entregables_RolGerente_PuedeSubirFalse()
    {
        // Arrange: misma configuración que 15 pero con rol Gerente.
        var (controller, apiClient) = CrearController();
        var accionId = Guid.NewGuid();
        ConfigurarEntregablesConRol(controller, apiClient, accionId, "Gerente");

        // Act
        var resultado = await controller.Entregables(accionId);

        // Assert 1: ViewResult con ViewModel correcto.
        var view = Assert.IsType<ViewResult>(resultado);
        var modelo = Assert.IsType<EntregablesViewModel>(view.Model);

        // Assert 2: reflexión sobre PuedeSubir (GER no puede subir — la API es JEF-only).
        var propPuedeSubir = typeof(EntregablesViewModel).GetProperty("PuedeSubir");
        Assert.NotNull(propPuedeSubir);  // FASE ROJA: PuedeSubir no existe actualmente
        Assert.Equal(typeof(bool), propPuedeSubir!.PropertyType);
        Assert.False((bool)propPuedeSubir!.GetValue(modelo)!);  // GER → false
    }

    // ─── 17. EntregablesViewModel_Reflexion_SinAccessTokenConPuedeSubir ────────

    [Fact]
    public void EntregablesViewModel_Reflexion_SinAccessTokenConPuedeSubir()
    {
        // Arrange: reflexión sobre typeof(EntregablesViewModel).
        // El código actual tiene AccessToken (Defecto D — JWT en el DOM) pero NO tiene
        // PuedeSubir (Defecto C — permisos UI server-side). El fix debe quitarla (mirror
        // del fix del hotfix HU-023 sobre PlanConsolidadoViewModel) y agregar PuedeSubir (bool).

        // Assert 1: AccessToken NO existe (Defecto D — JWT fuera del DOM, SEC-01/SEC-05).
        var propAccessToken = typeof(EntregablesViewModel).GetProperty("AccessToken");
        Assert.Null(propAccessToken);  // FASE ROJA: AccessToken existe actualmente

        // Assert 2: PuedeSubir existe y es bool (Defecto C — permisos UI server-side).
        var propPuedeSubir = typeof(EntregablesViewModel).GetProperty("PuedeSubir");
        Assert.NotNull(propPuedeSubir);  // FASE ROJA: PuedeSubir no existe actualmente
        Assert.Equal(typeof(bool), propPuedeSubir!.PropertyType);
    }
}
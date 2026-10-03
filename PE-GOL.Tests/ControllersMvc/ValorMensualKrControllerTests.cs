using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests TDD de HU-026 — Registro Mensual de Valores Reales de KRs, capa <b>MVC</b>
/// (Spec § "Tests requeridos" casos <b>88-101</b>, 14 métodos).
/// </summary>
/// <para><b>CONTRATO QUE @FrontendDev IMPLEMENTA</b> (§ Endpoints «Proxies MVC», ADR-016):</para>
/// <list type="bullet">
///   <item><c>PE-GOL.Aplicacion/Controllers/ValorMensualKrController.cs</c> →
///     <c>PE_GOL.Aplicacion.Controllers</c>, ctor
///     <c>(IApiClient, ILogger&lt;ValorMensualKrController&gt;)</c> (patrón <c>KeyResultController</c>),
///     con <c>[Authorize(Roles = "JefeArea")]</c> en las <b>3</b> acciones.</item>
///   <item><c>Index(Guid okrId)</c> → <c>ViewResult</c> con <c>ValorMensualKrIndexViewModel</c>
///     (<c>PE_GOL.Aplicacion.Models</c>).</item>
///   <item><c>Guardar(Guid okrId, Guid keyResultId, ValorMensualKrUpdateRequest request)</c> →
///     <c>JsonResult</c> (F3: el JS pinta la fila recalculada sin recargar).</item>
///   <item><c>EliminarValor(Guid okrId, Guid keyResultId, int mes)</c> → <c>JsonResult</c>.</item>
///   <item>Rutas internas: <c>GET /api/v1/okrs/{okrId}/valores-mensuales</c> ·
///     <c>PUT /api/v1/okrs/{okrId}/key-results/{id}/valores</c> ·
///     <c>DELETE /api/v1/okrs/{okrId}/key-results/{id}/valores/{mes}</c>.</item>
/// </list>
/// <para><b>ADR-016</b>: el navegador nunca ve una ruta <c>/api/v1</c> ni el JWT; por eso el
/// <c>path</c> se afirma en el <c>IApiClient</c> mockeado (servidor) y además se escanea el
/// <b>fuente</b> del JS y del sidebar (casos #100-101), que es la única forma de cazar un
/// <c>fetch('/api/v1/…')</c> porque ningún test C# renderiza vistas.</para>
/// </summary>
public class ValorMensualKrControllerTests
{
    private readonly Mock<IApiClient> _apiClientMock;
    private readonly Mock<ILogger<ValorMensualKrController>> _loggerMock;
    private readonly ValorMensualKrController _sut;

    private static readonly Guid OkrId = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid KrId1 = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid KrId2 = Guid.Parse("00000000-0000-0000-0000-000000000008");

    private const string RutaGrilla = "/api/v1/okrs/00000000-0000-0000-0000-000000000006/valores-mensuales";

    public ValorMensualKrControllerTests()
    {
        _apiClientMock = new Mock<IApiClient>();
        _loggerMock = new Mock<ILogger<ValorMensualKrController>>();
        _sut = new ValorMensualKrController(_apiClientMock.Object, _loggerMock.Object);
        _sut.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
    }

    // ─── Helpers de arrange ──────────────────────────────────────────────────────────────────

    private static ValorMensualKrCeldaResponse Celda(int mes, bool registrado = false, decimal? valor = null) => new()
    {
        Mes = mes,
        Nombre = new[] { "ENE", "FEB", "MAR", "ABR", "MAY", "JUN",
                         "JUL", "AGO", "SEP", "OCT", "NOV", "DIC" }[mes - 1],
        Registrado = registrado,
        Valor = valor,
        Editable = mes <= 6
    };

    private static ValorMensualKrFilaResponse Fila(Guid id, string codigo, decimal peso) => new()
    {
        KeyResultId = id,
        Codigo = codigo,
        Descripcion = $"Metrica {codigo}",
        Peso = peso,
        Meses = Enumerable.Range(1, 12).Select(m => Celda(m)).ToList(),
        PuntuacionQ1 = 0.700m,
        PuntuacionFinal = 0.700m,
        PuntuacionPonderada = 0.280m,
        Semaforo = "Rojo"
    };

    private static ValorMensualKrGrillaResponse Grilla(int mesMaxEditable = 6) => new()
    {
        OkrId = OkrId,
        OkrCodigo = "OKR.1",
        OkrDescripcion = "Incrementar ventas",
        PilarNombre = "Crecimiento",
        CicloId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        AnioFiscal = 2026,
        MesInicio = 1,
        FechaNegocio = new DateOnly(2026, 6, 15),
        MesActual = 6,
        MesMinEditable = 1,
        MesMaxEditable = mesMaxEditable,
        MesesEditables = Enumerable.Range(1, Math.Max(mesMaxEditable, 0)).ToList(),
        UmbralVerde = 0.90m,
        UmbralAmarillo = 0.70m,
        KRs = new List<ValorMensualKrFilaResponse>
        {
            Fila(KrId1, "KR.1", 0.400m),
            Fila(KrId2, "KR.2", 0.600m)
        },
        CalculoOkr = new ValorMensualKrCalculoOkrResponse
        {
            PuntuacionFinal = 0.700m, Semaforo = "Rojo", KrsConValor = 2
        }
    };

    private void SetupGrilla(ValorMensualKrGrillaResponse? grilla = null)
        => _apiClientMock
            .Setup(x => x.GetAsync<ValorMensualKrGrillaResponse>(
                RutaGrilla, It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grilla ?? Grilla());

    // ═══════════════════════════ 88-91 · Index ═══════════════════════════

    /// <summary>#88 · La vista recibe la grilla completa: 12 celdas por KR (CA #1).</summary>
    [Fact]
    public async Task Index_Get_RetornaVistaConGrilla()
    {
        SetupGrilla();

        var result = await _sut.Index(OkrId);
        var vm = (result as ViewResult)?.Model as ValorMensualKrIndexViewModel;

        Assert.NotNull(vm);
        Assert.Equal(OkrId, vm!.OkrId);
        Assert.Equal("OKR.1", vm.OkrCodigo);
        Assert.Equal("Crecimiento", vm.PilarNombre);
        Assert.Equal(2, vm.KRs.Count);
        Assert.All(vm.KRs, k => Assert.Equal(12, k.Meses.Count));
        Assert.Equal(0.90m, vm.UmbralVerde);
        Assert.False(vm.OkrNoExiste);
    }

    /// <summary>
    /// #89 · API 404 ⇒ empty-state (UX-05): la vista se devuelve con <c>OkrNoExiste = true</c>, no
    /// una excepción ni un error 500 (el OKR ajeno y el inexistente se ven igual, SEC-07).
    /// </summary>
    [Fact]
    public async Task Index_Get_OkrNoExiste_RetornaEmptyState()
    {
        _apiClientMock
            .Setup(x => x.GetAsync<ValorMensualKrGrillaResponse>(
                RutaGrilla, It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "El OKR no existe o no pertenece a su area."));

        var result = await _sut.Index(OkrId);
        var vm = (result as ViewResult)?.Model as ValorMensualKrIndexViewModel;

        Assert.NotNull(vm);
        Assert.True(vm!.OkrNoExiste);
        Assert.Empty(vm.KRs);
    }

    /// <summary>#90 · Un 500 de la API deja mensaje para el usuario y NO se pierde la vista.</summary>
    [Fact]
    public async Task Index_Get_ErrorApi_MuestraMensaje()
    {
        _apiClientMock
            .Setup(x => x.GetAsync<ValorMensualKrGrillaResponse>(
                RutaGrilla, It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(500, "Error interno del servidor."));

        var result = await _sut.Index(OkrId);

        Assert.IsType<ViewResult>(result);
        Assert.Equal("Error interno del servidor.", _sut.TempData["Error"]);
    }

    /// <summary>
    /// #91 · La ventana editable viaja al ViewModel para que el JS pinte los inputs deshabilitados
    /// de JUL..DIC. La UI solo refleja: el servidor sigue siendo la autoridad (CA #2/#3).
    /// </summary>
    [Fact]
    public async Task Index_Get_PropagaVentanaEditableAlViewModel()
    {
        SetupGrilla(Grilla(mesMaxEditable: 6));

        var result = await _sut.Index(OkrId);
        var vm = (result as ViewResult)?.Model as ValorMensualKrIndexViewModel;

        Assert.NotNull(vm);
        Assert.Equal(6, vm!.MesMaxEditable);
        Assert.Equal(1, vm.MesMinEditable);
        Assert.Equal(6, vm.MesActual);
        Assert.Equal(new DateOnly(2026, 6, 15), vm.FechaNegocio);
        Assert.Equal(Enumerable.Range(1, 6), vm.MesesEditables);
    }

    /// <summary>
    /// #102 · El agregado del OKR (puntuación final y semáforo <b>persistidos</b>, DB-04) viaja al
    /// ViewModel para que la vista lo pinte sin recalcular nada en el cliente. Sin esto el JS
    /// tendría que deducirlo de las filas y el semáforo del OKR no se vería hasta el guardado.
    /// </summary>
    [Fact]
    public async Task Index_Get_PropagaCalculoOkrPersistido()
    {
        SetupGrilla();

        var result = await _sut.Index(OkrId);
        var vm = (result as ViewResult)?.Model as ValorMensualKrIndexViewModel;

        Assert.NotNull(vm);
        Assert.NotNull(vm!.CalculoOkr);
        Assert.Equal(0.700m, vm.CalculoOkr!.PuntuacionFinal);
        Assert.Equal("Rojo", vm.CalculoOkr.Semaforo);
        Assert.Equal(2, vm.CalculoOkr.KrsConValor);
    }

    // ═══════════════════════════ 92-95 · Guardar ═══════════════════════════

    /// <summary>#92 · F3: el PUT devuelve la fila recalculada + el agregado, para pintar sin recargar.</summary>
    [Fact]
    public async Task Guardar_Post_Valido_RetornaJsonConFilaRecalculada()
    {
        var esperado = new ValorMensualKrGuardarResponse
        {
            KeyResult = Fila(KrId1, "KR.1", 0.400m),
            CalculoOkr = new ValorMensualKrCalculoOkrResponse
            {
                PuntuacionFinal = 0.850m, Semaforo = "Amarillo", KrsConValor = 2
            }
        };
        esperado.KeyResult.PuntuacionFinal = 0.850m;
        esperado.KeyResult.Semaforo = "Amarillo";
        _apiClientMock
            .Setup(x => x.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                $"/api/v1/okrs/{OkrId}/key-results/{KrId1}/valores",
                It.IsAny<ValorMensualKrUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(esperado);

        var result = await _sut.Guardar(OkrId, KrId1, new ValorMensualKrUpdateRequest
        {
            Valores = new List<ValorMensualKrItemRequest> { new() { Mes = 6, Valor = 0.7m } }
        });
        var json = result as JsonResult;

        Assert.NotNull(json);
        var model = json!.Value as ValorMensualKrGuardarResponse;
        Assert.NotNull(model);
        Assert.Equal(0.850m, model!.KeyResult.PuntuacionFinal);
        Assert.Equal("Amarillo", model.KeyResult.Semaforo);
        Assert.Equal(0.850m, model.CalculoOkr.PuntuacionFinal);
    }

    /// <summary>
    /// #93 · Un 422 de la API (mes no editable, valor fuera de escala) llega al usuario: los
    /// mensajes del servidor se vuelcan en <c>ModelState</c> (no se tragan).
    /// </summary>
    [Fact]
    public async Task Guardar_Post_Api422_MuestraErrores()
    {
        _apiClientMock
            .Setup(x => x.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                It.IsAny<string>(), It.IsAny<ValorMensualKrUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "Regla de negocio violada.",
                new[] { "El mes 7 (julio) aun no ha ocurrido y no puede registrarse." }));

        var result = await _sut.Guardar(OkrId, KrId1, new ValorMensualKrUpdateRequest
        {
            Valores = new List<ValorMensualKrItemRequest> { new() { Mes = 7, Valor = 0.5m } }
        });

        Assert.True(_sut.ModelState.ErrorCount > 0);
        var mensajes = _sut.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage ?? string.Empty);
        Assert.Contains(mensajes, m => m.Contains("julio"));
        Assert.NotNull(result);   // responde algo renderizable, no revienta
    }

    /// <summary>
    /// #103 · La grilla se guarda por <c>fetch</c> (edición en línea, F3), así que un 422 tiene que
    /// llegar <b>como JSON</b>: si el proxy devolviera una <c>ViewResult</c>, el JS recibiría HTML y
    /// la grilla se quedaría a medias. El ModelState se conserva (#93) para el render de servidor.
    /// </summary>
    [Fact]
    public async Task Guardar_Post_Api422_DevuelveJsonConLosMensajesDeLaBll()
    {
        _apiClientMock
            .Setup(x => x.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                It.IsAny<string>(), It.IsAny<ValorMensualKrUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "Regla de negocio violada.",
                new[] { "El mes 7 (julio) aun no ha ocurrido y no puede registrarse." }));

        var result = await _sut.Guardar(OkrId, KrId1, new ValorMensualKrUpdateRequest
        {
            Valores = new List<ValorMensualKrItemRequest> { new() { Mes = 7, Valor = 0.5m } }
        });

        var json = result as JsonResult;
        Assert.NotNull(json);

        var payload = System.Text.Json.JsonSerializer.Serialize(json!.Value);
        Assert.Contains("\"success\":false", payload);
        Assert.Contains("julio", payload);
    }

    /// <summary>#94 · 404 de la API (OKR/KR inexistente o ajeno) ⇒ <c>NotFoundResult</c>.</summary>
    [Fact]
    public async Task Guardar_Post_Api404_RetornaNotFound()
    {
        _apiClientMock
            .Setup(x => x.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                It.IsAny<string>(), It.IsAny<ValorMensualKrUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "El Key Result no existe o no pertenece a este OKR."));

        var result = await _sut.Guardar(OkrId, KrId1, new ValorMensualKrUpdateRequest
        {
            Valores = new List<ValorMensualKrItemRequest> { new() { Mes = 6, Valor = 0.5m } }
        });

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>
    /// #95 · ADR-016: el cuerpo va por <c>IApiClient</c> (el JWT se adjunta server-side) y el
    /// controlador <b>no</b> devuelve ni acepta token alguno. El mismo request que el navegador
    /// manda debe llegar íntegro a la API interna.
    /// </summary>
    [Fact]
    public async Task Guardar_Post_NoPropagaTokenNiRutaApi()
    {
        ValorMensualKrUpdateRequest? recibido = null;
        _apiClientMock
            .Setup(x => x.PutAsync<ValorMensualKrUpdateRequest, ValorMensualKrGuardarResponse>(
                It.IsAny<string>(), It.IsAny<ValorMensualKrUpdateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, ValorMensualKrUpdateRequest, CancellationToken>((_, body, _) => recibido = body)
            .ReturnsAsync(new ValorMensualKrGuardarResponse
            {
                KeyResult = Fila(KrId1, "KR.1", 0.400m),
                CalculoOkr = new ValorMensualKrCalculoOkrResponse()
            });

        var request = new ValorMensualKrUpdateRequest
        {
            Valores = new List<ValorMensualKrItemRequest>
            {
                new() { Mes = 5, Valor = 0.4m },
                new() { Mes = 6, Valor = 0.7m }
            }
        };
        var result = await _sut.Guardar(OkrId, KrId1, request);

        Assert.NotNull(recibido);
        Assert.Equal(2, recibido!.Valores.Count);
        Assert.Equal(6, recibido.Valores[1].Mes);
        Assert.Equal(0.7m, recibido.Valores[1].Valor);

        // El JsonResult no lleva ningún token (SEC-01/SEC-05: el JWT no viaja al DOM).
        var json = (JsonResult)result;
        var serializado = System.Text.Json.JsonSerializer.Serialize(json.Value);
        Assert.DoesNotContain("accessToken", serializado, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", serializado, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════ 96-97 · EliminarValor ═══════════════════════════

    /// <summary>#96 · El <c>mes</c> viaja en el cuerpo del form (no en la URL del MVC) y el proxy
    /// llama al DELETE con la ruta de la API interna.</summary>
    [Fact]
    public async Task EliminarValor_Post_Valido_LlamaApiYDevuelveJson()
    {
        _apiClientMock
            .Setup(x => x.DeleteAsync<ValorMensualKrGuardarResponse>(
                $"/api/v1/okrs/{OkrId}/key-results/{KrId1}/valores/6", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValorMensualKrGuardarResponse
            {
                KeyResult = Fila(KrId1, "KR.1", 0.400m),
                CalculoOkr = new ValorMensualKrCalculoOkrResponse
                {
                    PuntuacionFinal = 0.000m, Semaforo = "Rojo", KrsConValor = 1
                }
            });

        var result = await _sut.EliminarValor(OkrId, KrId1, 6);

        _apiClientMock.Verify(x => x.DeleteAsync<ValorMensualKrGuardarResponse>(
            $"/api/v1/okrs/{OkrId}/key-results/{KrId1}/valores/6", It.IsAny<CancellationToken>()), Times.Once);
        var json = result as JsonResult;
        Assert.NotNull(json);
        Assert.Equal(0.000m, ((ValorMensualKrGuardarResponse)json!.Value!).CalculoOkr.PuntuacionFinal);
    }

    /// <summary>#97 · Un 4xx/5xx al borrar deja mensaje visible (TempData o ModelState).</summary>
    [Fact]
    public async Task EliminarValor_Post_Error_MuestraMensaje()
    {
        _apiClientMock
            .Setup(x => x.DeleteAsync<ValorMensualKrGuardarResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "El ciclo activo se encuentra cerrado."));

        await _sut.EliminarValor(OkrId, KrId1, 6);

        var hayTempData = _sut.TempData["Error"] is string;
        var hayModelState = _sut.ModelState.ErrorCount > 0;
        Assert.True(hayTempData || hayModelState,
            "El error del servidor debe quedar visible para el usuario (TempData[\"Error\"] o ModelState).");
    }

    // ═══════════════════════════ 98-99 · Seguridad por reflexión (SEC-06/SEC-07) ═══════════════════════════

    /// <summary>
    /// #98 · Las <b>4</b> acciones exigen <c>JefeArea</c>: sin esto el Gerente (o un AdminTenant)
    /// alcanzaría los valores de un área ajena por el MVC aunque la API lo rechazara.
    /// </summary>
    [Fact]
    public void TodasLasAcciones_RequierenJefeArea()
    {
        var acciones = typeof(ValorMensualKrController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "Index" or "Guardar" or "EliminarValor" or "Exportar")
            .ToList();

        Assert.Equal(4, acciones.Count);
        foreach (var accion in acciones)
        {
            var authorize = accion.GetCustomAttribute<AuthorizeAttribute>();
            Assert.True(authorize is not null, $"{accion.Name} no lleva [Authorize].");
            Assert.Equal("JefeArea", authorize!.Roles);
        }
    }

    /// <summary>
    /// #99 · SEC-06: ninguna acción acepta <c>tenant_id</c>, <c>area_id</c> ni <c>ciclo_id</c> como
    /// parámetro — salen del <c>TenantContext</c> (JWT). Aceptarlos por query string sería la vía
    /// de escalada entre tenants.
    /// </summary>
    [Fact]
    public void Acciones_NoRecibenTenantNiAreaNiCiclo()
    {
        var prohibidos = new[] { "tenantid", "areaid", "cicloid" };

        var firmas = typeof(ValorMensualKrController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "Index" or "Guardar" or "EliminarValor" or "Exportar")
            .SelectMany(m => m.GetParameters().Select(p => p.Name ?? string.Empty))
            .ToList();

        Assert.NotEmpty(firmas);
        foreach (var nombre in firmas)
            Assert.DoesNotContain(nombre.Replace("_", string.Empty).ToLowerInvariant(), prohibidos);
    }

    // ═══════════════════════ 100-101 · Regresión de fuente (ADR-016 / F7) ═══════════════════════

    /// <summary>
    /// #100 · ADR-016: <c>wwwroot/js/valormensualkr.js</c> no contiene <c>/api/</c> ni
    /// <c>accessToken</c>. Mismo criterio que <c>JsApiBaseUrlRegressionTests</c>: los enlaces y el
    /// JS viven en <c>wwwroot</c> y ningún test C# renderiza vistas, así que el contrato se blinda
    /// escaneando el <b>fuente</b>.
    /// </summary>
    [Fact]
    public void Js_NoContieneRutaApiInternaNiToken()
    {
        var archivo = Path.Combine(ObtenerRutaWwwRootJs(), "valormensualkr.js");
        Assert.True(File.Exists(archivo), $"No existe {archivo} (F3/F4: el JS de la grilla inline).");

        var contenido = File.ReadAllText(archivo);
        Assert.DoesNotContain("/api/", contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", contenido, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetch(", contenido, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #101 · F7: los valores mensuales pertenecen a un OKR concreto, así que <b>no</b> hay ítem en
    /// <c>_Sidebar.cshtml</c>: el punto de entrada es el botón «Valores» por fila de
    /// <c>KeyResult/Index</c>. Un ítem global no tendría contexto de OKR ni de área.
    /// </summary>
    [Fact]
    public void Indice_NoTieneItemEnSidebar()
    {
        var sidebar = Path.Combine(ObtenerRutaViews(), "Shared", "_Sidebar.cshtml");
        Assert.True(File.Exists(sidebar), $"No existe {sidebar}.");

        var contenido = File.ReadAllText(sidebar);
        Assert.DoesNotContain("ValorMensualKr", contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("valores-mensuales", contenido, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EXP-3 · ADR-014 + precedente HU-023: el workbook lo genera la BLL, el controller solo lo
    /// envuelve en <c>FileContentResult</c>. El proxy pide bytes a
    /// <c>GET /api/v1/okrs/{id}/valores-mensuales/exportar</c> (nunca al revés) y fija el nombre de
    /// descarga con el código del OKR. Fijar el MIME es lo que hace que el navegador lo abra con
    /// Excel y no lo descargue como binario suelto.
    /// </summary>
    [Fact]
    public async Task Exportar_OkrValido_RetornaArchivoExcel()
    {
        // XLSX mínimo y real: contenedor ZIP con 'xl/workbook.xml' (magic "PK").
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                var entrada = zip.CreateEntry("xl/workbook.xml");
                using var writer = new StreamWriter(entrada.Open());
                writer.Write("<workbook/>");
            }
            bytes = ms.ToArray();
        }
        _apiClientMock
            .Setup(x => x.GetBytesAsync(
                $"/api/v1/okrs/{OkrId}/valores-mensuales/exportar", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var result = await _sut.Exportar(OkrId);

        _apiClientMock.Verify(x => x.GetBytesAsync(
            $"/api/v1/okrs/{OkrId}/valores-mensuales/exportar", null, It.IsAny<CancellationToken>()), Times.Once);
        var archivo = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, archivo.FileContents);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo.ContentType);
        Assert.EndsWith(".xlsx", archivo.FileDownloadName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EXP-4 · Un 404 de la API (OKR ajeno o inexistente) en la descarga ⇒ <c>NotFoundResult</c>:
    /// no se entrega un archivo vacío que el usuario abriría creyendo que no hay datos.
    /// </summary>
    [Fact]
    public async Task Exportar_OkrNoExiste_RetornaNotFound()
    {
        _apiClientMock
            .Setup(x => x.GetBytesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "El OKR no existe o no pertenece a su area."));

        var result = await _sut.Exportar(OkrId);

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>
    /// Localiza la raíz de la solución desde el assembly de tests
    /// (<c>{repo}/PE-GOL.Tests/bin/Debug/net8.0/PE-GOL.Tests.dll</c> → 4 niveles arriba). Mismo
    /// criterio que <c>JsApiBaseUrlRegressionTests</c>: reorganizar los proyectos exigiría un ADR
    /// (ARCH-01), así que el coste de mantenimiento es aceptable.
    /// </summary>
    private static string ObtenerRaizSolucion()
    {
        var ubicacion = typeof(ValorMensualKrControllerTests).Assembly.Location;
        var binDir = Path.GetDirectoryName(ubicacion)
            ?? throw new InvalidOperationException($"No se pudo obtener el directorio del assembly ({ubicacion}).");
        return Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", ".."));
    }

    private static string ObtenerRutaWwwRootJs()
        => Path.Combine(ObtenerRaizSolucion(), "PE-GOL.Aplicacion", "wwwroot", "js");

    private static string ObtenerRutaViews()
        => Path.Combine(ObtenerRaizSolucion(), "PE-GOL.Aplicacion", "Views");
}

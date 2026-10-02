using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using PE_GOL.Aplicacion.Controllers;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Tests.ControllersMvc;

/// <summary>
/// Tests TDD de HU-025 — <c>KeyResultController</c> MVC (Spec § UI, casos 66-71: 6 métodos de humo).
/// Activados tras implementar el controller MVC por @FrontendDev.
/// </summary>
public class KeyResultControllerTests
{
    private readonly Mock<IApiClient> _apiClientMock;
    private readonly Mock<ILogger<KeyResultController>> _loggerMock;
    private readonly KeyResultController _sut;

    private static readonly Guid OkrId = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid KrId1 = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid KrId2 = Guid.Parse("00000000-0000-0000-0000-000000000008");
    private static readonly Guid KrId3 = Guid.Parse("00000000-0000-0000-0000-000000000009");

    public KeyResultControllerTests()
    {
        _apiClientMock = new Mock<IApiClient>();
        _loggerMock = new Mock<ILogger<KeyResultController>>();
        _sut = new KeyResultController(_apiClientMock.Object, _loggerMock.Object);
        _sut.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
    }

    private static KeyResultResponse Kr(Guid id, string codigo, decimal peso, int orden) => new()
    {
        Id = id,
        OkrId = OkrId,
        TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        OkrCodigo = "OKR.1",
        Codigo = codigo,
        Descripcion = $"Metrica {codigo}",
        Peso = peso,
        PuntuacionFinal = 0.000m,
        PuntuacionPonderada = 0.000m,
        Orden = orden
    };

    private void SetupOkrPadre() =>
        _apiClientMock
            .Setup(x => x.GetAsync<OkrResponse>($"/api/v1/okrs/{OkrId}", It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OkrResponse { Id = OkrId, Codigo = "OKR.1", Descripcion = "Incrementar ventas" });

    private void SetupKrs(params KeyResultResponse[] krs) =>
        _apiClientMock
            .Setup(x => x.GetAsync<List<KeyResultResponse>>($"/api/v1/okrs/{OkrId}/key-results", It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(krs.ToList());

    // -- 66 ------------------------------------------------------------------------------

    /// <summary>#66 · Vista con el encabezado del OKR padre, los 3 KRs y la S calculada en el VM.</summary>
    [Fact]
    public async Task Index_ConOkrPadreYkrs_RetornaVistaConOkrYSuma()
    {
        SetupOkrPadre();
        SetupKrs(Kr(KrId1, "KR.1", 0.200m, 1), Kr(KrId2, "KR.2", 0.300m, 2), Kr(KrId3, "KR.3", 0.500m, 3));

        var result = await _sut.Index(OkrId);
        var vm = result as ViewResult;
        var model = vm?.Model as KeyResultIndexViewModel;

        Assert.NotNull(model);
        Assert.Equal("OKR.1", model!.OkrCodigo);
        Assert.Equal("Incrementar ventas", model.OkrDescripcion);
        Assert.Equal(3, model.Items.Count);
        Assert.Equal(1.000m, model.SumaPesos);
        Assert.Equal(3, model.Cantidad);
        Assert.Equal(5, model.MaxKeyResults);
        Assert.False(model.OkrNoExiste);
    }

    // -- 67 ------------------------------------------------------------------------------

    /// <summary>
    /// #67 · OKR padre inexistente o de otra área (404): la vista muestra el empty-state y
    /// <b>el proxy de KRs NUNCA se invoca</b> — pedir /okrs/{ajeno}/key-results sería una fuga
    /// que además dejaría KRs de otra área en la vista (SEC-07 / RN-008).
    /// </summary>
    [Fact]
    public async Task Index_OkrPadreNoExiste_OcultaAccionesYNoLlamaApiKr()
    {
        _apiClientMock
            .Setup(x => x.GetAsync<OkrResponse>($"/api/v1/okrs/{OkrId}", It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(404, "no encontrado"));

        var result = await _sut.Index(OkrId);
        var vm = result as ViewResult;
        var model = vm?.Model as KeyResultIndexViewModel;

        Assert.NotNull(model);
        Assert.True(model!.OkrNoExiste);
        Assert.Empty(model.Items);
        _apiClientMock.Verify(
            x => x.GetAsync<List<KeyResultResponse>>(It.IsAny<string>(), It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -- 68 ------------------------------------------------------------------------------

    /// <summary>
    /// #68 · F1 · Todas las acciones del controller MVC son JEF-only. El botón «KRs» de
    /// <c>Okr/Index.cshtml</c> ya es JEF-only por contexto, pero la autorización va en el controller.
    /// </summary>
    [Fact]
    public void Index_Autorizacion_SoloJefeArea()
    {
        var metodos = typeof(KeyResultController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "Index" or "Crear" or "Editar" or "Eliminar" or "ActualizarPesos")
            .ToList();

        Assert.NotEmpty(metodos);

        var rolEnAccion = new List<string?>();
        foreach (var metodo in metodos)
        {
            var attr = metodo.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            rolEnAccion.Add(attr?.Roles);
        }

        var attrClase = typeof(KeyResultController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);

        // O el atributo de clase es JefeArea, o TODAS las acciones lo son
        Assert.True(
            attrClase?.Roles == "JefeArea" || rolEnAccion.All(r => r == "JefeArea"),
            "KeyResultController MVC debe exigir el rol JefeArea (F1, SEC-07).");
    }

    // -- 69 ------------------------------------------------------------------------------

    /// <summary>
    /// #69 · Un 422 de la API (descripción inválida / peso fuera de escala) se proyecta al
    /// <c>ModelState</c> y se re-renderiza el formulario con el OKR padre re-poblado, para que el
    /// usuario no pierda el contexto del breadcrumb.
    /// </summary>
    [Fact]
    public async Task Crear_Post_ConError422_MuestraModelState()
    {
        SetupOkrPadre();
        SetupKrs(Kr(KrId1, "KR.1", 0.200m, 1), Kr(KrId2, "KR.2", 0.300m, 2), Kr(KrId3, "KR.3", 0.500m, 3));
        _apiClientMock
            .Setup(x => x.PostAsync<KeyResultCreateRequest, KeyResultResponse>($"/api/v1/okrs/{OkrId}/key-results",
                It.IsAny<KeyResultCreateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "La descripcion del Key Result es obligatoria.", new[] { "La descripcion del Key Result es obligatoria." }));

        var result = await _sut.Crear(OkrId, new KeyResultCreateRequest { Descripcion = "  ", Peso = 0.5m });
        var vm = result as ViewResult;
        var model = vm?.Model as KeyResultFormViewModel;

        Assert.NotNull(model);
        Assert.NotEmpty(_sut.ModelState);
        Assert.Equal("OKR.1", model!.OkrCodigo);
        Assert.False(model.EsEdicion);
    }

    /// <summary>
    /// #69b · El mismo camino para el proxy del endpoint masivo (ADR-016): el 422 de S de
    /// <c>ActualizarPesosAsync</c> («la suma actual es 0.999») llega al ModelState y la vista de
    /// Index se re-renderiza con el mensaje intacto — es el error que el usuario necesita ver
    /// para corregir el reparto.
    /// </summary>
    [Fact]
    public async Task ActualizarPesos_Post_ConError422DeSigma_MuestraModelState()
    {
        SetupOkrPadre();
        SetupKrs(Kr(KrId1, "KR.1", 0.333m, 1), Kr(KrId2, "KR.2", 0.333m, 2), Kr(KrId3, "KR.3", 0.333m, 3));
        _apiClientMock
            .Setup(x => x.PutAsync<KeyResultPesosUpdateRequest, object>($"/api/v1/okrs/{OkrId}/key-results/pesos",
                It.IsAny<KeyResultPesosUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "La suma de los pesos de los Key Results debe ser exactamente 1.000 (100%); la suma actual es 0.999.", new[] { "La suma de los pesos de los Key Results debe ser exactamente 1.000 (100%); la suma actual es 0.999." }));

        var result = await _sut.ActualizarPesos(OkrId, new KeyResultPesosUpdateRequest
        {
            Pesos = new List<KeyResultPesoRequest>
            {
                new() { Id = KrId1, Peso = 0.333m },
                new() { Id = KrId2, Peso = 0.333m },
                new() { Id = KrId3, Peso = 0.333m }
            }
        });
        var vm = result as ViewResult;
        var model = vm?.Model as KeyResultIndexViewModel;

        Assert.NotNull(model);
        Assert.NotEmpty(_sut.ModelState);
        Assert.Contains("0.999", string.Join(" ", _sut.ModelState.SelectMany(kv => kv.Value!.Errors).Select(e => e.ErrorMessage)),
            StringComparison.Ordinal);
    }

    // -- 70 ------------------------------------------------------------------------------

    /// <summary>
    /// #70 · SEC-06 · El <c>okr_id</c> viaja SOLO por la ruta (no en body). El DTO
    /// <c>KeyResultUpdateRequest</c> no expone <c>OkrId</c> — el test verifica que el proxy
    /// usa el <c>okrId</c> de la ruta y no permite inyección lateral.
    /// </summary>
    [Fact]
    public async Task Editar_Post_UsaOkrIdDeLaRuta_NoDelBody()
    {
        SetupOkrPadre();
        var krExistente = Kr(KrId1, "KR.1", 0.200m, 1);
        _apiClientMock
            .Setup(x => x.GetAsync<KeyResultResponse>($"/api/v1/okrs/{OkrId}/key-results/{KrId1}", It.IsAny<IDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(krExistente);
        _apiClientMock
            .Setup(x => x.PutAsync<KeyResultUpdateRequest, KeyResultResponse>($"/api/v1/okrs/{OkrId}/key-results/{KrId1}",
                It.IsAny<KeyResultUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(krExistente);

        var result = await _sut.Editar(OkrId, KrId1, new KeyResultUpdateRequest { Descripcion = "actualizada", Peso = 0.300m });

        var redirect = result as RedirectToActionResult;
        Assert.NotNull(redirect);
        Assert.Equal("Index", redirect!.ActionName);

        // Verificar que el PUT se llamó con el okrId de la RUTA, no del body (que no existe en el DTO)
        _apiClientMock.Verify(
            x => x.PutAsync<KeyResultUpdateRequest, KeyResultResponse>(
                $"/api/v1/okrs/{OkrId}/key-results/{KrId1}",
                It.Is<KeyResultUpdateRequest>(r => r.Descripcion == "actualizada" && r.Peso == 0.300m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -- 71 ------------------------------------------------------------------------------

    /// <summary>
    /// #71 · CA #4 · El 422 «tiene valores reales registrados» no es un fallo de la vista: se
    /// redirige a Index con el mensaje en TempData, para que el usuario vea el error y no pierda
    /// el listado de KRs del OKR.
    /// </summary>
    [Fact]
    public async Task Eliminar_Post_ConError422_RedirigeAIndexConFlash()
    {
        _apiClientMock
            .Setup(x => x.DeleteAsync<KeyResultResponse>($"/api/v1/okrs/{OkrId}/key-results/{KrId1}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiClientException(422, "No se puede eliminar el Key Result porque tiene valores reales registrados en el ciclo activo.", new[] { "No se puede eliminar el Key Result porque tiene valores reales registrados en el ciclo activo." }));

        var result = await _sut.Eliminar(OkrId, KrId1);
        var redirect = result as RedirectToActionResult;

        Assert.NotNull(redirect);
        Assert.Equal("Index", redirect!.ActionName);
        Assert.Equal(OkrId, redirect.RouteValues!["okrId"]);
        Assert.False(string.IsNullOrWhiteSpace(_sut.TempData["Error"]?.ToString()));
    }
}

using System.Data;
using System.Reflection;
using Moq;
using Npgsql;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de HU-025 — Gestión de Key Results (Spec § "Tests requeridos" casos <b>1-53</b>,
/// 53 métodos: 43 base + 10 del endpoint masivo de pesos).
/// Escritos contra el <b>SPEC</b>, no contra la implementación, para que cualquier desviación
/// entre el contrato aprobado (F0-F7) y el código aparezca como ROJO (TEST-01 / TEST-06).
/// </summary>
/// <para><b>CONTRATO QUE @BackendDev IMPLEMENTA</b> (firmas exactas, § Lógica BLL):</para>
/// <list type="bullet">
///   <item><c>PE-GOL.BLL/Services/KeyResultService.cs</c> → ctor de <b>4</b> argumentos:
///     <c>(IKeyResultRepository, IOkrRepository, ICicloRepository, TenantContext)</c>.
///     OJO: el borrador anterior de este archivo pasaba 5 (con <c>IPilarService</c>) — el spec
///     <b>no</b> lo pide: el pilar se resuelve dentro del OKR padre.</item>
///   <item><c>IKeyResultRepository</c> (namespace <c>PE_GOL.DAL.Interfaces</c>) — 11 miembros,
///     todos con <c>tenantId</c> como primer parámetro (SEC-06).</item>
///   <item>DTOs de request en <c>PE_GOL.DTO.Requests.Okr</c> y response en
///     <c>PE_GOL.DTO.Responses.Okr</c> (NO existen los namespaces <c>...Requests.KeyResult</c>).</item>
/// </list>
/// <para><b>DOBLE COMPUERTA SEC-07:</b> los 6 métodos invocan
/// <c>ObtenerOkrPadreOThrowAsync</c> → <c>IOkrRepository.ObtenerPorIdAsync(tenantId, areaId, okrId)</c>.
/// Por eso TODOS los Arrange de escritura pasan por <see cref="ConfigurarPadreValido"/>.</para>
/// </summary>
public class KeyResultServiceTests
{
    private readonly Mock<IKeyResultRepository> _krRepoMock;
    private readonly Mock<IOkrRepository> _okrRepoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<IDbTransaction> _txMock;
    private readonly TenantContext _tenantContext;
    private readonly KeyResultService _sut;

    private static readonly Guid TenantId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId    = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AreaId     = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid UserId     = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid OkrId      = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid OkrIdOtro  = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid KrId1      = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid KrId2      = Guid.Parse("00000000-0000-0000-0000-000000000021");
    private static readonly Guid KrId3      = Guid.Parse("00000000-0000-0000-0000-000000000022");
    private static readonly Guid KrIdAjeno  = Guid.Parse("00000000-0000-0000-0000-000000000099");

    public KeyResultServiceTests()
    {
        _krRepoMock = new Mock<IKeyResultRepository>();
        _okrRepoMock = new Mock<IOkrRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();
        _txMock = new Mock<IDbTransaction>();
        _tenantContext = new TenantContext
        {
            TenantId = TenantId,
            AreaId = AreaId,
            Rol = "JefeArea",
            UserId = UserId
        };
        _sut = new KeyResultService(
            _krRepoMock.Object,
            _okrRepoMock.Object,
            _cicloRepoMock.Object,
            _tenantContext);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Ciclo activo por defecto en estado "En Curso" (modificable, RC-12).</summary>
    private void ConfigurarCicloActivo(string estado = "En Curso") =>
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { Id = CicloId, Estado = estado });

    /// <summary>
    /// OKR padre válido: del área del JEF y del ciclo activo. Es la PRIMERA compuerta de SEC-07
    /// y se invoca en los 6 métodos, lecturas incluidas.
    /// </summary>
    private void ConfigurarPadreValido(Guid okrId = default, Guid? cicloId = null) =>
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, okrId == default ? OkrId : okrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OkrResponse
            {
                Id = okrId == default ? OkrId : okrId,
                CicloId = cicloId ?? CicloId,
                AreaId = AreaId,
                PilarId = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                PilarNombre = "Crecimiento",
                Codigo = "OKR.1",
                Descripcion = "OKR de prueba"
            });

    /// <summary>Transacción instrumentada: registra Commit / Rollback para poder afirmarlos.</summary>
    private void ConfigurarTransaccion() =>
        _krRepoMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_txMock.Object);

    private static KeyResultCreateRequest CrearKrValido(decimal peso = 0.500m) => new()
    {
        Descripcion = "Aumentar la conversion en 10 puntos",
        Peso = peso
    };

    private static KeyResultUpdateRequest ActualizarKrValido(decimal peso = 0.600m) => new()
    {
        Descripcion = "Aumentar la conversion en 15 puntos",
        Peso = peso
    };

    /// <summary>KR persistido, con las 6 puntuaciones en 0.000 (DB-04 hasta HU-026).</summary>
    private static KeyResultResponse KrPersistido(Guid id, string codigo = "KR.1", decimal peso = 0.500m, int orden = 1) => new()
    {
        Id = id,
        TenantId = TenantId,
        OkrId = OkrId,
        OkrCodigo = "OKR.1",
        Codigo = codigo,
        Descripcion = "KR persistido",
        Peso = peso,
        PuntuacionQ1 = 0.000m,
        PuntuacionQ2 = 0.000m,
        PuntuacionQ3 = 0.000m,
        PuntuacionQ4 = 0.000m,
        PuntuacionFinal = 0.000m,
        PuntuacionPonderada = 0.000m,
        Orden = orden,
        CreatedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>Configura el camino feliz completo de lectura: padre + listado.</summary>
    private void ConfigurarListado(params KeyResultResponse[] krs)
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        _krRepoMock
            .Setup(x => x.ListarAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(krs);
    }

    /// <summary>
    /// Camino feliz de escritura: padre OK, ciclo modificable, conteo bajo el tope de CA #2,
    /// secuencia conocida, INSERT que devuelve <paramref name="nuevoId"/> y re-lectura posterior.
    /// </summary>
    private void ConfigurarCrearExitoso(
        int siguienteN = 1,
        int siguienteOrden = 1,
        int conteo = 0,
        Guid nuevoId = default,
        string codigoRespuesta = "KR.1")
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        ConfigurarTransaccion();
        var id = nuevoId == default ? KrId1 : nuevoId;

        _krRepoMock
            .Setup(x => x.ContarKeyResultsAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteoKeyResultsDto { Cantidad = conteo });
        _krRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaKeyResultAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaKeyResultDto { SiguienteN = siguienteN, SiguienteOrden = siguienteOrden });
        _krRepoMock
            .Setup(x => x.CrearAsync(TenantId, OkrId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        _krRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(KrPersistido(id, codigoRespuesta));
    }

    private void ConfigurarActualizarExitoso(Guid id = default)
    {
        var krId = id == default ? KrId1 : id;
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        ConfigurarTransaccion();
        _krRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, krId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(KrPersistido(krId));
    }

    private void ConfigurarEliminarExitoso(bool tieneValoresReales, Guid id = default)
    {
        var krId = id == default ? KrId1 : id;
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        ConfigurarTransaccion();
        _krRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, krId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(KrPersistido(krId));
        _krRepoMock
            .Setup(x => x.VerificarValoresRealesAsync(TenantId, krId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tieneValoresReales);
    }

    /// <summary>
    /// Camino feliz del endpoint masivo de pesos: N KRs existentes + Σ persistida = 1.000
    /// (la verificación de integridad del paso 6.2 se ejecuta DENTRO de la transacción).
    /// </summary>
    private void ConfigurarPesosExitoso(params (Guid Id, decimal Peso)[] estadoActual)
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        ConfigurarTransaccion();

        var krs = estadoActual.Select(k => KrPersistido(k.Id, $"KR.{k.Peso * 1000:0}", k.Peso, (int)(k.Peso * 10))).ToArray();
        _krRepoMock
            .Setup(x => x.ListarAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(krs);
        _krRepoMock
            .Setup(x => x.ObtenerSumaPesosAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SumaPesosKeyResultDto { SumaPesos = estadoActual.Sum(k => k.Peso) });
    }

    private static KeyResultPesosUpdateRequest Vector(params (Guid Id, decimal Peso)[] pares) => new()
    {
        Pesos = pares.Select(p => new KeyResultPesoRequest { Id = p.Id, Peso = p.Peso }).ToList()
    };

    // ══════════════════════════ AUTORIZACIÓN Y CONTEXTO (1-8) ══════════════════════════

    /// <summary>#1 · Rol no JefeArea al crear → 403; la DAL nunca se toca.</summary>
    [Fact]
    public async Task CrearAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "Gerente";

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#2 · AdminTenant tampoco puede editar.</summary>
    [Fact]
    public async Task ActualizarAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "AdminTenant";

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            _sut.ActualizarAsync(OkrId, KrId1, ActualizarKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.ActualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<KeyResultUpdateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#3 · SuperAdmin tampoco puede eliminar.</summary>
    [Fact]
    public async Task EliminarAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "SuperAdmin";

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            _sut.EliminarAsync(OkrId, KrId1, CancellationToken.None));

        _krRepoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#4 · Sin tenant en el contexto (SuperAdmin) → 404 en lectura y DAL nunca invocada.</summary>
    [Fact]
    public async Task ListarAsync_SinTenant_LanzaNotFound()
    {
        _tenantContext.TenantId = null;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync(OkrId, CancellationToken.None));

        _krRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#5 · Sin area asignada → 404 con el mensaje del helper.</summary>
    [Fact]
    public async Task ListarAsync_SinArea_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        _tenantContext.AreaId = null;

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync(OkrId, CancellationToken.None));

        Assert.Contains("asignada", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#6 · Sin ciclo activo (RC-01) al escribir → 404.</summary>
    [Fact]
    public async Task CrearAsync_SinCicloActivo_LanzaNotFound()
    {
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);
        ConfigurarPadreValido();

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));
    }

    /// <summary>#7 · Ciclo Cerrado + escritura → 422 (RC-12).</summary>
    [Fact]
    public async Task CrearAsync_CicloCerrado_LanzaValidacionException()
    {
        ConfigurarCicloActivo("Cerrado");
        ConfigurarPadreValido();

        await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#8 · Ciclo Cerrado + LECTURA → permitida (RC-12 es solo escritura).</summary>
    [Fact]
    public async Task ListarAsync_CicloCerrado_PermiteLectura()
    {
        ConfigurarCicloActivo("Cerrado");
        ConfigurarPadreValido();
        _krRepoMock
            .Setup(x => x.ListarAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeyResultResponse> { KrPersistido(KrId1) });

        var result = await _sut.ListarAsync(OkrId, CancellationToken.None);

        Assert.Single(result);
    }

    // ══════════════════════ DOBLE COMPUERTA SEC-07 (9-13) ══════════════════════

    /// <summary>#9 · OKR de otra área → 404 y la DAL de KRs NUNCA se invoca (sin fuga, RN-008).</summary>
    [Fact]
    public async Task ListarAsync_OkrDeOtraArea_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync(OkrId, CancellationToken.None));

        Assert.Contains("no pertenece a su", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#10 · Crear sobre OKR ajeno → 404; ni INSERT ni auditoría.</summary>
    [Fact]
    public async Task CrearAsync_OkrDeOtraArea_LanzaNotFound_SinFuga()
    {
        ConfigurarCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#11 · Actualizar sobre OKR ajeno → 404.</summary>
    [Fact]
    public async Task ActualizarAsync_OkrDeOtraArea_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ActualizarAsync(OkrId, KrId1, ActualizarKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.ActualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<KeyResultUpdateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#12 · Eliminar sobre OKR ajeno → 404; el DELETE nunca se ejecuta.</summary>
    [Fact]
    public async Task EliminarAsync_OkrDeOtraArea_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.EliminarAsync(OkrId, KrId1, CancellationToken.None));

        _krRepoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#13 · OKR válido del área pero de OTRO ciclo → 404 ("no pertenece al ciclo activo").</summary>
    [Fact]
    public async Task ListarAsync_OkrDeOtroCiclo_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido(cicloId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync(OkrId, CancellationToken.None));

        Assert.Contains("ciclo activo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════ LISTAR / OBTENER (14-19) ═══════════════════════════

    /// <summary>#14 · Listado devuelve los 3 KRs con todos los campos de CA #1 mapeados.</summary>
    [Fact]
    public async Task ListarAsync_Exito_RetornaKRsConPesoYPuntuaciones()
    {
        var kr1 = KrPersistido(KrId1, "KR.1", 0.200m, 1);
        var kr2 = KrPersistido(KrId2, "KR.2", 0.300m, 2);
        var kr3 = KrPersistido(KrId3, "KR.3", 0.500m, 3);
        ConfigurarListado(kr1, kr2, kr3);

        var result = (await _sut.ListarAsync(OkrId, CancellationToken.None)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Collection(result,
            k => Assert.True(k.Id == KrId1 && k.Codigo == "KR.1" && k.Peso == 0.200m && k.OkrCodigo == "OKR.1" && k.Orden == 1),
            k => Assert.True(k.Id == KrId2 && k.Codigo == "KR.2" && k.Peso == 0.300m && k.Orden == 2),
            k => Assert.True(k.Id == KrId3 && k.Codigo == "KR.3" && k.Peso == 0.500m && k.Orden == 3));
        // Las 6 puntuaciones vienen tal cual están persistidas (0.000 hasta HU-026, DB-04)
        Assert.All(result, k =>
        {
            Assert.Equal(0.000m, k.PuntuacionQ1);
            Assert.Equal(0.000m, k.PuntuacionQ2);
            Assert.Equal(0.000m, k.PuntuacionQ3);
            Assert.Equal(0.000m, k.PuntuacionQ4);
            Assert.Equal(0.000m, k.PuntuacionFinal);
            Assert.Equal(0.000m, k.PuntuacionPonderada);
        });
    }

    /// <summary>#15 · OKR sin KRs → lista vacía (200 en API, .empty-state en UI, UX-05).</summary>
    [Fact]
    public async Task ListarAsync_SinKRs_RetornaListaVacia()
    {
        ConfigurarListado();

        var result = await _sut.ListarAsync(OkrId, CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>#16 · SEC-06/07 · La DAL recibe el tenant DEL CONTEXTO y el okrId DE LA RUTA.</summary>
    [Fact]
    public async Task ListarAsync_DalRecibeTenantYOkrId()
    {
        ConfigurarListado(KrPersistido(KrId1));

        await _sut.ListarAsync(OkrId, CancellationToken.None);

        _krRepoMock.Verify(x => x.ListarAsync(TenantId, OkrId, It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.ListarAsync(AreaId, OkrId, It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), OkrIdOtro, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#17 · Detalle de un KR con el OkrCodigo del JOIN (breadcrumb de la UI).</summary>
    [Fact]
    public async Task ObtenerPorIdAsync_Exito_RetornaDetalleConOkrCodigo()
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        var kr = KrPersistido(KrId1, "KR.2", 0.750m, 2);
        _krRepoMock.Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, KrId1, It.IsAny<CancellationToken>())).ReturnsAsync(kr);

        var result = await _sut.ObtenerPorIdAsync(OkrId, KrId1, CancellationToken.None);

        Assert.Equal(KrId1, result.Id);
        Assert.Equal("KR.2", result.Codigo);
        Assert.Equal("OKR.1", result.OkrCodigo);
        Assert.Equal(0.750m, result.Peso);
    }

    /// <summary>#18 · KR inexistente o de otro OKR → 404 (el filtro lleva okr_id, no hay fuga).</summary>
    [Fact]
    public async Task ObtenerPorIdAsync_KrDeOtroOkr_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        _krRepoMock.Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyResultResponse?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.ObtenerPorIdAsync(OkrId, KrId1, CancellationToken.None));

        Assert.Contains("no pertenece a este OKR", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #19 · F5 resuelto — <b>KeyResultResponse NO expone Semaforo</b>: <c>key_result</c> no tiene la
    /// columna en el DDL (06 L250-267). Blinda la decisión: nadie añade la propiedad sin el ADR +
    /// migración <c>V006__semaforo_key_result.sql</c> de HU-026.
    /// </summary>
    [Fact]
    public void ObtenerPorIdAsync_ResponseNoExponeSemaforo()
    {
        var props = typeof(KeyResultResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("Semaforo", props);

        // Y sí expone todo lo que CA #1 exige (el test no es solo "falta algo")
        Assert.Contains("Codigo", props);
        Assert.Contains("Descripcion", props);
        Assert.Contains("Peso", props);
        Assert.Contains("OkrCodigo", props);
    }

    // ═════════════════════════════ CREAR (20-34) — 15 ═════════════════════════════

    /// <summary>#20 · CA #1 · OKR sin KRs → codigo "KR.1", orden 1.</summary>
    [Fact]
    public async Task CrearAsync_Exito_GeneraCodigoKR1_CuandoOkrNoTieneKRs()
    {
        ConfigurarCrearExitoso(siguienteN: 1, siguienteOrden: 1, nuevoId: KrId1, codigoRespuesta: "KR.1");

        var creado = await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);

        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.1", 1, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("KR.1", creado.Codigo);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>#21 · F4 · MAX(N)+1: con KR.1 y KR.2 vivos el siguiente es KR.3.</summary>
    [Fact]
    public async Task CrearAsync_Exito_GeneraCodigoKR3_CuandoMaxNExistenteEs2()
    {
        ConfigurarCrearExitoso(siguienteN: 3, siguienteOrden: 3, nuevoId: KrId3, codigoRespuesta: "KR.3");

        await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);

        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.3", 3, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// #22 · F4 · Justifica MAX vs COUNT: tras borrar el KR intermedio (viven KR.1 y KR.3) el
    /// siguiente es <b>KR.4</b>. Con COUNT+1 colisionaría en KR.3 y el UNIQUE devolvería 23505.
    /// </summary>
    [Fact]
    public async Task CrearAsync_Exito_NoReutilizaCodigoTrasEliminarIntermedio()
    {
        ConfigurarCrearExitoso(siguienteN: 4, siguienteOrden: 4, nuevoId: KrId3, codigoRespuesta: "KR.4");

        await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);

        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.4", 4, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.3", It.IsAny<int>(), It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #23 · F4 · La secuencia es <b>por OKR</b>, no por área: dos OKR distintos del mismo área
    /// arrancan ambos en KR.1 (coherente con UNIQUE (okr_id, codigo)).
    /// </summary>
    [Fact]
    public async Task CrearAsync_Exito_CodigoEsSecuencialPorOkr_NoPorArea()
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        ConfigurarPadreValido(OkrIdOtro);
        ConfigurarTransaccion();
        _krRepoMock
            .Setup(x => x.ContarKeyResultsAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteoKeyResultsDto { Cantidad = 0 });
        _krRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaKeyResultAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaKeyResultDto { SiguienteN = 1, SiguienteOrden = 1 });
        _krRepoMock
            .Setup(x => x.CrearAsync(TenantId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(KrId1);
        _krRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(KrPersistido(KrId1, "KR.1"));

        await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);
        await _sut.CrearAsync(OkrIdOtro, CrearKrValido(), CancellationToken.None);

        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.1", 1, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrIdOtro, "KR.1", 1, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// #24 · DB-04 · Las 6 columnas de puntuación nacen en 0.000. A nivel BLL se afirma sobre el
    /// DTO re-leído; los literales <c>0.000</c> del INSERT se blindan en el test DAL (caso #59).
    /// </summary>
    [Fact]
    public async Task CrearAsync_Exito_PersisteLasSeisPuntuacionesEnCero()
    {
        ConfigurarCrearExitoso(nuevoId: KrId1);

        var creado = await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);

        Assert.Equal(0.000m, creado.PuntuacionQ1);
        Assert.Equal(0.000m, creado.PuntuacionQ2);
        Assert.Equal(0.000m, creado.PuntuacionQ3);
        Assert.Equal(0.000m, creado.PuntuacionQ4);
        Assert.Equal(0.000m, creado.PuntuacionFinal);
        Assert.Equal(0.000m, creado.PuntuacionPonderada);
    }

    /// <summary>#25 · Descripción solo con espacios → 422 (trim antes de validar).</summary>
    [Fact]
    public async Task CrearAsync_DescripcionVacia_LanzaValidacionException()
    {
        ConfigurarCrearExitoso();
        var req = CrearKrValido();
        req.Descripcion = "   ";

        await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(OkrId, req, CancellationToken.None));

        _krRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#26 · Descripción de 501 caracteres → 422 (límite de negocio 500).</summary>
    [Fact]
    public async Task CrearAsync_DescripcionMayorA500_LanzaValidacionException()
    {
        ConfigurarCrearExitoso();
        var req = CrearKrValido();
        req.Descripcion = new string('x', 501);

        await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(OkrId, req, CancellationToken.None));
    }

    /// <summary>#27 · Peso 0.000 → 422 (rango abierto por la izquierda, DDL CHECK peso > 0).</summary>
    [Fact]
    public async Task CrearAsync_PesoCero_LanzaValidacionException()
    {
        ConfigurarCrearExitoso();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.CrearAsync(OkrId, CrearKrValido(0.000m), CancellationToken.None));

        Assert.Contains("mayor que 0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>#28 · Peso 1.001 → 422 (tope inclusivo 1.000).</summary>
    [Fact]
    public async Task CrearAsync_PesoMayorAUno_LanzaValidacionException()
    {
        ConfigurarCrearExitoso();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.CrearAsync(OkrId, CrearKrValido(1.001m), CancellationToken.None));

        Assert.Contains("mayor que 1.000", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>#29 · Peso 0.1234 → 422 (escala DECIMAL(4,3)).</summary>
    [Fact]
    public async Task CrearAsync_PesoConMasDe3Decimales_LanzaValidacionException()
    {
        ConfigurarCrearExitoso();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.CrearAsync(OkrId, CrearKrValido(0.1234m), CancellationToken.None));

        Assert.Contains("3 decimales", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>#30 · CA #2 / F2 · Conteo = 5 → 422 "Máximo 5 Key Results por OKR".</summary>
    [Fact]
    public async Task CrearAsync_Maximo5KRsPorOkr_LanzaValidacionException()
    {
        ConfigurarCrearExitoso(conteo: 5);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));

        Assert.Contains("Key Results por OKR", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #31 · <b>F0 (Opción B)</b> · El POST singular NO valida Σ. Un OKR ya repartido 0.500/0.500
    /// acepta un tercer KR de 0.300 (Σ = 1.300) sin error: el invariante solo se aplica en el
    /// endpoint masivo. Si alguien reintroduce la regla dura aquí, <b>este test falla</b>.
    /// </summary>
    [Fact]
    public async Task CrearAsync_SinValidarSumaPesos_ExitoF0()
    {
        ConfigurarListado(KrPersistido(KrId1, "KR.1", 0.500m, 1), KrPersistido(KrId2, "KR.2", 0.500m, 2));
        ConfigurarCrearExitoso(siguienteN: 3, siguienteOrden: 3, conteo: 2, nuevoId: KrId3, codigoRespuesta: "KR.3");

        var creado = await _sut.CrearAsync(OkrId, CrearKrValido(0.300m), CancellationToken.None);

        Assert.Equal("KR.3", creado.Codigo);
        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.3", 3, It.IsAny<KeyResultCreateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>#32 · Un peso de 3 decimales válido SÍ se acepta: la validación de peso aplica en el singular.</summary>
    [Fact]
    public async Task CrearAsync_PesoValido_UsaValidarPesoNoLaSuma()
    {
        ConfigurarCrearExitoso(nuevoId: KrId1);

        await _sut.CrearAsync(OkrId, CrearKrValido(0.333m), CancellationToken.None);

        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, It.IsAny<string>(), It.IsAny<int>(),
            It.Is<KeyResultCreateRequest>(r => r.Peso == 0.333m), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// #33 · <b>F0 · El peso es LIBRE en la creación</b>: primer KR con 0.400 sobre un OKR vacío
    /// (Σ = 0.400) → 201, codigo KR.1. Con la regla dura en el POST —que fue lo que se descartó—
    /// este Arrange solo podría pasar con Peso = 1.000, y CA #2 (1-5 KRs) sería inalcanzable.
    /// </summary>
    [Fact]
    public async Task CrearAsync_SinOkrs_CodigoKR1YPesoLibre()
    {
        ConfigurarCrearExitoso(siguienteN: 1, siguienteOrden: 1, conteo: 0, nuevoId: KrId1);

        var creado = await _sut.CrearAsync(OkrId, CrearKrValido(0.400m), CancellationToken.None);

        Assert.Equal("KR.1", creado.Codigo);
        _krRepoMock.Verify(x => x.CrearAsync(TenantId, OkrId, "KR.1", 1, It.Is<KeyResultCreateRequest>(r => r.Peso == 0.400m),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// #34 · ADR-003 · (a) el CREATE audita en la MISMA transacción con ValorAnterior = null y
    /// ValorNuevo = JSON del creado; (b) la carrera TOCTOU del UNIQUE (okr_id, codigo) llega como
    /// PostgresException 23505 → 422 + rollback (capa 2 de F4).
    /// </summary>
    [Fact]
    public async Task CrearAsync_Exito_AuditaCreateYChoque23505()
    {
        // ── Fase A: auditoría del CREATE ──────────────────────────────────────────────
        ConfigurarCrearExitoso(nuevoId: KrId1, codigoRespuesta: "KR.1");

        await _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None);

        _krRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l =>
                l.Accion == "CREATE" &&
                l.Entidad == "KeyResult" &&
                l.EntidadId == KrId1.ToString() &&
                l.UsuarioId == UserId &&
                l.ValorAnterior == null &&
                l.ValorNuevo != null &&
                l.ValorNuevo.Contains("KR.1", StringComparison.Ordinal)),
            _txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
        _txMock.Verify(t => t.Rollback(), Times.Never);

        // ── Fase B: choque 23505 ─────────────────────────────────────────────────────
        _krRepoMock.Reset();
        _okrRepoMock.Reset();
        _cicloRepoMock.Reset();
        _txMock.Reset();

        _tenantContext.Rol = "JefeArea";
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        _krRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_txMock.Object);
        _krRepoMock.Setup(x => x.ContarKeyResultsAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteoKeyResultsDto { Cantidad = 1 });
        _krRepoMock.Setup(x => x.ObtenerSiguienteSecuenciaKeyResultAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaKeyResultDto { SiguienteN = 2, SiguienteOrden = 2 });
        _krRepoMock.Setup(x => x.CrearAsync(TenantId, OkrId, It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<KeyResultCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint \"key_result_okr_id_codigo_key\"",
                "ERROR", "ERROR", "23505"));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(OkrId, CrearKrValido(), CancellationToken.None));

        Assert.Contains("conflicto", ex.Message, StringComparison.OrdinalIgnoreCase);
        _txMock.Verify(t => t.Rollback(), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════ ACTUALIZAR (35-40) — 6 ═══════════════════════════

    /// <summary>#35 · Update correcto: DAL con (tenant, okr, id, request) + auditoría UPDATE con ambos snapshots.</summary>
    [Fact]
    public async Task ActualizarAsync_Exito_ActualizaDescripcionYPeso_AuditaUpdate()
    {
        ConfigurarActualizarExitoso();
        var req = ActualizarKrValido(0.750m);

        var actualizado = await _sut.ActualizarAsync(OkrId, KrId1, req, CancellationToken.None);

        _krRepoMock.Verify(x => x.ActualizarAsync(TenantId, OkrId, KrId1, req,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l =>
                l.Accion == "UPDATE" &&
                l.Entidad == "KeyResult" &&
                l.EntidadId == KrId1.ToString() &&
                l.ValorAnterior != null &&
                l.ValorNuevo != null),
            _txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
        Assert.Equal(KrId1, actualizado.Id);
    }

    /// <summary>#36 · F0 · Reenviar el MISMO peso se acepta (el singular no re-valida Σ).</summary>
    [Fact]
    public async Task ActualizarAsync_PesoSinCambio_QuedaValidoAcepta()
    {
        ConfigurarActualizarExitoso();

        var req = ActualizarKrValido(0.500m); // idéntico al persistido
        var actualizado = await _sut.ActualizarAsync(OkrId, KrId1, req, CancellationToken.None);

        Assert.Equal(KrId1, actualizado.Id);
        _krRepoMock.Verify(x => x.ActualizarAsync(TenantId, OkrId, KrId1, It.IsAny<KeyResultUpdateRequest>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Rollback(), Times.Never);
    }

    /// <summary>
    /// #37 · <b>F0</b> · Cambiar el peso de 0.400 a 0.500 sobre un OKR 0.400/0.600 (Σ = 1.000 →
    /// 1.100) se PERSISTE sin error. Blinda la decisión firme: la regla dura vive solo en
    /// ActualizarPesosAsync. Si alguien la reintroduce en el PUT singular, <b>este test falla</b>.
    /// </summary>
    [Fact]
    public async Task ActualizarAsync_CambiarPeso_NoValidaLaSuma_F0()
    {
        ConfigurarActualizarExitoso();
        ConfigurarListado(KrPersistido(KrId1, "KR.1", 0.400m, 1), KrPersistido(KrId2, "KR.2", 0.600m, 2));

        var req = ActualizarKrValido(0.500m);

        await _sut.ActualizarAsync(OkrId, KrId1, req, CancellationToken.None);

        _krRepoMock.Verify(x => x.ActualizarAsync(TenantId, OkrId, KrId1, It.Is<KeyResultUpdateRequest>(r => r.Peso == 0.500m),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>#38 · Pesos fuera de rango o de escala → 422 (teoría: los 3 casos del spec).</summary>
    [Theory]
    [InlineData(0.0000)]
    [InlineData(1.5000)]
    [InlineData(0.1234)]
    public async Task ActualizarAsync_PesoInvalido_LanzaValidacionException(double pesoInvalido)
    {
        ConfigurarActualizarExitoso();
        var req = ActualizarKrValido((decimal)pesoInvalido);

        await Assert.ThrowsAsync<ValidacionException>(() => _sut.ActualizarAsync(OkrId, KrId1, req, CancellationToken.None));

        _krRepoMock.Verify(x => x.ActualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<KeyResultUpdateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#39 · KR inexistente o ajeno al OKR de la ruta → 404.</summary>
    [Fact]
    public async Task ActualizarAsync_KrNoExisteODeOtroOkr_LanzaNotFound()
    {
        ConfigurarCicloActivo();
        ConfigurarPadreValido();
        _krRepoMock.Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyResultResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ActualizarAsync(OkrId, KrId1, ActualizarKrValido(), CancellationToken.None));

        _krRepoMock.Verify(x => x.ActualizarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<KeyResultUpdateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#40 · RC-12 · Ciclo Cerrado + UPDATE → 422.</summary>
    [Fact]
    public async Task ActualizarAsync_CicloCerrado_LanzaValidacionException()
    {
        ConfigurarCicloActivo("Cerrado");
        ConfigurarPadreValido();

        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarAsync(OkrId, KrId1, ActualizarKrValido(), CancellationToken.None));
    }

    // ═════════════════════════════ ELIMINAR (41-43) — 3 ═════════════════════════════

    /// <summary>#41 · Sin valores reales → DELETE físico + auditoría DELETE (ValorAnterior = JSON, ValorNuevo = null).</summary>
    [Fact]
    public async Task EliminarAsync_Exito_RealizaDeleteFisico_AuditaDelete()
    {
        ConfigurarEliminarExitoso(tieneValoresReales: false);

        await _sut.EliminarAsync(OkrId, KrId1, CancellationToken.None);

        _krRepoMock.Verify(x => x.EliminarAsync(TenantId, OkrId, KrId1,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l =>
                l.Accion == "DELETE" &&
                l.Entidad == "KeyResult" &&
                l.EntidadId == KrId1.ToString() &&
                l.ValorAnterior != null &&
                l.ValorNuevo == null),
            _txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>#42 · CA #4 · Con valores reales en valor_mensual_kr → 422 y el DELETE NUNCA se ejecuta.</summary>
    [Fact]
    public async Task EliminarAsync_ConValoresReales_LanzaValidacionException()
    {
        ConfigurarEliminarExitoso(tieneValoresReales: true);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _sut.EliminarAsync(OkrId, KrId1, CancellationToken.None));

        Assert.Contains("valores reales", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #43 · <b>F3 resuelto</b> · El DELETE bloquea SOLO por valores reales: con 2 KRs en 0.500/0.500
    /// borrar uno deja Σ = 0.500 y aun así se borra. Si el DELETE re-validase Σ, ningún KR sería
    /// eliminable jamás. Blinda F3: reintroducir la comprobación <b>rompe este test</b>.
    /// </summary>
    [Fact]
    public async Task EliminarAsync_NoValidaLaSumaDePesos_F3Resuelto()
    {
        ConfigurarListado(KrPersistido(KrId1, "KR.1", 0.500m, 1), KrPersistido(KrId2, "KR.2", 0.500m, 2));
        ConfigurarEliminarExitoso(tieneValoresReales: false);

        await _sut.EliminarAsync(OkrId, KrId1, CancellationToken.None);

        _krRepoMock.Verify(x => x.EliminarAsync(TenantId, OkrId, KrId1,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    // ═════════ ACTUALIZAR PESOS MASIVAMENTE (44-53) — 10 · CA #3 / F0 / F6 ═════════

    /// <summary>
    /// #44 · Camino feliz del endpoint masivo: UN único BeginTransaction, UN único batch UPDATE con
    /// los 3 pares id/peso, verificación de integridad (Σ persistida = 1.000) y Commit. El método
    /// devuelve <c>Task</c> sin valor (un Task&lt;T&gt; devolvería un estado no aplicado).
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_Exito_AplicaVectorCompletoEnUnaTransaccion()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.300m), (KrId2, 0.300m), (KrId3, 0.400m)); // Σ = 1.000

        await _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None);

        _krRepoMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(TenantId, OkrId,
            It.Is<List<KeyResultPesoRequest>>(l => l.Count == 3 && l[0].Id == KrId1 && l[0].Peso == 0.300m
                                                    && l[1].Id == KrId2 && l[1].Peso == 0.300m
                                                    && l[2].Id == KrId3 && l[2].Peso == 0.400m),
            _txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        // Verificación de integridad ANTES de confirmar (paso 6.2): Σ persistida releída
        _krRepoMock.Verify(x => x.ObtenerSumaPesosAsync(TenantId, OkrId, It.IsAny<CancellationToken>()), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Once);
        _txMock.Verify(t => t.Rollback(), Times.Never);
    }

    /// <summary>#45 · CA #3 · Σ = 1.100 → 422 con la suma en el mensaje; la DAL de escritura no se toca.</summary>
    [Fact]
    public async Task ActualizarPesosAsync_SumaDistintaDeUno_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.300m), (KrId2, 0.300m), (KrId3, 0.500m)); // Σ = 1.100

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Contains("1.100", ex.Message, StringComparison.Ordinal);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _krRepoMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #46 · <b>F6 · 0.333 × 3 = 0.999 ≠ 1.000</b> → 422. Blinda la igualdad EXACTA con
    /// Math.Round(..., 3, AwayFromZero) frente a una tolerancia con epsilon (±0.001), que dejaría
    /// pasar este reparto y contaminaría la puntuacion_ponderada de HU-026.
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_Suma999EnMil_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.333m), (KrId2, 0.333m), (KrId3, 0.334m));
        var request = Vector((KrId1, 0.333m), (KrId2, 0.333m), (KrId3, 0.333m)); // Σ = 0.999

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Contains("0.999", ex.Message, StringComparison.Ordinal);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#47 · Σ = 0.600 (por debajo) → 422.</summary>
    [Fact]
    public async Task ActualizarPesosAsync_SumaInferiorAUno_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.200m), (KrId3, 0.200m));
        var request = Vector((KrId1, 0.200m), (KrId2, 0.200m), (KrId3, 0.200m)); // Σ = 0.600

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Contains("0.600", ex.Message, StringComparison.Ordinal);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#48 · Vector incompleto (2 items para 3 KRs) → 422 mencionando el conteo esperado.</summary>
    [Fact]
    public async Task ActualizarPesosAsync_VectorIncompleto_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.500m), (KrId2, 0.500m)); // Σ = 1.000 pero faltan KRs

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Contains("exactamente los 3 Key Results", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #49 · Un Id ajeno al OKR → 422 <b>sin revelar si ese id existe</b> (SEC-07/RN-008): el mensaje
    /// habla del conjunto esperado, nunca del id rechazado.
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_ContieneIdAjeno_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.300m), (KrId2, 0.300m), (KrIdAjeno, 0.400m)); // Σ = 1.000

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Contains("exactamente los 3 Key Results", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KrIdAjeno.ToString(), ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#50 · El mismo Id dos veces → 422 (tambien lo cubre el Distinct() del validator).</summary>
    [Fact]
    public async Task ActualizarPesosAsync_ContieneIdRepetido_LanzaValidacionException()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.300m), (KrId1, 0.300m), (KrId3, 0.400m)); // KrId1 repetido

        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#51 · OKR sin KRs → 422 explícito (no un error genérico de cobertura).</summary>
    [Fact]
    public async Task ActualizarPesosAsync_OkrSinKRs_LanzaValidacionException()
    {
        ConfigurarListado();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarPesosAsync(OkrId, Vector((KrId1, 1.000m)), CancellationToken.None));

        Assert.Contains("asignar pesos", ex.Message, StringComparison.OrdinalIgnoreCase);
        _krRepoMock.Verify(x => x.ActualizarPesosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #52 · Atomicidad (F0) · Si el batch falla, NINGÚN peso queda aplicado: rollback, sin Commit,
    /// sin auditoría y la excepción se propaga intacta.
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_ErrorEnElBatch_HaceRollbackYNoAudita()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var fallo = new PostgresException("connection reset", "ERROR", "ERROR", "58030");
        _krRepoMock.Setup(x => x.ActualizarPesosAsync(TenantId, OkrId, It.IsAny<List<KeyResultPesoRequest>>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>())).ThrowsAsync(fallo);
        var request = Vector((KrId1, 0.300m), (KrId2, 0.300m), (KrId3, 0.400m));

        var propagada = await Assert.ThrowsAsync<PostgresException>(() =>
            _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None));

        Assert.Same(fallo, propagada);
        _txMock.Verify(t => t.Rollback(), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Never);
        _krRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #53 · ADR-003 · La reasignación masiva deja <b>UNA sola</b> entrada de auditoría con el
    /// reparto COMPLETO del OKR (granularidad = la operación, no la fila): EntidadId = okrId,
    /// ValorAnterior = foto previa, ValorNuevo = vector aplicado, en la MISMA transacción.
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_Exito_AuditaElRepartoCompletoEnUnaSolaEntrada()
    {
        ConfigurarPesosExitoso((KrId1, 0.200m), (KrId2, 0.300m), (KrId3, 0.500m));
        var request = Vector((KrId1, 0.300m), (KrId2, 0.300m), (KrId3, 0.400m));

        await _sut.ActualizarPesosAsync(OkrId, request, CancellationToken.None);

        _krRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l =>
                l.Accion == "UPDATE" &&
                l.Entidad == "KeyResult" &&
                l.EntidadId == OkrId.ToString() &&
                l.ValorAnterior != null &&
                l.ValorAnterior.Contains("KR.", StringComparison.Ordinal) &&
                l.ValorNuevo != null &&
                l.ValorNuevo.Contains("0.4", StringComparison.Ordinal)),
            _txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════ CONTRATO / SEC-06 ═══════════════════════════

    /// <summary>
    /// Contrato de DTO por reflexión (SEC-06): ningún request de HU-025 acepta TenantId, AreaId,
    /// CicloId ni OkrId — el okr_id viaja SOLO en la ruta del endpoint y los otros tres se
    /// resuelven en la BLL desde el TenantContext + ObtenerCicloActivoAsync.
    /// </summary>
    [Fact]
    public void Contrato_DtosKeyResult_SinOkrIdNiTenantIdEnRequest()
    {
        var prohibidos = new[] { "TenantId", "AreaId", "CicloId", "OkrId" };
        var tipos = new[]
        {
            typeof(KeyResultCreateRequest),
            typeof(KeyResultUpdateRequest),
            typeof(KeyResultPesosUpdateRequest),
            typeof(KeyResultPesoRequest)
        };

        foreach (var tipo in tipos)
        {
            var props = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).ToList();

            Assert.All(prohibidos, p => Assert.False(props.Contains(p, StringComparer.Ordinal),
                $"{tipo.Name} no puede exponer {p}: SEC-06 lo resuelve en BLL desde el TenantContext."));
        }

        // Y los payloads SÍ llevan exactamente lo que CA #1 exige
        Assert.Contains("Descripcion", typeof(KeyResultCreateRequest).GetProperties().Select(p => p.Name));
        Assert.Contains("Peso", typeof(KeyResultCreateRequest).GetProperties().Select(p => p.Name));
        Assert.Contains("Pesos", typeof(KeyResultPesosUpdateRequest).GetProperties().Select(p => p.Name));
    }
}
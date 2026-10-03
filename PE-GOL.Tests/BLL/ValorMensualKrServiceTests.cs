using System.Data;
using ClosedXML.Excel;
using Moq;
using Npgsql;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests.BLL;

/// <summary>
/// Tests TDD de HU-026 — Registro Mensual de Valores Reales de KRs, capa <b>BLL</b>
/// (Spec § "Tests requeridos" casos <b>1-70 y 102-105</b>: 74 métodos).
/// Escritos contra el <b>SPEC</b>, no contra la implementación, para que cualquier desviación
/// entre el contrato aprobado (F0-F8) y el código aparezca como ROJO (TEST-01 / TEST-06).
/// </summary>
/// <para><b>CONTRATO QUE @BackendDev IMPLEMENTA</b> (§ Lógica BLL del spec, firmas exactas):</para>
/// <list type="bullet">
///   <item><c>PE-GOL.BLL/Services/ValorMensualKrService.cs</c> → <c>PE_GOL.BLL.Services</c>,
///     ctor de <b>7</b> argumentos en este orden:
///     <c>(IValorMensualKrRepository, IOkrRepository, IKeyResultRepository, ICicloRepository,
///     ITenantRepository, TimeProvider, TenantContext)</c>.</item>
///   <item>3 métodos de servicio:
///     <c>ObtenerGrillaAsync(Guid okrId, ct)</c> ·
///     <c>GuardarValoresAsync(Guid okrId, Guid keyResultId, ValorMensualKrUpdateRequest, ct)</c> ·
///     <c>EliminarValorAsync(Guid okrId, Guid keyResultId, int mes, ct)</c>.
///     Las dos últimas devuelven <c>ValorMensualKrGuardarResponse</c>.</item>
///   <item><c>IValorMensualKrRepository</c> (namespace <c>PE_GOL.DAL.Interfaces</c>), 8 miembros:
///     <c>ListarPorOkrAsync</c> · <c>ListarPorKrAsync</c> · <c>ObtenerValorAsync</c> ·
///     <c>UpsertAsync</c> · <c>ActualizarPuntuacionesKrAsync</c> · <c>EliminarAsync</c> ·
///     <c>InsertLogAsync</c> · <c>BeginTransactionAsync</c>. Todas con <c>tenantId</c> primero (SEC-06).</item>
///   <item><b>Extensión aditiva</b> de <c>IOkrRepository</c> (no existe todavía):
///     <c>ActualizarPuntuacionOkrAsync(Guid tenantId, Guid areaId, Guid okrId,
///     decimal puntuacionFinal, string semaforo, IDbTransaction? tx, CancellationToken ct)</c>
///     — el § Queries DAL lo llama <c>ActualizarPuntuacionOkrAsync</c> (DAL-5) y es el nombre que
///     fijan los casos #56 y #102; el pseudocódigo BLL lo abrevía. Prevalece el nombre del DAL.</item>
///   <item>DTOs: requests en <c>PE_GOL.DTO.Requests.Okr</c>, responses en
///     <c>PE_GOL.DTO.Responses.Okr</c>, proyección DAL <c>ValorMensualKrDto</c> en
///     <c>PE_GOL.DTO.Dtos</c> (<c>Id</c>, <c>KeyResultId</c>, <c>Mes</c>, <c>Valor</c>,
///     <c>RegistradoPor</c>, <c>UpdatedAt</c>).</item>
/// </list>
/// <para><b>POR QUÉ LOS MOCKS TIENEN ESTADO</b> y no devuelven listas fijas: el cálculo es
/// <b>DB-04</b>, o sea la BLL lee <c>ListarPorKrAsync</c> <i>después</i> de los UPSERTs para
/// promediar. Con un <c>ReturnsAsync(fija)</c> el servicio vería el estado previo y los casos
/// 32-37, 55, 63 y 64 pasarían por el motivo equivocado. Aquí <c>UpsertAsync</c>/
/// <c>EliminarAsync</c> mutan un diccionario en memoria igual que lo haría PostgreSQL, y
/// <c>ActualizarPuntuacionesKrAsync</c> muta la fila del KR para que la relectura que hace
/// <c>RecalcularOkrAsync</c> vea la ponderada recién escrita.</para>
/// <para><b>F0c</b>: todos los tests fijan el instante con <see cref="Mock{T}"/> de
/// <see cref="TimeProvider"/> y el <c>tenant.zona_horaria</c> mockeado. Sin esto el «mes actual»
/// sería no determinista y los casos 2-5 y 103-104 no fijarían nada.</para>
/// </summary>
public class ValorMensualKrServiceTests
{
    // ─── IDs deterministas (SEC-06: el tenant NUNCA viaja en el request) ──────────────────────
    private static readonly Guid TenantId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId    = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AreaId     = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid UserId     = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid OkrId      = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid KrId1      = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid KrId2      = Guid.Parse("00000000-0000-0000-0000-000000000021");

    /// <summary>Instante por defecto: 15/06/2026 12:00 UTC → 07:00 en America/Lima → mes de negocio 6.</summary>
    private static readonly DateTimeOffset AhoraPorDefecto =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] NombreMeses =
    {
        "ENE", "FEB", "MAR", "ABR", "MAY", "JUN",
        "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"
    };

    private readonly Mock<IValorMensualKrRepository> _repoMock;
    private readonly Mock<IOkrRepository> _okrRepoMock;
    private readonly Mock<IKeyResultRepository> _krRepoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<ITenantRepository> _tenantRepoMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IDbTransaction> _txMock;
    private readonly TenantContext _tenantContext;

    /// <summary>Estado "BD" de <c>valor_mensual_kr</c>: (keyResultId, mes) → valor.</summary>
    private readonly Dictionary<(Guid KrId, int Mes), decimal> _valoresDb = new();

    /// <summary>Estado "BD" de las 6 columnas de puntuación de los KRs (DB-04).</summary>
    private readonly List<KeyResultResponse> _krsDb = new();

    /// <summary>Lo que el servicio escribe en <c>okr.puntuacion_final</c> / <c>okr.semaforo</c> (RN-026 / RN-027).</summary>
    private decimal _okrPuntuacionFinal;
    private string _okrSemaforo = "Rojo";

    /// <summary>Las 6 puntuaciones del KR tocado, tal como quedaron en el UPDATE.</summary>
    private readonly List<(decimal Q1, decimal Q2, decimal Q3, decimal Q4, decimal Final, decimal Ponderada)>
        _puntuacionesKrEscritas = new();

    /// <summary>Las entradas de <c>log_auditoria</c> que el servicio grabó.</summary>
    private readonly List<LogAuditoriaInsert> _auditorias = new();

    /// <summary><c>registrado_por</c> que recibió cada UPSERT (caso #16, SEC-06).</summary>
    private readonly Dictionary<(Guid KrId, int Mes), Guid> _registradoPor = new();

    public ValorMensualKrServiceTests()
    {
        _repoMock        = new Mock<IValorMensualKrRepository>();
        _okrRepoMock     = new Mock<IOkrRepository>();
        _krRepoMock      = new Mock<IKeyResultRepository>();
        _cicloRepoMock   = new Mock<ICicloRepository>();
        _tenantRepoMock  = new Mock<ITenantRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _txMock          = new Mock<IDbTransaction>();

        _tenantContext = new TenantContext
        {
            TenantId = TenantId,
            AreaId   = AreaId,
            Rol      = "JefeArea",
            UserId   = UserId
        };

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(AhoraPorDefecto);
    }

    // ─── Helpers de construcción del SUT ──────────────────────────────────────────────────────

    /// <summary>SUT con el escenario por defecto: ciclo 2026 activo, OKR y KR del área (SEC-07).</summary>
    private ValorMensualKrService CrearSut(
        string rol = "JefeArea",
        DateTimeOffset? ahora = null,
        string zonaHoraria = "America/Lima")
    {
        _tenantContext.Rol = rol;
        if (ahora.HasValue)
            _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(ahora.Value);

        ConfigurarCicloActivo();
        ConfigurarOkrDelArea();
        ConfigurarZonaHoraria(zonaHoraria);
        ConfigurarUmbralesKpi(0.90m, 0.70m);
        ConfigurarTransaccion();

        return new ValorMensualKrService(
            _repoMock.Object,
            _okrRepoMock.Object,
            _krRepoMock.Object,
            _cicloRepoMock.Object,
            _tenantRepoMock.Object,
            _timeProviderMock.Object,
            _tenantContext);
    }

    // ─── Helpers de mocks ────────────────────────────────────────────────────────────────────

    /// <summary>Ciclo activo. <c>Estado</c>: "Activo" | "Cerrado" (RC-12).</summary>
    private void ConfigurarCicloActivo(int anioFiscal = 2026, int mesInicio = 1, string estado = "Activo")
        => _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            {
                Id          = CicloId,
                TenantId    = TenantId,
                Nombre      = "Ciclo 2026",
                AñoFiscal     = anioFiscal,
                MesInicio   = mesInicio,
                Estado      = estado
            });

    /// <summary>No hay ciclo activo: <c>ObtenerCicloActivoAsync</c> devuelve null (404).</summary>
    private void ConfigurarSinCicloActivo()
        => _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

    /// <summary>
    /// Primera compuerta de SEC-07: el OKR existe y es del área del JEF y del ciclo activo.
    /// Para el caso «OKR de otra área» <b>no</b> se llama a este helper: el mock queda sin
    /// configurar → <c>null</c> → <c>NotFoundException</c>, que es lo que exige el spec
    /// («no se distingue entre inexistente y ajeno: no se filtra información»).
    /// </summary>
    private void ConfigurarOkrDelArea(decimal puntuacionFinal = 0.000m, string semaforo = "Rojo")
        => _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OkrResponse
            {
                Id             = OkrId,
                CicloId        = CicloId,
                AreaId         = AreaId,
                PilarId        = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                PilarNombre    = "Crecimiento",
                Codigo         = "OKR.1",
                Descripcion    = "Incrementar ventas",
                PuntuacionFinal = puntuacionFinal,
                Semaforo       = semaforo
            });

    /// <summary>
    /// KRs del OKR con estado real: <c>ListarAsync</c> proyecta <c>_krsDb</c> (que
    /// <see cref="ConfigurarActualizacionPuntuacionesKr"/> muta) y <c>ObtenerPorIdAsync</c>
    /// devuelve la fila o null si el KR no es de este OKR (sin fuga de existencia).
    /// </summary>
    private void ConfigurarKrs(params KeyResultResponse[] krs)
    {
        _krsDb.Clear();
        _krsDb.AddRange(krs);

        _krRepoMock
            .Setup(x => x.ListarAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _krsDb.ToList());

        foreach (var kr in krs)
        {
            var krId = kr.Id;
            _krRepoMock
                .Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, krId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _krsDb.FirstOrDefault(k => k.Id == krId));
        }
    }

    /// <summary>KR con las 6 puntuaciones en 0.000 (estado inicial, DB-04).</summary>
    private static KeyResultResponse Kr(Guid id, string codigo, decimal peso, int orden = 1) => new()
    {
        Id       = id,
        OkrId    = OkrId,
        TenantId = TenantId,
        OkrCodigo = "OKR.1",
        Codigo   = codigo,
        Descripcion = $"Metrica {codigo}",
        Peso     = peso,
        PuntuacionQ1 = 0.000m,
        PuntuacionQ2 = 0.000m,
        PuntuacionQ3 = 0.000m,
        PuntuacionQ4 = 0.000m,
        PuntuacionFinal     = 0.000m,
        PuntuacionPonderada = 0.000m,
        Orden = orden
    };

    /// <summary>KR ya calculado (puntuación final y ponderada persistidas por un guardado previo).</summary>
    private static KeyResultResponse KrConPuntuacion(Guid id, string codigo, decimal peso, decimal puntuacionFinal)
    {
        var kr = Kr(id, codigo, peso);
        kr.PuntuacionFinal = puntuacionFinal;
        kr.PuntuacionPonderada = decimal.Round(puntuacionFinal * peso, 3, MidpointRounding.AwayFromZero);
        return kr;
    }

    /// <summary>F0c: zona horaria del tenant (F0c). Sin ella el fallback es UTC−5.</summary>
    private void ConfigurarZonaHoraria(string? zona)
        => _tenantRepoMock
            .Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity { Id = TenantId, Nombre = "Dicegsa", ZonaHoraria = zona });

    /// <summary>Umbrales del ciclo, tipo <c>KPI</c> (RN-027).</summary>
    private void ConfigurarUmbralesKpi(decimal verde, decimal amarillo)
        => _cicloRepoMock
            .Setup(x => x.ObtenerUmbralesAsync(TenantId, CicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UmbralSemaforoEntity>
            {
                new() { Id = Guid.NewGuid(), CicloId = CicloId, TenantId = TenantId, Tipo = "KPI",
                        UmbralVerde = verde, UmbralAmarillo = amarillo }
            });

    /// <summary>Sin filas en <c>umbral_semaforo</c> → defaults 0.90 / 0.70 (HU-008 D5).</summary>
    private void ConfigurarSinUmbrales()
        => _cicloRepoMock
            .Setup(x => x.ObtenerUmbralesAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UmbralSemaforoEntity>());

    /// <summary>Transacción instrumentada para poder afirmar Commit y Rollback.</summary>
    private void ConfigurarTransaccion()
        => _repoMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_txMock.Object);

    /// <summary>
    /// Almacén con estado de <c>valor_mensual_kr</c>: los <c>Listar*</c> proyectan
    /// <see cref="_valoresDb"/> y los <c>UpsertAsync</c>/<c>EliminarAsync</c> la mutan, igual que
    /// haría PostgreSQL. Sin esto, el recálculo (que relee DESPUÉS de escribir, DB-04)
    /// promediaría un estado viejo y los casos 32-37, 55, 63 y 64 pasarían por el motivo
    /// equivocado.
    /// </summary>
    private void ConfigurarAlmacenValores()
    {
        _repoMock
            .Setup(x => x.ListarPorOkrAsync(TenantId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _valoresDb
                .Where(kv => _krsDb.Any(k => k.Id == kv.Key.KrId))
                .OrderBy(kv => kv.Key.KrId).ThenBy(kv => kv.Key.Mes)
                .Select(kv => Dto(kv.Key.KrId, kv.Key.Mes, kv.Value))
                .ToList());

        _repoMock
            .Setup(x => x.ListarPorKrAsync(TenantId, OkrId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid krId, CancellationToken __) => _valoresDb
                .Where(kv => kv.Key.KrId == krId)
                .OrderBy(kv => kv.Key.Mes)
                .Select(kv => Dto(kv.Key.KrId, kv.Key.Mes, kv.Value))
                .ToList());

        _repoMock
            .Setup(x => x.ObtenerValorAsync(TenantId, OkrId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid krId, int mes, CancellationToken __) =>
                _valoresDb.TryGetValue((krId, mes), out var v) ? Dto(krId, mes, v) : null);

        _repoMock
            .Setup(x => x.UpsertAsync(TenantId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
                                      It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid krId, int mes, decimal valor, Guid usuarioId, IDbTransaction? __, CancellationToken ___) =>
            {
                _valoresDb[(krId, mes)] = valor;
                _registradoPor[(krId, mes)] = usuarioId;
                return Task.FromResult<ValorMensualKrDto?>(Dto(krId, mes, valor, usuarioId));
            });

        _repoMock
            .Setup(x => x.EliminarAsync(TenantId, OkrId, It.IsAny<Guid>(), It.IsAny<int>(),
                                        It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid _, Guid krId, int mes, IDbTransaction? __, CancellationToken ___) =>
            {
                _valoresDb.Remove((krId, mes));
                return Task.FromResult(1);
            });

        _repoMock
            .Setup(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns((LogAuditoriaInsert dto, IDbTransaction? _, CancellationToken __) =>
            {
                _auditorias.Add(dto);
                return Task.FromResult(1);
            });
    }

    /// <summary>
    /// UPDATE de las 6 columnas de <c>key_result</c> (DB-04): muta la fila del KR para que la
    /// relectura de <c>RecalcularOkrAsync</c> vea la ponderada recién escrita, y registra el
    /// vector para poder afirmarlo.
    /// </summary>
    private void ConfigurarActualizacionPuntuacionesKr()
        => _repoMock
            .Setup(x => x.ActualizarPuntuacionesKrAsync(TenantId, OkrId, It.IsAny<Guid>(),
                    It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                    It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid _, Guid krId, decimal q1, decimal q2, decimal q3, decimal q4,
                      decimal final, decimal ponderada, IDbTransaction? __, CancellationToken ___) =>
            {
                var kr = _krsDb.FirstOrDefault(k => k.Id == krId);
                if (kr is not null)
                {
                    kr.PuntuacionQ1 = q1;
                    kr.PuntuacionQ2 = q2;
                    kr.PuntuacionQ3 = q3;
                    kr.PuntuacionQ4 = q4;
                    kr.PuntuacionFinal = final;
                    kr.PuntuacionPonderada = ponderada;
                }
                _puntuacionesKrEscritas.Add((q1, q2, q3, q4, final, ponderada));
                return Task.FromResult(1);
            });

    /// <summary>UPDATE de <c>okr.puntuacion_final</c> y <c>okr.semaforo</c> (RN-026 / RN-027).</summary>
    private void ConfigurarActualizacionPuntuacionOkr()
        => _okrRepoMock
            .Setup(x => x.ActualizarPuntuacionOkrAsync(TenantId, AreaId, OkrId, It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid _, Guid _, decimal puntuacionFinal, string semaforo,
                      IDbTransaction? __, CancellationToken ___) =>
            {
                _okrPuntuacionFinal = puntuacionFinal;
                _okrSemaforo = semaforo;
                return Task.FromResult(1);
            });

    /// <summary>Escenario completo de escritura: KR único + almacén con estado + captura de UPDATE.</summary>
    private ValorMensualKrService CrearSutEscenarioCompleto(
        Guid? krId = null,
        decimal peso = 0.500m,
        int anioFiscal = 2026,
        int mesInicio = 1,
        string estadoCiclo = "Activo",
        DateTimeOffset? ahora = null,
        string zonaHoraria = "America/Lima")
    {
        var sut = CrearSut(ahora: ahora, zonaHoraria: zonaHoraria);
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            {
                Id = CicloId, TenantId = TenantId, Nombre = "Ciclo",
                AñoFiscal = anioFiscal, MesInicio = mesInicio, Estado = estadoCiclo
            });
        ConfigurarKrs(Kr(krId ?? KrId1, "KR.1", peso));
        ConfigurarAlmacenValores();
        ConfigurarActualizacionPuntuacionesKr();
        ConfigurarActualizacionPuntuacionOkr();
        return sut;
    }

    private static ValorMensualKrDto Dto(Guid krId, int mes, decimal valor, Guid? registradoPor = null) => new()
    {
        Id            = Guid.NewGuid(),
        KeyResultId   = krId,
        Mes           = mes,
        Valor         = valor,
        RegistradoPor = registradoPor,
        UpdatedAt     = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
    };

    /// <summary>Sembrado directo del estado previo (equivalente a filas ya en la BD).</summary>
    private void SembrarValor(Guid krId, int mes, decimal valor) => _valoresDb[(krId, mes)] = valor;

    private static ValorMensualKrUpdateRequest Request(params (int Mes, decimal Valor)[] items)
        => new()
        {
            Valores = items
                .Select(i => new ValorMensualKrItemRequest { Mes = i.Mes, Valor = i.Valor })
                .ToList()
        };

    private static ValorMensualKrCeldaResponse Celda(ValorMensualKrFilaResponse fila, int mes)
        => fila.Meses.Single(c => c.Mes == mes);

    // =========================================================================================
    // BLOQUE A · ObtenerGrillaAsync — casos 1-12
    // =========================================================================================

    /// <summary>#1 · CA #1: 2 KRs × <b>exactamente 12</b> celdas, meses 1..12 en orden.</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_OkrDelArea_RetornaDoceCeldasPorKr()
    {
        var sut = CrearSut();
        ConfigurarKrs(Kr(KrId1, "KR.1", 0.400m, 1), Kr(KrId2, "KR.2", 0.600m, 2));
        ConfigurarAlmacenValores();
        ConfigurarActualizacionPuntuacionesKr();
        ConfigurarActualizacionPuntuacionOkr();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal(OkrId, grilla.OkrId);
        Assert.Equal("OKR.1", grilla.OkrCodigo);
        Assert.Equal(2, grilla.KRs.Count);
        Assert.All(grilla.KRs, f => Assert.Equal(12, f.Meses.Count));
        Assert.All(grilla.KRs, f => Assert.Equal(Enumerable.Range(1, 12), f.Meses.Select(c => c.Mes)));
        Assert.All(grilla.KRs, f => Assert.Equal(NombreMeses, f.Meses.Select(c => c.Nombre)));
    }

    /// <summary>
    /// #2 · CA #1 + F0: con <c>MesInicio = 3</c> los meses 1 y 2 existen en la grilla pero no son
    /// editables y dicen por qué («Mes anterior al inicio del ciclo»).
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_MesInicioEnMarzo_EneroYFebreroNoEditables()
    {
        var sut = CrearSut();
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            { Id = CicloId, TenantId = TenantId, AñoFiscal = 2026, MesInicio = 3, Estado = "Activo" });
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var fila = grilla.KRs.Single();

        Assert.Equal(3, grilla.MesMinEditable);
        Assert.False(Celda(fila, 1).Editable);
        Assert.Equal("Mes anterior al inicio del ciclo", Celda(fila, 1).MotivoBloqueo);
        Assert.False(Celda(fila, 2).Editable);
        Assert.Equal("Mes anterior al inicio del ciclo", Celda(fila, 2).MotivoBloqueo);
        Assert.True(Celda(fila, 3).Editable);
        Assert.Null(Celda(fila, 3).MotivoBloqueo);
    }

    /// <summary>#3 · CA #2: con fecha de negocio 15/06/2026, JUN es editable y JUL todavía no.</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_MesActualMarcadoEditable()
    {
        var sut = CrearSut();
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var fila = grilla.KRs.Single();

        Assert.Equal(6, grilla.MesActual);
        Assert.Equal(6, grilla.MesMaxEditable);
        Assert.True(Celda(fila, 6).Editable);
        Assert.False(Celda(fila, 7).Editable);
        Assert.Equal("Mes aún no ocurrido", Celda(fila, 7).MotivoBloqueo);
        Assert.Equal(Enumerable.Range(1, 6), grilla.MesesEditables);
    }

    /// <summary>
    /// #4 · F0: un ciclo de un año anterior tiene los 12 meses editables — RC-12 (ciclo cerrado)
    /// es la única barrera para escribir, no la frontera de F0.
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_CicloDelAnterior_TodosLosMesesEditables()
    {
        var sut = CrearSut();
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            { Id = CicloId, TenantId = TenantId, AñoFiscal = 2025, MesInicio = 1, Estado = "Cerrado" });
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var fila = grilla.KRs.Single();

        Assert.Equal(12, grilla.MesMaxEditable);
        Assert.Equal(12, grilla.MesesEditables.Count);
        Assert.All(fila.Meses, c => Assert.True(c.Editable));
        Assert.All(fila.Meses, c => Assert.Null(c.MotivoBloqueo));
    }

    /// <summary>
    /// #5 · F0: ciclo del futuro ⇒ <c>MesMaxEditable = 0</c> y ventana vacía. <b>No</b> lanza
    /// excepción: es una lectura (CA #2 solo restringe escrituras).
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_CicloFuturo_SinMesesEditables()
    {
        var sut = CrearSut();
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            { Id = CicloId, TenantId = TenantId, AñoFiscal = 2027, MesInicio = 1, Estado = "Borrador" });
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var fila = grilla.KRs.Single();

        Assert.Equal(0, grilla.MesMaxEditable);
        Assert.Empty(grilla.MesesEditables);
        Assert.Equal(12, fila.Meses.Count);
        Assert.All(fila.Meses, c => Assert.False(c.Editable));
    }

    /// <summary>#6 · La fila existente se refleja como <c>Registrado = true</c> con su valor.</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_ValorRegistrado_MarcaRegistradoYValor()
    {
        var sut = CrearSut();
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();
        SembrarValor(KrId1, 1, 0.6m);

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var enero = Celda(grilla.KRs.Single(), 1);

        Assert.True(enero.Registrado);
        Assert.Equal(0.6m, enero.Valor);
    }

    /// <summary>#7 · KR sin filas: las 12 celdas <c>Registrado = false</c> y <c>Valor = null</c>.</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_SinValores_RegistradoFalsoYValorNull()
    {
        var sut = CrearSut();
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);
        var fila = grilla.KRs.Single();

        Assert.All(fila.Meses, c => Assert.False(c.Registrado));
        Assert.All(fila.Meses, c => Assert.Null(c.Valor));
        Assert.Equal(0, fila.MesesConValor);
    }

    /// <summary>#8 · RN-027: los umbrales del tipo <c>KPI</c> del ciclo llegan a la respuesta.</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_UsaUmbralesKpiDelCiclo()
    {
        var sut = CrearSut();
        ConfigurarUmbralesKpi(0.95m, 0.80m);
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal(0.95m, grilla.UmbralVerde);
        Assert.Equal(0.80m, grilla.UmbralAmarillo);
    }

    /// <summary>#9 · Sin fila en <c>umbral_semaforo</c> → defaults 0.90 / 0.70 (HU-008 D5).</summary>
    [Fact]
    public async Task ObtenerGrillaAsync_SinFilaUmbral_UsaDefaults()
    {
        var sut = CrearSut();
        ConfigurarSinUmbrales();
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal(0.90m, grilla.UmbralVerde);
        Assert.Equal(0.70m, grilla.UmbralAmarillo);
    }

    /// <summary>
    /// #10 · RC-12: el bloqueo por ciclo <c>Cerrado</c> es <b>solo de escritura</b>; la lectura
    /// devuelve la grilla completa.
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_CicloCerrado_PermiteLectura()
    {
        var sut = CrearSut();
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            { Id = CicloId, TenantId = TenantId, AñoFiscal = 2026, MesInicio = 1, Estado = "Cerrado" });
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();
        SembrarValor(KrId1, 6, 0.8m);

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.NotNull(grilla);
        Assert.Single(grilla.KRs);
        Assert.Equal(0.8m, Celda(grilla.KRs.Single(), 6).Valor);
    }

    /// <summary>
    /// #11 · SEC-07 (doble compuerta): OKR de otra área → <c>NotFoundException</c>, el mismo
    /// error que si no existiera (no se filtra información).
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_OkrDeOtraArea_LanzaExcepcion()
    {
        var sut = CrearSut();
        // El DAL filtra por area_id (SEC-07): un OKR de otra área llega como null desde
        // ObtenerPorIdAsync → NotFoundException, el mismo error que si no existiera.
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.ObtenerGrillaAsync(OkrId, CancellationToken.None));
    }

    /// <summary>
    /// #12 · F8: el semáforo del KR se <b>calcula</b> en la BLL a partir de la
    /// <c>puntuacion_final</c> persistida (0.850 con umbrales 0.90/0.70 ⇒ «Amarillo») y la lectura
    /// <b>no</b> invoca ningún UPDATE (DB-04).
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_CalculaSemaforoDeFilaSinPersistir()
    {
        var sut = CrearSut();
        ConfigurarKrs(KrConPuntuacion(KrId1, "KR.1", 0.400m, 0.850m));
        ConfigurarAlmacenValores();
        ConfigurarActualizacionPuntuacionesKr();
        ConfigurarActualizacionPuntuacionOkr();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal("Amarillo", grilla.KRs.Single().Semaforo);
        _repoMock.Verify(x => x.ActualizarPuntuacionesKrAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _okrRepoMock.Verify(x => x.ActualizarPuntuacionOkrAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(x => x.InsertLogAsync(
            It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================
    // BLOQUE B · GuardarValoresAsync — casos 13-55 (43 métodos)
    // =========================================================================================

    /// <summary>
    /// #13 · CA #4: un valor válido ⇒ 1 UPSERT (RC-07), 1 UPDATE de las 6 columnas del KR (DB-04)
    /// y la respuesta trae el trimestre recalculado (JUN → Q2 = 0.700).
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_ValorValido_PersisteYRecalcula()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None);

        _repoMock.Verify(x => x.UpsertAsync(TenantId, KrId1, 6, 0.7m, UserId,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.ActualizarPuntuacionesKrAsync(TenantId, OkrId, KrId1,
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0.700m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.700m, resp.KeyResult.PuntuacionFinal);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>
    /// #14 · F6: 12 meses en <b>un</b> PUT ⇒ 12 UPSERTs dentro de <b>una</b> transacción
    /// (BeginTransactionAsync una sola vez).
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_DoceMesesEnUnGuardado_PersisteTodos()
    {
        // Ciclo del año anterior ⇒ los 12 meses son editables (F0, caso #4).
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);
        var vector = Enumerable.Range(1, 12).Select(m => (Mes: m, Valor: 0.5m)).ToArray();

        await sut.GuardarValoresAsync(OkrId, KrId1, Request(vector), CancellationToken.None);

        _repoMock.Verify(x => x.UpsertAsync(TenantId, KrId1, It.IsAny<int>(), It.IsAny<decimal>(), UserId,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Exactly(12));
        _repoMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(12, _valoresDb.Count);
        Assert.Equal(12, _valoresDb.Keys.Select(k => k.Mes).Distinct().Count());
        _txMock.Verify(t => t.Commit(), Times.Once);
        _txMock.Verify(t => t.Rollback(), Times.Never);
    }

    /// <summary>
    /// #15 · RC-07: un mes ya existente se <b>upsertea</b> (nunca un INSERT suelto) y sigue siendo
    /// una sola fila.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_MesYaRegistrado_HaceUpsertNoDuplica()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 6, 0.4m);

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.5m)), CancellationToken.None);

        _repoMock.Verify(x => x.UpsertAsync(TenantId, KrId1, 6, 0.5m, UserId,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_valoresDb);
        Assert.Equal(0.5m, _valoresDb[(KrId1, 6)]);
        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ2);
    }

    /// <summary>#16 · <c>registrado_por</c> = el usuario del JWT (SEC-06: nunca del body).</summary>
    [Fact]
    public async Task GuardarValoresAsync_RegistraUsuarioActual()
    {
        var sut = CrearSutEscenarioCompleto();

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None);

        Assert.Equal(UserId, _registradoPor[(KrId1, 6)]);
    }

    /// <summary>
    /// #17 · CA #3 (capa de servidor): el mes siguiente al actual no se puede registrar aunque se
    /// invoque la API directamente — 422 mencionando el mes.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_MesFuturo_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((7, 0.5m)), CancellationToken.None));

        Assert.Contains("7", ex.Message);
        Assert.Empty(_valoresDb);
        _repoMock.Verify(x => x.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
            It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#18 · CA #1 + F0: mes anterior a <c>MesInicio = 3</c> → 422.</summary>
    [Fact]
    public async Task GuardarValoresAsync_MesPreCiclo_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto(mesInicio: 3);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.5m)), CancellationToken.None));

        Assert.Contains("inicio del ciclo", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>
    /// #19 · F0: ventana vacía (<c>MesInicio = 12</c> con hoy en marzo) ⇒ 422 con el mensaje
    /// específico de «el ciclo aún no se ha iniciado», no el genérico de mes no editable.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_VentanaVacia_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto(
            mesInicio: 12,
            ahora: new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.5m)), CancellationToken.None));

        Assert.Contains("no se ha iniciado", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>#20 · RC-12: con el ciclo <c>Cerrado</c> ninguna escritura pasa (422).</summary>
    [Fact]
    public async Task GuardarValoresAsync_CicloCerrado_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto(estadoCiclo: "Cerrado");

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));

        Assert.Contains("cerrado", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_valoresDb);
        _repoMock.Verify(x => x.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
            It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#21 · Sin ciclo activo ⇒ 404 (no 422: no hay contra qué comparar).</summary>
    [Fact]
    public async Task GuardarValoresAsync_SinCicloActivo_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        ConfigurarSinCicloActivo();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>#22 · Solo <c>JefeArea</c> escribe: el Gerente recibe 403.</summary>
    [Fact]
    public async Task GuardarValoresAsync_RolGerente_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _tenantContext.Rol = "Gerente";

        var ex = await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));

        Assert.Contains("Gerente", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>#23 · SEC-07: OKR de otra área ⇒ 404 (mismo error que inexistente).</summary>
    [Fact]
    public async Task GuardarValoresAsync_OkrDeOtraArea_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>#24 · KR de otro OKR ⇒ 404 (segunda compuerta de SEC-07).</summary>
    [Fact]
    public async Task GuardarValoresAsync_KrDeOtroOkr_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        var krAjeno = Guid.Parse("00000000-0000-0000-0000-000000000099");
        _krRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, OkrId, krAjeno, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyResultResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GuardarValoresAsync(OkrId, krAjeno, Request((6, 0.7m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>#25 · F6: el mismo mes dos veces en el mismo PUT ⇒ 422 (equivalente al UNIQUE).</summary>
    [Fact]
    public async Task GuardarValoresAsync_MesesRepetidos_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((3, 0.5m), (3, 0.7m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>#26 · Vector vacío ⇒ 422 «al menos un valor mensual».</summary>
    [Fact]
    public async Task GuardarValoresAsync_ListaVacia_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request(), CancellationToken.None));

        Assert.Contains("al menos un valor mensual", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>#27 · Más de 12 ítems ⇒ 422 «como máximo 12 meses».</summary>
    [Fact]
    public async Task GuardarValoresAsync_MasDeDoce_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);
        var vector = Enumerable.Range(1, 12).Select(m => (Mes: m, Valor: 0.5m)).Append((1, 0.4m)).ToArray();
        Assert.Equal(13, vector.Length);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request(vector), CancellationToken.None));

        Assert.Contains("12", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>#28 · RN-024: 1.1 fuera de la escala 0.0-1.0 ⇒ 422.</summary>
    [Fact]
    public async Task GuardarValoresAsync_ValorMayorQueUno_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 1.1m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>#29 · RN-024: valor negativo ⇒ 422.</summary>
    [Fact]
    public async Task GuardarValoresAsync_ValorNegativo_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, -0.1m)), CancellationToken.None));
        Assert.Empty(_valoresDb);
    }

    /// <summary>
    /// #30 · RN-024: la columna es <c>DECIMAL(2,1)</c>, así que 0.55 se rechaza con el mensaje
    /// del requisito «como máximo 1 decimal» (no el genérico de rango).
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_ValorConDosDecimales_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.55m)), CancellationToken.None));

        Assert.Contains("1 decimal", ex.Message);
        Assert.Empty(_valoresDb);
    }

    /// <summary>#31 · RN-024: 0.0 y 1.0 son <b>bordes inclusivos</b> del CHECK y se aceptan.</summary>
    [Fact]
    public async Task GuardarValoresAsync_ValorCeroYUno_Aceptados()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((5, 0.0m), (6, 1.0m)), CancellationToken.None);

        Assert.Equal(0.0m, _valoresDb[(KrId1, 5)]);
        Assert.Equal(1.0m, _valoresDb[(KrId1, 6)]);
        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ2);
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    // -- B.2 · Cálculo trimestral, final, ponderada y semáforo (casos 32-44) ----------------------

    /// <summary>
    /// #32 · RN-025 con los 3 meses del trimestre: promedio simple (0.5+0.7+0.9)/3 = 0.700.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_TrimestreCompleto_CalculaPromedioSimple()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.5m), (2, 0.7m), (3, 0.9m)), CancellationToken.None);

        Assert.Equal(0.700m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ4);
    }

    /// <summary>
    /// #33 · RN-025 <b>divisor = meses con valor</b>, no 3: FEB vacío ⇒ (0.5+1.0)/2 = 0.750.
    /// Si se dividiera por 3 saldría 0.500 y el caso fallaría — es el test que fija RN-025.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_TrimestreParcial_PromediaSoloMesesConValor()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.5m), (3, 1.0m)), CancellationToken.None);

        Assert.Equal(0.750m, resp.KeyResult.PuntuacionQ1);
    }

    /// <summary>#34 · Un trimestre sin datos vale 0.000 (no promedia con los demás).</summary>
    [Fact]
    public async Task GuardarValoresAsync_TrimestreSinValores_QEnCero()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.5m)), CancellationToken.None);

        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ4);
    }

    /// <summary>#35 · Los 4 trimestres con datos ⇒ final = su promedio (0.7+0.8+0.9+1.0)/4 = 0.850.</summary>
    [Fact]
    public async Task GuardarValoresAsync_CuatroTrimestresCompleto_FinalEsPromedio()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);
        var vector = new[]
        {
            (Mes: 1, Valor: 0.7m), (Mes: 4, Valor: 0.8m), (Mes: 7, Valor: 0.9m), (Mes: 10, Valor: 1.0m)
        };

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request(vector), CancellationToken.None);

        Assert.Equal(0.700m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.800m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.900m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(1.000m, resp.KeyResult.PuntuacionQ4);
        Assert.Equal(0.850m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>
    /// #36 · <b>F1 Opción B — este es EL test que fija la decisión.</b> Con solo Q1 = 0.500 la
    /// final es 0.500, no 0.125: los tres trimestres sin datos no cuentan (RN-025 aplicado al año).
    /// Si Jorge elige la Opción A (los vacíos valen 0), este test es el que hay que cambiar.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_SoloUnTrimestreConDatos_FinalNoPenaliza()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.5m)), CancellationToken.None);

        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.500m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>#37 · Dos trimestres con datos (Q1 = 0.4, Q3 = 0.8) ⇒ final = 0.600.</summary>
    [Fact]
    public async Task GuardarValoresAsync_DosTrimestres_FinalPromediaSoloEsos()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.4m), (7, 0.8m)), CancellationToken.None);

        Assert.Equal(0.400m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.800m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(0.600m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>
    /// #38 · Las 6 columnas de puntuación quedan en 0.000.
    /// <para><b>DESVIACIÓN DOCUMENTADA respecto al spec</b> (se escala a @Orquestador/@Arquitecto):
    /// el caso está especificado como «ningún mes del KR», estado <b>inalcanzable</b> a través de
    /// <c>GuardarValoresAsync</c> — F6 exige entre 1 y 12 ítems (caso #26) y un UPSERT siempre
    /// deja al menos un mes. La forma alcanzable equivalente es un KR cuyos meses tienen valor
    /// 0.0; el KR realmente vacío se cubre en #7 (GET) y en #64 (DELETE del último valor).
    /// Se mantiene el nombre del spec para que la traza caso↔test no se pierda.</para>
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_SinValores_PuntuacionesEnCero()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.0m), (2, 0.0m), (3, 0.0m)), CancellationToken.None);

        var vector = _puntuacionesKrEscritas.Single();
        Assert.Equal(0.000m, vector.Q1);
        Assert.Equal(0.000m, vector.Q2);
        Assert.Equal(0.000m, vector.Q3);
        Assert.Equal(0.000m, vector.Q4);
        Assert.Equal(0.000m, vector.Final);
        Assert.Equal(0.000m, vector.Ponderada);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>#39 · RF-037: ponderada = final × peso = 0.850 × 0.400 = 0.340.</summary>
    [Fact]
    public async Task GuardarValoresAsync_PonderadaEsFinalTimesPeso()
    {
        var sut = CrearSutEscenarioCompleto(peso: 0.400m);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);

        Assert.Equal(0.850m, resp.KeyResult.PuntuacionFinal);
        Assert.Equal(0.340m, resp.KeyResult.PuntuacionPonderada);
    }

    /// <summary>
    /// #40 · RN-026: la final del OKR es la <b>suma de las ponderadas persistidas</b> de sus KRs
    /// (0.340 + 0.510), no un promedio ni un recálculo desde cero.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_OkrFinalEsSumaDePonderadas()
    {
        var sut = CrearSut();
        ConfigurarKrs(Kr(KrId1, "KR.1", 0.400m, 1), Kr(KrId2, "KR.2", 0.600m, 2));
        ConfigurarAlmacenValores();
        ConfigurarActualizacionPuntuacionesKr();
        ConfigurarActualizacionPuntuacionOkr();

        // Ambos KRs quedan con final 0.850 ⇒ 0.340 y 0.510 ponderadas.
        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);
        await sut.GuardarValoresAsync(OkrId, KrId2, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);

        Assert.Equal(0.850m, _okrPuntuacionFinal);
        Assert.Equal(0.340m, _krsDb.Single(k => k.Id == KrId1).PuntuacionPonderada);
        Assert.Equal(0.510m, _krsDb.Single(k => k.Id == KrId2).PuntuacionPonderada);
    }

    /// <summary>#41 · RN-027: final 0.850 con umbrales 0.90/0.70 ⇒ semáforo «Amarillo» del OKR.</summary>
    [Fact]
    public async Task GuardarValoresAsync_ActualizaSemaforoDelOkr()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);

        Assert.Equal(0.850m, _okrPuntuacionFinal);
        Assert.Equal("Amarillo", _okrSemaforo);
    }

    /// <summary>#42 · RN-027 borde <b>inclusivo</b>: justo en el umbral verde ⇒ «Verde», no «Amarillo».</summary>
    [Fact]
    public async Task GuardarValoresAsync_SemaforoVerdeEnElUmbral()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.9m), (2, 0.9m)), CancellationToken.None);

        Assert.Equal(0.900m, _okrPuntuacionFinal);
        Assert.Equal("Verde", _okrSemaforo);
    }

    /// <summary>
    /// #43 · RN-027: por debajo del umbral amarillo ⇒ «Rojo».
    /// <para><b>DESVIACIÓN DOCUMENTADA</b> (se escala): el spec escribe «final 0.699», pero
    /// 0.699 es <b>irreproducible</b> con la escala de la HU: los valores son DECIMAL(2,1) y cada
    /// trimestre tiene como mucho 3 meses, así que un promedio de k/3 redondeado a 3 decimales
    /// nunca da 0.699. Se usa el valor alcanzable más cercano por debajo del umbral (0.667 =
    /// (0.6+0.7+0.7)/3), que ejercita exactamente la misma rama de RN-027.</para>
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_SemaforoRojoBajoElAmarillo()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.6m), (2, 0.7m), (3, 0.7m)), CancellationToken.None);

        Assert.Equal(0.667m, _okrPuntuacionFinal);
        Assert.True(_okrPuntuacionFinal < 0.70m);
        Assert.Equal("Rojo", _okrSemaforo);
    }

    /// <summary>
    /// #44 · Los umbrales salen del ciclo, no de los defaults: con 0.60/0.30 una final de 0.650
    /// es «Verde» — con los defaults (0.90/0.70) sería «Rojo».
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_UsaUmbralesDelCicloNoDefaults()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);
        ConfigurarUmbralesKpi(0.60m, 0.30m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.6m), (2, 0.7m)), CancellationToken.None);

        Assert.Equal(0.650m, _okrPuntuacionFinal);
        Assert.Equal("Verde", _okrSemaforo);
    }

    // -- B.3 · Auditoría (F5), atomicidad, errores de BD y respuesta (casos 45-55) ---------------

    /// <summary>#45 · F5: 12 celdas guardadas ⇒ <b>una</b> entrada de auditoría (granularidad = operación).</summary>
    [Fact]
    public async Task GuardarValoresAsync_RegistraUnaSolaAuditoria()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);
        var vector = Enumerable.Range(1, 12).Select(m => (Mes: m, Valor: 0.5m)).ToArray();

        await sut.GuardarValoresAsync(OkrId, KrId1, Request(vector), CancellationToken.None);

        Assert.Single(_auditorias);
        _repoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#46 · F5: algún mes nuevo ⇒ <c>accion = "CREATE"</c> (el enum no admite acciones libres).</summary>
    [Fact]
    public async Task GuardarValoresAsync_AuditoriaAccionCreateCuandoHayMesNuevo()
    {
        var sut = CrearSutEscenarioCompleto();

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None);

        Assert.Single(_auditorias);
        Assert.Equal("CREATE", _auditorias[0].Accion);
        Assert.Equal("ValorMensualKR", _auditorias[0].Entidad);
        Assert.Equal(KrId1.ToString(), _auditorias[0].EntidadId);
        Assert.Equal(UserId, _auditorias[0].UsuarioId);
        Assert.Equal(TenantId, _auditorias[0].TenantId);
    }

    /// <summary>#47 · F5: todos los meses existían ⇒ <c>accion = "UPDATE"</c>.</summary>
    [Fact]
    public async Task GuardarValoresAsync_AuditoriaAccionUpdateCuandoTodosExistian()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 6, 0.4m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None);

        Assert.Single(_auditorias);
        Assert.Equal("UPDATE", _auditorias[0].Accion);
    }

    /// <summary>
    /// #48 · F5 + ADR-003: el snapshot <c>ValorAnterior</c> incluye el mes guardado con
    /// <c>null</c> cuando no existía (para que la auditoría diga «creado», no «cambiado»).
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_AuditoriaSnapshotAnteriorIncluyeNull()
    {
        var sut = CrearSutEscenarioCompleto();

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None);

        var auditoria = Assert.Single(_auditorias);
        Assert.NotNull(auditoria.ValorAnterior);
        Assert.Contains("6", auditoria.ValorAnterior!);
        Assert.Contains("null", auditoria.ValorAnterior!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #49 · Atomicidad (DB-04): si el recálculo del OKR falla, <b>Rollback</b> y nada queda
    /// confirmado — ni commit ni auditoría.
    /// <para>La señal observable de «no se persistió» es el <c>Rollback</c>: el doble no simula la
    /// atomicidad de PostgreSQL, así que se afirma la transacción, no el diccionario en memoria
    /// (que el mock muta antes del fallo).</para>
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_FalloEnRecalculo_HaceRollback()
    {
        var sut = CrearSutEscenarioCompleto();
        var fallo = new InvalidOperationException("fallo al actualizar el OKR");
        _okrRepoMock
            .Setup(x => x.ActualizarPuntuacionOkrAsync(TenantId, AreaId, OkrId, It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fallo);

        var propagada = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));

        Assert.Same(fallo, propagada);
        _txMock.Verify(t => t.Rollback(), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Never);
        _repoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #50 · Capa 2 (defensa en profundidad): un <c>23514</c> de PostgreSQL (CHECK de rango) se
    /// traduce a 422 con mensaje accionable, no a un 500.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_CheckViolationEnValor_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _repoMock
            .Setup(x => x.UpsertAsync(TenantId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
                It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "new row violates check constraint \"valor_mensual_kr_valor_check\"", "ERROR", "ERROR", "23514"));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));

        Assert.Contains("0.0", ex.Message);
        _txMock.Verify(t => t.Commit(), Times.Never);
    }

    /// <summary>#51 · Un <c>23505</c> (UNIQUE) también se traduce a 422, no a 500.</summary>
    [Fact]
    public async Task GuardarValoresAsync_UniqueViolation_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _repoMock
            .Setup(x => x.UpsertAsync(TenantId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
                It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint \"valor_mensual_kr_key_result_id_mes_key\"",
                "ERROR", "ERROR", "23505"));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.GuardarValoresAsync(OkrId, KrId1, Request((6, 0.7m)), CancellationToken.None));

        Assert.Contains("onflicto", ex.Message, StringComparison.OrdinalIgnoreCase);
        _txMock.Verify(t => t.Commit(), Times.Never);
    }

    /// <summary>
    /// #52 · F3: la respuesta trae la fila recalculada <b>y</b> el agregado del OKR (evita un GET
    /// extra), y el semáforo de la fila viene calculado en BLL (F8).
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_RespuestaIncluyeFilaYAgregadoDelOkr()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);

        Assert.NotNull(resp.KeyResult);
        Assert.Equal(KrId1, resp.KeyResult.KeyResultId);
        Assert.Equal("KR.1", resp.KeyResult.Codigo);
        Assert.Equal(12, resp.KeyResult.Meses.Count);
        Assert.Equal(0.850m, resp.KeyResult.PuntuacionFinal);
        Assert.Equal("Amarillo", resp.KeyResult.Semaforo);

        Assert.NotNull(resp.CalculoOkr);
        Assert.Equal(0.850m, resp.CalculoOkr.PuntuacionFinal);
        Assert.Equal("Amarillo", resp.CalculoOkr.Semaforo);
        Assert.Equal(1, resp.CalculoOkr.KrsConValor);
    }

    /// <summary>#53 · El trimestre es <b>calendario</b>: mes 1 → Q1 y mes 10 → Q4 (no «índice de ciclo»).</summary>
    [Fact]
    public async Task GuardarValoresAsync_MesesDeTrimestresDistintos_CalculaCadaQ()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.5m), (10, 1.0m)), CancellationToken.None);

        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ2);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(1.000m, resp.KeyResult.PuntuacionQ4);
        Assert.Equal(0.750m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>
    /// #54 · <c>MesesConValor</c> cuenta los 12 meses con fila, no los ítems del vector.
    /// <para><b>NOMBRE CORREGIDO</b>: el spec escribe <c>GuardarValoresActualiza_MesesConValor</c>,
    /// que rompe el patrón TEST-05 (el método real es <c>GuardarValoresAsync</c>). Se renombra para
    /// cumplir TEST-05 sin cambiar el caso de negocio.</para>
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_ActualizaMesesConValor()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.5m), (2, 0.6m), (3, 0.7m)), CancellationToken.None);

        Assert.Equal(3, resp.KeyResult.MesesConValor);
    }

    /// <summary>
    /// #55 · DB-04: el recálculo parte de <b>todos</b> los valores del KR, no solo del vector.
    /// Con ENE = 0.5 ya guardado y JUL = 0.9 nuevo, Q1 se conserva en 0.500 y Q3 pasa a 0.900.
    /// </summary>
    [Fact]
    public async Task GuardarValoresAsync_ReutilizaValorPrevioNoReseteaTrimestres()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);
        SembrarValor(KrId1, 1, 0.5m);

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((7, 0.9m)), CancellationToken.None);

        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.900m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(2, resp.KeyResult.MesesConValor);
        Assert.Equal(0.700m, resp.KeyResult.PuntuacionFinal);
    }

    // =========================================================================================
    // BLOQUE C · EliminarValorAsync — casos 56-64 (9 métodos)
    // =========================================================================================

    /// <summary>
    /// #56 · CA #4: borrar un mes borra la fila, recalcula el KR y recalcula el OKR — es el
    /// desbloqueo de <c>KeyResultService.EliminarAsync</c> (mensaje «primero deben borrarse los
    /// valores del período»).
    /// </summary>
    [Fact]
    public async Task EliminarValorAsync_ValorExistente_EliminaYRecalcula()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 1, 0.5m);
        SembrarValor(KrId1, 2, 0.6m);

        var resp = await sut.EliminarValorAsync(OkrId, KrId1, 2, CancellationToken.None);

        _repoMock.Verify(x => x.EliminarAsync(TenantId, OkrId, KrId1, 2,
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(x => x.ActualizarPuntuacionesKrAsync(TenantId, OkrId, KrId1,
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        _okrRepoMock.Verify(x => x.ActualizarPuntuacionOkrAsync(TenantId, AreaId, OkrId,
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
        Assert.False(_valoresDb.ContainsKey((KrId1, 2)));
        _txMock.Verify(t => t.Commit(), Times.Once);
    }

    /// <summary>#57 · Sin fila para ese mes ⇒ 404 «No hay un valor registrado para ese mes».</summary>
    [Fact]
    public async Task EliminarValorAsync_MesSinValor_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 1, 0.5m);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 3, CancellationToken.None));

        Assert.Contains("No hay un valor registrado", ex.Message);
        _repoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #58 · <c>mes</c> fuera de [1,12] ⇒ 422 (error de contrato del request, no de estado: por
    /// eso no es 404). Se comprueban los dos extremos.
    /// </summary>
    [Fact]
    public async Task EliminarValorAsync_MesFueraDeRango_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();

        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 13, CancellationToken.None));

        _repoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#59 · RC-12: el borrado también se bloquea con el ciclo <c>Cerrado</c>.</summary>
    [Fact]
    public async Task EliminarValorAsync_CicloCerrado_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto(estadoCiclo: "Cerrado");
        SembrarValor(KrId1, 1, 0.5m);

        await Assert.ThrowsAsync<ValidacionException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 1, CancellationToken.None));
        Assert.True(_valoresDb.ContainsKey((KrId1, 1)));
    }

    /// <summary>#60 · SEC-07: OKR de otra área ⇒ 404.</summary>
    [Fact]
    public async Task EliminarValorAsync_OkrDeOtraArea_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 1, CancellationToken.None));
    }

    /// <summary>#61 · El Gerente no borra: 403.</summary>
    [Fact]
    public async Task EliminarValorAsync_RolGerente_LanzaExcepcion()
    {
        var sut = CrearSutEscenarioCompleto();
        _tenantContext.Rol = "Gerente";

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 1, CancellationToken.None));
    }

    /// <summary>#62 · F5: el borrado audita con <c>accion = "DELETE"</c> y <c>ValorNuevo = null</c>.</summary>
    [Fact]
    public async Task EliminarValorAsync_RegistraAuditoriaDelete()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 1, 0.5m);

        await sut.EliminarValorAsync(OkrId, KrId1, 1, CancellationToken.None);

        var auditoria = Assert.Single(_auditorias);
        Assert.Equal("DELETE", auditoria.Accion);
        Assert.Equal("ValorMensualKR", auditoria.Entidad);
        Assert.Null(auditoria.ValorNuevo);
        Assert.NotNull(auditoria.ValorAnterior);
    }

    /// <summary>
    /// #63 · El trimestre afectado se recalcula: FEB era el único mes con valor de Q1 ⇒
    /// <c>PuntuacionQ1 = 0.000</c> (no queda un 0.500 "colgado" de un estado anterior).
    /// </summary>
    [Fact]
    public async Task EliminarValorAsync_RecalculaQDelTrimestreAfectado()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 2, 0.5m);

        var resp = await sut.EliminarValorAsync(OkrId, KrId1, 2, CancellationToken.None);

        Assert.Equal(0.000m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionFinal);
        Assert.Equal(0, resp.KeyResult.MesesConValor);
    }

    /// <summary>
    /// #64 · Borra el <b>último</b> valor: el KR vuelve a 0.000 en las 6 columnas y el OKR queda
    /// en 0.000 / «Rojo». Es el escenario que desbloquea el borrado del KR en HU-025 CA #4.
    /// </summary>
    [Fact]
    public async Task EliminarValorAsync_PermiteEliminarElUltimoValor()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 6, 1.0m);

        var resp = await sut.EliminarValorAsync(OkrId, KrId1, 6, CancellationToken.None);

        var vector = _puntuacionesKrEscritas.Single();
        Assert.Equal(0.000m, vector.Q1);
        Assert.Equal(0.000m, vector.Q2);
        Assert.Equal(0.000m, vector.Q3);
        Assert.Equal(0.000m, vector.Q4);
        Assert.Equal(0.000m, vector.Final);
        Assert.Equal(0.000m, vector.Ponderada);
        Assert.Equal(0.000m, resp.KeyResult.PuntuacionFinal);
        Assert.Equal(0.000m, resp.CalculoOkr.PuntuacionFinal);
        Assert.Equal("Rojo", resp.CalculoOkr.Semaforo);
    }

    // =========================================================================================
    // BLOQUE D · Bordes del cálculo y del semáforo — casos 65-70 (6 métodos)
    // Los helpers internos se ejercitan A TRAVÉS del servicio (nota del spec): no se crea una
    // clase de cálculo nueva fuera de la N-Tier (ARCH-01/ARCH-02).
    // =========================================================================================

    /// <summary>#65 · Un solo mes con valor ⇒ el trimestre vale exactamente ese valor.</summary>
    [Fact]
    public async Task CalcularTrimestral_UnSoloMes_RetornaEseValor()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(OkrId, KrId1, Request((4, 1.0m)), CancellationToken.None);

        Assert.Equal(1.000m, resp.KeyResult.PuntuacionQ2);
    }

    /// <summary>#66 · Extremos de la escala (RN-024): 0.0 y 1.0 en el mismo trimestre ⇒ 0.500.</summary>
    [Fact]
    public async Task CalcularTrimestral_Extremos_Promedio0500()
    {
        var sut = CrearSutEscenarioCompleto();

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.0m), (2, 1.0m)), CancellationToken.None);

        Assert.Equal(0.500m, resp.KeyResult.PuntuacionQ1);
    }

    /// <summary>
    /// #67 · Redondeo explícito: (0.1+0.1+0.2)/3 = 0.13333… ⇒ <b>0.133</b> con
    /// <c>MidpointRounding.AwayFromZero</c> (no el <c>ToEven</c> por defecto).
    /// </summary>
    [Fact]
    public async Task CalcularTrimestral_RedondeoA3ConMidpointAwayFromZero()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.1m), (2, 0.1m), (3, 0.2m)), CancellationToken.None);

        Assert.Equal(0.133m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.133m, _okrPuntuacionFinal);
    }

    /// <summary>
    /// #68 · F1 sobre trimestres <b>irregulares</b>: Q1 = 0.333 y Q3 = 0.667 ⇒ final 0.500
    /// (no 0.667 «redondeando» a un trimestre, ni 0.250 penalizando los vacíos).
    /// </summary>
    [Fact]
    public async Task CalcularFinal_PromedioDeTrimestres_Irregular()
    {
        var sut = CrearSutEscenarioCompleto(anioFiscal: 2025);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.1m), (2, 0.4m), (3, 0.5m), (7, 0.6m), (8, 0.7m), (9, 0.7m)),
            CancellationToken.None);

        Assert.Equal(0.333m, resp.KeyResult.PuntuacionQ1);
        Assert.Equal(0.667m, resp.KeyResult.PuntuacionQ3);
        Assert.Equal(0.500m, resp.KeyResult.PuntuacionFinal);
    }

    /// <summary>#69 · Ponderada con peso de 3 decimales: 0.850 × 0.125 = 0.10625 ⇒ <b>0.106</b>.</summary>
    [Fact]
    public async Task CalcularPonderada_PesoConTresDecimales_RedondeoA3()
    {
        var sut = CrearSutEscenarioCompleto(peso: 0.125m);

        var resp = await sut.GuardarValoresAsync(
            OkrId, KrId1, Request((1, 0.8m), (2, 0.9m)), CancellationToken.None);

        Assert.Equal(0.850m, resp.KeyResult.PuntuacionFinal);
        Assert.Equal(0.106m, resp.KeyResult.PuntuacionPonderada);
    }

    /// <summary>
    /// #70 · RN-027 borde <b>inclusivo</b> del umbral amarillo: exactamente 0.700 con umbral
    /// 0.700 ⇒ «Amarillo» (si el borde fuera exclusivo saldría «Rojo»).
    /// </summary>
    [Fact]
    public async Task Semaforo_EnElUmbralAmarillo_Amarillo()
    {
        var sut = CrearSutEscenarioCompleto(peso: 1.000m);

        await sut.GuardarValoresAsync(OkrId, KrId1, Request((1, 0.6m), (2, 0.8m)), CancellationToken.None);

        Assert.Equal(0.700m, _okrPuntuacionFinal);
        Assert.Equal("Amarillo", _okrSemaforo);
    }

    // =========================================================================================
    // BLOQUE E · Cobertura transversal — casos 102-105 (4 métodos)
    // =========================================================================================

    /// <summary>
    /// #102 · Atomicidad del <b>DELETE</b> (el bloque A-D solo probaba la del PUT, caso #49): si
    /// falla el recálculo del OKR hay Rollback, no commit, y la auditoría no se escribe.
    /// </summary>
    [Fact]
    public async Task EliminarValorAsync_FalloEnRecalculo_HaceRollback()
    {
        var sut = CrearSutEscenarioCompleto();
        SembrarValor(KrId1, 1, 0.5m);
        var fallo = new InvalidOperationException("fallo al actualizar el OKR");
        _okrRepoMock
            .Setup(x => x.ActualizarPuntuacionOkrAsync(TenantId, AreaId, OkrId, It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fallo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.EliminarValorAsync(OkrId, KrId1, 1, CancellationToken.None));

        _txMock.Verify(t => t.Rollback(), Times.Once);
        _txMock.Verify(t => t.Commit(), Times.Never);
        _repoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #103 · <b>F0c — la prueba ejecutable de la zona horaria del tenant.</b> 2026-03-01T05:30Z en
    /// America/Lima (UTC−5) son las 00:30 del <b>1 de marzo</b>: el mes de negocio es marzo. Sin la
    /// conversión, un <c>DateTime.UtcNow</c> daría el mes correcto y el bug sería invisible.
    /// </summary>
    [Fact]
    public async Task ObtenerVentanaEdicionAsync_AplicaZonaHorariaDelTenant()
    {
        var sut = CrearSut(
            ahora: new DateTimeOffset(2026, 3, 1, 5, 30, 0, TimeSpan.Zero),
            zonaHoraria: "America/Lima");
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 3, 1), grilla.FechaNegocio);
        Assert.Equal(3, grilla.MesActual);
        Assert.Equal(3, grilla.MesMaxEditable);
    }

    /// <summary>
    /// #104 · El <b>mismo instante</b> en America/Managua (UTC−6) son las 23:30 del 28 de febrero:
    /// mes de negocio 2. Es el par que demuestra que la fecha depende de <c>tenant.zona_horaria</c>
    /// y no del reloj del servidor ni del UTC crudo.
    /// </summary>
    [Fact]
    public async Task ObtenerVentanaEdicionAsync_RespetaZonaHorariaDistinta()
    {
        var sut = CrearSut(
            ahora: new DateTimeOffset(2026, 3, 1, 5, 30, 0, TimeSpan.Zero),
            zonaHoraria: "America/Managua");
        ConfigurarKrs(Kr(KrId1, "KR.1", 1.000m));
        ConfigurarAlmacenValores();

        var grilla = await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 2, 28), grilla.FechaNegocio);
        Assert.Equal(2, grilla.MesActual);
        Assert.Equal(2, grilla.MesMaxEditable);
    }

    /// <summary>
    /// #105 · DB-04: la lectura <b>no</b> recalcula nada — ni UPDATE de <c>key_result</c>, ni
    /// UPDATE de <c>okr</c>, ni auditoría — aunque el OKR tenga KRs con valores y puntuaciones.
    /// </summary>
    [Fact]
    public async Task ObtenerGrillaAsync_NoEjecutaNingunaEscritura()
    {
        var sut = CrearSut();
        ConfigurarKrs(KrConPuntuacion(KrId1, "KR.1", 0.400m, 0.850m));
        ConfigurarAlmacenValores();
        SembrarValor(KrId1, 1, 0.8m);
        SembrarValor(KrId1, 2, 0.9m);

        await sut.ObtenerGrillaAsync(OkrId, CancellationToken.None);

        _repoMock.Verify(x => x.ActualizarPuntuacionesKrAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _okrRepoMock.Verify(x => x.ActualizarPuntuacionOkrAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(x => x.InsertLogAsync(
            It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(x => x.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>(),
            It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════
    // EXPORTACIÓN XLSX —requested por @Orquestador, NO figura en el spec § "Tests requeridos"
    // (HU-026 cierra en 105 casos). Se aplica el precedente HU-023 + ADR-014: ClosedXML genera
    // el byte[] en la BLL y el controller solo lo envuelve en FileContentResult.
    // ══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EXP-1 · <c>ExportarAsync(okrId)</c> devuelve un <c>byte[]</c> XLSX real (no un
    /// <c>Stream</c> ni una cadena base64) con una hoja por OKR y una fila por KR × 12 meses, y el
    /// agregado del OKR al pie. Se valida <b>abriendo el workbook con ClosedXML</b> — un simple
    /// <c>IsNullOrEmpty</c> dejaría pasar un archivo corrupto.
    /// </summary>
    [Fact]
    public async Task ExportarAsync_OkrValido_RetornaExcel()
    {
        var sut = CrearSutEscenarioCompleto();
        ConfigurarKrs(Kr(KrId1, "KR.1", 0.400m), Kr(KrId2, "KR.2", 0.600m));
        ConfigurarAlmacenValores();
        SembrarValor(KrId1, 1, 0.8m);
        SembrarValor(KrId2, 1, 0.7m);

        var bytes = await sut.ExportarAsync(OkrId, CancellationToken.None);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes!);
        Assert.Equal(0x50, bytes![0]);   // 'P'
        Assert.Equal(0x4B, bytes[1]);   // 'K' — contenedor ZIP, como todo XLSX

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var hoja = Assert.Single(wb.Worksheets);

        // El código y la descripción del OKR dan nombre a la hoja (trazabilidad del archivo).
        Assert.Contains("OKR.1", hoja.Name);

        // Fila 1 = cabecera con los 12 meses de F0 (no 12 columnas sueltas sin rótulo).
        var cabeceras = hoja.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("ENE", cabeceras);
        Assert.Contains("DIC", cabeceras);

        // Cada KR aporta una fila: el código debe aparecer en el contenido de la hoja.
        var cuerpo = hoja.RangeUsed()!.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains(cuerpo, v => v.Contains("KR.1"));
        Assert.Contains(cuerpo, v => v.Contains("KR.2"));
    }

    /// <summary>
    /// EXP-2 · Multi-tenant + SEC-07: el XLSX solo puede contener KRs del tenant/área del contexto.
    /// Un OKR de otra área se rechaza con <c>NotFoundException</c> y <b>no</b> se genera archivo.
    /// </summary>
    [Fact]
    public async Task ExportarAsync_OkrDeOtraArea_LanzaNotFoundYNoGeneraArchivo()
    {
        var sut = CrearSutEscenarioCompleto();
        // SEC-07: el DAL filtra por area_id → el OKR de otra área llega como null.
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, OkrId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.ExportarAsync(OkrId, CancellationToken.None));
    }
}

using System.Data;
using System.Text.RegularExpressions;
using Moq;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Repositories.Objetivos;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de HU-025 — Gestión de Key Results, capa <b>DAL</b> (Spec § "Tests requeridos"
/// casos <b>54-65</b>, 12 métodos).
/// </summary>
/// <para>
/// <b>Por qué NO hay arnés de integración:</b> <c>PE-GOL.Tests.csproj</c> no referencia Npgsql
/// ni existe arnés contra Supabase (restricción documentada en el hotfix v2 de HU-022). Estos 12
/// tests son UNITARIOS sobre el <b>SQL capturado</b>, con el doble escrito a mano.
/// </para>
/// <para>
/// <b>DOBLES REUTILIZADOS, NO COPIADOS:</b> <c>ConexionCapturadora</c>, <c>ConsultaSql</c>,
/// <c>ComandoCapturador</c>, <c>TransaccionFalsa</c>, <c>ParametroCapturador</c> y
/// <c>ParametroColeccion</c> ya están declarados como <c>internal sealed class</c> en el
/// namespace <c>PE_GOL.Tests</c> dentro de <c>EntregableAdjuntoRepositoryTests.cs</c> (HU-022 v3).
/// Redeclararlos aquí daría <b>CS0101</b> (mismo namespace, mismo ensamblado). El spec decía
/// "copiar el doble"; copiar el <b>comportamiento</b> sí, duplicar las <b>clases</b> no.
/// </para>
/// <para>
/// <b>CONTRATO QUE @BackendDev IMPLEMENTA:</b>
/// <list type="bullet">
///   <item><c>PE-GOL.DAL/Interfaces/IKeyResultRepository.cs</c> → <c>PE_GOL.DAL.Interfaces</c>,
///     11 métodos, <b>todas</b> con <c>tenantId</c> como primer parámetro (SEC-06).</item>
///   <item><c>PE-GOL.DAL/Repositories/Objetivos/KeyResultRepository.cs</c> →
///     <c>PE_GOL.DAL.Repositories.Objetivos</c>, ctor de <b>UN</b> argumento
///     <c>(IDbConnectionFactory)</c> — sin <c>ILogger</c> (precedente de los 9 repositorios).</item>
///   <item>DTOs: requests en <c>PE_GOL.DTO.Requests.Okr</c>, response en
///     <c>PE_GOL.DTO.Responses.Okr</c>, proyections en <c>PE_GOL.DTO.Dtos</c>.</item>
/// </list>
/// <para>
/// <b>Prueba de "sin concatenación" (STACK-03 / SEC-05):</b> el "+" o el <c>$"…{valor}"</c> no son
/// observables en runtime. Lo que SÍ es observable —y es la prueba real de que el valor viaja como
/// parámetro nombrado— es que <b>ningún GUID aparezca dentro del texto SQL</b>. Si la query se
/// construyera concatenando, el id se filtraría al texto y el caso #61 lo detectaría.
/// </para>
/// </summary>
public class KeyResultRepositoryTests
{
    private static readonly Guid TenantId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AreaId     = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid OkrId      = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid KrId1      = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid KrId2      = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid KrId3      = Guid.Parse("00000000-0000-0000-0000-000000000008");
    private static readonly Guid UsuarioId  = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private ConexionCapturadora _conexion = null!;
    private KeyResultRepository _sut = null!;

    /// <summary>
    /// Arrange común: la fábrica devuelve SIEMPRE la misma conexión capturadora (el repositorio
    /// cachea una, y devolver la misma vuelve el test inmune a ese detalle) con la proyección de
    /// <c>key_result</c> y 0 filas — la doble NO simula red.
    /// </summary>
    private void CrearRepositorio()
    {
        _conexion = new ConexionCapturadora { Filas = TablaKeyResult() };
        var factory = new Mock<IDbConnectionFactory>();
        factory.Setup(f => f.CreateConnection()).Returns(() => _conexion);
        _sut = new KeyResultRepository(factory.Object);
    }

    /// <summary>El último SQL ejecutado, con el texto normalizado (blancos colapsados a 1 espacio).</summary>
    private ConsultaSql Ultima => _conexion.Consultas[^1];

    /// <summary>
    /// Normaliza el SQL para que las afirmaciones no dependan del indentado ni de los saltos de
    /// línea que elija el implementador, pero sí del contenido (STACK-03 exige parametrizado, no
    /// una cadena concreta).
    /// </summary>
    private static string Normalizar(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    /// <summary>
    /// Proyección de las Queries 1 y 2 con las 15 columnas de <see cref="KeyResultResponse"/>.
    /// Se declaran para que <c>FieldCount &gt; 0</c> y Dapper no entre por su rama de "comando que
    /// no devuelve nada" (que solo aplica con <c>FieldCount == 0</c>).
    /// </summary>
    private static DataTable TablaKeyResult()
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(Guid));
        t.Columns.Add("TenantId", typeof(Guid));
        t.Columns.Add("OkrId", typeof(Guid));
        t.Columns.Add("OkrCodigo", typeof(string));
        t.Columns.Add("Codigo", typeof(string));
        t.Columns.Add("Descripcion", typeof(string));
        t.Columns.Add("Peso", typeof(decimal));
        t.Columns.Add("PuntuacionQ1", typeof(decimal));
        t.Columns.Add("PuntuacionQ2", typeof(decimal));
        t.Columns.Add("PuntuacionQ3", typeof(decimal));
        t.Columns.Add("PuntuacionQ4", typeof(decimal));
        t.Columns.Add("PuntuacionFinal", typeof(decimal));
        t.Columns.Add("PuntuacionPonderada", typeof(decimal));
        t.Columns.Add("Orden", typeof(int));
        t.Columns.Add("CreatedAt", typeof(DateTime));
        t.Columns.Add("UpdatedAt", typeof(DateTime));
        return t;
    }

    private static KeyResultCreateRequest CrearRequest(decimal peso = 0.500m) => new()
    {
        Descripcion = "Aumentar la conversion en 10 puntos",
        Peso = peso
    };

    // ═══════════════════════════ 54 · ListarAsync ═══════════════════════════

    /// <summary>
    /// #54 · Query 1 · SELECT con INNER JOIN okr (OkrCodigo), aislamiento tenant + okr y orden
    /// estable por orden/created_at. Los dos ids viajan como PARÁMETRO NOMBRADO (SEC-05).
    /// </summary>
    [Fact]
    public async Task ListarAsync_Sql_FiltraTenantOkrYOrdena()
    {
        CrearRepositorio();

        var filas = await _sut.ListarAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM key_result kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN okr o ON o.id = kr.okr_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY kr.orden ASC, kr.created_at ASC", sql, StringComparison.OrdinalIgnoreCase);

        // Las 6 puntuaciones se proyectan tal cual (DB-04: se devuelven, no se calculan aquí)
        Assert.Contains("kr.puntuacion_ponderada AS PuntuacionPonderada", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.codigo AS OkrCodigo", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);

        // La doble no simula red: 0 filas → la BLL responderá 200 con lista vacía (UX-05)
        Assert.Empty(filas);
    }

    // ═══════════════════════════ 55 · ObtenerPorIdAsync ═══════════════════════════

    /// <summary>
    /// #55 · Query 2 · Filtra por LAS TRES columnas (id + tenant + okr): un KR de otro OKR
    /// devuelve null y la BLL lo traduce a 404 — nunca a 403 (sin fuga, RN-008).
    /// </summary>
    [Fact]
    public async Task ObtenerPorIdAsync_Sql_FiltraIdTenantOkr()
    {
        CrearRepositorio();

        var kr = await _sut.ObtenerPorIdAsync(TenantId, OkrId, KrId1, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("kr.id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN okr", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(KrId1, Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);

        Assert.Null(kr);
    }

    // ═══════════════════════════ 56 · ContarKeyResultsAsync ═══════════════════════════

    /// <summary>#56 · Query 3 · COUNT con los 2 filtros de aislamiento; proyecta al alias PascalCase.</summary>
    [Fact]
    public async Task ContarKeyResultsAsync_Sql_CountConTenantOkr()
    {
        CrearRepositorio();

        await _sut.ContarKeyResultsAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("COUNT(1) AS Cantidad", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM key_result", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
    }

    // ═════════════════════ 57 · ObtenerSumaPesosAsync ═════════════════════

    /// <summary>
    /// #57 · Query 4 · COALESCE(SUM(peso), 0) con los 2 filtros. Uso ÚNICO: la verificación de
    /// integridad post-batch (BLL 6.2). <b>NO</b> lleva parámetro de exclusión ni <c>IS NULL</c>:
    /// la aritmética la hace la BLL con decimal exacto (F6), y evitar el null-param contra columna
    /// tipada es la lección de ADR-015.
    /// </summary>
    [Fact]
    public async Task ObtenerSumaPesosAsync_Sql_SumaPesosDelOkr()
    {
        CrearRepositorio();

        await _sut.ObtenerSumaPesosAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("COALESCE(SUM(peso), 0) AS SumaPesos", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);

        // La suma se calcula sobre el OKR entero: sin exclusión, sin IS NULL, sin JOIN
        Assert.DoesNotContain("@ExcluirId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
    }

    // ═════════ 58 · ObtenerSiguienteSecuenciaKeyResultAsync ═════════

    /// <summary>
    /// #58 · Query 5 · <c>MAX(regexp_match(codigo,'^KR\.(\d+)$'))+1</c> <b>por okr_id</b> (F4), más
    /// <c>MAX(orden)+1</c>. Es MAX y no COUNT: por eso nunca reutiliza un código tras eliminar un
    /// KR intermedio.
    /// </summary>
    [Fact]
    public async Task ObtenerSiguienteSecuenciaKeyResultAsync_Sql_MaxRegexpPorOkr()
    {
        CrearRepositorio();

        await _sut.ObtenerSiguienteSecuenciaKeyResultAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains(@"regexp_match(codigo, '^KR\.(\d+)$')", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(MAX(orden), 0) + 1", sql, StringComparison.OrdinalIgnoreCase);
        // Alias PascalCase para el mapeo directo de Dapper
        Assert.Contains("AS SiguienteN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SiguienteOrden", sql, StringComparison.OrdinalIgnoreCase);
        // Filtrado por okr_id (coherente con UNIQUE (okr_id, codigo)), NO por área
        Assert.Contains("okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("area_id", sql, StringComparison.OrdinalIgnoreCase);
        // MAX, no COUNT
        Assert.DoesNotContain("COUNT", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
    }

    // ═══════════════════════════ 59 · VerificarValoresRealesAsync ═══════════════════════════

    /// <summary>
    /// #59 · Query 6 · <c>EXISTS</c> sobre <c>valor_mensual_kr</c> por <c>key_result_id</c> — la regla
    /// de CA #4. <c>valor_mensual_kr</c> no tiene <c>ciclo_id</c>, luego el EXISTS por KR es
    /// equivalente al "ciclo activo": el OKR ya se validó contra el ciclo activo en la BLL.
    /// </summary>
    [Fact]
    public async Task VerificarValoresRealesAsync_Sql_ExistsAnidado()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = false;

        var tieneValores = await _sut.VerificarValoresRealesAsync(TenantId, KrId1, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("EXISTS(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM valor_mensual_kr vmk", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.key_result_id = @KeyResultId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(KrId1, Ultima.Parametros["KeyResultId"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.False(tieneValores);
    }

    // ═══════════════════════════ 60 · CrearAsync ═══════════════════════════

    /// <summary>
    /// #60 · Query 7 · INSERT de <b>12 columnas</b> con las <b>6 puntuaciones a 0.000</b> (DB-04)
    /// y <c>RETURNING id</c>. Solo 6 parámetros: <c>created_at</c>/<c>updated_at</c> los fija el
    /// DEFAULT now() del DDL y las puntuaciones son literales (no entrada del usuario).
    /// </summary>
    [Fact]
    public async Task CrearAsync_Sql_InsertaDoceColumnasConPuntuacionesEnCero()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = KrId1;
        using var tx = _conexion.BeginTransactionFalsa();

        var id = await _sut.CrearAsync(TenantId, OkrId, "KR.1", 1, CrearRequest(), tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO key_result", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RETURNING id", sql, StringComparison.OrdinalIgnoreCase);

        // Las 12 columnas de escritura (DB-04: created_at/updated_at no se insertan)
        Assert.Contains("tenant_id, okr_id, codigo, descripcion, peso", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_q1, puntuacion_q2, puntuacion_q3, puntuacion_q4", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_final, puntuacion_ponderada, orden", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updated_at", sql, StringComparison.OrdinalIgnoreCase);

        // Los SEIS ceros de puntuación (DB-04: se persisten, no se calculan en SQL)
        Assert.Contains(@"0.000, 0.000, 0.000, 0.000, 0.000, 0.000", sql, StringComparison.Ordinal);

        // Solo 6 parámetros: los que llegan de la BLL + los dos del request
        Assert.Equal(6, Ultima.Parametros.Count);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
        Assert.Equal("KR.1", Ultima.Parametros["Codigo"]);
        Assert.Equal("Aumentar la conversion en 10 puntos", Ultima.Parametros["Descripcion"]);
        Assert.Equal(0.500m, Ultima.Parametros["Peso"]);
        Assert.Equal(1, Ultima.Parametros["Orden"]);

        Assert.Equal(KrId1, id);
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ═══════════════════════════ 61 · ActualizarAsync ═══════════════════════════

    /// <summary>
    /// #61 · Query 8 · UPDATE <b>mínimo</b>: solo <c>descripcion</c>, <c>peso</c> y
    /// <c>updated_at</c>. NO toca <c>codigo</c>, <c>orden</c> ni las 6 columnas de puntuación
    /// (CA #1, DB-04). WHERE con los TRES filtros de aislamiento, también en escritura (SEC-07).
    /// </summary>
    [Fact]
    public async Task ActualizarAsync_Sql_SoloTocaDescripcionPesoUpdatedAt()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        await _sut.ActualizarAsync(TenantId, OkrId, KrId1,
            new KeyResultUpdateRequest { Descripcion = "Nueva descripcion", Peso = 0.750m }, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("UPDATE key_result", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET descripcion = @Descripcion", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("peso = @Peso", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at = CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);

        // Lo que el UPDATE NO puede tocar
        Assert.DoesNotContain("codigo =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orden =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_q1 =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_final =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_ponderada =", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(KrId1, Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
        Assert.Equal("Nueva descripcion", Ultima.Parametros["Descripcion"]);
        Assert.Equal(0.750m, Ultima.Parametros["Peso"]);
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ═══════════════════════════ 62 · ActualizarPesosAsync ═══════════════════════════

    /// <summary>
    /// #62 · Query 8b (F0) · <b>UN</b> solo comando con <c>FROM (VALUES …) AS x(id, peso)</c> y
    /// <c>SET peso = x.peso, updated_at = CURRENT_TIMESTAMP</c>. Se ejecuta con la <b>misma
    /// conexión de la transacción del caller</b> → o se aplica el reparto completo o no se aplica
    /// nada. Solo se interpolan MARCADORES: los GUID viajan en parámetros, nunca en el texto
    /// (SEC-05) — de ahí que no aparezca ninguno en el SQL.
    /// </summary>
    [Fact]
    public async Task ActualizarPesosAsync_Sql_BatchUpdateEnTransaccion()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();
        var pesos = new List<KeyResultPesoRequest>
        {
            new() { Id = KrId1, Peso = 0.300m },
            new() { Id = KrId2, Peso = 0.300m },
            new() { Id = KrId3, Peso = 0.400m }
        };

        await _sut.ActualizarPesosAsync(TenantId, OkrId, pesos, tx, CancellationToken.None);

        // UN solo SQL capturado (un round-trip, no N)
        Assert.Single(_conexion.Consultas);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("UPDATE key_result kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET peso = x.peso", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at = CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM (VALUES (@Id0, @Peso0), (@Id1, @Peso1), (@Id2, @Peso2)) AS x(id, peso)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE kr.id = x.id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);

        // UPDATE mínimo: solo peso + updated_at
        Assert.DoesNotContain("descripcion =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codigo =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orden =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_q1 =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_ponderada =", sql, StringComparison.OrdinalIgnoreCase);

        // SEC-05/STACK-03: los 3 ids y pesos viajan como PARÁMETROS; NINGÚN GUID en el texto SQL
        Assert.DoesNotContain(KrId1.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KrId2.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KrId3.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
        Assert.Equal(KrId1, Ultima.Parametros["Id0"]);
        Assert.Equal(0.300m, Ultima.Parametros["Peso0"]);
        Assert.Equal(KrId2, Ultima.Parametros["Id1"]);
        Assert.Equal(0.300m, Ultima.Parametros["Peso1"]);
        Assert.Equal(KrId3, Ultima.Parametros["Id2"]);
        Assert.Equal(0.400m, Ultima.Parametros["Peso2"]);

        // Se respeta la transacción del caller (atomicidad de F0)
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ═══════════════════════════ 63 · EliminarAsync ═══════════════════════════

    /// <summary>#63 · Query 9 · DELETE físico acotado por los TRES filtros (no se hace borrado lógico).</summary>
    [Fact]
    public async Task EliminarAsync_Sql_DeleteFisicoFiltraTenantOkr()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        await _sut.EliminarAsync(TenantId, OkrId, KrId1, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("DELETE FROM key_result", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AND okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        // Físico: sin "borrado lógico", sin columna de estado
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activo", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(KrId1, Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ═══════════════════════════ 64 · InsertLogAsync ═══════════════════════════

    /// <summary>
    /// #64 · Query 10 (ADR-003) · INSERT en log_auditoria con los 7 campos, el enum de acción con
    /// cast <c>::accion_auditoria</c> y los dos JSON con <c>::jsonb</c>, respetando la tx.
    /// </summary>
    [Fact]
    public async Task InsertLogAsync_Sql_PatronAuditoria()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        await _sut.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = TenantId,
            UsuarioId = UsuarioId,
            Accion = "CREATE",
            Entidad = "KeyResult",
            EntidadId = KrId1.ToString(),
            ValorAnterior = null,
            ValorNuevo = "{\"codigo\":\"KR.1\",\"peso\":0.5}"
        }, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO log_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Accion::accion_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ValorAnterior::jsonb", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ValorNuevo::jsonb", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(7, Ultima.Parametros.Count);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(UsuarioId, Ultima.Parametros["UsuarioId"]);
        Assert.Equal("CREATE", Ultima.Parametros["Accion"]);
        Assert.Equal("KeyResult", Ultima.Parametros["Entidad"]);
        Assert.Equal(KrId1.ToString(), Ultima.Parametros["EntidadId"]);
        Assert.True(Ultima.Parametros["ValorAnterior"] is null or DBNull,
            $"valor_anterior debe ser SQL NULL en un CREATE, pero viaja: {Ultima.Parametros["ValorAnterior"]}");
        Assert.Contains("KR.1", (string)Ultima.Parametros["ValorNuevo"]!, StringComparison.Ordinal);

        // La tabla log_auditoria NO tiene okr_id/area_id/ciclo_id (ADR-003: el alcance va en EntidadId)
        Assert.DoesNotContain("okr_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("area_id", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Same(tx, Ultima.Transaccion);
    }

    // ═══════════════════════════ 65 · BeginTransactionAsync ═══════════════════════════

    /// <summary>#65 · Abre la conexión (o la reutiliza si ya está Open) y devuelve una IDbTransaction real.</summary>
    [Fact]
    public async Task BeginTransactionAsync_AbreConexionEIniciaTx()
    {
        CrearRepositorio();

        var tx = await _sut.BeginTransactionAsync(CancellationToken.None);

        Assert.NotNull(tx);
        // La transacción pertenece a la conexión del repositorio (no se crea una suelta)
        Assert.Same(_conexion, tx.Connection);
        Assert.IsType<TransaccionFalsa>(tx);
    }

    // ═════════════════════ SEC-06 / SEC-07 · aislamiento en TODAS las consultas ═════════════════════

    /// <summary>
    /// SEC-06 · DB-03 · Las 10 queries de entidad de HU-025 filtran SIEMPRE por
    /// <c>tenant_id = @TenantId</c> como PARÁMETRO NOMBRADO (capa 1; la RLS es la capa 2,
    /// ARCH-04) y ninguna concatena un GUID en el texto SQL (STACK-03 / SEC-05).
    /// </summary>
    [Fact]
    public async Task TodasLasQueries_FiltranTenantYNoConcatenanValores()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        await _sut.ListarAsync(TenantId, OkrId, CancellationToken.None);
        await _sut.ObtenerPorIdAsync(TenantId, OkrId, KrId1, CancellationToken.None);
        await _sut.ContarKeyResultsAsync(TenantId, OkrId, CancellationToken.None);
        await _sut.ObtenerSumaPesosAsync(TenantId, OkrId, CancellationToken.None);
        await _sut.ObtenerSiguienteSecuenciaKeyResultAsync(TenantId, OkrId, CancellationToken.None);
        await _sut.VerificarValoresRealesAsync(TenantId, KrId1, CancellationToken.None);
        await _sut.CrearAsync(TenantId, OkrId, "KR.1", 1, CrearRequest(), tx, CancellationToken.None);
        await _sut.ActualizarAsync(TenantId, OkrId, KrId1, new KeyResultUpdateRequest { Descripcion = "d", Peso = 0.5m }, tx, CancellationToken.None);
        await _sut.ActualizarPesosAsync(TenantId, OkrId, new List<KeyResultPesoRequest> { new() { Id = KrId1, Peso = 1.0m } }, tx, CancellationToken.None);
        await _sut.EliminarAsync(TenantId, OkrId, KrId1, tx, CancellationToken.None);
        await _sut.InsertLogAsync(new LogAuditoriaInsert { TenantId = TenantId, Accion = "DELETE", Entidad = "KeyResult" }, tx, CancellationToken.None);

        // Las 10 de entidad + 1 de auditoría = 11 SQL capturados
        Assert.Equal(11, _conexion.Consultas.Count);

        // SEC-06/DB-03: las 11 llevan @TenantId como PARÁMETRO NOMBRADO con el tenant del
        // llamante (nunca un literal). Un INSERT no lleva WHERE, pero sigue llevando el tenant.
        Assert.All(_conexion.Consultas, c =>
        {
            Assert.True(c.Parametros.ContainsKey("TenantId"),
                "Toda query de HU-025 debe recibir el tenant como parámetro (SEC-06).");
            Assert.Equal(TenantId, c.Parametros["TenantId"]);
        });

        // Y las que SÍ filtran (los 5 SELECT, los 3 UPDATE/DELETE y el EXISTS) lo hacen en la
        // cláusula WHERE. Los 2 INSERT (key_result, log_auditoria) no llevan WHERE por definición.
        var conWhere = _conexion.Consultas
            .Where(c => Normalizar(c.Sql).Contains("WHERE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(9, conWhere.Count);
        Assert.All(conWhere, c =>
            Assert.Contains("tenant_id = @TenantId", Normalizar(c.Sql), StringComparison.OrdinalIgnoreCase));

        // STACK-03 / SEC-05: ningún identificador viaja dentro del texto SQL
        Assert.All(_conexion.Consultas, c =>
        {
            var sql = Normalizar(c.Sql);
            Assert.DoesNotContain(TenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(OkrId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AreaId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(KrId1.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        });
    }
}
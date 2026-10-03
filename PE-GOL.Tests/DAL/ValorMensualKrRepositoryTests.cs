using System.Data;
using System.Text.RegularExpressions;
using Moq;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Repositories.Objetivos;
using PE_GOL.DTO.Dtos;

namespace PE_GOL.Tests.DAL;

/// <summary>
/// Tests TDD de HU-026 — Registro Mensual de Valores Reales de KRs, capa <b>DAL</b>
/// (Spec § "Tests requeridos" casos <b>71-87</b>, 17 métodos).
/// </summary>
/// <para>
/// <b>Por qué NO hay arnés de integración:</b> <c>PE-GOL.Tests.csproj</c> no referencia Npgsql de
/// forma directa ni existe arnés contra Supabase (restricción documentada en el hotfix v2 de
/// HU-022). Estos 17 tests son UNITARIOS sobre el <b>SQL capturado</b>.
/// </para>
/// <para>
/// <b>DOBLES REUTILIZADOS, NO COPIADOS:</b> <c>ConexionCapturadora</c>, <c>ConsultaSql</c>,
/// <c>ComandoCapturador</c>, <c>TransaccionFalsa</c>, <c>ParametroCapturador</c> y
/// <c>ParametroColeccion</c> ya están declarados como <c>internal sealed class</c> en el namespace
/// <c>PE_GOL.Tests</c> dentro de <c>EntregableAdjuntoRepositoryTests.cs</c> (HU-022 v3).
/// Redeclararlos aquí daría <b>CS0101</b>: se copia el comportamiento, no las clases.
/// </para>
/// <para>
/// <b>CONTRATO QUE @BackendDev IMPLEMENTA:</b>
/// <list type="bullet">
///   <item><c>PE-GOL.DAL/Repositories/Objetivos/ValorMensualKrRepository.cs</c> →
///     <c>PE_GOL.DAL.Repositories.Objetivos</c>, ctor de <b>UN</b> argumento
///     <c>(IDbConnectionFactory)</c> (precedente de <c>KeyResultRepository</c>).</item>
///   <item><c>IValorMensualKrRepository</c> (namespace <c>PE_GOL.DAL.Interfaces</c>).</item>
///   <item>Proyección <c>ValorMensualKrDto</c> en <c>PE_GOL.DTO.Dtos</c>.</item>
///   <item><b>Extensión aditiva</b> de <c>OkrRepository</c>:
///     <c>ActualizarPuntuacionOkrAsync</c> (DAL-5 del spec), que escribe en <c>okr</c> y por eso
///     lleva <c>AND area_id = @AreaId</c> además de <c>tenant_id</c> (SEC-07).</item>
/// </list>
/// <para>
/// <b>Lo que estos tests NO pueden probar</b> (y está dicho aquí para que nadie lo dé por hecho):
/// la unicidad real <c>UNIQUE (key_result_id, mes)</c> y los <c>CHECK</c> son garantías de
/// PostgreSQL. Sin arnés, el caso #79 verifica <b>el mecanismo</b> que las hace cumplir
/// (<c>ON CONFLICT ... DO UPDATE</c> sobre ese mismo índice) y no el efecto; el efecto se
/// comprueba en la validación final contra la BD, no aquí.
/// </para>
/// </summary>
public class ValorMensualKrRepositoryTests
{
    private static readonly Guid TenantId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AreaId    = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid OkrId     = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid KrId1     = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid KrId2     = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid ValorId   = Guid.Parse("00000000-0000-0000-0000-000000000008");
    private static readonly Guid UsuarioId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private ConexionCapturadora _conexion = null!;
    private Mock<IDbConnectionFactory> _factory = null!;

    /// <summary>
    /// Arrange común: la fábrica devuelve SIEMPRE la misma conexión capturadora (el repositorio
    /// cachea una, y devolver la misma vuelve el test inmune a ese detalle).
    /// </summary>
    private void CrearRepositorio()
    {
        _conexion = new ConexionCapturadora { Filas = TablaValor() };
        _factory = new Mock<IDbConnectionFactory>();
        _factory.Setup(f => f.CreateConnection()).Returns(() => _conexion);
    }

    private ValorMensualKrRepository Sut => new(_factory.Object);
    private OkrRepository SutOkr => new(_factory.Object);

    /// <summary>El último SQL ejecutado, con el texto normalizado (blancos colapsados a 1 espacio).</summary>
    private ConsultaSql Ultima => _conexion.Consultas[^1];

    /// <summary>
    /// Normaliza el SQL para que las afirmaciones no dependan del indentado ni de los saltos de
    /// línea que elija el implementador, pero sí del contenido (STACK-03 exige parametrizado, no
    /// una cadena concreta).
    /// </summary>
    private static string Normalizar(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    /// <summary>
    /// Proyección de las queries de lectura. Se declara con columnas para que
    /// <c>FieldCount &gt; 0</c> y Dapper no entre por su rama de "comando que no devuelve nada".
    /// </summary>
    private static DataTable TablaValor()
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(Guid));
        t.Columns.Add("KeyResultId", typeof(Guid));
        t.Columns.Add("Mes", typeof(int));
        t.Columns.Add("Valor", typeof(decimal));
        t.Columns.Add("RegistradoPor", typeof(Guid));
        t.Columns.Add("UpdatedAt", typeof(DateTime));
        return t;
    }

    /// <summary>Sembrado de filas en el lector del doble.</summary>
    private static void AgregarFila(DataTable t, Guid krId, int mes, decimal valor)
        => t.Rows.Add(ValorId, krId, mes, valor, UsuarioId, new DateTime(2026, 6, 15, 12, 0, 0));

    // ═══════════════════════════ 71-73 · ListarPorOkrAsync ═══════════════════════════

    /// <summary>
    /// #71 · DAL-1 · Devuelve los valores de <b>todos</b> los KRs del OKR y los proyecta a los
    /// alias PascalCase; el gate <c>kr.okr_id = @OkrId</c> es la segunda compuerta de SEC-07.
    /// </summary>
    [Fact]
    public async Task ListarPorOkrAsync_OkrConFilas_RetornaValoresDeTodosLosKrs()
    {
        CrearRepositorio();
        var tabla = TablaValor();
        AgregarFila(tabla, KrId1, 1, 0.5m);
        AgregarFila(tabla, KrId1, 2, 0.6m);
        AgregarFila(tabla, KrId1, 3, 0.7m);
        AgregarFila(tabla, KrId2, 1, 0.8m);
        AgregarFila(tabla, KrId2, 2, 0.9m);
        AgregarFila(tabla, KrId2, 3, 1.0m);
        _conexion.Filas = tabla;

        var filas = await Sut.ListarPorOkrAsync(TenantId, OkrId, CancellationToken.None);

        Assert.Equal(6, filas.Count());

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM valor_mensual_kr vmk", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN key_result kr ON kr.id = vmk.key_result_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.mes AS Mes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.valor AS Valor", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY vmk.key_result_id, vmk.mes", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
    }

    /// <summary>#72 · OKR sin valores ⇒ lista vacía (la BLL pintará el empty-state, UX-05).</summary>
    [Fact]
    public async Task ListarPorOkrAsync_SinFilas_RetornaVacio()
    {
        CrearRepositorio();

        var filas = await Sut.ListarPorOkrAsync(TenantId, OkrId, CancellationToken.None);

        Assert.Empty(filas);
    }

    /// <summary>
    /// #73 · Un OKR ajeno no ve filas: el <c>AND kr.okr_id = @OkrId</c> está en el JOIN, no
    /// solo en la BLL (doble compuerta SEC-07).
    /// </summary>
    [Fact]
    public async Task ListarPorOkrAsync_OtroOkr_NoDevuelveFilas()
    {
        CrearRepositorio();
        var tabla = TablaValor();
        AgregarFila(tabla, KrId1, 1, 0.5m);
        _conexion.Filas = tabla;

        // El doble no filtra: la garantía la da el WHERE. Lo que se afirma aquí es que el gate
        // está en el SQL y que el id ajeno viaja como parámetro (no concatenado).
        await Sut.ListarPorOkrAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OkrId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OkrId, Ultima.Parametros["OkrId"]);
    }

    // ═══════════════════════════ 74 · ListarPorKrAsync ═══════════════════════════

    /// <summary>#74 · DAL-2 · KR sin valores ⇒ vacío; el gate incluye el <c>key_result_id</c>.</summary>
    [Fact]
    public async Task ListarPorKrAsync_KrSinValores_RetornaVacio()
    {
        CrearRepositorio();

        var filas = await Sut.ListarPorKrAsync(TenantId, OkrId, KrId1, CancellationToken.None);

        Assert.Empty(filas);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("vmk.key_result_id = @KeyResultId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY vmk.mes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(KrId1, Ultima.Parametros["KeyResultId"]);
    }

    // ═══════════════════════════ 75-76 · ObtenerValorAsync ═══════════════════════════

    /// <summary>#75 · DAL-6 · Fila de un mes concreto, con <c>valor</c> y <c>registrado_por</c>.</summary>
    [Fact]
    public async Task ObtenerValorAsync_KrYMesValidos_RetornaFila()
    {
        CrearRepositorio();
        var tabla = TablaValor();
        AgregarFila(tabla, KrId1, 6, 0.7m);
        _conexion.Filas = tabla;

        var valor = await Sut.ObtenerValorAsync(TenantId, OkrId, KrId1, 6, CancellationToken.None);

        Assert.NotNull(valor);
        Assert.Equal(6, valor!.Mes);
        Assert.Equal(0.7m, valor.Valor);
        Assert.Equal(UsuarioId, valor.RegistradoPor);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("vmk.mes = @Mes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, Ultima.Parametros["Mes"]);
        Assert.Equal(KrId1, Ultima.Parametros["KeyResultId"]);
    }

    /// <summary>#76 · Mes sin fila ⇒ <c>null</c> (la BLL lo traduce a 404, no a 422).</summary>
    [Fact]
    public async Task ObtenerValorAsync_MesSinFila_RetornaNull()
    {
        CrearRepositorio();

        var valor = await Sut.ObtenerValorAsync(TenantId, OkrId, KrId1, 11, CancellationToken.None);

        Assert.Null(valor);
        Assert.Equal(11, Ultima.Parametros["Mes"]);
    }

    // ═══════════════════════════ 77-81 · UpsertAsync (RC-07 / DB-06) ═══════════════════════════

    /// <summary>
    /// #77 · DAL-3 · Un mes nuevo es un INSERT con las 6 columnas de la tabla
    /// (<c>tenant_id</c>, <c>key_result_id</c>, <c>mes</c>, <c>valor</c>, <c>registrado_por</c>,
    /// <c>updated_at</c>).
    /// </summary>
    [Fact]
    public async Task UpsertAsync_MesNuevo_InsertaFila()
    {
        CrearRepositorio();
        var tabla = TablaValor();
        AgregarFila(tabla, KrId1, 6, 0.7m);
        _conexion.Filas = tabla;

        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.7m, UsuarioId, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO valor_mensual_kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(tenant_id, key_result_id, mes, valor, registrado_por, updated_at)", sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES (@TenantId, @KeyResultId, @Mes, @Valor, @UsuarioId, CURRENT_TIMESTAMP)", sql,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(KrId1, Ultima.Parametros["KeyResultId"]);
        Assert.Equal(6, Ultima.Parametros["Mes"]);
        Assert.Equal(0.7m, Ultima.Parametros["Valor"]);
        Assert.Equal(UsuarioId, Ultima.Parametros["UsuarioId"]);
    }

    /// <summary>
    /// #78 · Un mes existente entra por la rama <c>DO UPDATE SET</c>, que pisa
    /// <c>valor</c>, <c>registrado_por</c> y <c>updated_at</c> (y <b>no</b> crea una segunda fila).
    /// </summary>
    [Fact]
    public async Task UpsertAsync_MesExistente_ActualizaValorYRegistradoPor()
    {
        CrearRepositorio();
        var tabla = TablaValor();
        AgregarFila(tabla, KrId1, 6, 0.9m);
        _conexion.Filas = tabla;

        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.9m, UsuarioId, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("DO UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET valor = EXCLUDED.valor", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registrado_por = EXCLUDED.registrado_por", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at = CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #79 · RC-07: dos guardados del mismo mes ejecutan la <b>misma</b> sentencia idempotente.
    /// La unicidad la garantiza el <c>UNIQUE (key_result_id, mes)</c> del DDL base y la Lindsey
    /// <c>ON CONFLICT</c> de la query — ver la nota de la cabecera: sin arnés no se puede contar
    /// filas, se verifica el mecanismo.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_DosLlamadasMismoMes_NoDuplicaFila()
    {
        CrearRepositorio();

        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.5m, UsuarioId, null, CancellationToken.None);
        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.7m, UsuarioId, null, CancellationToken.None);

        Assert.Equal(2, _conexion.Consultas.Count);
        var primera = Normalizar(_conexion.Consultas[0].Sql);
        var segunda = Normalizar(_conexion.Consultas[1].Sql);
        Assert.Equal(primera, segunda);
        Assert.Contains("ON CONFLICT (key_result_id, mes)", primera, StringComparison.OrdinalIgnoreCase);

        // Las dos llamadas llevan el MISMO (key_result_id, mes) → misma clave de conflicto.
        Assert.Equal(KrId1, _conexion.Consultas[0].Parametros["KeyResultId"]);
        Assert.Equal(6, _conexion.Consultas[1].Parametros["Mes"]);
        Assert.Equal(0.7m, _conexion.Consultas[1].Parametros["Valor"]);
    }

    /// <summary>
    /// #80 · DB-06: <b>ON CONFLICT (key_result_id, mes) DO UPDATE</b> sobre el UNIQUE del DDL,
    /// y ninguna sentencia <c>SELECT</c> previa (prohibido el SELECT+UPDATE separados).
    /// </summary>
    [Fact]
    public async Task UpsertAsync_UsaOnConflictDelUnique()
    {
        CrearRepositorio();

        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.7m, UsuarioId, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("ON CONFLICT (key_result_id, mes) DO UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXCLUDED.valor", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #81 · SEC-05 / STACK-03: el valor viaja como <b>parámetro nombrado</b>. Lo observable en
    /// runtime no es el «+» del código sino que <b>ningún literal de valor aparece en el texto
    /// SQL</b>: si la query concatenara, 0.7 se filtraría al SQL y este caso lo detectaría.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_ParametrosParametrizados_NoConcatenaValores()
    {
        CrearRepositorio();

        await Sut.UpsertAsync(TenantId, KrId1, 6, 0.7m, UsuarioId, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.DoesNotContain("0.7", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(KrId1.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UsuarioId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Valor", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.7m, Ultima.Parametros["Valor"]);
    }

    // ═══════════════════════════ 82-83 · ActualizarPuntuacionesKrAsync (DB-04) ═══════════════════════════

    /// <summary>
    /// #82 · DAL-4 · Las <b>6</b> columnas calculadas se escriben desde la BLL (DB-04, sin
    /// trigger): <c>puntuacion_q1..q4</c>, <c>puntuacion_final</c> y <c>puntuacion_ponderada</c>.
    /// El gate <c>okr_id = @OkrId</c> también va en el UPDATE (SEC-07).
    /// </summary>
    [Fact]
    public async Task ActualizarPuntuacionesKrAsync_EscribeLasSeisColumnas()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 1;

        var afectadas = await Sut.ActualizarPuntuacionesKrAsync(
            TenantId, OkrId, KrId1, 0.700m, 0.800m, 0.900m, 1.000m, 0.850m, 0.340m, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("UPDATE key_result", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_q1 = @PuntuacionQ1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_q2 = @PuntuacionQ2", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_q3 = @PuntuacionQ3", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_q4 = @PuntuacionQ4", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_final = @PuntuacionFinal", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_ponderada = @PuntuacionPonderada", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE id = @KeyResultId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0.700m, Ultima.Parametros["PuntuacionQ1"]);
        Assert.Equal(0.850m, Ultima.Parametros["PuntuacionFinal"]);
        Assert.Equal(0.340m, Ultima.Parametros["PuntuacionPonderada"]);
        Assert.Equal(1, afectadas);
    }

    /// <summary>
    /// #83 · Un KR de otro OKR no se toca: el <c>AND okr_id = @OkrId</c> del UPDATE deja 0 filas
    /// afectadas y la BLL debe detectarlo (o, en este caso, simplemente no escribir nada).
    /// </summary>
    [Fact]
    public async Task ActualizarPuntuacionesKrAsync_KrDeOtroOkr_NoAfectaFilas()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 0;   // el WHERE no matchea

        var afectadas = await Sut.ActualizarPuntuacionesKrAsync(
            TenantId, OkrId, KrId1, 0.700m, 0.000m, 0.000m, 0.000m, 0.700m, 0.280m, null, CancellationToken.None);

        Assert.Equal(0, afectadas);
        Assert.Contains("okr_id = @OkrId", Normalizar(Ultima.Sql), StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════ 84-85 · ActualizarPuntuacionOkrAsync (extensión de IOkrRepository) ═══════════════════════

    /// <summary>
    /// #84 · DAL-5 · Escribe <c>okr.puntuacion_final</c> y <c>okr.semaforo</c> (RF-038 / RN-027).
    /// El semáforo necesita el cast explícito a <c>semaforo_color</c>: sin él Npgsql no puede
    /// resolver el enum (mismo motivo que <c>@Accion::accion_auditoria</c>).
    /// </summary>
    [Fact]
    public async Task ActualizarPuntuacionOkrAsync_EscribePuntuacionYSemaforo()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 1;

        var afectadas = await SutOkr.ActualizarPuntuacionOkrAsync(
            TenantId, AreaId, OkrId, 0.850m, "Amarillo", null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("UPDATE okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puntuacion_final = @PuntuacionFinal", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semaforo = @Semaforo::semaforo_color", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0.850m, Ultima.Parametros["PuntuacionFinal"]);
        Assert.Equal("Amarillo", Ultima.Parametros["Semaforo"]);
        Assert.Equal(AreaId, Ultima.Parametros["AreaId"]);
        Assert.Equal(1, afectadas);
    }

    /// <summary>
    /// #85 · SEC-07 en el UPDATE del OKR: un OKR de <b>otra área</b> del mismo tenant no se
    /// modifica — el <c>AND area_id = @AreaId</c> deja 0 filas.
    /// </summary>
    [Fact]
    public async Task ActualizarPuntuacionOkrAsync_OkrDeOtraArea_NoAfectaFilas()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 0;

        var afectadas = await SutOkr.ActualizarPuntuacionOkrAsync(
            TenantId, AreaId, OkrId, 0.850m, "Verde", null, CancellationToken.None);

        Assert.Equal(0, afectadas);
        Assert.Contains("area_id = @AreaId", Normalizar(Ultima.Sql), StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════ 86 · EliminarAsync ═══════════════════════════

    /// <summary>
    /// #86 · DAL-7 · El DELETE necesita el gate de OKR, así que va con <c>USING key_result kr</c>
    /// (no basta el <c>tenant_id</c> de la fila): sin él se podrían borrar valores de otro OKR.
    /// </summary>
    [Fact]
    public async Task EliminarAsync_Existing_MesEliminado()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 1;

        var afectadas = await Sut.EliminarAsync(TenantId, OkrId, KrId1, 2, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("DELETE FROM valor_mensual_kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING key_result kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.key_result_id = kr.id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.key_result_id = @KeyResultId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.mes = @Mes", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(KrId1, Ultima.Parametros["KeyResultId"]);
        Assert.Equal(2, Ultima.Parametros["Mes"]);
        Assert.Equal(1, afectadas);
    }

    // ═══════════════════════════ 87 · InsertLogAsync (ADR-003) ═══════════════════════════

    /// <summary>
    /// #87 · DAL-8 · Una sola entrada por operación (F5) en <c>log_auditoria</c>, con el detalle
    /// JSON del vector. Los JSON van como <c>jsonb</c> <b>explícito</b>: Npgsql rechaza
    /// <c>IDbDataParameter</c> y <c>DBNull</c> en una columna jsonb (ADR-003).
    /// </summary>
    [Fact]
    public async Task InsertLogAsync_DetalleJson_SePersiste()
    {
        CrearRepositorio();
        _conexion.FilasAfectadas = 1;
        var detalle = """{"krId":"00000000-0000-0000-0000-000000000006","codigo":"KR.1","meses":{"1":0.5,"3":null,"6":0.7}}""";

        var afectadas = await Sut.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId      = TenantId,
            UsuarioId     = UsuarioId,
            Accion        = "UPDATE",
            Entidad       = "ValorMensualKR",
            EntidadId     = KrId1.ToString(),
            ValorAnterior = detalle,
            ValorNuevo    = detalle
        }, null, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("INSERT INTO log_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Accion::accion_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NpgsqlDbType.Jsonb", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ValorAnterior::jsonb", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("UPDATE", Ultima.Parametros["Accion"]);
        Assert.Equal("ValorMensualKR", Ultima.Parametros["Entidad"]);
        Assert.Equal(KrId1.ToString(), Ultima.Parametros["EntidadId"]);
        Assert.Equal(detalle, Ultima.Parametros["ValorAnterior"]);
        Assert.Equal(1, afectadas);
    }
}

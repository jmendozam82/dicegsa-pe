using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Moq;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DAL.Repositories.Objetivos;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-022 — casos <b>52-57</b> (spec v3 § "Tests requeridos"), los 6
/// unitarios de la capa DAL. Escritos ANTES de la implementación (TEST-01): <b>el rojo legítimo es
/// el fallo de compilación</b> (falta <c>EntregableAdjuntoRepository</c>, <c>IEntregableAdjuntoRepository</c> y
/// <c>EntregableAdjuntoEntity</c>). Misma convención que AccionPlanGanttServiceTests (HU-021).
/// <para>
/// <b>Por qué NO hay arnés de integración</b> (spec v2, nota «d»): <c>PE-GOL.Tests.csproj</c> no
/// referencia Npgsql y en el repo no existe <c>AccionPlanRepositoryTests</c> ni
/// <c>PilarRepositoryTests</c>. Por eso estos 6 tests son UNITARIOS sobre el <b>SQL capturado</b>
/// con un doble escrito a mano, no smoke tests contra Supabase. Ni AutoMoq (no está referenciado)
/// ni red: <c>Mock&lt;IDbConnectionFactory&gt;</c> + dobles de <c>System.Data.Common</c>.
/// </para>
/// <para>
/// <b>CONTRATO QUE @BackendDev DEBE IMPLEMENTAR</b> (sin esto el archivo no compila):
/// <list type="number">
///   <item><c>PE-GOL.DAL/Interfaces/IEntregableAdjuntoRepository.cs</c> →
///     <c>namespace PE_GOL.DAL.Interfaces</c> (<b>PLANO</b>, corrección 3.a de la v3).</item>
///   <item><c>PE-GOL.DAL/Repositories/Objetivos/EntregableAdjuntoRepository.cs</c> →
///     <c>namespace PE_GOL.DAL.Repositories.Objetivos</c> (esa carpeta SÍ existe), ctor
///     <b><c>(IDbConnectionFactory)</c></b> — <b>SOLO</b> un argumento (bloqueo 5).</item>
///   <item><c>PE-GOL.Entity/PlanOperativo/EntregableAdjuntoEntity.cs</c> →
///     <c>namespace PE_GOL.Entity.PlanOperativo</c>, con
///     <c>Id, TenantId, AccionId, NombreArchivo, FilePath, FileSizeBytes (long), TipoMime,
///     SubidoPor, CreatedAt (DateTime), SubidoPorNombre (string?)</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>LOS 5 BLOQUEOS DE LA v3 QUE ESTOS 6 TESTS FIJAN</b> (los resolvió la revisión v3 del spec,
/// 2026-09-27, exactamente porque esta fase roja los encontró):
/// </para>
/// <para>
/// <b>B-2 · <c>EliminarAsync</c> devuelve <c>Task&lt;int&gt;</c> (filas afectadas), no <c>Task</c>.</b>
/// La interfaz y el <c>DELETE</c> se contradecían en el spec v2, lo que hacía IMPOSIBLE implementar
/// la regla de la carrera que el propio spec exigía. El caso 56 se llama ahora
/// <c>…_EliminarAsync_Existente_EliminaYRetornaUno</c> y afirma el <b>1</b> devuelto (con
/// <c>ExecuteNonQuery</c> → 0 devolvería 0 y la BLL respondería 404).
/// </para>
/// <para>
/// <b>B-5 · <c>ILogger</c> DESAPARECE del constructor.</b> El spec v2 pedía
/// <c>ILogger&lt;EntregableAdjuntoRepository&gt;</c> «para los warnings de Dapper», pero los
/// <b>9 repositorios</b> del repo (<c>CicloRepository.cs:28</c>, <c>ObjetivoCgRepository.cs:32</c>,
/// <c>AccionPlanRepository.cs:19</c>, <c>HistorialProgresoRepository.cs:17</c>, <c>AuthRepository.cs:25</c>,
/// <c>LogAuditoriaRepository.cs:24</c>, <c>PlanRepository.cs:33</c>, <c>TenantRepository.cs:23</c>,
/// <c>UsuarioRepository.cs:34</c>) reciben <b>únicamente</b> <c>IDbConnectionFactory</c>. El logging vive en
/// BLL/Services (STACK-11). <c>CrearRepositorio()</c> lo construye con UN argumento.
/// </para>
/// <para>
/// <b>B-6 · El caso 57 afirma sobre los <b>6</b> SQL capturados, no sobre 4.</b> La numeración 52-56
/// ejercita 5 métodos y <c>ContarPorAccionAsync</c> es un 6º con <c>tenant_id = @TenantId</c> igual que
/// todos. Afirmar sobre 4 dejaría queries sin comprobar; <c>Assert.All</c> sobre TODAS es
/// estrictamente más fuerte y no depende de una cuenta que el spec no cerraba.
/// </para>
/// <para>
/// <b>B-7 · El caso 54 se titula <c>…EnviaLosOchoParametrosYOmiteCreatedAt</c></b> (antes «Nueve»,
/// nombre que mentía). El <c>INSERT</c> tiene <b>8</b> columnas de escritura:
/// <c>created_at</c> lo fija el <c>DEFAULT now()</c> del DDL y <c>subido_por_nombre</c> es la columna
/// del <c>LEFT JOIN</c>, no de la tabla. El test comprueba los 8 uno a uno (con su valor) y la
/// AUSENCIA de ambos.
/// </para>
/// <para>
/// <b>Sobre la prueba de «sin concatenación» (STACK-03 / SEC-05):</b> el «+» o el
/// <c>$"…{valor}"</c> no son observables en runtime. Lo que SÍ es observable —y es la prueba real
/// de que el valor viaja como parámetro nombrado— es que <b>el GUID no aparezca nunca dentro del
/// texto SQL</b>. Si la query se construyera concatenando, el id se filtraría al texto y estos
/// tests lo detectarían. Por eso el caso 57 afirma, para cada SQL capturado, que contiene
/// <c>tenant_id = @TenantId</c> y que NO contiene ninguno de los tres GUIDs literales.
/// </para>
/// </summary>
public class EntregableAdjuntoRepositoryTests
{
    private static readonly Guid TenantId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AccionId     = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid EntregableId = Guid.Parse("00000000-0000-0000-0000-000000000040");
    private static readonly Guid UsuarioId    = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid OtraAccionId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private ConexionCapturadora _conexion = null!;
    private EntregableAdjuntoRepository _sut = null!;

    /// <summary>
    /// Arrange común: la fábrica devuelve SIEMPRE la misma conexión capturadora (el repositorio
    /// cachea una conexión, pero devolver la misma hace el test inmune a ese detalle). El ctor
    /// recibe UN SOLO argumento —<c>IDbConnectionFactory</c>— sin <c>ILogger</c> (bloqueo 5 de la v3):
    /// los 9 repositorios del repo reciben solo la fábrica y el logging vive en BLL/Services.
    /// </summary>
    private void CrearRepositorio()
    {
        _conexion = new ConexionCapturadora();
        var factory = new Mock<IDbConnectionFactory>();
        factory.Setup(f => f.CreateConnection()).Returns(() => _conexion);

        _sut = new EntregableAdjuntoRepository(factory.Object);
    }

    /// <summary>El último SQL ejecutado, con el texto normalizado (blancos colapsados a 1 espacio).</summary>
    private ConsultaSql Ultima => _conexion.Consultas[^1];

    /// <summary>
    /// Normaliza el SQL para que las afirmaciones no dependan del indentado ni de los saltos de
    /// línea que elija el implementador, pero sí del contenido (STACK-03 exige parametrizado, no
    /// una cadena de formato concreta).
    /// </summary>
    private static string Normalizar(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    private static EntregableAdjuntoEntity CrearEntidad() => new()
    {
        Id = EntregableId,
        TenantId = TenantId,
        AccionId = AccionId,
        NombreArchivo = "informe.pdf",
        FilePath = $"{TenantId}/ciclo/accion/{EntregableId}.pdf",
        FileSizeBytes = 12_345,
        TipoMime = "application/pdf",
        SubidoPor = UsuarioId,
        CreatedAt = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc),
        SubidoPorNombre = "Jefe de Área"
    };

    // ─────────────────────────── 52 · ListarPorAccionAsync ─────────────────────────────────

    [Fact]
    public async Task EntregableAdjuntoRepository_ListarPorAccionAsync_AnexaTenantIdYAccionId()
    {
        // Arrange
        CrearRepositorio();

        // Act
        var filas = await _sut.ListarPorAccionAsync(AccionId, TenantId, CancellationToken.None);

        // Assert: la consulta es un SELECT con el LEFT JOIN y las TRES cláusulas que exige el caso
        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM entregable_adjunto", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN usuario", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accion_id = @AccionId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY e.created_at ASC, e.nombre_archivo ASC", sql, StringComparison.OrdinalIgnoreCase);

        // Los 2 ids viajan como PARÁMETRO NOMBRADO con el valor recibido (SEC-05, sin concatenar)
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AccionId, Ultima.Parametros["AccionId"]);

        // La doble no simula red: 0 filas
        Assert.Empty(filas);
    }

    // ─────────────────────────── 53 · ObtenerPorIdAsync ──────────────────────────────────

    [Fact]
    public async Task EntregableAdjuntoRepository_ObtenerPorIdAsync_ExigeAccionYEntregable_AnadeFiltro()
    {
        // Arrange: el adjunto se pide con una acción que NO es la suya → 0 filas
        CrearRepositorio();

        // Act
        var fila = await _sut.ObtenerPorIdAsync(EntregableId, OtraAccionId, TenantId, CancellationToken.None);

        // Assert: el SQL filga por LAS TRES columnas, no solo por el id
        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accion_id = @AccionId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id = @EntregableId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN usuario", sql, StringComparison.OrdinalIgnoreCase);

        // Protección contra ids cruzados: el accion_id DESCONOCIDO viaja como parámetro…
        Assert.Equal(OtraAccionId, Ultima.Parametros["AccionId"]);
        Assert.Equal(EntregableId, Ultima.Parametros["EntregableId"]);

        // …y al no haber fila, se devuelve null (el BLL lo traduce a 404, nunca a 403)
        Assert.Null(fila);
    }

    // ─────────────────────────── 54 · InsertarAsync · BLOQUEO 7 ───────────────────────────

    /// <summary>
    /// El nombre decía «NueveParametros» y mentía: el INSERT tiene <b>8</b> columnas de escritura.
    /// <c>created_at</c> lo fija el <c>DEFAULT now()</c> del DDL y <c>subido_por_nombre</c> es la
    /// columna del <c>LEFT JOIN</c>, no de la tabla (bloqueo 7 de la v3).
    /// </summary>
    [Fact]
    public async Task EntregableAdjuntoRepository_InsertarAsync_EnviaLosOchoParametrosYOmiteCreatedAt()
    {
        // Arrange: una transacción real (falsa) es lo que exige la firma
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        // Act
        await _sut.InsertarAsync(CrearEntidad(), tx, CancellationToken.None);

        // Assert: INSERT en la tabla correcta, parametrizado
        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO entregable_adjunto", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);

        // Los 8 campos de escritura, con su valor (DB-04: created_at lo pone el DDL)
        Assert.Equal(8, Ultima.Parametros.Count);
        Assert.Equal(EntregableId, Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AccionId, Ultima.Parametros["AccionId"]);
        Assert.Equal("informe.pdf", Ultima.Parametros["NombreArchivo"]);
        Assert.Equal($"{TenantId}/ciclo/accion/{EntregableId}.pdf", Ultima.Parametros["FilePath"]);
        Assert.Equal(12_345L, Ultima.Parametros["FileSizeBytes"]);
        Assert.Equal("application/pdf", Ultima.Parametros["TipoMime"]);
        Assert.Equal(UsuarioId, Ultima.Parametros["SubidoPor"]);

        // Ni created_at (DEFAULT now() del DDL) ni subido_por_nombre (columna del LEFT JOIN)
        Assert.DoesNotContain("created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subido_por_nombre", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(Ultima.Parametros.ContainsKey("CreatedAt"), "created_at NO se inserta: lo fija el DDL.");
        Assert.False(Ultima.Parametros.ContainsKey("SubidoPorNombre"), "SubidoPorNombre es del LEFT JOIN, no una columna.");

        // La transacción se propaga al comando (corrección 1: la fila y su log son atómicos)
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 55 · InsertLogAsync ─────────────────────────────────────

    [Fact]
    public async Task EntregableAdjuntoRepository_InsertLogAsync_UsaLosSieteCamposYLaMismaTransaccion()
    {
        // Arrange
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();
        var log = new LogAuditoriaInsert
        {
            TenantId = TenantId,
            UsuarioId = UsuarioId,
            Accion = "CREATE",
            Entidad = "EntregableAdjunto",
            EntidadId = EntregableId.ToString(),
            ValorAnterior = null,
            ValorNuevo = "{\"nombreArchivo\":\"informe.pdf\",\"fileSizeBytes\":12345}"
        };

        // Act
        await _sut.InsertLogAsync(log, tx, CancellationToken.None);

        // Assert: exactamente los 7 campos de LogAuditoriaInsert (spec L765)
        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO log_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, Ultima.Parametros.Count);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(UsuarioId, Ultima.Parametros["UsuarioId"]);
        Assert.Equal("CREATE", Ultima.Parametros["Accion"]);
        Assert.Equal("EntregableAdjunto", Ultima.Parametros["Entidad"]);
        Assert.Equal(EntregableId.ToString(), Ultima.Parametros["EntidadId"]);
        // Un CREATE no tiene estado anterior. Dapper traduce el null de C# a DBNull.Value —que es
        // lo que espera el provider ADO.NET— así que lo que se afirma es que NO viaja un JSON,
        // no que la clave falte (la clave existe: son 7 parámetros, y esta es la 6.ª).
        Assert.True(Ultima.Parametros["ValorAnterior"] is null or DBNull,
            $"valor_anterior debe ser SQL NULL en un CREATE, pero viaja: {Ultima.Parametros["ValorAnterior"]}");
        Assert.Contains("fileSizeBytes", (string)Ultima.Parametros["ValorNuevo"]!, StringComparison.Ordinal);

        // La tabla NO tiene estas columnas (nota «b» del encabezado del spec)
        Assert.DoesNotContain("fecha", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resultado", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mensaje", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accion_id", sql, StringComparison.OrdinalIgnoreCase);

        // La tx recibida llega a DbCommand.Transaction: es lo que prueba la corrección 1 en DAL
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 56 · EliminarAsync · BLOQUEO 2 ───────────────────────────

    [Fact]
    public async Task EntregableAdjuntoRepository_EliminarAsync_Existente_EliminaYRetornaUno()
    {
        // Arrange
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();
        _conexion.FilasAfectadas = 1;

        // Act
        var afectadas = await _sut.EliminarAsync(EntregableId, AccionId, TenantId, tx, CancellationToken.None);

        // Assert: DELETE acotado por tenant + acción + id
        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("DELETE FROM entregable_adjunto", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accion_id = @AccionId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id = @EntregableId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AccionId, Ultima.Parametros["AccionId"]);
        Assert.Equal(EntregableId, Ultima.Parametros["EntregableId"]);

        // Devuelve las FILAS AFECTADAS (bloqueo 2: Task<int> en la interfaz) y propaga la tx.
        // Con ExecuteNonQuery → 0 (carrera) devolvería 0 y la BLL respondería 404.
        Assert.Equal(1, afectadas);
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────── 57 · las 6 consultas, todas con tenant_id · BLOQUEO 6 ─────────────

    [Fact]
    public async Task EntregableAdjuntoRepository_ListarPorAccionAsync_SqlContieneTenantId_EnTodosLos6()
    {
        // Arrange
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        // Act: se ejercitan los 6 métodos de IEntregableAdjuntoRepository
        await _sut.ListarPorAccionAsync(AccionId, TenantId, CancellationToken.None);
        await _sut.ContarPorAccionAsync(AccionId, TenantId, CancellationToken.None);
        await _sut.ObtenerPorIdAsync(EntregableId, AccionId, TenantId, CancellationToken.None);
        await _sut.InsertarAsync(CrearEntidad(), tx, CancellationToken.None);
        await _sut.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = TenantId,
            UsuarioId = UsuarioId,
            Accion = "DELETE",
            Entidad = "EntregableAdjunto",
            EntidadId = EntregableId.ToString(),
            ValorAnterior = "{}",
            ValorNuevo = null
        }, tx, CancellationToken.None);
        await _sut.EliminarAsync(EntregableId, AccionId, TenantId, tx, CancellationToken.None);

        // Assert: los 6 SQL se capturaron
        Assert.Equal(6, _conexion.Consultas.Count);

        // DB-03 + SEC-06: TODOS filtran por tenant_id con PARÁMETRO NOMBRADO
        Assert.All(_conexion.Consultas, c =>
            Assert.Contains("tenant_id = @TenantId", Normalizar(c.Sql), StringComparison.OrdinalIgnoreCase));

        // STACK-03 + SEC-05: ningún id viaja dentro del texto SQL. Si alguien concatenara
        // (`$"...{id}"`, `+` o `string.Format`), el GUID aparecería en el texto y esto falla.
        Assert.All(_conexion.Consultas, c =>
        {
            var sql = Normalizar(c.Sql);
            Assert.DoesNotContain(TenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AccionId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(EntregableId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        });

        // Y el aislamiento multi-tenant es estructural: 4 de las 6 consultas llevan tenant_id
        // como PARÁMETRO con el tenant del llamante (no un literal), lo que la RLS no puede dar.
        Assert.All(_conexion.Consultas.Where(c => Normalizar(c.Sql).Contains("= @TenantId")), c =>
            Assert.Equal(TenantId, c.Parametros["TenantId"]));
    }
}

// ══════════════════════════════ DOBLES DE System.Data.Common ═══════════════════════════════
// Escritos a mano (NO AutoMoq, NO red). El repo NO tiene arnés de integración
// (PE-GOL.Tests.csproj no referencia Npgsql y no existen tests de repositorio previos), así que
// estos dobles son lo que permite afirmar sobre el SQL real que produce Dapper.

/// <summary>Un SQL ejecutado: su texto, sus parámetros y la transacción que recibió.</summary>
internal sealed class ConsultaSql
{
    public ConsultaSql(string sql, IReadOnlyDictionary<string, object?> parametros, IDbTransaction? transaccion)
    {
        Sql = sql;
        Parametros = parametros;
        Transaccion = transaccion;
    }

    public string Sql { get; }
    public IReadOnlyDictionary<string, object?> Parametros { get; }
    public IDbTransaction? Transaccion { get; }
}

/// <summary>
/// <see cref="DbConnection"/> de mentira: registra cada <see cref="DbCommand"/> ejecutado y
/// devuelve filas de mentira. Se declara <see cref="ConnectionState"/> Open porque el patrón de
/// <c>BeginTransactionAsync</c> del repo comprueba <c>conn.State != ConnectionState.Open</c> antes
/// de abrir.
/// <para>
/// Nota de compilación: <see cref="DbConnection.State"/> es <b>abstracto</b> en .NET 8 (no una
/// propiedad virtual que delegue en una protegida), así que el ejemplo del spec (L1003-1029) no
/// compila tal cual y aquí se implementa como override de solo lectura. Lo mismo con
/// <see cref="DbCommand.DesignTimeVisible"/> / <see cref="DbCommand.UpdatedRowSource"/> y con
/// <c>DbParameterCollection.Insert(int, object)</c>: los tres son abstractos.
/// </para>
/// </summary>
internal sealed class ConexionCapturadora : DbConnection
{
    /// <summary>Un registro por consulta ejecutada, en orden.</summary>
    public List<ConsultaSql> Consultas { get; } = new();

    /// <summary>Filas que devuelve el próximo lector (0 filas por defecto: no hay red).</summary>
    public DataTable Filas { get; set; } = CrearTabla();

    /// <summary>Valor que devuelve <see cref="ExecuteScalar"/> (para el COUNT).</summary>
    public object? ValorEscalar { get; set; } = 0;

    /// <summary>Filas afectadas que devuelve <see cref="ExecuteNonQuery"/>.</summary>
    public int FilasAfectadas { get; set; } = 1;

    // [AllowNull] en los 4 setters de string: es la anotación con la que el BCL declara estas
    // propiedades (getter no anulable, setter que admite null). Sin ella, el doble genera CS8764/CS8765.
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get => "fake"; set { } }
    public override string Database => "fake";
    public override string DataSource => "fake";
    public override string ServerVersion => "15.0";
    public override ConnectionState State { get; } = ConnectionState.Open;

    public override void ChangeDatabase(string database) { }
    public override void Open() { }
    public override void Close() { }

    protected override DbCommand CreateDbCommand() => new ComandoCapturador(this);

    protected override DbTransaction BeginDbTransaction(IsolationLevel il) => new TransaccionFalsa(this, il);

    /// <summary>Transacción falsa respaldada por ESTA conexión (Dapper la propaga a DbCommand).</summary>
    public TransaccionFalsa BeginTransactionFalsa() => new(this, IsolationLevel.ReadCommitted);

    /// <summary>DataTableReader: es lo que consume la parte de lectura de Dapper.</summary>
    internal DbDataReader CrearReader() => Filas.CreateDataReader();

    /// <summary>
    /// Tabla con la proyección del SELECT de adjuntos y 0 filas. Las columnas se declaran para que
    /// <c>FieldCount &gt; 0</c> y Dapper no entre por su rama especial de "comando que no devuelve
    /// nada" (que solo aplica con <c>FieldCount == 0</c>).
    /// </summary>
    private static DataTable CrearTabla()
    {
        var tabla = new DataTable();
        tabla.Columns.Add("id", typeof(Guid));
        tabla.Columns.Add("tenant_id", typeof(Guid));
        tabla.Columns.Add("accion_id", typeof(Guid));
        tabla.Columns.Add("nombre_archivo", typeof(string));
        tabla.Columns.Add("file_path", typeof(string));
        tabla.Columns.Add("file_size_bytes", typeof(long));
        tabla.Columns.Add("tipo_mime", typeof(string));
        tabla.Columns.Add("subido_por", typeof(Guid));
        tabla.Columns.Add("created_at", typeof(DateTime));
        tabla.Columns.Add("subido_por_nombre", typeof(string));
        return tabla;
    }
}

/// <summary>
/// <see cref="DbCommand"/> de mentira: Dapper le pone el <c>CommandText</c> y le añade sus
/// <c>SqlParameter</c> (que implementan <see cref="IDbDataParameter"/>, NO derivan de
/// <see cref="DbParameter"/>), y al ejecutar se registra texto + parámetros + transacción.
/// Las sobrecargas <c>…Async</c> de <see cref="DbCommand"/> delegan en las síncronas, así que
/// con sobrescribir estas tres basta para el camino asíncrono de Dapper.
/// </summary>
internal sealed class ComandoCapturador : DbCommand
{
    private readonly ConexionCapturadora _conexion;
    private readonly ParametroColeccion _parametros = new();

    public ComandoCapturador(ConexionCapturadora conexion) => _conexion = conexion;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parametros;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new ParametroCapturador();

    public override int ExecuteNonQuery()
    {
        Registrar();
        return _conexion.FilasAfectadas;
    }

    public override object? ExecuteScalar()
    {
        Registrar();
        return _conexion.ValorEscalar;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Registrar();
        return _conexion.CrearReader();
    }

    /// <summary>Captura el estado del comando en el momento de ejecutarlo.</summary>
    private void Registrar() => _conexion.Consultas.Add(
        new ConsultaSql(CommandText, _parametros.Capturar(), DbTransaction));
}

/// <summary>
/// <see cref="DbTransaction"/> falsa. Expone <c>DbConnection</c> REAL (no null) a propósito:
/// Dapper asigna la transacción al comando y algunas versiones validan que su conexión sea la
/// misma; dársela de verdad hace el test inmune a esa comprobación sin perder lo que se afirma
/// (que la tx llega a <c>DbCommand.Transaction</c>).
/// </summary>
internal sealed class TransaccionFalsa : DbTransaction
{
    private readonly DbConnection _conexion;

    public TransaccionFalsa(DbConnection conexion, IsolationLevel nivel)
    {
        _conexion = conexion;
        IsolationLevel = nivel;
    }

    public override IsolationLevel IsolationLevel { get; }
    protected override DbConnection DbConnection => _conexion;

    public bool Commiteada { get; private set; }
    public bool Revertida { get; private set; }

    public override void Commit() => Commiteada = true;
    public override void Rollback() => Revertida = true;
}

/// <summary>Parámetro de mentira (lo usa el doble si alguien llama a <c>CreateDbParameter</c>).</summary>
internal sealed class ParametroCapturador : DbParameter
{
    public override DbType DbType { get; set; } = DbType.String;
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }

    public override void ResetDbType() => DbType = DbType.String;
}

/// <summary>
/// Colección de parámetros que acepta lo que Dapper mete: sus <c>SqlParameter</c> son
/// <see cref="IDbDataParameter"/> y <b>no</b> derivan de <see cref="DbParameter"/>, así que
/// <see cref="Add"/> guarda <c>object</c> y <see cref="Capturar"/> proyecta nombre/valor leyendo
/// la interfaz que Dapper sí implementa.
/// </summary>
internal sealed class ParametroColeccion : DbParameterCollection
{
    private readonly List<object> _items = new();

    /// <summary>Proyecta lo capturado a <c>nombre → valor</c> (sin distinguir mayúsculas).</summary>
    public IReadOnlyDictionary<string, object?> Capturar()
    {
        var mapa = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            if (item is IDbDataParameter p)
                mapa[p.ParameterName ?? string.Empty] = p.Value;
        }
        return mapa;
    }

    public override int Count => _items.Count;
    public override object SyncRoot { get; } = new();
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;

    public override int Add(object value)
    {
        _items.Add(value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var v in values) Add(v!);
    }

    public override void Clear() => _items.Clear();

    public override bool Contains(object value) => _items.Contains(value);

    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(object value) => _items.IndexOf(value);

    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _items.Count; i++)
            if (_items[i] is IDbDataParameter p &&
                string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    public override void Insert(int index, object value) => _items.Insert(index, value);
    public override void Remove(object value) => _items.Remove(value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var i = IndexOf(parameterName);
        if (i >= 0) _items.RemoveAt(i);
    }

    protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];

    protected override DbParameter GetParameter(string parameterName)
        => (DbParameter)_items[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
        => _items[IndexOf(parameterName)] = value;
}

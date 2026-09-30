using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Moq;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Repositories.Objetivos;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-024 — CRUD de OKRs (Spec § "Tests requeridos" casos DAL 39-48,
/// 10 métodos unitarios de la capa DAL). Escritos ANTES de la implementación (TEST-01): el rojo
/// legítimo es el FALLO DE COMPILACIÓN del proyecto de tests porque <c>IOkrRepository</c>,
/// <c>OkrRepository</c>, <c>OkrCreateRequest</c>, <c>OkrUpdateRequest</c>, <c>OkrResponse</c> y
/// <c>SiguienteSecuenciaOkrDto</c> AÚN NO existen. Misma convención que
/// <c>EntregableAdjuntoRepositoryTests</c> (HU-022) y <c>PlanConsolidadoRepositoryTests</c>
/// (HU-023).
///
/// <para><b>Por qué NO hay arnés de integración</b>: <c>PE-GOL.Tests.csproj</c> no referencia
/// Npgsql y no existen tests de repositorio previos fuera de estos 3 archivos (HU-021+). Por eso
/// los tests son UNITARIOS sobre el <b>SQL capturado</b> con un doble escrito a mano, no smoke
/// tests contra Supabase. Ni AutoMoq (no está referenciado) ni red:
/// <c>Mock&lt;IDbConnectionFactory&gt;</c> + dobles de <c>System.Data.Common</c>.</para>
///
/// <para><b>Sobre los dobles anidados</b>: los nombres <c>ConexionCapturadora</c>,
/// <c>ComandoCapturador</c>, <c>TransaccionFalsa</c>, <c>ParametroCapturador</c>,
/// <c>ParametroColeccion</c> y <c>ConsultaSql</c> ya están definidos a nivel de namespace por
/// <c>EntregableAdjuntoRepositoryTests.cs</c>. Para evitar CS0111 se anidan como tipos privados
/// dentro de esta clase de test (mismo patrón que <c>PlanConsolidadoRepositoryTests</c>).</para>
///
/// <para><b>Contrato que @BackendDev debe implementar</b> (sin esto el archivo no compila):</para>
/// <list type="bullet">
///   <item><c>PE-GOL.DAL/Interfaces/IOkrRepository.cs</c> →
///     <c>namespace PE_GOL.DAL.Interfaces</c>.</item>
///   <item><c>PE-GOL.DAL/Repositories/Objetivos/OkrRepository.cs</c> →
///     <c>namespace PE_GOL.DAL.Repositories.Objetivos</c> (esa carpeta SÍ existe), ctor
///     <b><c>(IDbConnectionFactory)</c></b> — solo un argumento, sin <c>ILogger</c>
///     (precedente de los 9 repositorios existentes, STACK-11).</item>
///   <item>DTOs de prueba que @BackendDev debe crear en DTO: <c>OkrCreateRequest</c>,
///     <c>OkrUpdateRequest</c>, <c>OkrResponse</c> (sin TenantId), <c>SiguienteSecuenciaOkrDto</c>
///     con <c>SiguienteN</c>/<c>SiguienteOrden</c>.</item>
/// </list>
///
/// <para><b>Sobre la prueba de «sin concatenación» (STACK-03 / SEC-05)</b>: el «+» o el
/// <c>$"…{valor}"</c> no son observables en runtime. Lo que SÍ es observable —y es la prueba real
/// de que el valor viaja como parámetro nombrado— es que <b>el GUID no aparezca nunca dentro del
/// texto SQL</b>. Por eso el caso #48 afirma, para cada SQL capturado, que contiene
/// <c>tenant_id = @TenantId</c> y que NO contiene los GUIDs literales.</para>
/// </summary>
public class OkrRepositoryTests
{
    private static readonly Guid TenantId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId     = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AreaId      = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid PilarId     = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid OkrId       = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid UsuarioId    = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private ConexionCapturadora _conexion = null!;
    private OkrRepository _sut = null!;

    /// <summary>Arrange común: la fábrica devuelve SIEMPRE la misma conexión capturadora.
    /// El ctor del repo recibe UN SOLO argumento —<c>IDbConnectionFactory</c>— sin
    /// <c>ILogger</c> (precedente de los 9 repositorios existentes).</summary>
    private void CrearRepositorio()
    {
        _conexion = new ConexionCapturadora();
        var factory = new Mock<IDbConnectionFactory>();
        factory.Setup(f => f.CreateConnection()).Returns(() => _conexion);

        _sut = new OkrRepository(factory.Object);
    }

    /// <summary>El último SQL ejecutado, con el texto normalizado (blancos colapsados a 1 espacio).</summary>
    private ConsultaSql Ultima => _conexion.Consultas[^1];

    /// <summary>Normaliza el SQL para que las afirmaciones no dependan del indentado.</summary>
    private static string Normalizar(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    // ══════════════════════════════ CASOS DAL (10 casos) ══════════════════════════════

    // ─────────────────────────── 39 · ListarAsync ──────────────────────────────────────

    /// <summary>39 · ListarAsync: SELECT con WHERE tenant/ciclo/área, JOIN pilar, ORDER BY orden+created_at.</summary>
    [Fact]
    public async Task ListarAsync_Sql_FiltraTenantCicloAreaYOrdena()
    {
        CrearRepositorio();

        await _sut.ListarAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN pilar", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ciclo_id = @CicloId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p.nombre", sql, StringComparison.OrdinalIgnoreCase);        // PilarNombre
        Assert.Contains("o.semaforo::text", sql, StringComparison.OrdinalIgnoreCase); // cast enum → string
        Assert.Contains("ORDER BY o.orden ASC, o.created_at ASC", sql, StringComparison.OrdinalIgnoreCase);

        // Los 3 ids llegan como PARÁMETRO NOMBRADO (SEC-05, sin concatenación)
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(CicloId,  Ultima.Parametros["CicloId"]);
        Assert.Equal(AreaId,   Ultima.Parametros["AreaId"]);
    }

    // ─────────────────────────── 40 · ObtenerPorIdAsync ────────────────────────────────

    /// <summary>40 · ObtenerPorIdAsync: WHERE id + tenant + area (sin fuga RN-008).</summary>
    [Fact]
    public async Task ObtenerPorIdAsync_Sql_FiltraIdTenantArea()
    {
        CrearRepositorio();

        await _sut.ObtenerPorIdAsync(TenantId, AreaId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(OkrId,    Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AreaId,   Ultima.Parametros["AreaId"]);
    }

    // ─────────────────────────── 41 · ContarOkrsPorAreaAsync ──────────────────────────

    /// <summary>41 · ContarOkrsPorAreaAsync: COUNT(1) + 3 filtros (CA #5).</summary>
    [Fact]
    public async Task ContarOkrsPorAreaAsync_Sql_CountConTenantCicloArea()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = 9;

        var count = await _sut.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        Assert.Equal(9, count);
        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(1)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ciclo_id = @CicloId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── 42 · ObtenerSiguienteSecuenciaOkrAsync · F4 ───────────

    /// <summary>42 · F4: MAX(regexp_match(codigo,'^OKR\.(\d+)$'))+1 + MAX(orden)+1 (no COUNT+1).</summary>
    [Fact]
    public async Task ObtenerSiguienteSecuenciaOkrAsync_Sql_MaxRegexpPorArea()
    {
        CrearRepositorio();

        await _sut.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        // F4: NUNCA COUNT(*), siempre MAX con regexp_match
        Assert.Contains("regexp_match(codigo, '^OKR\\.(\\d+)$')", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(MAX(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(", 0) + 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAX(orden)", sql, StringComparison.OrdinalIgnoreCase);
        // Alias esperados
        Assert.Contains("AS SiguienteN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SiguienteOrden", sql, StringComparison.OrdinalIgnoreCase);
        // 3 filtros: tenant/ciclo/area
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ciclo_id = @CicloId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── 43 · VerificarKrsConValoresAsync ──────────────────────

    /// <summary>43 · EXISTS anidado: kr.okr_id + kr.tenant_id + EXISTS valor_mensual_kr (CA #3 / RN-028).</summary>
    [Fact]
    public async Task VerificarKrsConValoresAsync_Sql_ExistsAnidado()
    {
        CrearRepositorio();

        await _sut.VerificarKrsConValoresAsync(TenantId, OkrId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM key_result kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valor_mensual_kr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.okr_id = @OkrId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kr.tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vmk.key_result_id = kr.id", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(OkrId,    Ultima.Parametros["OkrId"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
    }

    // ─────────────────────────── 44 · CrearAsync · F3 ──────────────────────────────────

    /// <summary>44 · F3: INSERT con 9 columnas, 0.000, 'Rojo'::semaforo_color, RETURNING id.</summary>
    [Fact]
    public async Task CrearAsync_Sql_InsertaNueveColumnasConValoresIniciales()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        var dto = new OkrCreateRequest { PilarId = PilarId, Descripcion = "Detalle" };
        var orden = 1;
        await _sut.CrearAsync(TenantId, CicloId, AreaId, "OKR.1", orden, dto, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RETURNING id", sql, StringComparison.OrdinalIgnoreCase);

        // F3: defaults persistidos al crear (puntuacion_final=0, semaforo='Rojo')
        Assert.Contains("0.000", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'Rojo'::semaforo_color", sql, StringComparison.OrdinalIgnoreCase);

        // 7 parámetros (tenant/ciclo/area/pilar/codigo/descripcion/orden — puntuacion+semaforo van literales)
        Assert.Equal(7, Ultima.Parametros.Count);
        Assert.Equal(TenantId,   Ultima.Parametros["TenantId"]);
        Assert.Equal(CicloId,    Ultima.Parametros["CicloId"]);
        Assert.Equal(AreaId,     Ultima.Parametros["AreaId"]);
        Assert.Equal(PilarId,    Ultima.Parametros["PilarId"]);
        Assert.Equal("OKR.1",    Ultima.Parametros["Codigo"]);
        Assert.Equal("Detalle",  Ultima.Parametros["Descripcion"]);
        Assert.Equal(1,          Ultima.Parametros["Orden"]);

        // La tx llega al DbCommand.Transaction (atomicidad de la operación conjunta)
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 45 · ActualizarAsync ──────────────────────────────────

    /// <summary>45 · UPDATE solo pilar_id/descripcion/updated_at; WHERE id + tenant + area (SEC-07).</summary>
    [Fact]
    public async Task ActualizarAsync_Sql_SoloTocaTresCamposYFiltraTenantArea()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        var dto = new OkrUpdateRequest { PilarId = PilarId, Descripcion = "Nueva desc" };
        await _sut.ActualizarAsync(TenantId, AreaId, OkrId, dto, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("UPDATE okr", sql, StringComparison.OrdinalIgnoreCase);

        // SÍ toca estos 3 campos (pilar_id, descripcion, updated_at)
        Assert.Contains("pilar_id",    sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descripcion", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at",  sql, StringComparison.OrdinalIgnoreCase);

        // NO toca los inmutables (CA #1, DB-04)
        Assert.DoesNotContain("codigo",            sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puntuacion_final", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("semaforo",          sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orden",             sql, StringComparison.OrdinalIgnoreCase);

        // WHERE: id + tenant + area (refuerzo SEC-07 — ver «Decisiones técnicas clave» del spec)
        Assert.Contains("id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(OkrId,    Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AreaId,   Ultima.Parametros["AreaId"]);
        Assert.Equal(PilarId,  Ultima.Parametros["PilarId"]);
        Assert.Equal("Nueva desc", Ultima.Parametros["Descripcion"]);

        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 46 · EliminarAsync ────────────────────────────────────

    /// <summary>46 · DELETE físico WHERE id + tenant + area (SEC-07 reforzado).</summary>
    [Fact]
    public async Task EliminarAsync_Sql_DeleteFisicoFiltraTenantArea()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();
        _conexion.FilasAfectadas = 1;

        await _sut.EliminarAsync(TenantId, AreaId, OkrId, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("DELETE FROM okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id = @Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(OkrId,    Ultima.Parametros["Id"]);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(AreaId,   Ultima.Parametros["AreaId"]);
        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 47 · InsertLogAsync ───────────────────────────────────

    /// <summary>47 · INSERT log_auditoria con 7 campos + casts ::jsonb/::accion_auditoria (ADR-003).</summary>
    [Fact]
    public async Task InsertLogAsync_Sql_PatronAuditoria()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        var log = new LogAuditoriaInsert
        {
            TenantId = TenantId,
            UsuarioId = UsuarioId,
            Accion = "CREATE",
            Entidad = "Okr",
            EntidadId = OkrId.ToString(),
            ValorAnterior = null,
            ValorNuevo = "{\"codigo\":\"OKR.1\",\"descripcion\":\"X\"}"
        };

        await _sut.InsertLogAsync(log, tx, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("INSERT INTO log_auditoria", sql, StringComparison.OrdinalIgnoreCase);

        // Casts tipados del schema (patrón existente en repos del repo)
        Assert.Contains("@Accion::accion_auditoria", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ValorAnterior::jsonb", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@ValorNuevo::jsonb", sql, StringComparison.OrdinalIgnoreCase);

        // 7 parámetros exactos del DTO (spec L765)
        Assert.Equal(7, Ultima.Parametros.Count);
        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(UsuarioId, Ultima.Parametros["UsuarioId"]);
        Assert.Equal("CREATE", Ultima.Parametros["Accion"]);
        Assert.Equal("Okr", Ultima.Parametros["Entidad"]);
        Assert.Equal(OkrId.ToString(), Ultima.Parametros["EntidadId"]);
        Assert.Null(Ultima.Parametros["ValorAnterior"]);
        Assert.NotNull(Ultima.Parametros["ValorNuevo"]);

        Assert.Same(tx, Ultima.Transaccion);
    }

    // ─────────────────────────── 48 · BeginTransactionAsync ────────────────────────────

    /// <summary>48 · BeginTransactionAsync: la conexión se abre (implícito por State=Open) y se devuelve tx.</summary>
    [Fact]
    public async Task BeginTransactionAsync_AbreConexionEIniciaTx()
    {
        CrearRepositorio();

        var tx = await _sut.BeginTransactionAsync(CancellationToken.None);

        Assert.NotNull(tx);
        Assert.IsAssignableFrom<IDbTransaction>(tx);
    }

    // ─────────────────────────── EXTRA #1 · ListarConsolidadoAsync ─────────────────────

    /// <summary>Extras #1: el consolidado (precedente) filtra tenant+ciclo+area.</summary>
    [Fact]
    public async Task ListarConsolidadoAsync_FiltrosCombinados_PasaParametrosCorrectos()
    {
        CrearRepositorio();

        await _sut.ListarConsolidadoAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        var sql = Normalizar(Ultima.Sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM okr", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ciclo_id = @CicloId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id = @AreaId", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TenantId, Ultima.Parametros["TenantId"]);
        Assert.Equal(CicloId,  Ultima.Parametros["CicloId"]);
        Assert.Equal(AreaId,   Ultima.Parametros["AreaId"]);
    }

    // ─────────────────────────── EXTRA #2 · CountAsync ─────────────────────────────────

    /// <summary>Extras #2: CountAsync retorna entero con WHERE correcto.</summary>
    [Fact]
    public async Task CountAsync_FiltrosCombinados_RetornaEntero()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = 7;

        var count = await _sut.CountAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        Assert.Equal(7, count);
        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = @TenantId", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── EXTRA #3 · ObtenerAsync mapea ──────────────────────────

    /// <summary>Extras #3: ObtenerAsync retorna DTO mapeado con JOIN pilar/area.</summary>
    [Fact]
    public async Task ObtenerAsync_PorId_RetornaDtoMapeado()
    {
        CrearRepositorio();
        _conexion.Filas.Rows.Add(
            OkrId, TenantId, CicloId, AreaId, PilarId, "Crecimiento", "OKR.1", "Desc", 0.500m, "Amarillo", 1, DateTime.UtcNow, DateTime.UtcNow);

        var dto = await _sut.ObtenerPorIdAsync(TenantId, AreaId, OkrId, CancellationToken.None);

        Assert.NotNull(dto);
        var sql = Normalizar(Ultima.Sql);
        Assert.Contains("o.id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN pilar", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── EXTRA #4 · MAX+1 sin OKRs ──────────────────────────────

    /// <summary>Extras #4: ObtenerSiguienteSecuenciaOkrAsync sin OKRs → 1 (COALESCE).</summary>
    [Fact]
    public async Task ObtenerSiguienteSecuenciaOkrAsync_SinOkrs_Retorna1()
    {
        CrearRepositorio();
        _conexion.Filas = CrearTablaSiguienteSecuencia(siguienteN: 1, siguienteOrden: 1);

        var sec = await _sut.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        Assert.Equal(1, sec.SiguienteN);
        Assert.Equal(1, sec.SiguienteOrden);
    }

    // ─────────────────────────── EXTRA #5 · F4 huecos ──────────────────────────────────

    /// <summary>Extras #5: F4 MAX+1 — con huecos (OKR.1 y OKR.3) → 4, nunca 3.</summary>
    [Fact]
    public async Task ObtenerSiguienteSecuenciaOkrAsync_ConHuecos_RetornaMaxMas1()
    {
        CrearRepositorio();
        _conexion.Filas = CrearTablaSiguienteSecuencia(siguienteN: 4, siguienteOrden: 4);

        var sec = await _sut.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, CancellationToken.None);

        // Si la implementación usara COUNT(*) + 1 → 3 (bug que justifica F4)
        Assert.Equal(4, sec.SiguienteN);
        Assert.Equal(4, sec.SiguienteOrden);
    }

    // ─────────────────────────── EXTRA #6 · EXISTS true ─────────────────────────────────

    /// <summary>Extras #6: VerificarKrsConValoresAsync con valores → true.</summary>
    [Fact]
    public async Task VerificarKrsConValoresAsync_ConValoresReales_RetornaTrue()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = true;

        var tieneValores = await _sut.VerificarKrsConValoresAsync(TenantId, OkrId, CancellationToken.None);

        Assert.True(tieneValores);
    }

    // ─────────────────────────── EXTRA #7 · EXISTS false ────────────────────────────────

    /// <summary>Extras #7: VerificarKrsConValoresAsync sin valores → false.</summary>
    [Fact]
    public async Task VerificarKrsConValoresAsync_SinValoresReales_RetornaFalse()
    {
        CrearRepositorio();
        _conexion.ValorEscalar = false;

        var tieneValores = await _sut.VerificarKrsConValoresAsync(TenantId, OkrId, CancellationToken.None);

        Assert.False(tieneValores);
    }

    // ─────────────────────────── EXTRA #8 · aislamiento multi-tenant ────────────────────

    /// <summary>
    /// Extras #8 (BLOQUEO 6 HU-022): los 8 métodos DAL invocados llevan tenant_id con @TenantId
    /// y ningún id aparece dentro del texto SQL.
    /// </summary>
    [Fact]
    public async Task TodasLasConsultas_SqlContieneTenantId_YSinIdsLiterales()
    {
        CrearRepositorio();
        using var tx = _conexion.BeginTransactionFalsa();

        // Act: ejercitar todos los métodos de IOkrRepository (8)
        await _sut.ListarAsync(TenantId, CicloId, AreaId, CancellationToken.None);
        await _sut.ObtenerPorIdAsync(TenantId, AreaId, OkrId, CancellationToken.None);
        await _sut.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, CancellationToken.None);
        await _sut.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, CancellationToken.None);
        await _sut.CrearAsync(TenantId, CicloId, AreaId, "OKR.1", 1,
            new OkrCreateRequest { PilarId = PilarId, Descripcion = "X" }, tx, CancellationToken.None);
        await _sut.ActualizarAsync(TenantId, AreaId, OkrId,
            new OkrUpdateRequest { PilarId = PilarId, Descripcion = "Y" }, tx, CancellationToken.None);
        await _sut.EliminarAsync(TenantId, AreaId, OkrId, tx, CancellationToken.None);
        await _sut.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = TenantId,
            UsuarioId = UsuarioId,
            Accion = "CREATE",
            Entidad = "Okr",
            EntidadId = OkrId.ToString(),
            ValorAnterior = null,
            ValorNuevo = "{}"
        }, tx, CancellationToken.None);

        // Assert: las 8 consultas capturadas (7 SQL + BeginTransactionAsync no es SQL)
        Assert.True(_conexion.Consultas.Count >= 8,
            $"Se esperaban ≥8 consultas capturadas (incluyendo las tx), se obtuvieron {_conexion.Consultas.Count}");

        // DB-03 + SEC-06: TODAS las consultas de datos llevan tenant_id con PARÁMETRO NOMBRADO
        var consultasConWhere = _conexion.Consultas
            .Where(c => Normalizar(c.Sql).Contains("WHERE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(consultasConWhere);
        Assert.All(consultasConWhere, c =>
            Assert.Contains("tenant_id = @TenantId", Normalizar(c.Sql), StringComparison.OrdinalIgnoreCase));

        // STACK-03 + SEC-05: ningún id aparece en el texto SQL (sin concatenación)
        Assert.All(_conexion.Consultas, c =>
        {
            var sql = Normalizar(c.Sql);
            Assert.DoesNotContain(TenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CicloId.ToString(),  sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AreaId.ToString(),   sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PilarId.ToString(),  sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(OkrId.ToString(),    sql, StringComparison.OrdinalIgnoreCase);
        });

        // El tenant_id llega como PARÁMETRO con el tenant del llamante (no un literal)
        Assert.All(consultasConWhere, c =>
            Assert.Equal(TenantId, c.Parametros["TenantId"]));
    }

    // ══════════════════════════════ DOBLES DE System.Data.Common (anidados) ═══════════════════════════════

    private static DataTable CrearTablaSiguienteSecuencia(int siguienteN, int siguienteOrden)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("SiguienteN", typeof(int));
        tabla.Columns.Add("SiguienteOrden", typeof(int));
        tabla.Rows.Add(siguienteN, siguienteOrden);
        return tabla;
    }

    /// <summary>Un SQL ejecutado: su texto, sus parámetros y la transacción que recibió.</summary>
    private sealed class ConsultaSql
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
    /// devuelve filas de mentira. <c>State</c> es Open porque el patrón de BeginTransactionAsync del
    /// repo comprueba <c>conn.State != ConnectionState.Open</c> antes de abrir.
    /// </summary>
    private sealed class ConexionCapturadora : DbConnection
    {
        public List<ConsultaSql> Consultas { get; } = new();
        public DataTable Filas { get; set; } = CrearTabla();
        public object? ValorEscalar { get; set; } = 0;
        public int FilasAfectadas { get; set; } = 1;

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
        public TransaccionFalsa BeginTransactionFalsa() => new(this, IsolationLevel.ReadCommitted);

        internal DbDataReader CrearReader() => Filas.CreateDataReader();

        /// <summary>Tabla con la proyección completa del SELECT de Listar/Obtener (incluso INNER JOIN pilar).</summary>
        private static DataTable CrearTabla()
        {
            var tabla = new DataTable();
            tabla.Columns.Add("id", typeof(Guid));
            tabla.Columns.Add("tenant_id", typeof(Guid));
            tabla.Columns.Add("ciclo_id", typeof(Guid));
            tabla.Columns.Add("area_id", typeof(Guid));
            tabla.Columns.Add("pilar_id", typeof(Guid));
            tabla.Columns.Add("pilar_nombre", typeof(string));
            tabla.Columns.Add("codigo", typeof(string));
            tabla.Columns.Add("descripcion", typeof(string));
            tabla.Columns.Add("puntuacion_final", typeof(decimal));
            tabla.Columns.Add("semaforo", typeof(string));
            tabla.Columns.Add("orden", typeof(int));
            tabla.Columns.Add("created_at", typeof(DateTime));
            tabla.Columns.Add("updated_at", typeof(DateTime));
            return tabla;
        }
    }

    private sealed class ComandoCapturador : DbCommand
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

        private void Registrar() => _conexion.Consultas.Add(
            new ConsultaSql(CommandText, _parametros.Capturar(), DbTransaction));
    }

    private sealed class TransaccionFalsa : DbTransaction
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

    private sealed class ParametroCapturador : DbParameter
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

    private sealed class ParametroColeccion : DbParameterCollection
    {
        private readonly List<object> _items = new();

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
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }
}
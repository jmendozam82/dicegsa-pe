using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DAL.Repositories.PlanOperativo;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-023 — Vista Consolidada del Plan (Gerente).
/// Spec HU-023 § "Tests requeridos" (casos DAL 1-7).
/// Escritos ANTES de la implementación (TEST-01): PlanConsolidadoRepository NO existe todavía
/// → el rojo legítimo es el FALLO DE COMPILACIÓN del proyecto de tests.
///
/// Dobles de System.Data.Common escritos a mano (mismo patrón que EntregableAdjuntoRepositoryTests).
/// </summary>
public class PlanConsolidadoRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AreaId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid CgId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private ConexionCapturadora _conexion = null!;
    private PlanConsolidadoRepository _sut = null!;

    private void CrearRepositorio()
    {
        _conexion = new ConexionCapturadora();
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(_conexion);
        _sut = new PlanConsolidadoRepository(factoryMock.Object);
    }

    private static FiltrosConsolidadoRequest CrearFiltros() => new();

    // ══════════════════════════════ CASOS DAL (7 casos) ══════════════════════════════

    [Fact]
    public async Task ConsultarAsync_SinFiltros_SQLConJoinsYTenantId()
    {
        // Arrange
        CrearRepositorio();
        var filtros = CrearFiltros();

        // Act
        await _sut.ListarConsolidadoAsync(TenantId, CicloId, filtros, false, CancellationToken.None);

        // Assert
        var sql = _conexion.Consultas.Single().Sql;
        Assert.Contains("FROM accion_plan", sql);
        Assert.Contains("JOIN objetivo_cg", sql);
        Assert.Contains("JOIN pilar", sql);
        Assert.Contains("JOIN area", sql);
        Assert.Contains("JOIN ciclo", sql);
        Assert.Contains("LEFT JOIN usuario", sql);
        Assert.Contains("tenant_id = @TenantId", sql);
    }

    [Fact]
    public async Task ConsultarAsync_ConFiltros_SQLConWhereDinamico()
    {
        // Arrange
        CrearRepositorio();
        var filtros = new FiltrosConsolidadoRequest
        {
            AreaId = AreaId,
            CgId = CgId,
            Status = "Atrasado",
            Clasificacion = "Proyecto",
            Tipo = "OPEX",
            FechaDesde = new DateTime(2026, 1, 1),
            FechaHasta = new DateTime(2026, 12, 31)
        };

        // Act
        await _sut.ListarConsolidadoAsync(TenantId, CicloId, filtros, false, CancellationToken.None);

        // Assert
        var sql = _conexion.Consultas.Single().Sql;
        Assert.Contains("@AreaId", sql);
        Assert.Contains("@CgId", sql);
        Assert.Contains("@Status", sql);
        Assert.Contains("@Clasificacion", sql);
        Assert.Contains("@Tipo", sql);
        Assert.Contains("@FechaDesde", sql);
        Assert.Contains("@FechaHasta", sql);
    }

    [Fact]
    public async Task ConsultarAsync_Paginacion_LimitOffset()
    {
        // Arrange
        CrearRepositorio();
        var filtros = new FiltrosConsolidadoRequest { Page = 2, PageSize = 10 };

        // Act
        await _sut.ListarConsolidadoAsync(TenantId, CicloId, filtros, false, CancellationToken.None);

        // Assert
        var sql = _conexion.Consultas.Single().Sql;
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
    }

    [Fact]
    public async Task ConsultarAsync_ConteoTotal_SQLCount()
    {
        // Arrange
        CrearRepositorio();
        var filtros = CrearFiltros();
        _conexion.ValorEscalar = 42;

        // Act
        var count = await _sut.CountConsolidadoAsync(TenantId, CicloId, filtros, CancellationToken.None);

        // Assert
        Assert.Equal(42, count);
        var sql = _conexion.Consultas.Single().Sql;
        Assert.Contains("COUNT(*)", sql);
        Assert.Contains("tenant_id = @TenantId", sql);
    }

    [Fact]
    public async Task ConsultarAsync_ResumenPorStatus_SQLGroupByFilter()
    {
        // Arrange
        CrearRepositorio();
        var filtros = CrearFiltros();

        // Act
        await _sut.ObtenerResumenAsync(TenantId, CicloId, filtros, CancellationToken.None);

        // Assert
        var sql = _conexion.Consultas.Single().Sql;
        Assert.Contains("COUNT(*) FILTER", sql);
        Assert.Contains("NoIniciado", sql);
        Assert.Contains("EnProgreso", sql);
        Assert.Contains("Terminado", sql);
        Assert.Contains("Atrasado", sql);
    }

    [Fact]
    public async Task ConsultarAsync_Exportacion_SinPaginacion()
    {
        // Arrange
        CrearRepositorio();
        var filtros = CrearFiltros();

        // Act
        await _sut.ListarConsolidadoAsync(TenantId, CicloId, filtros, true, CancellationToken.None);

        // Assert
        var sql = _conexion.Consultas.Single().Sql;
        Assert.DoesNotContain("OFFSET", sql);
        Assert.DoesNotContain("FETCH NEXT", sql);
    }

    [Fact]
    public async Task ConsultarAsync_TenantIdEnTodasLasQueries()
    {
        // Arrange
        CrearRepositorio();
        var filtros = CrearFiltros();

        // Act
        await _sut.ListarConsolidadoAsync(TenantId, CicloId, filtros, false, CancellationToken.None);
        await _sut.CountConsolidadoAsync(TenantId, CicloId, filtros, CancellationToken.None);
        await _sut.ObtenerResumenAsync(TenantId, CicloId, filtros, CancellationToken.None);

        // Assert
        Assert.All(_conexion.Consultas, c => Assert.Contains("tenant_id = @TenantId", c.Sql));
    }

    // ══════════════════════════════ DOBLES DE System.Data.Common ══════════════════════════════

    internal sealed class ConexionCapturadora : DbConnection
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

        private static DataTable CrearTabla()
        {
            var tabla = new DataTable();
            tabla.Columns.Add("AccionId", typeof(Guid));
            tabla.Columns.Add("AccionCodigo", typeof(string));
            tabla.Columns.Add("AccionDescripcion", typeof(string));
            tabla.Columns.Add("FechaInicio", typeof(DateTime));
            tabla.Columns.Add("FechaVencimiento", typeof(DateTime));
            tabla.Columns.Add("Clasificacion", typeof(string));
            tabla.Columns.Add("TipoPresupuesto", typeof(string));
            tabla.Columns.Add("Progreso", typeof(decimal));
            tabla.Columns.Add("Status", typeof(string));
            tabla.Columns.Add("Peso", typeof(decimal));
            tabla.Columns.Add("ResponsableNombre", typeof(string));
            tabla.Columns.Add("ObjetivoCgId", typeof(Guid));
            tabla.Columns.Add("ObjetivoCodigo", typeof(string));
            tabla.Columns.Add("ObjetivoDescripcion", typeof(string));
            tabla.Columns.Add("ObjetivoProgreso", typeof(decimal));
            tabla.Columns.Add("ObjetivoSemaforo", typeof(string));
            tabla.Columns.Add("PilarCodigo", typeof(string));
            tabla.Columns.Add("PilarNombre", typeof(string));
            tabla.Columns.Add("AreaId", typeof(Guid));
            tabla.Columns.Add("AreaCodigo", typeof(string));
            tabla.Columns.Add("AreaNombre", typeof(string));
            tabla.Columns.Add("CicloId", typeof(Guid));
            tabla.Columns.Add("CicloNombre", typeof(string));
            tabla.Columns.Add("CicloAnioFiscal", typeof(int));
            return tabla;
        }
    }

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

        private void Registrar() => _conexion.Consultas.Add(
            new ConsultaSql(CommandText, _parametros.Capturar(), DbTransaction));
    }

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

    internal sealed class ParametroColeccion : DbParameterCollection
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

        protected override void SetParameter(string parameterName, DbParameter value)
            => _items[IndexOf(parameterName)] = value;
    }

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
}

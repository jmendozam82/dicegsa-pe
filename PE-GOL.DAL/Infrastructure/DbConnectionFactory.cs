using System.Data;
using Dapper;
using Npgsql;

namespace PE_GOL.DAL.Infrastructure;

/// <summary>
/// Implementación simple de IDbConnectionFactory sobre Npgsql.
/// En el constructor se activa el mapeo snake_case → PascalCase de Dapper
/// (plan_id → PlanId) para que las columnas del DDL (06_MODELO_DATOS.md) se
/// asignen a las propiedades de las entidades sin alias por columna.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
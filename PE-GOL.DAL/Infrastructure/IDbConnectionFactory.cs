using System.Data;

namespace PE_GOL.DAL.Infrastructure;

/// <summary>
/// Fábrica de conexiones IDbConnection (STACK-03: Dapper + SQL parametrizado).
/// La implementación concreta devuelve NpgsqlConnection a Supabase/PostgreSQL.
/// Se registra como singleton en IOC con la connection string de configuración.
/// </summary>
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
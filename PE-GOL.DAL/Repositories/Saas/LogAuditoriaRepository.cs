using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Saas;

/// <summary>
/// Repositorio de log_auditoria con Dapper (Spec HU-005 § Queries DAL: DAL-L1 a DAL-L4).
/// log_auditoria NO está bajo RLS (verificado: no aparece en ENABLE ROW LEVEL SECURITY,
/// 06_MODELO_DATOS.md L465-484) → las queries NO llevan WHERE tenant_id de política (D6);
/// el alcance se garantiza por [Authorize(Roles = "SuperAdmin")] + re-validación BLL (D12).
/// El filtro por tenant_id es un FILTRO DE NEGOCIO opcional (CA #2), no de aislamiento.
/// CA #3 (inmutabilidad): SOLO lectura + EliminarAnterioresAAsync (batch de retención,
/// invocado únicamente por el HostedService — no expuesto por API).
/// Todas las queries usan parámetros nombrados (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// </summary>
public sealed class LogAuditoriaRepository : ILogAuditoriaRepository
{
    private readonly IDbConnectionFactory _factory;

    public LogAuditoriaRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>DAL-L2 · COUNT con los mismos filtros dinámicos del listado (paginado).</summary>
    public async Task<int> CountAsync(LogAuditoriaFiltrosDto filtros, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM log_auditoria la
            WHERE (@TenantId  IS NULL OR la.tenant_id  = @TenantId)
              AND (@UsuarioId IS NULL OR la.usuario_id = @UsuarioId)
              AND (@Accion    IS NULL OR la.accion     = @Accion::accion_auditoria)
              AND (@Desde     IS NULL OR la.created_at >= @Desde)
              AND (@Hasta     IS NULL OR la.created_at <= @Hasta);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, filtros, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-L1 · SELECT paginado con filtros dinámicos, LEFT JOIN tenant/usuario,
    /// ORDER BY created_at DESC, LIMIT/OFFSET. SIN valor_anterior/valor_nuevo (D3).
    /// El offset (Page-1)*PageSize se calcula dentro del repositorio (decisión de @QA).</summary>
    public async Task<IEnumerable<LogAuditoriaEntity>> GetPagedAsync(LogAuditoriaFiltrosDto filtros, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT la.id, la.tenant_id, t.nombre AS tenant_nombre,
                   la.usuario_id, u.nombre AS usuario_nombre,
                   la.accion, la.entidad, la.entidad_id, la.created_at
            FROM log_auditoria la
            LEFT JOIN tenant t  ON t.id  = la.tenant_id
            LEFT JOIN usuario u ON u.id  = la.usuario_id
            WHERE (@TenantId  IS NULL OR la.tenant_id  = @TenantId)
              AND (@UsuarioId IS NULL OR la.usuario_id = @UsuarioId)
              AND (@Accion    IS NULL OR la.accion     = @Accion::accion_auditoria)
              AND (@Desde     IS NULL OR la.created_at >= @Desde)
              AND (@Hasta     IS NULL OR la.created_at <= @Hasta)
            ORDER BY la.created_at DESC
            LIMIT @PageSize OFFSET @Offset;";

        var offset = (filtros.Page - 1) * filtros.PageSize;

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new
        {
            filtros.TenantId,
            filtros.UsuarioId,
            filtros.Accion,
            filtros.Desde,
            filtros.Hasta,
            filtros.PageSize,
            Offset = offset
        }, cancellationToken: ct);
        return await conn.QueryAsync<LogAuditoriaEntity>(cmd);
    }

    /// <summary>DAL-L3 · SELECT detalle por id CON valor_anterior/valor_nuevo (JSONB → string,
    /// ADR-003). Retorna null si no existe.</summary>
    public async Task<LogAuditoriaEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT la.id, la.tenant_id, t.nombre AS tenant_nombre,
                   la.usuario_id, u.nombre AS usuario_nombre,
                   la.accion, la.entidad, la.entidad_id,
                   la.valor_anterior, la.valor_nuevo, la.created_at
            FROM log_auditoria la
            LEFT JOIN tenant t  ON t.id  = la.tenant_id
            LEFT JOIN usuario u ON u.id  = la.usuario_id
            WHERE la.id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<LogAuditoriaEntity>(cmd);
    }

    /// <summary>DAL-L4 · DELETE FROM log_auditoria WHERE created_at &lt; @Cutoff (batch de
    /// retención, CA #4). Retorna filas afectadas. ÚNICA operación de borrado sobre la tabla;
    /// solo la invoca LogAuditoriaLimpiezaService (HostedService), nunca la API (CA #3/D12).
    /// @Cutoff = NOW() - RetencionDias calculado en el HostedService (BLL), nunca en SQL (DB-04).</summary>
    public async Task<int> EliminarAnterioresAAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM log_auditoria
            WHERE created_at < @Cutoff;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Cutoff = cutoff }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }
}
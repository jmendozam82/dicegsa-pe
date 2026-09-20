using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.DAL.Repositories.Objetivos;

public sealed class ObjetivoCgRepository : IObjetivoCgRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var conn = GetConnection();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        return Task.FromResult(conn.BeginTransaction());
    }

    public async Task<int> InsertLogAsync(PE_GOL.DTO.Dtos.LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id, valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion, @Entidad, @EntidadId, @ValorAnterior::jsonb, @ValorNuevo::jsonb);";

        return await GetConnection().ExecuteAsync(sql, dto, transaction: tx);
    }

    public ObjetivoCgRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private IDbConnection GetConnection()
    {
        _connectionActiva ??= _factory.CreateConnection();
        return _connectionActiva;
    }

    public async Task<IEnumerable<ObjetivoCgResponse>> ListarAsync(Guid tenantId, Guid cicloId, Guid areaId)
    {
        var sql = @"
            SELECT o.id AS Id, 
                   o.pilar_id AS PilarId, 
                   p.nombre AS PilarNombre, 
                   o.codigo AS Codigo, 
                   o.descripcion AS Descripcion, 
                   o.trimestre_objetivo::text AS TrimestreObjetivo, 
                   o.progreso AS Progreso, 
                   o.semaforo::text AS Semaforo, 
                   o.created_at AS CreatedAt, 
                   o.updated_at AS UpdatedAt
            FROM objetivo_cg o
            INNER JOIN pilar p ON o.pilar_id = p.id
            WHERE o.tenant_id = @TenantId 
              AND o.ciclo_id = @CicloId 
              AND o.area_id = @AreaId
            ORDER BY o.orden ASC, o.created_at ASC;";

        return await GetConnection().QueryAsync<ObjetivoCgResponse>(sql, new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public async Task<ObjetivoCgResponse?> ObtenerPorIdAsync(Guid tenantId, Guid areaId, Guid id)
    {
        var sql = @"
            SELECT o.id AS Id, 
                   o.pilar_id AS PilarId, 
                   p.nombre AS PilarNombre, 
                   o.codigo AS Codigo, 
                   o.descripcion AS Descripcion, 
                   o.trimestre_objetivo::text AS TrimestreObjetivo, 
                   o.progreso AS Progreso, 
                   o.semaforo::text AS Semaforo, 
                   o.created_at AS CreatedAt, 
                   o.updated_at AS UpdatedAt
            FROM objetivo_cg o
            INNER JOIN pilar p ON o.pilar_id = p.id
            WHERE o.id = @Id
              AND o.tenant_id = @TenantId 
              AND o.area_id = @AreaId;";

        return await GetConnection().QuerySingleOrDefaultAsync<ObjetivoCgResponse>(sql, new { Id = id, TenantId = tenantId, AreaId = areaId });
    }

    public async Task<Guid> CrearAsync(Guid tenantId, Guid cicloId, Guid areaId, string codigo, ObjetivoCgCreateRequest request, IDbTransaction? tx = null)
    {
        var sql = @"
            INSERT INTO objetivo_cg (tenant_id, pilar_id, ciclo_id, area_id, codigo, descripcion, trimestre_objetivo, progreso, semaforo)
            VALUES (@TenantId, @PilarId, @CicloId, @AreaId, @Codigo, @Descripcion, @TrimestreObjetivo::trimestre, 0.0000, 'Rojo'::semaforo_color)
            RETURNING id;";

        var p = new
        {
            TenantId = tenantId,
            PilarId = request.PilarId,
            CicloId = cicloId,
            AreaId = areaId,
            Codigo = codigo,
            Descripcion = request.Descripcion,
            TrimestreObjetivo = request.TrimestreObjetivo
        };

        return await GetConnection().ExecuteScalarAsync<Guid>(sql, p, transaction: tx);
    }

    public async Task ActualizarAsync(Guid id, ObjetivoCgUpdateRequest request, IDbTransaction? tx = null)
    {
        var sql = @"
            UPDATE objetivo_cg
            SET pilar_id = @PilarId,
                descripcion = @Descripcion,
                trimestre_objetivo = @TrimestreObjetivo::trimestre,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @Id;";

        var p = new
        {
            Id = id,
            PilarId = request.PilarId,
            Descripcion = request.Descripcion,
            TrimestreObjetivo = request.TrimestreObjetivo
        };

        await GetConnection().ExecuteAsync(sql, p, transaction: tx);
    }

    public async Task EliminarAsync(Guid id, IDbTransaction? tx = null)
    {
        var sql = "DELETE FROM objetivo_cg WHERE id = @Id;";
        await GetConnection().ExecuteAsync(sql, new { Id = id }, transaction: tx);
    }

    public async Task<bool> VerificarAccionesAsociadasAsync(Guid tenantId, Guid objetivoCgId)
    {
        var sql = "SELECT EXISTS(SELECT 1 FROM accion_plan WHERE objetivo_cg_id = @Id AND tenant_id = @TenantId);";
        return await GetConnection().ExecuteScalarAsync<bool>(sql, new { Id = objetivoCgId, TenantId = tenantId });
    }

    public async Task<int> ObtenerConteoPorAreaAsync(Guid tenantId, Guid cicloId, Guid areaId)
    {
        var sql = "SELECT COUNT(1) FROM objetivo_cg WHERE tenant_id = @TenantId AND ciclo_id = @CicloId AND area_id = @AreaId;";
        return await GetConnection().ExecuteScalarAsync<int>(sql, new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public async Task<string?> ObtenerCodigoAreaAsync(Guid tenantId, Guid areaId)
    {
        var sql = "SELECT codigo FROM area WHERE id = @AreaId AND tenant_id = @TenantId;";
        return await GetConnection().ExecuteScalarAsync<string?>(sql, new { AreaId = areaId, TenantId = tenantId });
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}

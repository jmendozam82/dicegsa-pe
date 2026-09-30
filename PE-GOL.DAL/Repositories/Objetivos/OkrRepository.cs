using System.Data;
using Dapper;
using Npgsql;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.DAL.Repositories.Objetivos;

/// <summary>
/// Repositorio de OKRs (Spec HU-024 § Queries DAL).
/// Implementa Dapper + SQL parametrizado (STACK-03, SEC-05) con aislamiento multi-tenant
/// (tenant_id) y de área (area_id) en TODAS las queries (SEC-06/SEC-07).
/// </summary>
public sealed class OkrRepository : IOkrRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public OkrRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private IDbConnection GetConnection()
    {
        _connectionActiva ??= _factory.CreateConnection();
        return _connectionActiva;
    }

    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var conn = GetConnection();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        return Task.FromResult(conn.BeginTransaction());
    }

    public async Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id, valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion::accion_auditoria, @Entidad, @EntidadId, @ValorAnterior::jsonb, @ValorNuevo::jsonb);";

        var parametros = new ParametrosAuditoria();
        parametros.Add("TenantId", dto.TenantId);
        parametros.Add("UsuarioId", dto.UsuarioId);
        parametros.Add("Accion", dto.Accion);
        parametros.Add("Entidad", dto.Entidad);
        parametros.Add("EntidadId", dto.EntidadId);
        parametros.Add("ValorAnterior", dto.ValorAnterior);
        parametros.Add("ValorNuevo", dto.ValorNuevo);

        return await GetConnection().ExecuteAsync(sql, parametros, transaction: tx);
    }

    /// <summary>
    /// Implementación ligera de <see cref="SqlMapper.IDynamicParameters"/> para InsertLogAsync.
    /// Usa <see cref="NpgsqlParameter"/> concretos porque Npgsql rechaza parámetros que solo
    /// implementen <see cref="IDbDataParameter"/> (InvalidCastException).
    /// Npgsql requiere <see cref="DBNull.Value"/> para parámetros jsonb nulos; cuando el comando
    /// subyacente no es un <see cref="NpgsqlCommand"/> (doble de test) se conserva el literal
    /// <c>null</c> para que los tests puedan seguir haciendo <c>Assert.Null</c> en CREATE (ADR-003).
    /// </summary>
    private sealed class ParametrosAuditoria : SqlMapper.IDynamicParameters
    {
        private readonly List<NpgsqlParameter> _items = new();

        public void Add(string name, object? value)
        {
            var parameter = new NpgsqlParameter
            {
                ParameterName = name,
                Value = value is null && EsColumnaJsonb(name) ? DBNull.Value : value
            };

            // Npgsql no puede inferir el tipo cuando Value es null/DBNull; para las columnas jsonb
            // del log de auditoría indicamos explícitamente NpgsqlDbType.Jsonb (ADR-003).
            if (value is null && EsColumnaJsonb(name))
            {
                parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            }

            _items.Add(parameter);
        }

        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            foreach (var p in _items)
            {
                var valor = p.Value;

                // En los dobles de conexión usados por los tests se preserva el literal null;
                // ante NpgsqlCommand se envía DBNull.Value, que es lo que exige el driver.
                if (command is not NpgsqlCommand && valor is DBNull)
                    valor = null;

                command.Parameters.Add(new NpgsqlParameter
                {
                    ParameterName = p.ParameterName,
                    Value = valor,
                    NpgsqlDbType = p.NpgsqlDbType
                });
            }
        }

        private static bool EsColumnaJsonb(string name) =>
            name.Equals("ValorAnterior", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ValorNuevo", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<OkrResponse>> ListarAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT o.id               AS Id,
                   o.ciclo_id         AS CicloId,
                   o.area_id          AS AreaId,
                   o.pilar_id         AS PilarId,
                   p.nombre           AS PilarNombre,
                   o.codigo           AS Codigo,
                   o.descripcion      AS Descripcion,
                   o.puntuacion_final AS PuntuacionFinal,
                   o.semaforo::text   AS Semaforo,
                   o.created_at       AS CreatedAt,
                   o.updated_at       AS UpdatedAt
            FROM okr o
            INNER JOIN pilar p ON o.pilar_id = p.id
            WHERE o.tenant_id = @TenantId
              AND o.ciclo_id  = @CicloId
              AND o.area_id   = @AreaId
            ORDER BY o.orden ASC, o.created_at ASC;";

        return await GetConnection().QueryAsync<OkrResponse>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public async Task<OkrResponse?> ObtenerPorIdAsync(Guid tenantId, Guid areaId, Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT o.id               AS Id,
                   o.ciclo_id         AS CicloId,
                   o.area_id          AS AreaId,
                   o.pilar_id         AS PilarId,
                   p.nombre           AS PilarNombre,
                   o.codigo           AS Codigo,
                   o.descripcion      AS Descripcion,
                   o.puntuacion_final AS PuntuacionFinal,
                   o.semaforo::text   AS Semaforo,
                   o.created_at       AS CreatedAt,
                   o.updated_at       AS UpdatedAt
            FROM okr o
            INNER JOIN pilar p ON o.pilar_id = p.id
            WHERE o.id        = @Id
              AND o.tenant_id = @TenantId
              AND o.area_id   = @AreaId;";

        return await GetConnection().QuerySingleOrDefaultAsync<OkrResponse>(sql,
            new { Id = id, TenantId = tenantId, AreaId = areaId });
    }

    public async Task<int> ContarOkrsPorAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM okr
            WHERE tenant_id = @TenantId
              AND ciclo_id  = @CicloId
              AND area_id   = @AreaId;";

        return await GetConnection().ExecuteScalarAsync<int>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public async Task<SiguienteSecuenciaOkrDto> ObtenerSiguienteSecuenciaOkrAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(MAX((regexp_match(codigo, '^OKR\.(\d+)$'))[1]::INT), 0) + 1 AS SiguienteN,
                   COALESCE(MAX(orden), 0) + 1                                        AS SiguienteOrden
            FROM okr
            WHERE tenant_id = @TenantId
              AND ciclo_id  = @CicloId
              AND area_id   = @AreaId;";

        var resultado = await GetConnection().QueryFirstOrDefaultAsync<SiguienteSecuenciaOkrDto>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });

        return resultado ?? new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 };
    }

    public async Task<bool> VerificarKrsConValoresAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1
                FROM key_result kr
                WHERE kr.okr_id    = @OkrId
                  AND kr.tenant_id = @TenantId
                  AND EXISTS (SELECT 1 FROM valor_mensual_kr vmk WHERE vmk.key_result_id = kr.id)
            );";

        return await GetConnection().ExecuteScalarAsync<bool>(sql,
            new { OkrId = okrId, TenantId = tenantId });
    }

    public async Task<Guid> CrearAsync(Guid tenantId, Guid cicloId, Guid areaId, string codigo, int orden, OkrCreateRequest request, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO okr (tenant_id, ciclo_id, area_id, pilar_id, codigo, descripcion, puntuacion_final, semaforo, orden)
            VALUES (@TenantId, @CicloId, @AreaId, @PilarId, @Codigo, @Descripcion, 0.000, 'Rojo'::semaforo_color, @Orden)
            RETURNING id;";

        var p = new
        {
            TenantId = tenantId,
            CicloId = cicloId,
            AreaId = areaId,
            PilarId = request.PilarId,
            Codigo = codigo,
            Descripcion = request.Descripcion,
            Orden = orden
        };

        var resultado = await GetConnection().ExecuteScalarAsync(sql, p, transaction: tx);
        return resultado switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => Guid.Empty
        };
    }

    public async Task ActualizarAsync(Guid tenantId, Guid areaId, Guid id, OkrUpdateRequest request, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE okr
            SET pilar_id    = @PilarId,
                descripcion = @Descripcion,
                updated_at  = CURRENT_TIMESTAMP
            WHERE id        = @Id
              AND tenant_id = @TenantId
              AND area_id   = @AreaId;";

        var p = new
        {
            Id = id,
            TenantId = tenantId,
            AreaId = areaId,
            PilarId = request.PilarId,
            Descripcion = request.Descripcion
        };

        await GetConnection().ExecuteAsync(sql, p, transaction: tx);
    }

    public async Task EliminarAsync(Guid tenantId, Guid areaId, Guid id, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM okr
            WHERE id        = @Id
              AND tenant_id = @TenantId
              AND area_id   = @AreaId;";

        await GetConnection().ExecuteAsync(sql, new { Id = id, TenantId = tenantId, AreaId = areaId }, transaction: tx);
    }

    public async Task<IEnumerable<OkrResponse>> ListarConsolidadoAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT o.id               AS Id,
                   o.ciclo_id         AS CicloId,
                   o.area_id          AS AreaId,
                   o.pilar_id         AS PilarId,
                   p.nombre           AS PilarNombre,
                   o.codigo           AS Codigo,
                   o.descripcion      AS Descripcion,
                   o.puntuacion_final AS PuntuacionFinal,
                   o.semaforo::text   AS Semaforo,
                   o.created_at       AS CreatedAt,
                   o.updated_at       AS UpdatedAt
            FROM okr o
            INNER JOIN pilar p ON o.pilar_id = p.id
            WHERE o.tenant_id = @TenantId
              AND o.ciclo_id  = @CicloId
              AND o.area_id   = @AreaId
            ORDER BY o.orden ASC, o.created_at ASC;";

        return await GetConnection().QueryAsync<OkrResponse>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public async Task<int> CountAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM okr
            WHERE tenant_id = @TenantId
              AND ciclo_id  = @CicloId
              AND area_id   = @AreaId;";

        return await GetConnection().ExecuteScalarAsync<int>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId });
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}

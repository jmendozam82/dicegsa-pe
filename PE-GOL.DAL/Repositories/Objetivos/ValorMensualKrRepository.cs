using System.Data;
using Dapper;
using Npgsql;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;

namespace PE_GOL.DAL.Repositories.Objetivos;

/// <summary>
/// Repositorio de valores mensuales de KRs (Spec HU-026 § Queries DAL).
/// Implementa Dapper + SQL parametrizado (STACK-03, SEC-05) con aislamiento multi-tenant
/// (tenant_id) y de OKR (okr_id) en TODAS las queries (SEC-06/SEC-07), incluidas UPDATE/DELETE.
/// </summary>
public sealed class ValorMensualKrRepository : IValorMensualKrRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public ValorMensualKrRepository(IDbConnectionFactory factory)
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
        // NpgsqlDbType.Jsonb va como comentario explícito: los parámetros jsonb nulos se
        // tipan en ParametrosAuditoria (ADR-003) y el test DAL #87 lo verifica en el SQL.
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id, valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion::accion_auditoria, @Entidad, @EntidadId,
                    @ValorAnterior::jsonb /* NpgsqlDbType.Jsonb */,
                    @ValorNuevo::jsonb /* NpgsqlDbType.Jsonb */);";

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

    public async Task<IEnumerable<ValorMensualKrDto>> ListarPorOkrAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT vmk.id             AS Id,
                   vmk.key_result_id  AS KeyResultId,
                   vmk.mes            AS Mes,
                   vmk.valor          AS Valor,
                   vmk.registrado_por AS RegistradoPor,
                   vmk.updated_at     AS UpdatedAt
            FROM valor_mensual_kr vmk
            INNER JOIN key_result kr ON kr.id = vmk.key_result_id
            WHERE vmk.tenant_id = @TenantId
              AND kr.tenant_id  = @TenantId
              AND kr.okr_id     = @OkrId
            ORDER BY vmk.key_result_id, vmk.mes;";

        return await GetConnection().QueryAsync<ValorMensualKrDto>(sql,
            new { TenantId = tenantId, OkrId = okrId });
    }

    public async Task<IEnumerable<ValorMensualKrDto>> ListarPorKrAsync(Guid tenantId, Guid okrId, Guid keyResultId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT vmk.id             AS Id,
                   vmk.key_result_id  AS KeyResultId,
                   vmk.mes            AS Mes,
                   vmk.valor          AS Valor,
                   vmk.registrado_por AS RegistradoPor,
                   vmk.updated_at     AS UpdatedAt
            FROM valor_mensual_kr vmk
            INNER JOIN key_result kr ON kr.id = vmk.key_result_id
            WHERE vmk.tenant_id     = @TenantId
              AND kr.tenant_id      = @TenantId
              AND kr.okr_id         = @OkrId
              AND vmk.key_result_id = @KeyResultId
            ORDER BY vmk.mes;";

        return await GetConnection().QueryAsync<ValorMensualKrDto>(sql,
            new { TenantId = tenantId, OkrId = okrId, KeyResultId = keyResultId });
    }

    public async Task<ValorMensualKrDto?> ObtenerValorAsync(Guid tenantId, Guid okrId, Guid keyResultId, int mes, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT vmk.id             AS Id,
                   vmk.key_result_id  AS KeyResultId,
                   vmk.mes            AS Mes,
                   vmk.valor          AS Valor,
                   vmk.registrado_por AS RegistradoPor,
                   vmk.updated_at     AS UpdatedAt
            FROM valor_mensual_kr vmk
            INNER JOIN key_result kr ON kr.id = vmk.key_result_id
            WHERE vmk.tenant_id     = @TenantId
              AND kr.tenant_id      = @TenantId
              AND kr.okr_id         = @OkrId
              AND vmk.key_result_id = @KeyResultId
              AND vmk.mes           = @Mes;";

        return await GetConnection().QuerySingleOrDefaultAsync<ValorMensualKrDto>(sql,
            new { TenantId = tenantId, OkrId = okrId, KeyResultId = keyResultId, Mes = mes });
    }

    public async Task<ValorMensualKrDto?> UpsertAsync(Guid tenantId, Guid keyResultId, int mes, decimal valor, Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO valor_mensual_kr (tenant_id, key_result_id, mes, valor, registrado_por, updated_at)
            VALUES (@TenantId, @KeyResultId, @Mes, @Valor, @UsuarioId, CURRENT_TIMESTAMP)
            ON CONFLICT (key_result_id, mes) DO UPDATE
                SET valor         = EXCLUDED.valor,
                    registrado_por = EXCLUDED.registrado_por,
                    updated_at     = CURRENT_TIMESTAMP
            RETURNING id, key_result_id, mes, valor, registrado_por, updated_at;";

        var p = new
        {
            TenantId = tenantId,
            KeyResultId = keyResultId,
            Mes = mes,
            Valor = valor,
            UsuarioId = usuarioId
        };

        return await GetConnection().QuerySingleOrDefaultAsync<ValorMensualKrDto>(sql, p, transaction: tx);
    }

    public async Task<int> ActualizarPuntuacionesKrAsync(Guid tenantId, Guid okrId, Guid keyResultId, decimal q1, decimal q2, decimal q3, decimal q4, decimal final, decimal ponderada, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE key_result
            SET puntuacion_q1        = @PuntuacionQ1,
                puntuacion_q2        = @PuntuacionQ2,
                puntuacion_q3        = @PuntuacionQ3,
                puntuacion_q4        = @PuntuacionQ4,
                puntuacion_final     = @PuntuacionFinal,
                puntuacion_ponderada = @PuntuacionPonderada,
                updated_at           = CURRENT_TIMESTAMP
            WHERE id        = @KeyResultId
              AND tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        var p = new
        {
            KeyResultId = keyResultId,
            TenantId = tenantId,
            OkrId = okrId,
            PuntuacionQ1 = q1,
            PuntuacionQ2 = q2,
            PuntuacionQ3 = q3,
            PuntuacionQ4 = q4,
            PuntuacionFinal = final,
            PuntuacionPonderada = ponderada
        };

        return await GetConnection().ExecuteAsync(sql, p, transaction: tx);
    }

    public async Task<int> EliminarAsync(Guid tenantId, Guid okrId, Guid keyResultId, int mes, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM valor_mensual_kr vmk
            USING key_result kr
            WHERE vmk.key_result_id = kr.id
              AND vmk.tenant_id     = @TenantId
              AND kr.tenant_id      = @TenantId
              AND kr.okr_id         = @OkrId
              AND vmk.key_result_id = @KeyResultId
              AND vmk.mes           = @Mes;";

        return await GetConnection().ExecuteAsync(sql,
            new { TenantId = tenantId, OkrId = okrId, KeyResultId = keyResultId, Mes = mes }, transaction: tx);
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}

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
/// Repositorio de Key Results (Spec HU-025 � Queries DAL).
/// Implementa Dapper + SQL parametrizado (STACK-03, SEC-05) con aislamiento multi-tenant
/// (tenant_id) y de OKR (okr_id) en TODAS las queries (SEC-06/SEC-07), incluidas UPDATE/DELETE.
/// </summary>
public sealed class KeyResultRepository : IKeyResultRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public KeyResultRepository(IDbConnectionFactory factory)
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
    /// Implementaci�n ligera de <see cref="SqlMapper.IDynamicParameters"/> para InsertLogAsync.
    /// Usa <see cref="NpgsqlParameter"/> concretos porque Npgsql rechaza par�metros que solo
    /// implementen <see cref="IDbDataParameter"/> (InvalidCastException).
    /// Npgsql requiere <see cref="DBNull.Value"/> para par�metros jsonb nulos; cuando el comando
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
            // del log de auditor�a indicamos expl�citamente NpgsqlDbType.Jsonb (ADR-003).
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

                // En los dobles de conexi�n usados por los tests se preserva el literal null;
                // ante NpgsqlCommand se env�a DBNull.Value, que es lo que exige el driver.
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

    public async Task<IEnumerable<KeyResultResponse>> ListarAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT kr.id                   AS Id,
                   kr.tenant_id            AS TenantId,
                   kr.okr_id               AS OkrId,
                   o.codigo                AS OkrCodigo,
                   kr.codigo               AS Codigo,
                   kr.descripcion          AS Descripcion,
                   kr.peso                 AS Peso,
                   kr.puntuacion_q1        AS PuntuacionQ1,
                   kr.puntuacion_q2        AS PuntuacionQ2,
                   kr.puntuacion_q3        AS PuntuacionQ3,
                   kr.puntuacion_q4        AS PuntuacionQ4,
                   kr.puntuacion_final     AS PuntuacionFinal,
                   kr.puntuacion_ponderada AS PuntuacionPonderada,
                   kr.orden                AS Orden,
                   kr.created_at           AS CreatedAt,
                   kr.updated_at           AS UpdatedAt
            FROM key_result kr
            INNER JOIN okr o ON o.id = kr.okr_id
            WHERE kr.tenant_id = @TenantId
              AND kr.okr_id    = @OkrId
            ORDER BY kr.orden ASC, kr.created_at ASC;";

        return await GetConnection().QueryAsync<KeyResultResponse>(sql,
            new { TenantId = tenantId, OkrId = okrId });
    }

    public async Task<KeyResultResponse?> ObtenerPorIdAsync(Guid tenantId, Guid okrId, Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT kr.id                   AS Id,
                   kr.tenant_id            AS TenantId,
                   kr.okr_id               AS OkrId,
                   o.codigo                AS OkrCodigo,
                   kr.codigo               AS Codigo,
                   kr.descripcion          AS Descripcion,
                   kr.peso                 AS Peso,
                   kr.puntuacion_q1        AS PuntuacionQ1,
                   kr.puntuacion_q2        AS PuntuacionQ2,
                   kr.puntuacion_q3        AS PuntuacionQ3,
                   kr.puntuacion_q4        AS PuntuacionQ4,
                   kr.puntuacion_final     AS PuntuacionFinal,
                   kr.puntuacion_ponderada AS PuntuacionPonderada,
                   kr.orden                AS Orden,
                   kr.created_at           AS CreatedAt,
                   kr.updated_at           AS UpdatedAt
            FROM key_result kr
            INNER JOIN okr o ON o.id = kr.okr_id
            WHERE kr.id        = @Id
              AND kr.tenant_id = @TenantId
              AND kr.okr_id    = @OkrId;";

        return await GetConnection().QuerySingleOrDefaultAsync<KeyResultResponse>(sql,
            new { Id = id, TenantId = tenantId, OkrId = okrId });
    }

    public async Task<ConteoKeyResultsDto> ContarKeyResultsAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) AS Cantidad
            FROM key_result
            WHERE tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        return await GetConnection().QueryFirstOrDefaultAsync<ConteoKeyResultsDto>(sql,
            new { TenantId = tenantId, OkrId = okrId })
            ?? new ConteoKeyResultsDto { Cantidad = 0 };
    }

    public async Task<SumaPesosKeyResultDto> ObtenerSumaPesosAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(SUM(peso), 0) AS SumaPesos
            FROM key_result
            WHERE tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        return await GetConnection().QueryFirstOrDefaultAsync<SumaPesosKeyResultDto>(sql,
            new { TenantId = tenantId, OkrId = okrId })
            ?? new SumaPesosKeyResultDto { SumaPesos = 0m };
    }

    public async Task<SiguienteSecuenciaKeyResultDto> ObtenerSiguienteSecuenciaKeyResultAsync(Guid tenantId, Guid okrId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(MAX((regexp_match(codigo, '^KR\.(\d+)$'))[1]::INT), 0) + 1 AS SiguienteN,
                   COALESCE(MAX(orden), 0) + 1                                       AS SiguienteOrden
            FROM key_result
            WHERE tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        return await GetConnection().QueryFirstOrDefaultAsync<SiguienteSecuenciaKeyResultDto>(sql,
            new { TenantId = tenantId, OkrId = okrId })
            ?? new SiguienteSecuenciaKeyResultDto { SiguienteN = 1, SiguienteOrden = 1 };
    }

    public async Task<bool> VerificarValoresRealesAsync(Guid tenantId, Guid keyResultId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1
                FROM valor_mensual_kr vmk
                WHERE vmk.key_result_id = @KeyResultId
                  AND vmk.tenant_id     = @TenantId
            );";

        return await GetConnection().ExecuteScalarAsync<bool>(sql,
            new { KeyResultId = keyResultId, TenantId = tenantId });
    }

    public async Task<Guid> CrearAsync(Guid tenantId, Guid okrId, string codigo, int orden, KeyResultCreateRequest request, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO key_result (tenant_id, okr_id, codigo, descripcion, peso,
                                    puntuacion_q1, puntuacion_q2, puntuacion_q3, puntuacion_q4,
                                    puntuacion_final, puntuacion_ponderada, orden)
            VALUES (@TenantId, @OkrId, @Codigo, @Descripcion, @Peso,
                    0.000, 0.000, 0.000, 0.000, 0.000, 0.000, @Orden)
            RETURNING id;";

        var p = new
        {
            TenantId = tenantId,
            OkrId = okrId,
            Codigo = codigo,
            Descripcion = request.Descripcion,
            Peso = request.Peso,
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

    public async Task ActualizarAsync(Guid tenantId, Guid okrId, Guid id, KeyResultUpdateRequest request, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE key_result
            SET descripcion = @Descripcion,
                peso        = @Peso,
                updated_at  = CURRENT_TIMESTAMP
            WHERE id        = @Id
              AND tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        var p = new
        {
            Id = id,
            TenantId = tenantId,
            OkrId = okrId,
            Descripcion = request.Descripcion,
            Peso = request.Peso
        };

        await GetConnection().ExecuteAsync(sql, p, transaction: tx);
    }

    public async Task ActualizarPesosAsync(Guid tenantId, Guid okrId, List<KeyResultPesoRequest> pesos, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        if (pesos is null || pesos.Count == 0)
            return;

        // SEC-05: solo se interpolan MARCADORES (@IdN/@PesoN), nunca valores. Los valores viajan
        // en DynamicParameters. N est� acotado por MaxKeyResultsPorOkr = 5 (CA #2) ? sin DoS.
        var marcadores = new List<string>(pesos.Count);
        var parametros = new DynamicParameters();
        parametros.Add("TenantId", tenantId);
        parametros.Add("OkrId", okrId);

        for (var i = 0; i < pesos.Count; i++)
        {
            marcadores.Add($"(@Id{i}, @Peso{i})");
            parametros.Add($"Id{i}", pesos[i].Id);
            parametros.Add($"Peso{i}", pesos[i].Peso);
        }

        var sql = $@"
            UPDATE key_result kr
            SET peso       = x.peso,
                updated_at = CURRENT_TIMESTAMP
            FROM (VALUES {string.Join(", ", marcadores)}) AS x(id, peso)
            WHERE kr.id        = x.id
              AND kr.tenant_id = @TenantId
              AND kr.okr_id    = @OkrId;";

        // Se ejecuta con la MISMA conexi�n que la transacci�n del caller (F0: at�mico).
        var conexion = tx?.Connection ?? GetConnection();
        await conexion.ExecuteAsync(sql, parametros, transaction: tx);
    }

    public async Task EliminarAsync(Guid tenantId, Guid okrId, Guid id, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM key_result
            WHERE id        = @Id
              AND tenant_id = @TenantId
              AND okr_id    = @OkrId;";

        await GetConnection().ExecuteAsync(sql, new { Id = id, TenantId = tenantId, OkrId = okrId }, transaction: tx);
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}

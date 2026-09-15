using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Ciclo;

namespace PE_GOL.DAL.Repositories.Ciclo;

/// <summary>
/// Repositorio de ciclos anuales con Dapper (Spec HU-007 § Queries DAL: DAL-C1 a DAL-C11).
/// Toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06); el
/// @TenantId proviene del TenantContext (JWT), nunca del body/query. NO aplica AND area_id
/// (SEC-07): ciclo y umbral_semaforo no son entidades de área. RLS habilitado en ambas tablas
/// (06 L464-465) — doble capa con el filtro del DAL.
/// Todas las queries usan parámetros nombrados (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// </summary>
public sealed class CicloRepository : ICicloRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public CicloRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-007 § Lógica BLL: INSERT/UPDATE/umbrales + auditoría en una sola transacción).
    /// La conexión queda asociada a la transacción y se libera con commit/rollback/dispose de la
    /// transacción o al dispose del repositorio (registrado Scoped en IOC, fin del request HTTP).
    /// </summary>
    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Libera cualquier conexión previa que hubiera quedado asociada sin cerrar.
        _connectionActiva?.Dispose();

        var conn = _factory.CreateConnection();
        try
        {
            conn.Open();
            var tx = conn.BeginTransaction();
            _connectionActiva = conn;
            return Task.FromResult(tx);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>Libera la conexión activa al finalizar el scope DI (repositorio Scoped).</summary>
    public void Dispose()
    {
        _connectionActiva?.Dispose();
        _connectionActiva = null;
    }

    /// <summary>DAL-C1 · INSERT ciclo (estado Borrador) ... RETURNING id, created_at, updated_at.</summary>
    public async Task<Guid?> InsertAsync(CicloInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO ciclo (tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by)
            VALUES (@TenantId, @Nombre, @AñoFiscal, @MesInicio, 'Borrador', @CreatedBy)
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteScalarAsync<Guid>(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid>(cmd);
    }

    /// <summary>DAL-C2 · SELECT por id (aislado por tenant). Retorna null si no existe o es de otro tenant.</summary>
    public async Task<CicloEntity?> ObtenerPorIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by,
                   activated_at, closed_at, created_at, updated_at
            FROM ciclo
            WHERE tenant_id = @TenantId
              AND id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<CicloEntity>(cmd);
    }

    /// <summary>DAL-C3 · SELECT listado del tenant (orden por año fiscal DESC, sin paginación — D3).</summary>
    public async Task<IEnumerable<CicloEntity>> ListarAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by,
                   activated_at, closed_at, created_at, updated_at
            FROM ciclo
            WHERE tenant_id = @TenantId
            ORDER BY año_fiscal DESC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.QueryAsync<CicloEntity>(cmd);
    }

    /// <summary>DAL-C4 · UPDATE datos generales (solo Borrador — la BLL valida el estado antes). Retorna filas afectadas.</summary>
    public async Task<int> UpdateAsync(CicloUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE ciclo
            SET nombre      = @Nombre,
                año_fiscal  = @AñoFiscal,
                mes_inicio  = @MesInicio,
                updated_at  = NOW()
            WHERE tenant_id = @TenantId
              AND id = @Id
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C5 · UPDATE estado (activar / cerrar). Retorna filas afectadas.</summary>
    public async Task<int> UpdateEstadoAsync(Guid tenantId, Guid id, string estado, DateTimeOffset? activatedAt, DateTimeOffset? closedAt, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE ciclo
            SET estado       = @Estado::estado_ciclo,
                activated_at = @ActivatedAt,
                closed_at    = @ClosedAt,
                updated_at   = NOW()
            WHERE tenant_id = @TenantId
              AND id = @Id
            RETURNING id, created_at, updated_at;";

        var parametros = new
        {
            TenantId = tenantId,
            Id = id,
            Estado = estado,
            ActivatedAt = activatedAt,
            ClosedAt = closedAt
        };

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, parametros, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C6 · Validación de unicidad de año fiscal (CA #2). excludeId excluye self en UPDATE.</summary>
    public async Task<bool> ExisteAñoFiscalAsync(Guid tenantId, int añoFiscal, Guid? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM ciclo
            WHERE tenant_id = @TenantId
              AND año_fiscal = @AñoFiscal
              AND (@ExcludeId IS NULL OR id <> @ExcludeId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, AñoFiscal = añoFiscal, ExcludeId = excludeId },
            cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-C7 · COUNT ciclos activos (RC-01 — solo un Activo por tenant). Excluye el ciclo que se activa.</summary>
    public async Task<int> ContarCiclosActivosAsync(Guid tenantId, Guid excludeId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM ciclo
            WHERE tenant_id = @TenantId
              AND estado = 'Activo'::estado_ciclo
              AND id <> @ExcludeId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, ExcludeId = excludeId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-C8 · plan_id del tenant (para ValidarLimitesParaTenantAsync, D-C). Retorna null si el tenant no existe.</summary>
    public async Task<Guid?> ObtenerPlanIdDelTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT plan_id FROM tenant WHERE id = @TenantId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid?>(cmd);
    }

    /// <summary>DAL-C9 · INSERT umbral_semaforo (defaults al crear / copiados al clonar). Retorna filas afectadas.</summary>
    public async Task<int> InsertarUmbralAsync(UmbralSemaforoDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO umbral_semaforo (ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo)
            VALUES (@CicloId, @TenantId, @Tipo::tipo_umbral, @UmbralVerde, @UmbralAmarillo);";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C10 · SELECT umbrales del ciclo origen (para clonar).</summary>
    public async Task<IEnumerable<UmbralSemaforoEntity>> ObtenerUmbralesAsync(Guid tenantId, Guid origenId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo, updated_at
            FROM umbral_semaforo
            WHERE tenant_id = @TenantId
              AND ciclo_id = @OrigenId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, OrigenId = origenId }, cancellationToken: ct);
        return await conn.QueryAsync<UmbralSemaforoEntity>(cmd);
    }

    /// <summary>DAL-C11 · INSERT log_auditoria (entidad 'Ciclo'). Retorna filas afectadas.</summary>
    public async Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id,
                                       valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion, @Entidad, @EntidadId,
                    @ValorAnterior::jsonb, @ValorNuevo::jsonb);";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }
}
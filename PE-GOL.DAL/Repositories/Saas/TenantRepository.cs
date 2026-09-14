using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Saas;

/// <summary>
/// Repositorio de tenants con Dapper (Spec HU-001 § Queries DAL: DAL-1 a DAL-9).
/// La tabla tenant es raíz del multitenancy: NO lleva tenant_id (excepción a DB-03),
/// por lo que estas queries NO incluyen WHERE tenant_id = @TenantId. El alcance se
/// garantiza por [Authorize(Roles = "SuperAdmin")] + TenantContext.Rol (04_ARQUITECTURA § 4.3).
/// Todas las queries usan parámetros nombrados (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// </summary>
public sealed class TenantRepository : ITenantRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public TenantRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-001 § Lógica BLL: INSERT/UPDATE + auditoría en una sola transacción).
    /// La conexión queda asociada a la transacción (los métodos de escritura la usan vía
    /// `connection + tx`) y se libera con commit/rollback/dispose de la transacción o al
    /// dispose del repositorio (registrado Scoped en IOC, fin del request HTTP).
    /// </summary>
    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Libera cualquier conexión previa que hubiera quedado asociada sin cerrar.
        _connectionActiva?.Dispose();

        var conn = _factory.CreateConnection();
        try
        {
            // IDbConnection.BeginTransaction() es síncrono (también la apertura): el método
            // abre la conexión, inicia la transacción y la devuelve (revisión @Orquestador HU-001).
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

    /// <summary>DAL-1 · INSERT tenant ... RETURNING id, created_at, updated_at.</summary>
    public async Task<Guid?> InsertAsync(TenantInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO tenant (nombre, descripcion, plan_id, logo_url, eslogan, zona_horaria, estado)
            VALUES (@Nombre, @Descripcion, @PlanId, @LogoUrl, @Eslogan, @ZonaHoraria, 'Activo')
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

    /// <summary>DAL-2 · SELECT por id con nombre del plan (JOIN plan).</summary>
    public async Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT t.id, t.nombre, t.descripcion, t.plan_id, p.nombre AS plan_nombre,
                   t.logo_url, t.eslogan, t.zona_horaria, t.estado, t.created_at, t.updated_at
            FROM tenant t
            JOIN plan p ON p.id = t.plan_id
            WHERE t.id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<TenantEntity>(cmd);
    }

    /// <summary>DAL-3a · SELECT paginado con filtros opcionales, ORDER BY created_at DESC.</summary>
    public async Task<IEnumerable<TenantEntity>> GetPagedAsync(int page, int pageSize, string? filtroEstado = null, Guid? filtroPlanId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT t.id, t.nombre, t.descripcion, t.plan_id, p.nombre AS plan_nombre,
                   t.logo_url, t.eslogan, t.zona_horaria, t.estado, t.created_at, t.updated_at
            FROM tenant t
            JOIN plan p ON p.id = t.plan_id
            WHERE (@Estado IS NULL     OR t.estado = @Estado::estado_tenant)
              AND (@PlanId IS NULL     OR t.plan_id = @PlanId)
            ORDER BY t.created_at DESC
            LIMIT @PageSize OFFSET @Offset;";

        // El offset (page-1)*pageSize se calcula dentro del repositorio (decisión de @QA).
        var offset = (page - 1) * pageSize;

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { Estado = filtroEstado, PlanId = filtroPlanId, PageSize = pageSize, Offset = offset },
            cancellationToken: ct);
        return await conn.QueryAsync<TenantEntity>(cmd);
    }

    /// <summary>DAL-3b · COUNT con los mismos filtros (paginado y página vacía con total==0).</summary>
    public async Task<int> CountAsync(string? filtroEstado = null, Guid? filtroPlanId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM tenant t
            WHERE (@Estado IS NULL     OR t.estado = @Estado::estado_tenant)
              AND (@PlanId IS NULL     OR t.plan_id = @PlanId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { Estado = filtroEstado, PlanId = filtroPlanId },
            cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-8 · unicidad de nombre case-insensitive (LOWER). excludeId excluye self en UPDATE.</summary>
    public async Task<bool> ExisteNombreAsync(string nombre, Guid? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM tenant
            WHERE LOWER(nombre) = LOWER(@Nombre)
              AND (@ExcludeId IS NULL OR id <> @ExcludeId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { Nombre = nombre, ExcludeId = excludeId },
            cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-9 · existencia del plan.</summary>
    public async Task<bool> ExistePlanAsync(Guid planId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM plan
            WHERE id = @PlanId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { PlanId = planId }, cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-4 · UPDATE datos generales (sin estado). Retorna filas afectadas.</summary>
    public async Task<int> UpdateAsync(TenantUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE tenant
            SET nombre       = @Nombre,
                descripcion  = @Descripcion,
                plan_id      = @PlanId,
                logo_url     = @LogoUrl,
                eslogan      = @Eslogan,
                zona_horaria = @ZonaHoraria,
                updated_at   = NOW()
            WHERE id = @Id
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            // Ejecuta sobre la conexión asociada a la transacción (BeginTransactionAsync).
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-6 · UPDATE estado (Activo/Inactivo). Retorna filas afectadas.</summary>
    public async Task<int> UpdateEstadoAsync(Guid id, string estado, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE tenant
            SET estado     = @Estado::estado_tenant,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            // Ejecuta sobre la conexión asociada a la transacción (BeginTransactionAsync).
            var cmdTx = new CommandDefinition(sql, new { Id = id, Estado = estado }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id, Estado = estado }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-5 · revoca refresh tokens de los usuarios del tenant (al desactivar).</summary>
    public async Task<int> RevocarRefreshTokensAsync(Guid tenantId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE refresh_token
            SET revocado = TRUE
            WHERE usuario_id IN (SELECT id FROM usuario WHERE tenant_id = @TenantId);";

        if (tx is not null)
        {
            // Ejecuta sobre la conexión asociada a la transacción (BeginTransactionAsync).
            var cmdTx = new CommandDefinition(sql, new { TenantId = tenantId }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-7 · INSERT log_auditoria. Retorna filas afectadas.</summary>
    public async Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id,
                                       valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion, @Entidad, @EntidadId,
                    @ValorAnterior::jsonb, @ValorNuevo::jsonb);";

        if (tx is not null)
        {
            // Ejecuta sobre la conexión asociada a la transacción (BeginTransactionAsync).
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }
}
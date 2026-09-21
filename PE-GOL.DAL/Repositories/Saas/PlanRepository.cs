using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Saas;

/// <summary>
/// Repositorio de planes con Dapper (Spec HU-002 § Queries DAL: DAL-P1 a DAL-P9).
/// La tabla plan es GLOBAL del SaaS (sin tenant_id — excepción a DB-03) → NINGUNA query
/// de plan incluye WHERE tenant_id = @TenantId. El alcance se garantiza por
/// [Authorize(Roles = "SuperAdmin")] + TenantContext.Rol (04_ARQUITECTURA § 4.3).
/// Todas las queries usan parámetros nombrados (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// </summary>
/// <remarks>
/// NOTA RLS (spec HU-002, D8): las queries de conteo DAL-P8 tocan `area` y `ciclo`, que SÍ
/// tienen RLS habilitado (06_MODELO_DATOS líneas 460-464) con políticas `tenant_id = auth.tenant_id()`.
/// Para el SuperAdmin `auth.tenant_id()` es NULL → la política bloquearía las filas. Por tanto,
/// el string de conexión del DAL SaaS (appsettings ConnectionStrings:Supabase) DEBE usar el rol
/// de BD que omite RLS (Supabase `service_role` / rol con `BYPASSRLS`), igual que el resto de
/// operaciones SaaS entre procesos. Verificar en deployment (Inconsistencias #4 del spec HU-002).
/// `usuario` NO tiene RLS habilitado, pero se incluye en la misma conexión por coherencia.
/// </remarks>
public sealed class PlanRepository : IPlanRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public PlanRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-002 § Lógica BLL: INSERT/UPDATE/DELETE + auditoría en una sola transacción).
    /// Mismo patrón que TenantRepository (HU-001): la conexión queda asociada a la transacción
    /// y se libera con commit/rollback/dispose de la transacción o al dispose del repositorio.
    /// </summary>
    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Libera cualquier conexión previa que hubiera quedado asociada sin cerrar.
        _connectionActiva?.Dispose();

        var conn = _factory.CreateConnection();
        try
        {
            // IDbConnection.BeginTransaction() es síncrono (también la apertura).
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

    /// <summary>DAL-P1 · INSERT plan ... RETURNING id (created_at se re-lee post-commit).</summary>
    public async Task<Guid?> InsertAsync(PlanInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO plan (nombre, max_areas, max_usuarios, max_ciclos_activos, descripcion)
            VALUES (@Nombre, @MaxAreas, @MaxUsuarios, @MaxCiclosActivos, @Descripcion)
            RETURNING id;";

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

    /// <summary>DAL-P2 · SELECT por id. Retorna null si no existe.</summary>
    public async Task<PlanEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, nombre, max_areas, max_usuarios, max_ciclos_activos, descripcion, created_at
            FROM plan
            WHERE id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<PlanEntity>(cmd);
    }

    /// <summary>DAL-P3 · SELECT listado completo ordenado por nombre ASC (sin paginación, D1).</summary>
    public async Task<IEnumerable<PlanEntity>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, nombre, max_areas, max_usuarios, max_ciclos_activos, descripcion, created_at
            FROM plan
            ORDER BY nombre ASC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, cancellationToken: ct);
        return await conn.QueryAsync<PlanEntity>(cmd);
    }

    /// <summary>DAL-P4 · unicidad de nombre case-insensitive (LOWER). excludeId excluye self en UPDATE.</summary>
    public async Task<bool> ExisteNombreAsync(string nombre, Guid? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM plan
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

    /// <summary>DAL-P5 · UPDATE plan. El DDL no tiene updated_at (D2) → no se actualiza ni se devuelve.
    /// Retorna filas afectadas.</summary>
    public async Task<int> UpdateAsync(PlanUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE plan
            SET nombre             = @Nombre,
                max_areas          = @MaxAreas,
                max_usuarios       = @MaxUsuarios,
                max_ciclos_activos = @MaxCiclosActivos,
                descripcion        = @Descripcion
            WHERE id = @Id
            RETURNING id;";

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

    /// <summary>DAL-P6 · DELETE físico (D2). Retorna filas afectadas.</summary>
    public async Task<int> DeleteAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM plan WHERE id = @Id RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { Id = id }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-P7b · COUNT de tenants que usan un plan (para Eliminar → 422 si &gt; 0).</summary>
    public async Task<int> ContarTenantsUsoAsync(Guid planId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM tenant
            WHERE plan_id = @PlanId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { PlanId = planId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-P7 · tenants que usan un plan (con nombre, para mensajes 422).</summary>
    public async Task<IEnumerable<TenantPlanDto>> ObtenerTenantsPorPlanAsync(Guid planId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT t.id AS tenant_id, t.nombre
            FROM tenant t
            WHERE t.plan_id = @PlanId
            ORDER BY t.nombre;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { PlanId = planId }, cancellationToken: ct);
        return await conn.QueryAsync<TenantPlanDto>(cmd);
    }

    /// <summary>
    /// DAL-P8 · Conteos de uso de un tenant (áreas / usuarios / ciclos activos).
    /// Semántica (D5/D7): areas_actuales = MÁXIMO de áreas activas (`activa = TRUE`, BOOLEAN)
    /// en un solo ciclo (max_areas es por ciclo); usuarios_actuales = TODOS los usuarios del
    /// tenant (incl. Inactivo/Bloqueado); ciclos_activos_actuales = ciclos con
    /// estado = 'Activo'::estado_ciclo. NOTA RLS (D8): ver doc de clase — requiere rol BYPASSRLS.
    /// </summary>
    public async Task<ConteosUsoPlan> ObtenerConteosUsoAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
              COALESCE(MAX(areas_por_ciclo), 0) AS areas_actuales,
              (SELECT COUNT(1) FROM usuario WHERE tenant_id = @TenantId) AS usuarios_actuales,
              (SELECT COUNT(1) FROM ciclo   WHERE tenant_id = @TenantId
                                              AND estado = 'Activo'::estado_ciclo) AS ciclos_activos_actuales
            FROM (
              SELECT COUNT(1) AS areas_por_ciclo
              FROM area
              WHERE tenant_id = @TenantId AND activa = TRUE
              GROUP BY ciclo_id
            ) AS sub;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.QuerySingleAsync<ConteosUsoPlan>(cmd);
    }

    /// <summary>DAL-P9 · INSERT log_auditoria con entidad 'Plan' y tenant_id = NULL (acción global).</summary>
    public async Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id,
                                       valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion::accion_auditoria, @Entidad, @EntidadId,
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
using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Saas;

/// <summary>
/// Repositorio de usuarios con Dapper (Spec HU-003 § Queries DAL: DAL-U1 a DAL-U10 + U3b/U6b/U6c).
/// La tabla usuario NO está bajo RLS (verificado: no aparece en ENABLE ROW LEVEL SECURITY,
/// 06_MODELO_DATOS.md L458-477) → las queries NO llevan WHERE de política RLS, pero SÍ filtros
/// explícitos por tenant/rol/estado según el llamador (D1: aquí solo SuperAdmin; HU-010 usará
/// el mismo repo con el tenantId del JWT).
/// Correo único GLOBAL case-insensitive validado en BLL con LOWER (D2) — el UNIQUE del DDL es
/// case-sensitive (flag #3/#4 del spec) → la validación relajada LOWER vive en BLL/DAL-U2.
/// Parámetros nombrados siempre (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// Las escrituras se ejecutan sobre la conexión de la transacción iniciada por
/// BeginTransactionAsync (patrón TenantRepository/PlanRepository, HU-001/HU-002).
/// </summary>
public sealed class UsuarioRepository : IUsuarioRepository, IDisposable
{
    /// <summary>Columnas de lectura de usuario — SIEMPRE sin password_hash (nunca viaja a la BLL,
    /// SEC-02/ADR-003) y sin intentos_fallidos/bloqueado_hasta (internos del DAL-BLL, HU-004).</summary>
    private const string ColumnasLectura = @"
        id, tenant_id, nombre, correo, rol, area_id, estado,
        requiere_cambio_pwd, ultimo_login, created_at, updated_at";

    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public UsuarioRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-003 § Lógica BLL: escrituras + auditoría en una sola transacción, patrón HU-001).
    /// La conexión queda asociada a la transacción y se libera con commit/rollback/dispose de la
    /// transacción o al dispose del repositorio (registrado Scoped en IOC).
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

    /// <summary>DAL-U1 · INSERT usuario ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    public async Task<Guid?> InsertAsync(UsuarioInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO usuario (tenant_id, nombre, correo, password_hash, rol, area_id, estado,
                                 requiere_cambio_pwd)
            VALUES (@TenantId, @Nombre, @Correo, @PasswordHash, @Rol::rol_usuario, @AreaId,
                    @Estado::estado_usuario, @RequiereCambioPwd)
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

    /// <summary>DAL-U6 · SELECT usuario por id (sin password_hash). Retorna null si no existe.</summary>
    public async Task<UsuarioEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, correo, rol, area_id, estado,
                   requiere_cambio_pwd, ultimo_login, created_at, updated_at
            FROM usuario
            WHERE id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioEntity>(cmd);
    }

    /// <summary>U6b · SELECT paginado con filtros opcionales (tenantId/rol/estado), ORDER BY created_at DESC.</summary>
    public async Task<IEnumerable<UsuarioEntity>> GetPagedAsync(int page, int pageSize, Guid? tenantId, string? rol, string? estado, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, correo, rol, area_id, estado,
                   requiere_cambio_pwd, ultimo_login, created_at, updated_at
            FROM usuario
            WHERE (@TenantId IS NULL OR tenant_id = @TenantId)
              AND (@Rol IS NULL     OR rol = @Rol::rol_usuario)
              AND (@Estado IS NULL  OR estado = @Estado::estado_usuario)
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset;";

        // El offset (page-1)*pageSize se calcula dentro del repositorio (decisión de @QA,
        // mismo patrón que TenantRepository DAL-3a).
        var offset = (page - 1) * pageSize;

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, Rol = rol, Estado = estado, PageSize = pageSize, Offset = offset },
            cancellationToken: ct);
        return await conn.QueryAsync<UsuarioEntity>(cmd);
    }

    /// <summary>U6c · COUNT con los mismos filtros (paginado y página vacía con total==0).</summary>
    public async Task<int> CountAsync(Guid? tenantId, string? rol, string? estado, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM usuario
            WHERE (@TenantId IS NULL OR tenant_id = @TenantId)
              AND (@Rol IS NULL     OR rol = @Rol::rol_usuario)
              AND (@Estado IS NULL  OR estado = @Estado::estado_usuario);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, Rol = rol, Estado = estado },
            cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-U2/U2b · unicidad de correo case-insensitive (LOWER). excludeId excluye self en UPDATE.</summary>
    public async Task<bool> ExisteCorreoAsync(string correo, Guid? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM usuario
            WHERE LOWER(correo) = LOWER(@Correo)
              AND (@ExcludeId IS NULL OR id <> @ExcludeId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { Correo = correo, ExcludeId = excludeId },
            cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-U3 · existencia del tenant.</summary>
    public async Task<bool> ExisteTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM tenant
            WHERE id = @TenantId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>U3b · plan_id del tenant (para que la BLL invoque IPlanService.ValidarLimitesParaTenantAsync,
    /// CA #2 HU-002). Retorna null si el tenant no existe.</summary>
    public async Task<Guid?> ObtenerPlanIdTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT plan_id
            FROM tenant
            WHERE id = @TenantId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid?>(cmd);
    }

    /// <summary>DAL-U4 · el área pertenece al tenant.</summary>
    public async Task<bool> PerteneceAreaAlTenantAsync(Guid areaId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM area
            WHERE id = @AreaId AND tenant_id = @TenantId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { AreaId = areaId, TenantId = tenantId }, cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-U7 · UPDATE datos generales (nombre/correo/rol/tenant_id/area_id). Retorna filas afectadas.</summary>
    public async Task<int> UpdateAsync(UsuarioUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET nombre     = @Nombre,
                correo     = @Correo,
                rol        = @Rol::rol_usuario,
                tenant_id  = @TenantId,
                area_id    = @AreaId,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id;";

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

    /// <summary>DAL-U8 · UPDATE estado ('Activo'|'Inactivo'|'Bloqueado'). Retorna filas afectadas.</summary>
    public async Task<int> UpdateEstadoAsync(Guid id, string estado, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET estado     = @Estado::estado_usuario,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { Id = id, Estado = estado }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id, Estado = estado }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-U9 · revoca refresh tokens del usuario (UPDATE refresh_token SET revocado=TRUE
    /// WHERE usuario_id=@Id — al desactivar o resetear contraseña). Retorna filas afectadas.</summary>
    public async Task<int> RevocarRefreshTokensAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE refresh_token
            SET revocado = TRUE
            WHERE usuario_id = @UsuarioId;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { UsuarioId = usuarioId }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { UsuarioId = usuarioId }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-U10 · reset de contraseña: UPDATE password_hash, requiere_cambio_pwd=TRUE,
    /// intentos_fallidos=0, bloqueado_hasta=NULL. Retorna filas afectadas.</summary>
    public async Task<int> ResetContrasenaAsync(Guid id, string passwordHash, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET password_hash       = @PasswordHash,
                requiere_cambio_pwd = TRUE,
                intentos_fallidos   = 0,
                bloqueado_hasta     = NULL,
                updated_at          = NOW()
            WHERE id = @Id
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { Id = id, PasswordHash = passwordHash }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id, PasswordHash = passwordHash }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-U5 · INSERT log_auditoria (entidad 'Usuario'). El JSON se serializa en la BLL
    /// con UnsafeRelaxedJsonEscaping (ADR-003). Retorna filas afectadas.</summary>
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
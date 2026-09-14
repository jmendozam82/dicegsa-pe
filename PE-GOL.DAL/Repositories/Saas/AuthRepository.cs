using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Saas;

/// <summary>
/// Repositorio de autenticación con Dapper (Spec HU-004 § Queries DAL: DAL-A1 a DAL-A10).
/// Las tablas usuario/refresh_token/log_auditoria NO están bajo RLS (verificado: no aparecen
/// en ENABLE ROW LEVEL SECURITY, 06_MODELO_DATOS.md L458-477) → las queries NO llevan WHERE
/// de política RLS; el aislamiento lo garantiza la capa de aplicación (el correo/token
/// identifica al usuario). Parámetros nombrados siempre (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// Las escrituras se ejecutan sobre la conexión de la transacción iniciada por
/// BeginTransactionAsync (patrón UsuarioRepository/TenantRepository, HU-001/002/003).
/// </summary>
public sealed class AuthRepository : IAuthRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public AuthRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-004 § Lógica BLL: rotación D1 / cambio de contraseña D14 + auditoría en una sola
    /// transacción, patrón HU-001). La conexión queda asociada a la transacción y se libera con
    /// commit/rollback/dispose de la transacción o al dispose del repositorio (Scoped en IOC).
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

    /// <summary>DAL-A1 · SELECT usuario por correo CON password_hash + tenant_estado (JOIN tenant).
    /// ÚNICA query de auth que lee password_hash (junto con DAL-A1b): el hash NUNCA sale del
    /// DAL-BLL (SEC-02/ADR-003). tenant_estado NULL ⇔ SuperAdmin (D15).</summary>
    public async Task<UsuarioAuthDto?> ObtenerUsuarioPorCorreoAsync(string correo, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT u.id, u.tenant_id, u.nombre, u.correo, u.password_hash, u.rol, u.area_id,
                   u.estado, u.intentos_fallidos, u.bloqueado_hasta, u.requiere_cambio_pwd, u.ultimo_login,
                   t.estado AS tenant_estado
            FROM usuario u
            LEFT JOIN tenant t ON t.id = u.tenant_id
            WHERE LOWER(u.correo) = LOWER(@Correo);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Correo = correo }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioAuthDto>(cmd);
    }

    /// <summary>DAL-A1b · SELECT usuario por id CON password_hash (para cambiar-contrasena).</summary>
    public async Task<UsuarioEntity?> ObtenerUsuarioPorIdConHashAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, correo, password_hash, rol, area_id, estado,
                   intentos_fallidos, bloqueado_hasta, requiere_cambio_pwd, ultimo_login
            FROM usuario
            WHERE id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioEntity>(cmd);
    }

    /// <summary>DAL-A2 · INSERT refresh_token (persiste el HASH SHA-256 del token, D2). RETURNING id.</summary>
    public async Task<Guid?> InsertarRefreshTokenAsync(Guid usuarioId, string tokenHash, DateTimeOffset expiraEn, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO refresh_token (usuario_id, token, expira_en)
            VALUES (@UsuarioId, @TokenHash, @ExpiraEn)
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql,
                new { UsuarioId = usuarioId, TokenHash = tokenHash, ExpiraEn = expiraEn },
                tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteScalarAsync<Guid>(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { UsuarioId = usuarioId, TokenHash = tokenHash, ExpiraEn = expiraEn },
            cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid>(cmd);
    }

    /// <summary>DAL-A3 · SELECT refresh_token por hash. Retorna null si no existe.</summary>
    public async Task<RefreshTokenEntity?> ObtenerRefreshTokenPorHashAsync(string tokenHash, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, usuario_id, token, expira_en, revocado, created_at
            FROM refresh_token
            WHERE token = @TokenHash;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TokenHash = tokenHash }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<RefreshTokenEntity>(cmd);
    }

    /// <summary>DAL-A4 · SELECT usuario por id SIN password_hash (para refresh).</summary>
    public async Task<UsuarioEntity?> ObtenerUsuarioPorIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, correo, rol, area_id, estado, requiere_cambio_pwd, ultimo_login
            FROM usuario
            WHERE id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioEntity>(cmd);
    }

    /// <summary>DAL-A5 · revoca refresh token por hash (revocado=TRUE AND revocado=FALSE). Retorna filas afectadas.</summary>
    public async Task<int> RevocarRefreshTokenPorHashAsync(string tokenHash, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE refresh_token
            SET revocado = TRUE
            WHERE token = @TokenHash
              AND revocado = FALSE
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { TokenHash = tokenHash }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TokenHash = tokenHash }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-A6 · revoca TODOS los refresh tokens del usuario (replay D1 / cambio pwd D14). Retorna filas afectadas.</summary>
    public async Task<int> RevocarTodosLosRefreshTokensAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE refresh_token
            SET revocado = TRUE
            WHERE usuario_id = @UsuarioId
              AND revocado = FALSE;";

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

    /// <summary>DAL-A7 · registra intento fallido o bloqueo (intentos=0 + estado Bloqueado +
    /// bloqueado_hasta=NOW+15min en el 5º, D9). Retorna filas afectadas.</summary>
    public async Task<int> RegistrarIntentoFallidoAsync(Guid id, int intentos, DateTimeOffset? bloqueadoHasta, string estado, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET intentos_fallidos = @Intentos,
                bloqueado_hasta   = @BloqueadoHasta,
                estado            = @Estado::estado_usuario,
                updated_at        = NOW()
            WHERE id = @Id
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql,
                new { Id = id, Intentos = intentos, BloqueadoHasta = bloqueadoHasta, Estado = estado },
                tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { Id = id, Intentos = intentos, BloqueadoHasta = bloqueadoHasta, Estado = estado },
            cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-A8 · login exitoso / desbloqueo automático: intentos=0, bloqueado_hasta=NULL, ultimo_login=NOW().</summary>
    public async Task<int> RegistrarLoginExitosoAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET intentos_fallidos = 0,
                bloqueado_hasta   = NULL,
                ultimo_login      = NOW(),
                updated_at        = NOW()
            WHERE id = @Id
            RETURNING id;";

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

    /// <summary>DAL-A9 · cambio de contraseña: password_hash=@NuevoHash, requiere_cambio_pwd=FALSE,
    /// intentos=0, bloqueado_hasta=NULL. Retorna filas afectadas.</summary>
    public async Task<int> CambiarContrasenaAsync(Guid id, string nuevoHash, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET password_hash       = @NuevoHash,
                requiere_cambio_pwd = FALSE,
                intentos_fallidos   = 0,
                bloqueado_hasta     = NULL,
                updated_at          = NOW()
            WHERE id = @Id
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { Id = id, NuevoHash = nuevoHash }, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { Id = id, NuevoHash = nuevoHash }, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-A10 · INSERT log_auditoria (entidad 'Auth' | 'Usuario'). El JSON se serializa
    /// en la BLL con UnsafeRelaxedJsonEscaping (ADR-003). Retorna filas afectadas.</summary>
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
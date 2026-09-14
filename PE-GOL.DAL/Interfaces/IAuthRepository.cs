using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos de autenticación (Spec HU-004 § Queries DAL: DAL-A1 a DAL-A10).
/// Las tablas usuario/refresh_token/log_auditoria NO están bajo RLS (06 L458-477) → sin WHERE
/// de política; el aislamiento lo garantiza la capa de aplicación (correo/token identifican al
/// usuario). Parámetros nombrados siempre (SEC-05); CancellationToken vía CommandDefinition.
/// Las escrituras multi-paso (rotación D1, cambio de contraseña D14) se ejecutan en UNA sola
/// transacción IDbTransaction gestionada por la BLL (patrón HU-001/002/003).
/// </summary>
public interface IAuthRepository
{
    /// <summary>DAL-A1 · SELECT usuario por correo CON password_hash + tenant_estado (JOIN tenant). SOLO uso interno de auth.</summary>
    Task<UsuarioAuthDto?> ObtenerUsuarioPorCorreoAsync(string correo, CancellationToken ct = default);

    /// <summary>DAL-A1b · SELECT usuario por id CON password_hash (para cambiar-contrasena).</summary>
    Task<UsuarioEntity?> ObtenerUsuarioPorIdConHashAsync(Guid id, CancellationToken ct = default);

    /// <summary>DAL-A2 · INSERT refresh_token (persiste el HASH SHA-256 del token, D2). RETURNING id.</summary>
    Task<Guid?> InsertarRefreshTokenAsync(Guid usuarioId, string tokenHash, DateTimeOffset expiraEn, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A3 · SELECT refresh_token por hash. Retorna null si no existe.</summary>
    Task<RefreshTokenEntity?> ObtenerRefreshTokenPorHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>DAL-A4 · SELECT usuario por id SIN password_hash (para refresh).</summary>
    Task<UsuarioEntity?> ObtenerUsuarioPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>DAL-A5 · revoca refresh token por hash (revocado=TRUE AND revocado=FALSE). Retorna filas afectadas.</summary>
    Task<int> RevocarRefreshTokenPorHashAsync(string tokenHash, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A6 · revoca TODOS los refresh tokens del usuario (replay D1 / cambio pwd D14). Retorna filas afectadas.</summary>
    Task<int> RevocarTodosLosRefreshTokensAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A7 · registra intento fallido o bloqueo (intentos=0 + estado Bloqueado + bloqueado_hasta=NOW+15min en el 5º, D9).</summary>
    Task<int> RegistrarIntentoFallidoAsync(Guid id, int intentos, DateTimeOffset? bloqueadoHasta, string estado, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A8 · login exitoso / desbloqueo automático: intentos=0, bloqueado_hasta=NULL, ultimo_login=NOW().</summary>
    Task<int> RegistrarLoginExitosoAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A9 · cambio de contraseña: password_hash=@NuevoHash, requiere_cambio_pwd=FALSE, intentos=0, bloqueado_hasta=NULL.</summary>
    Task<int> CambiarContrasenaAsync(Guid id, string nuevoHash, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A10 · INSERT log_auditoria (entidad 'Auth' | 'Usuario'). JSON con UnsafeRelaxedJsonEscaping (ADR-003).</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre conexión e inicia transacción IDbTransaction (patrón repositorios HU-001/002/003).</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
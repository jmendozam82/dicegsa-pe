using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para usuario (Spec HU-003 § Queries DAL: DAL-U1 a DAL-U10).
/// La tabla usuario NO está bajo RLS (verificado en 06_MODELO_DATOS.md L458-477) → las queries
/// no llevan WHERE de política RLS, pero SÍ filtros explícitos por tenant/rol/estado según el
/// llamador (D1: aquí solo SuperAdmin; HU-010 usará el mismo repo con el tenantId del JWT).
/// Correo único GLOBAL case-insensitive validado en BLL con LOWER (D2) — el UNIQUE del DDL es
/// case-sensitive (flag #3/#4 del spec).
/// Las operaciones de escritura (INSERT/UPDATE/estado/reset/tokens + auditoría) se ejecutan en
/// UNA sola transacción IDbTransaction gestionada por la BLL (patrón HU-001/HU-002).
/// </summary>
public interface IUsuarioRepository
{
    /// <summary>DAL-U1 · INSERT usuario ... RETURNING id, created_at, updated_at. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertAsync(UsuarioInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U6 · SELECT usuario por id (sin password_hash — nunca viaja a la BLL). Retorna null si no existe.</summary>
    Task<UsuarioEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>U6b · SELECT paginado con filtros opcionales (tenantId/rol/estado), ORDER BY created_at DESC.</summary>
    Task<IEnumerable<UsuarioEntity>> GetPagedAsync(int page, int pageSize, Guid? tenantId, string? rol, string? estado, CancellationToken ct = default);

    /// <summary>U6c · COUNT con los mismos filtros (paginado y página vacía con total==0).</summary>
    Task<int> CountAsync(Guid? tenantId, string? rol, string? estado, CancellationToken ct = default);

    /// <summary>DAL-U2 / U2b · unicidad de correo case-insensitive (LOWER). excludeId excluye el propio usuario en UPDATE.</summary>
    Task<bool> ExisteCorreoAsync(string correo, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>DAL-U3 · existencia del tenant (SELECT COUNT(1) FROM tenant WHERE id = @TenantId).</summary>
    Task<bool> ExisteTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// U3b · plan_id del tenant destino (SELECT plan_id FROM tenant WHERE id = @TenantId).
    /// NUEVO vs spec: la BLL necesita el planId del tenant para invocar
    /// IPlanService.ValidarLimitesParaTenantAsync(tenantId, planId, ct) (CA #2 HU-002);
    /// el spec DAL-U3 solo cuenta existencia → se añade esta query de lectura del plan.
    /// Retorna null si el tenant no existe.
    /// </summary>
    Task<Guid?> ObtenerPlanIdTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-U4 · el área pertenece al tenant (SELECT COUNT(1) FROM area WHERE id = @AreaId AND tenant_id = @TenantId).</summary>
    Task<bool> PerteneceAreaAlTenantAsync(Guid areaId, Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-U7 · UPDATE datos generales (nombre/correo/rol/tenant_id/area_id). Retorna filas afectadas.</summary>
    Task<int> UpdateAsync(UsuarioUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U8 · UPDATE estado (Activo/Inactivo/Bloqueado). Retorna filas afectadas.</summary>
    Task<int> UpdateEstadoAsync(Guid id, string estado, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U9 · revoca refresh tokens del usuario (UPDATE refresh_token SET revocado=TRUE WHERE usuario_id=@Id). Retorna filas afectadas.</summary>
    Task<int> RevocarRefreshTokensAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U10 · reset de contraseña: UPDATE password_hash, requiere_cambio_pwd=TRUE, intentos_fallidos=0, bloqueado_hasta=NULL. Retorna filas afectadas.</summary>
    Task<int> ResetContrasenaAsync(Guid id, string passwordHash, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U5 · INSERT log_auditoria (entidad 'Usuario'). El JSON se serializa con UnsafeRelaxedJsonEscaping (ADR-003). Retorna filas afectadas.</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction (mismo patrón que ITenantRepository).</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
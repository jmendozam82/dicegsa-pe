using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para tenant (Spec HU-001 § Queries DAL: DAL-1 a DAL-9).
/// La tabla tenant NO lleva tenant_id (excepción a DB-03): estas queries no incluyen
/// WHERE tenant_id = @TenantId. El alcance se garantiza por [Authorize(Roles = "SuperAdmin")]
/// + TenantContext.Rol en la API.
/// Todos los métodos aceptan CancellationToken; las operaciones de escritura que exige el
/// spec (INSERT + auditoría, UPDATE + auditoría, desactivar) se ejecutan en una sola
/// transacción IDbTransaction gestionada por la BLL.
/// </summary>
public interface ITenantRepository
{
    /// <summary>DAL-1: INSERT tenant ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertAsync(TenantInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-2: SELECT por id con nombre del plan. Retorna null si no existe.</summary>
    Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>DAL-3a: SELECT paginado con filtros opcionales (estado/plan), ORDER BY created_at DESC.</summary>
    Task<IEnumerable<TenantEntity>> GetPagedAsync(int page, int pageSize, string? filtroEstado = null, Guid? filtroPlanId = null, CancellationToken ct = default);

    /// <summary>DAL-3b: COUNT con los mismos filtros (para paginado y página vacía con total==0).</summary>
    Task<int> CountAsync(string? filtroEstado = null, Guid? filtroPlanId = null, CancellationToken ct = default);

    /// <summary>DAL-8: unicidad de nombre case-insensitive. excludeId excluye el propio tenant en UPDATE.</summary>
    Task<bool> ExisteNombreAsync(string nombre, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>DAL-9: existencia del plan.</summary>
    Task<bool> ExistePlanAsync(Guid planId, CancellationToken ct = default);

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction.
    /// La conexión queda asociada a la transacción y se libera con commit/rollback/dispose de la
    /// transacción o al dispose del repositorio (registrado Scoped en IOC, fin del request HTTP).
    /// Los métodos de escritura aceptan esta transacción como parámetro opcional para ejecutar
    /// INSERT/UPDATE + auditoría de forma atómica (spec HU-001 § Lógica BLL).
    /// </summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>DAL-4: UPDATE datos generales. Retorna filas afectadas.</summary>
    Task<int> UpdateAsync(TenantUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-6: UPDATE estado (Activo/Inactivo). Retorna filas afectadas.</summary>
    Task<int> UpdateEstadoAsync(Guid id, string estado, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-5: revoca refresh tokens de los usuarios del tenant (al desactivar). Retorna filas afectadas.</summary>
    Task<int> RevocarRefreshTokensAsync(Guid tenantId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-7: INSERT log_auditoria. Retorna filas afectadas.</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-E2 (HU-006): UPDATE configuración de empresa (nombre, eslogan, zona_horaria).
    /// Retorna filas afectadas. Stub de contrato — implementación real de @BackendDev.</summary>
    Task<int> ActualizarConfiguracionAsync(TenantConfiguracionUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-E3 (HU-006): UPDATE logo_url (solo logo). Retorna filas afectadas.
    /// Stub de contrato — implementación real de @BackendDev.</summary>
    Task<int> ActualizarLogoUrlAsync(Guid tenantId, string logoUrl, IDbTransaction? tx = null, CancellationToken ct = default);
}
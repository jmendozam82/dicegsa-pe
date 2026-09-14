using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para plan (Spec HU-002 § Queries DAL: DAL-P1 a DAL-P9).
/// La tabla plan es GLOBAL del SaaS: NO lleva tenant_id → ninguna query incluye
/// WHERE tenant_id = @TenantId (excepción a DB-03). El alcance se garantiza por
/// [Authorize(Roles = "SuperAdmin")] + TenantContext.Rol en la API.
/// Las operaciones de escritura (INSERT/UPDATE/DELETE + auditoría) se ejecutan en UNA sola
/// transacción IDbTransaction gestionada por la BLL (mismo patrón que ITenantRepository, HU-001).
/// </summary>
public interface IPlanRepository
{
    /// <summary>DAL-P1: INSERT plan ... RETURNING id, created_at. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertAsync(PlanInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-P2: SELECT por id. Retorna null si no existe.</summary>
    Task<PlanEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>DAL-P3: SELECT listado completo ordenado por nombre ASC (sin paginación, decisión D1).</summary>
    Task<IEnumerable<PlanEntity>> GetAllAsync(CancellationToken ct = default);

    /// <summary>DAL-P4: unicidad de nombre case-insensitive. excludeId excluye el propio plan en UPDATE.</summary>
    Task<bool> ExisteNombreAsync(string nombre, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>DAL-P5: UPDATE plan. Retorna filas afectadas.</summary>
    Task<int> UpdateAsync(PlanUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-P6: DELETE físico. Retorna filas afectadas.</summary>
    Task<int> DeleteAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-P7b: COUNT de tenants que usan un plan (para Eliminar).</summary>
    Task<int> ContarTenantsUsoAsync(Guid planId, CancellationToken ct = default);

    /// <summary>DAL-P7: tenants que usan un plan (con nombre, para mensajes 422).</summary>
    Task<IEnumerable<TenantPlanDto>> ObtenerTenantsPorPlanAsync(Guid planId, CancellationToken ct = default);

    /// <summary>DAL-P8: conteos de uso de un tenant (áreas / usuarios / ciclos activos).</summary>
    Task<ConteosUsoPlan> ObtenerConteosUsoAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-P9: INSERT log_auditoria (entidad Plan, tenant_id = NULL). Retorna filas afectadas.</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre conexión gestionada e inicia transacción IDbTransaction (mismo patrón que ITenantRepository).</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
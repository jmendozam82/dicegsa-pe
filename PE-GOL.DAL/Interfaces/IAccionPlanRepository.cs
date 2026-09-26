using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Interfaces;

public interface IAccionPlanRepository
{
    Task<AccionPlanEntity> InsertAsync(AccionPlanEntity entity, CancellationToken ct = default);
    Task UpdateAsync(AccionPlanEntity entity, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<AccionPlanEntity?> ObtenerPorIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<IEnumerable<AccionPlanEntity>> ListarPorObjetivoCgAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default);
    Task<decimal> ObtenerSumaPesosAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default);
    Task<decimal> ObtenerSumaPonderadaProgresoAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default);
    Task<int> ObtenerMaximoOrdenAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default);
}
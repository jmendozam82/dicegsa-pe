using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Responses.PlanOperativo;
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

    /// <summary>
    /// HU-021 · DAL-G1 — acciones del ciclo para el Gantt, aplanadas, con los metadatos del CG y del
    /// área (JOIN) para el agrupado en cliente. areaIdFiltro (SEC-07): null → todas las áreas
    /// (Gerente, RN-006); con valor → AND a.area_id = @AreaIdFiltro (JefeArea).
    /// Orden estable para el agrupado: oc.orden, oc.codigo, a.orden, a.codigo.
    /// </summary>
    Task<IEnumerable<GanttAccionFilaDto>> ListarParaGanttAsync(
        Guid tenantId, Guid cicloId, Guid? areaIdFiltro, CancellationToken ct = default);
}
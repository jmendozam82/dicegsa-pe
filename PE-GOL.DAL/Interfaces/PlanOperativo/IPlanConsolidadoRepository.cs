using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para la vista consolidada del Plan (Spec HU-023 § Queries DAL).
/// Toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06).
/// SEC-07 NO APLICA: el rol Gerente ve todas las áreas (RN-006), sin filtro area_id.
/// Solo lectura: sin INSERT/UPDATE/DELETE.
/// </summary>
public interface IPlanConsolidadoRepository
{
    /// <summary>
    /// Query 1 — SELECT paginado con JOINs a objetivo_cg, pilar, area, ciclo y LEFT JOIN usuario.
    /// skipPaginacion = true → sin OFFSET/FETCH (para exportación).
    /// </summary>
    Task<List<ConsolidadoItemResponse>> ListarConsolidadoAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        bool skipPaginacion = false, CancellationToken ct = default);

    /// <summary>
    /// Query 2 — COUNT total con mismos WHERE que ListarConsolidado.
    /// </summary>
    Task<int> CountConsolidadoAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        CancellationToken ct = default);

    /// <summary>
    /// Query 3 — GROUP BY status (COUNT FILTER) para resumen de conteos.
    /// </summary>
    Task<ResumenConsolidado> ObtenerResumenAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        CancellationToken ct = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.BLL.Interfaces;

public interface IAccionPlanService
{
    Task<AccionPlanResponse> CrearAsync(Guid objetivoCgId, AccionPlanCreateRequest request, CancellationToken ct = default);
    Task<AccionPlanResponse> ActualizarAsync(Guid id, AccionPlanUpdateRequest request, CancellationToken ct = default);
    Task EliminarAsync(Guid id, CancellationToken ct = default);
    Task<AccionPlanResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<AccionPlanResponse>> ListarPorObjetivoCgAsync(Guid objetivoCgId, CancellationToken ct = default);

    // HU-020
    Task<AccionPlanResponse> ActualizarProgresoAsync(Guid accionId, ActualizarProgresoRequest request, CancellationToken ct = default);
    Task<IEnumerable<HistorialProgresoResponse>> ListarHistorialAsync(Guid accionId, CancellationToken ct = default);

    // HU-021 — Vista Gantt (solo lectura). Sin argumentos de negocio: el tenant sale del
    // TenantContext (SEC-06), el ciclo activo se resuelve aquí (RC-01/DAL-D1) y el filtro de
    // área se deriva del rol (SEC-07). Retorna el DTO plano, nunca ApiResponse<T> (ADR-010).
    Task<GanttPlanResponse> ObtenerGanttAsync(CancellationToken ct = default);
}
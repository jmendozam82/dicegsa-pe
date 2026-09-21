using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;

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
}
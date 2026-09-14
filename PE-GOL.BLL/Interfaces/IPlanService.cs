using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Planes de Suscripción (Spec HU-002 § Contrato IPlanService — firma exacta).
/// Solo SuperAdmin. Los métodos lanzan ValidacionException (422) y NotFoundException (404)
/// desde PE_GOL.Utility.Exceptions.
/// </summary>
public interface IPlanService
{
    Task<PlanResponse> CrearAsync(PlanCreateRequest request, CancellationToken ct = default);
    Task<PlanResponse> ActualizarAsync(Guid id, PlanUpdateRequest request, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid id, CancellationToken ct = default);                     // true si eliminó
    Task<List<PlanResponse>> ListarAsync(CancellationToken ct = default);                 // sin paginación (D1)
    Task<PlanResponse?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<ResultadoValidacionLimites> ValidarLimitesParaTenantAsync(Guid tenantId, Guid planId, CancellationToken ct = default);
    Task<ResultadoValidacionLimites> ValidarLimitesParaTenantsDelPlanAsync(Guid planId, PlanLimits nuevosLimites, CancellationToken ct = default);
}
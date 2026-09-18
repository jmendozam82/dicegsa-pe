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

    /// <summary>
    /// HU-009 (Flag #2, aditivo — no rompe HU-002): devuelve los límites configurados del plan
    /// (PlanLimits: MaxAreas/MaxUsuarios/MaxCiclosActivos). Necesario para el chequeo PRECISO por
    /// ciclo de RN-010 (DAL-A6 &gt;= MaxAreas → 422): ValidarLimitesParaTenantAsync (DAL-P8,
    /// comparación estricta &gt;) NO detecta el caso "ciclo objetivo al tope de áreas".
    /// 404 si el plan no existe (mismo criterio que ValidarLimitesParaTenantAsync).
    /// </summary>
    Task<PlanLimits> ObtenerLimitesAsync(Guid planId, CancellationToken ct = default);
}
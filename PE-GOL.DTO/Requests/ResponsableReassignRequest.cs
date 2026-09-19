namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de reasignación de Responsable (Spec HU-010 § DTOs — PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId}).
/// Solo el nuevo areaId: la BLL libera el área origen (DAL-R6 null) y asigna el destino (DAL-R6
/// responsableId) en la misma transacción (D-E). tenant_id NUNCA viaja en el body (SEC-06).
/// </summary>
public class ResponsableReassignRequest
{
    /// <summary>Requerido: nueva área destino (RN-011: área Activa sin responsable; RN-012 excluyendo self).</summary>
    public Guid AreaId { get; set; }
}
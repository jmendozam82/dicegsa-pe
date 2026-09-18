namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request del PUT /api/v1/ciclos/{cicloId}/umbrales (Spec HU-008 § DTOs).
/// UPSERT CONJUNTO de ambos tipos (D1): el PUT reemplaza el estado completo del recurso
/// (semántica REST) — no existe POST/PUT/DELETE por tipo individual.
/// cicloId NO va en el body: viene de la ruta; tenantId se resuelve del TenantContext (SEC-06).
/// </summary>
public class UmbralesUpdateRequest
{
    /// <summary>Umbrales para KPIs (CA #1: categorías independientes). Obligatorio.</summary>
    public UmbralCategoriaRequest Kpi { get; set; } = new();

    /// <summary>Umbrales para Plan de Acción. Obligatorio.</summary>
    public UmbralCategoriaRequest PlanAccion { get; set; } = new();
}

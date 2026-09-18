namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de GET/PUT /api/v1/ciclos/{cicloId}/umbrales (Spec HU-008 § DTOs).
/// Expone ambos tipos del ciclo (KPI y Plan de Acción) — CA #1: categorías independientes.
/// Si el ciclo no tiene filas (defensivo D5, no debería ocurrir: HU-007 inserta 2 defaults),
/// el GET responde los defaults 0.90/0.70 sin escribir en BD.
/// </summary>
public class UmbralesCicloResponse
{
    /// <summary>Ciclo dueño de los umbrales (de la ruta, no del body).</summary>
    public Guid CicloId { get; set; }

    /// <summary>Umbrales para KPIs (tipo 'KPI').</summary>
    public UmbralCategoriaResponse Kpi { get; set; } = new();

    /// <summary>Umbrales para Plan de Acción (tipo 'PlanAccion').</summary>
    public UmbralCategoriaResponse PlanAccion { get; set; } = new();
}

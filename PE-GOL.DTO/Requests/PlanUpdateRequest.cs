namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request PUT /api/v1/planes/{id} (Spec HU-002 § DTOs).
/// Mismos campos y reglas que PlanCreateRequest; la unicidad de nombre excluye el propio plan.
/// </summary>
public class PlanUpdateRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
}
namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request POST /api/v1/planes (Spec HU-002 § DTOs).
/// Los rangos (maxAreas 1..20, maxUsuarios 1..1000, maxCiclosActivos 1..10) se validan en la
/// API (FluentValidation) y se RE-validan en la BLL (fuente de verdad, D9).
/// </summary>
public class PlanCreateRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
}
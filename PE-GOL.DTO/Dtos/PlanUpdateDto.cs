namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de un plan (DAL-P5 del spec HU-002).
/// </summary>
public class PlanUpdateDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
}
namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de datos generales del tenant (DAL-4 del spec HU-001).
/// </summary>
public class TenantUpdateDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
}
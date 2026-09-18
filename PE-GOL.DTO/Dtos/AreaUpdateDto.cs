namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de área (Spec HU-009 § DTOs — DAL-A4).
/// codigo/orden NO se actualizan (inmutables, auto-generados por la BLL — DB-04).
/// </summary>
public class AreaUpdateDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Comentarios { get; set; }
    public Guid? ResponsableId { get; set; }
}
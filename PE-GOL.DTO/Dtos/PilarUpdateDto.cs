namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de pilar (Spec HU-013 § DTOs — DAL-P7).
/// codigo NO se actualiza (auto-generado, CA #1 — DAL-P7 no lo incluye en el UPDATE).
/// </summary>
public class PilarUpdateDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public Guid PilarId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? EstrategiaVictoria { get; set; }
    public int Orden { get; set; }
}
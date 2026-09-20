namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción de pilar (Spec HU-013 § DTOs — DAL-P6).
/// Codigo: "PEC-N" generado por la BLL (CA #1, D-B) — nunca viaja en requests.
/// </summary>
public class PilarInsertDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>"PEC-N" generado por la BLL (CA #1, D-B): "PEC-1", "PEC-2", ...</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? EstrategiaVictoria { get; set; }
    public int Orden { get; set; }
}
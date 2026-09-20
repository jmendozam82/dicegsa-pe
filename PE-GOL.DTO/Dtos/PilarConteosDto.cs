namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Fila del listado/detalle de pilares con conteos (Spec HU-013 § DTOs — DAL-P1/P2, CA #5, D-D).
/// Sin TenantId: el response lo toma del TenantContext (SEC-06).
/// TotalObjetivosCg/TotalOkrs: COUNT(DISTINCT ...) del LEFT JOIN a objetivo_cg/okr (D-D).
/// </summary>
public class PilarConteosDto
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? EstrategiaVictoria { get; set; }
    public int Orden { get; set; }

    /// <summary>CA #5 — conteo de objetivo_cg asociados (D-D).</summary>
    public int TotalObjetivosCg { get; set; }

    /// <summary>CA #5 — conteo de okr asociados (D-D).</summary>
    public int TotalOkrs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
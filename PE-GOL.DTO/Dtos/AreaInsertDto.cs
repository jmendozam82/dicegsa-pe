namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción de área (Spec HU-009 § DTOs — DAL-A1).
/// Activa = true en creación; se copia tal cual al clonar (HU-009 §7 paso 2).
/// </summary>
public class AreaInsertDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>Código GOL auto-generado por la BLL (CA #1, DB-04): "GOL1", "GOL2", ...</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Comentarios { get; set; }
    public Guid? ResponsableId { get; set; }
    public int Orden { get; set; }

    /// <summary>true en creación; se copia tal cual al clonar.</summary>
    public bool Activa { get; set; } = true;
}
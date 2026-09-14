namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción del tenant para la DAL (DAL-1 del spec HU-001).
/// La tabla tenant es raíz del multitenancy: NO lleva tenant_id (excepción a DB-03).
/// </summary>
public class TenantInsertDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
    public string Estado { get; set; } = "Activo";
}
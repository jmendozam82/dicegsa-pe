namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request POST /api/v1/tenants (Spec HU-001 § DTOs).
/// El campo estado NO forma parte del request: se crea siempre en 'Activo' (default BD).
/// </summary>
public class TenantCreateRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
}
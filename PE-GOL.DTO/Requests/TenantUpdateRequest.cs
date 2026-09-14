namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request PUT /api/v1/tenants/{id} (Spec HU-001 § DTOs).
/// El campo estado NO se modifica por este endpoint (se usa activar/desactivar).
/// </summary>
public class TenantUpdateRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
}
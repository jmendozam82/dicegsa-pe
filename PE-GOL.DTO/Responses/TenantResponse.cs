namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de tenant (Spec HU-001 § DTOs).
/// zonaHoraria es string no-null: la BLL aplica el default 'America/Managua' cuando el request no lo envía.
/// </summary>
public class TenantResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string PlanNombre { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string ZonaHoraria { get; set; } = "America/Managua";
    public string Estado { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
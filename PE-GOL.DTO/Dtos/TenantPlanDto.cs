namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Tenant que usa un plan (DAL-P7 del spec HU-002). Incluye el nombre para mensajes 422
/// ("El tenant 'X' supera el límite ...").
/// </summary>
public class TenantPlanDto
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
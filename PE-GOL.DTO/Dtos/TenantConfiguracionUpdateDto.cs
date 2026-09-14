namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de la configuración de empresa (DAL-E2 del spec HU-006).
/// UPDATE tenant SET nombre, eslogan, zona_horaria WHERE id = @Id.
/// Stub de contrato (TDD fase roja, TEST-01): la implementación real es de @BackendDev.
/// </summary>
public class TenantConfiguracionUpdateDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Eslogan { get; set; }
    public string ZonaHoraria { get; set; } = string.Empty;
}
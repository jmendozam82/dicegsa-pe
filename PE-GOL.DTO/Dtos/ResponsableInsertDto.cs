namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción de usuario responsable (Spec HU-010 § DTOs — DAL-R1).
/// PasswordHash: BCrypt cost 12 (SEC-02), generado en BLL — NUNCA viaja en requests.
/// Rol fijo 'JefeArea' (D-B); estado inicial 'Activo'; requiere_cambio_pwd = TRUE (HU-003/HU-004).
/// </summary>
public class ResponsableInsertDto
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;

    /// <summary>BCrypt cost 12 (SEC-02), generado en BLL.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Rol { get; set; } = "JefeArea";

    /// <summary>Área a la que se asigna como responsable (sync SEC-07, D-J).</summary>
    public Guid AreaId { get; set; }

    public string Estado { get; set; } = "Activo";
    public bool RequiereCambioPwd { get; set; } = true;
}
namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de creación de Responsable (Spec HU-010 § DTOs — POST /api/v1/ciclos/{cicloId}/responsables).
/// password NUNCA viaja en requests: se genera temporal en BLL (BCrypt cost 12) y se envía por
/// correo (IEmailService, D-C). rol se fija a 'JefeArea' en BLL. estado inicial = 'Activo'.
/// tenant_id NUNCA viaja en el body (SEC-06): proviene del TenantContext (JWT).
/// </summary>
public class ResponsableCreateRequest
{
    /// <summary>Requerido, máx 150 chars (VARCHAR(150)).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Requerido, email válido, máx 200, único en tenant (DAL-R4 + UNIQUE global DDL).</summary>
    public string Correo { get; set; } = string.Empty;

    /// <summary>Requerido: área a la que se asigna como responsable (RN-011: área Activa sin responsable).</summary>
    public Guid AreaId { get; set; }
}
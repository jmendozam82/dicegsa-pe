namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de la configuración de la empresa (Spec HU-006 § DTOs).
/// logoUrl es la URL FIRMADA (24 h) generada en cada lectura (D5); null si no hay logo.
/// La BD almacena el path de storage en tenant.logo_url; el DTO expone la URL firmada,
/// nunca el path crudo ni la URL firmada persistida (las URLs firmadas expiran).
/// Stub de contrato (TDD fase roja, TEST-01): la implementación real es de @BackendDev.
/// </summary>
public class EmpresaResponse
{
    /// <summary>Id del tenant (del TenantContext, nunca del body — SEC-06).</summary>
    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Eslogan { get; set; }

    /// <summary>URL firmada (24 h) generada en cada lectura; null si no hay logo.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Siempre presente (default BD 'America/Managua').</summary>
    public string ZonaHoraria { get; set; } = "America/Managua";

    /// <summary>Solo lectura (gestionada por SuperAdmin en HU-001).</summary>
    public string? Descripcion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Payload de creación de OKR — Spec HU-024 (CA #2). Sin TenantId/AreaId/CicloId (SEC-06).</summary>
public class OkrCreateRequest
{
    /// <summary>Pilar estratégico asociado (GUID). Se valida que exista en el ciclo activo del tenant.</summary>
    public Guid PilarId { get; set; }

    /// <summary>Descripción del objetivo (1-500 chars).</summary>
    public string Descripcion { get; set; } = string.Empty;
}

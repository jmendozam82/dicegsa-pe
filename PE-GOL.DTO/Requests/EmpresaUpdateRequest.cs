namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de actualización de la configuración de la empresa (Spec HU-006 § DTOs).
/// D8: SOLO los campos del CA (nombre, eslogan, zonaHoraria) — descripcion/planId/estado
/// son gestionados por el SuperAdmin vía HU-001 (PUT /api/v1/tenants/{id}).
/// Stub de contrato (TDD fase roja, TEST-01): la implementación real es de @BackendDev.
/// </summary>
public class EmpresaUpdateRequest
{
    /// <summary>Requerido, máx 150 chars, único en la plataforma (excluyendo el propio tenant).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional, máx 200 chars.</summary>
    public string? Eslogan { get; set; }

    /// <summary>Requerido, máx 50 chars, valor IANA válido (ej: 'America/Managua').</summary>
    public string ZonaHoraria { get; set; } = string.Empty;
}
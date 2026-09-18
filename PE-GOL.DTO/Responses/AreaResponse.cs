namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de Área Estratégica (Spec HU-009 § DTOs — GET/POST/PUT/desactivar).
/// tenantId proviene del TenantContext (SEC-06), nunca del body.
/// ResponsableId null solo si el área está inactiva (RN-011: área Activa con responsable).
/// ResponsableNombre/ResponsableCorreo vienen del JOIN a usuario (DAL-A2/A3).
/// </summary>
public class AreaResponse
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>Del TenantContext, nunca del body (SEC-06).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Código GOL auto-generado: "GOL1", "GOL2", ... (CA #1, DB-04).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Comentarios { get; set; }

    /// <summary>Null solo si el área está inactiva (RN-011).</summary>
    public Guid? ResponsableId { get; set; }

    /// <summary>JOIN a usuario (nombre del responsable).</summary>
    public string? ResponsableNombre { get; set; }

    /// <summary>JOIN a usuario (correo del responsable).</summary>
    public string? ResponsableCorreo { get; set; }

    public int Orden { get; set; }
    public bool Activa { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de ciclo anual (Spec HU-007 § DTOs — GET/POST/PUT/activar/cerrar/clonar).
/// tenantId proviene del TenantContext (SEC-06), nunca del body. estado: 'Borrador'|'Activo'|'Cerrado'.
/// Los umbrales de semáforo NO se exponen aquí: su configuración es responsabilidad de HU-008.
/// </summary>
public class CicloResponse
{
    public Guid Id { get; set; }

    /// <summary>Del TenantContext, nunca del body (SEC-06).</summary>
    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public int AñoFiscal { get; set; }

    public int MesInicio { get; set; }

    /// <summary>'Borrador' | 'Activo' | 'Cerrado'.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Usuario que creó el ciclo (TenantContext.UserId).</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>Null hasta que se active.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Null hasta que se cierre.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
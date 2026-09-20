namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de Pilar Estratégico (Spec HU-013 § DTOs — GET/POST/PUT/DELETE).
/// tenantId proviene del TenantContext (SEC-06), nunca del body.
/// Codigo: "PEC-N" auto-generado por la BLL (CA #1, D-B) — no editable.
/// TotalObjetivosCg/TotalOkrs: conteos del listado/detalle (CA #5, D-D — LEFT JOIN + COUNT DISTINCT).
/// NO incluye objetivo_q1..q4 (HU-014, FUERA DE ALCANCE — se añadirán aditivamente en esa HU).
/// </summary>
public class PilarResponse
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>Del TenantContext, nunca del body (SEC-06).</summary>
    public Guid TenantId { get; set; }

    /// <summary>"PEC-N" auto-generado (CA #1, D-B).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? EstrategiaVictoria { get; set; }
    public int Orden { get; set; }

    /// <summary>CA #5 — conteo de objetivo_cg asociados (D-D).</summary>
    public int TotalObjetivosCg { get; set; }

    /// <summary>CA #5 — conteo de okr asociados (D-D).</summary>
    public int TotalOkrs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
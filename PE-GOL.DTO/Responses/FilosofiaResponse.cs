namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de Visión, Misión y Valores Corporativos del ciclo (Spec HU-011 § DTOs —
/// GET/PUT /api/v1/ciclos/{cicloId}/filosofia; Spec HU-012 § DTOs — PUT /filosofia/valores).
/// tenantId proviene del TenantContext (SEC-06), nunca del body.
/// Defensivo D-H: si no existe fila filosofia, el GET responde Id=Guid.Empty, Vision/Mision="",
/// Valores=[], UpdatedBy/UpdatedByNombre/UpdatedAt=null (defaults en memoria, sin escritura —
/// patrón D5 HU-008).
/// Valores (HU-012): campo NUEVO aditivo y backward-compatible — lista ordenada (D-C: el orden
/// del array JSONB ES el orden de visualización); vacía si no existe fila (D-H).
/// UpdatedByNombre viene del JOIN a usuario (DAL-F1) — trazabilidad (CA #4).
/// </summary>
public class FilosofiaResponse
{
    /// <summary>Guid.Empty si no existe fila (D-H).</summary>
    public Guid Id { get; set; }

    public Guid CicloId { get; set; }

    /// <summary>Del TenantContext, nunca del body (SEC-06).</summary>
    public Guid TenantId { get; set; }

    /// <summary>HTML sanitizado (vacío si no existe fila, D-H).</summary>
    public string Vision { get; set; } = string.Empty;

    /// <summary>HTML sanitizado (vacío si no existe fila, D-H).</summary>
    public string Mision { get; set; } = string.Empty;

    /// <summary>Lista ordenada de Valores Corporativos (HU-012, D-C); vacía si no existe fila (D-H).</summary>
    public List<string> Valores { get; set; } = new();

    /// <summary>Null si no existe fila.</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>JOIN a usuario (nombre del último editor) — trazabilidad.</summary>
    public string? UpdatedByNombre { get; set; }

    /// <summary>Null si no existe fila.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de edición de Pilar Estratégico (Spec HU-013 § DTOs — PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId}).
/// Mismos campos que PilarCreateRequest (nombre, estrategia de victoria, orden).
/// El código PEC-N NO es editable (CA #1: auto-generado en BLL, D-B) — no viaja en el request.
/// </summary>
public class PilarUpdateRequest
{
    /// <summary>Requerido, máx 150 chars (VARCHAR(150)) — trim en BLL.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT), máx 2000 chars (D-C).</summary>
    public string? EstrategiaVictoria { get; set; }

    /// <summary>Opcional; null → conserva el orden existente (D-H).</summary>
    public int? Orden { get; set; }
}
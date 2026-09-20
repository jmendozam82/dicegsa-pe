namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de creación de Pilar Estratégico (Spec HU-013 § DTOs — POST /api/v1/ciclos/{cicloId}/pilares).
/// El código PEC-N NO viaja en el request (CA #1: auto-generado en BLL, D-B).
/// tenant_id/ciclo_id se resuelven de la ruta y del TenantContext (SEC-06).
/// </summary>
public class PilarCreateRequest
{
    /// <summary>Requerido, máx 150 chars (VARCHAR(150)) — trim en BLL.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT), máx 2000 chars (D-C).</summary>
    public string? EstrategiaVictoria { get; set; }

    /// <summary>Opcional; null → default secuencial MAX(orden)+1 del ciclo (D-H).</summary>
    public int? Orden { get; set; }
}
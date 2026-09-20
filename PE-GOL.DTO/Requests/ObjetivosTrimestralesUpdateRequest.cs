namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId}/objetivos-trimestrales
/// (Spec HU-014 § DTOs — CA #1, CA #2, D-C, D-D).
/// Los 4 trimestres son OPCIONALES (CA #2): null o string vacío tras trim se persiste null (D-H).
/// Texto plano (D-D), máx 2000 chars por trimestre (D-C).
/// tenant_id/ciclo_id/pilar_id se resuelven de la ruta y del TenantContext (SEC-06) — nunca viajan aquí.
/// </summary>
public class ObjetivosTrimestralesUpdateRequest
{
    /// <summary>TEXT — opcional (CA #2), máx 2000 chars (D-C), texto plano (D-D).</summary>
    public string? ObjetivoQ1 { get; set; }

    /// <summary>TEXT — opcional (CA #2), máx 2000 chars (D-C), texto plano (D-D).</summary>
    public string? ObjetivoQ2 { get; set; }

    /// <summary>TEXT — opcional (CA #2), máx 2000 chars (D-C), texto plano (D-D).</summary>
    public string? ObjetivoQ3 { get; set; }

    /// <summary>TEXT — opcional (CA #2), máx 2000 chars (D-C), texto plano (D-D).</summary>
    public string? ObjetivoQ4 { get; set; }
}
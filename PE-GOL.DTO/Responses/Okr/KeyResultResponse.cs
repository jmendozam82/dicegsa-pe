namespace PE_GOL.DTO.Responses.Okr;

/// <summary>Respuesta de KR — Spec HU-025 (CA #1).
/// A diferencia de OkrResponse, NO expone Semaforo: key_result no tiene columna `semaforo`
/// en el DDL (ver F5). Las 6 columnas de puntuación son calculadas y se devuelven tal cual
/// están persistidas (0.000 hasta HU-026).</summary>
public class KeyResultResponse
{
    public Guid Id { get; set; }
    public Guid OkrId { get; set; }
    public Guid TenantId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public decimal PuntuacionQ1 { get; set; }
    public decimal PuntuacionQ2 { get; set; }
    public decimal PuntuacionQ3 { get; set; }
    public decimal PuntuacionQ4 { get; set; }
    public decimal PuntuacionFinal { get; set; }
    public decimal PuntuacionPonderada { get; set; }
    public int Orden { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

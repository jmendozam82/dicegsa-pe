namespace PE_GOL.DTO.Responses.Okr;

/// <summary>
/// Una fila = un KR con sus 12 meses y su cálculo (persistido + semáforo calculado, F8).
/// </summary>
public class ValorMensualKrFilaResponse
{
    public Guid KeyResultId { get; set; }
    public string Codigo { get; set; } = string.Empty;          // "KR.1"
    public string Descripcion { get; set; } = string.Empty;
    public decimal Peso { get; set; }                           // 3 decimales, persistido por HU-025
    public List<ValorMensualKrCeldaResponse> Meses { get; set; } = new();   // exactamente 12
    public int MesesConValor { get; set; }
    public decimal PuntuacionQ1 { get; set; }
    public decimal PuntuacionQ2 { get; set; }
    public decimal PuntuacionQ3 { get; set; }
    public decimal PuntuacionQ4 { get; set; }
    public decimal PuntuacionFinal { get; set; }
    public decimal PuntuacionPonderada { get; set; }
    public string Semaforo { get; set; } = string.Empty;        // "Verde" | "Amarillo" | "Rojo" — calculado en BLL (F8)
}

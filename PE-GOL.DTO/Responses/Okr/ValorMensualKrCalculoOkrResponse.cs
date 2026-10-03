namespace PE_GOL.DTO.Responses.Okr;

/// <summary>
/// Agregado del OKR tras el recálculo (RF-038 · RN-027).
/// </summary>
public class ValorMensualKrCalculoOkrResponse
{
    public decimal PuntuacionFinal { get; set; }
    public string Semaforo { get; set; } = string.Empty;
    public int KrsConValor { get; set; }
}

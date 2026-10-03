namespace PE_GOL.DTO.Requests.Okr;

/// <summary>
/// Un valor mensual de un KR (RF-035 · RN-024). Escala 0.0–1.0 con 1 decimal.
/// </summary>
public class ValorMensualKrItemRequest
{
    public int Mes { get; set; }
    public decimal Valor { get; set; }
}

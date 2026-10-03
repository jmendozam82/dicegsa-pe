namespace PE_GOL.DTO.Responses.Okr;

/// <summary>
/// Respuesta del PUT/DELETE: la fila tocada + el agregado del OKR (evita un GET extra).
/// </summary>
public class ValorMensualKrGuardarResponse
{
    public ValorMensualKrFilaResponse KeyResult { get; set; } = new();
    public ValorMensualKrCalculoOkrResponse CalculoOkr { get; set; } = new();
}

namespace PE_GOL.DTO.Requests.Okr;

/// <summary>
/// UPSERT masivo de los valores mensuales de UN KR (F6). 1..12 ítems, sin meses repetidos.
/// </summary>
public class ValorMensualKrUpdateRequest
{
    public List<ValorMensualKrItemRequest> Valores { get; set; } = new();
}

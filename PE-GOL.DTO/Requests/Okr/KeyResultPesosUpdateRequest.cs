namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Payload de actualización masiva de pesos — Spec HU-025 § F0 (Opción B, aprobada).
/// Se aplica sobre el conjunto COMPLETO de KRs del OKR y se valida Σ = 1.000 sobre el
/// conjunto resultante (CA #3 / RC-06). Es la única vía por la que el invariante se establece.</summary>
public class KeyResultPesosUpdateRequest
{
    /// <summary>Lista completa de pesos de todos los KRs del OKR.</summary>
    public List<KeyResultPesoRequest> Pesos { get; set; } = new();
}

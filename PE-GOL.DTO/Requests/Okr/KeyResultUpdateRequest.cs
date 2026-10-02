namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Payload de actualización de KR — Spec HU-025. Mismos campos que el create;
/// el codigo/orden/puntuaciones no son editables (CA #1, DB-04).</summary>
public class KeyResultUpdateRequest
{
    /// <summary>Descripción de la métrica concreta (1-500 chars).</summary>
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Peso ponderado del KR dentro del OKR. Rango (0, 1] con escala de 3 decimales.
    /// El invariante de suma NO se valida aquí (F0): solo el endpoint masivo de pesos lo valida.</summary>
    public decimal Peso { get; set; }
}

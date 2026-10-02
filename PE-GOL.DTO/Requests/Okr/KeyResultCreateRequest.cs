namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Payload de creación de KR — Spec HU-025 (CA #1). Sin TenantId/AreaId/CicloId/OkrId (SEC-06:
/// el okr_id viaja solo en la ruta del endpoint).</summary>
public class KeyResultCreateRequest
{
    /// <summary>Descripción de la métrica concreta (1-500 chars).</summary>
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Peso ponderado del KR dentro del OKR. Rango (0, 1] con escala de 3 decimales
    /// (DDL: peso DECIMAL(4,3) CHECK (peso > 0 AND peso <= 1)). El invariante de que la suma de
    /// los pesos de los KRs del mismo OKR sea exactamente 1.000 (CA #3 / RC-06) NO se valida aquí:
    /// se establece y se repara en exclusiva por el endpoint masivo de pesos (F0).</summary>
    public decimal Peso { get; set; }
}

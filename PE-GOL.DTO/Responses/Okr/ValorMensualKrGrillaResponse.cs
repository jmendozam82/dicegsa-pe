namespace PE_GOL.DTO.Responses.Okr;

/// <summary>
/// Respuesta del GET: toda la grilla del OKR en una sola llamada (F3).
/// </summary>
public class ValorMensualKrGrillaResponse
{
    public Guid OkrId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public string OkrDescripcion { get; set; } = string.Empty;
    public string PilarNombre { get; set; } = string.Empty;
    public Guid CicloId { get; set; }
    public int AnioFiscal { get; set; }
    public int MesInicio { get; set; }
    public DateOnly FechaNegocio { get; set; }       // F0c — la fecha de negocio que define la ventana
    public int MesActual { get; set; }               // mes calendario de FechaNegocio (1..12)
    public int MesMinEditable { get; set; }          // = MesInicio
    public int MesMaxEditable { get; set; }          // 0 | mes(hoy) | 12
    public List<int> MesesEditables { get; set; } = new();
    public decimal UmbralVerde { get; set; }         // del ciclo, tipo 'KPI' (RN-027)
    public decimal UmbralAmarillo { get; set; }
    public List<ValorMensualKrFilaResponse> KRs { get; set; } = new();
    public ValorMensualKrCalculoOkrResponse CalculoOkr { get; set; } = new();
}

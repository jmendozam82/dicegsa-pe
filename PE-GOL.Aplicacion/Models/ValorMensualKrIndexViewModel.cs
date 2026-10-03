using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel de la grilla de valores mensuales de KRs de un OKR (Spec HU-026 § UI).</summary>
public class ValorMensualKrIndexViewModel
{
    public Guid OkrId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public string OkrDescripcion { get; set; } = string.Empty;
    public string PilarNombre { get; set; } = string.Empty;
    public bool OkrNoExiste { get; set; }
    public List<ValorMensualKrFilaResponse> KRs { get; set; } = new();
    public decimal UmbralVerde { get; set; }
    public decimal UmbralAmarillo { get; set; }
    public int MesActual { get; set; }
    public int MesMinEditable { get; set; }
    public int MesMaxEditable { get; set; }
    public DateOnly FechaNegocio { get; set; }
    public List<int> MesesEditables { get; set; } = new();
    public ValorMensualKrCalculoOkrResponse? CalculoOkr { get; set; }
}

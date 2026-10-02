using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel del listado de Key Results de un OKR (Spec HU-025 § UI).</summary>
public class KeyResultIndexViewModel
{
    public Guid OkrId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public string OkrDescripcion { get; set; } = string.Empty;
    public string? PilarNombre { get; set; }
    public bool OkrNoExiste { get; set; }
    public List<KeyResultResponse> Items { get; set; } = new();
    public decimal SumaPesos { get; set; }
    public int Cantidad { get; set; }
    public int MaxKeyResults { get; set; } = 5;
}

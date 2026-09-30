using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel del listado de OKRs del área (Spec HU-024 § UI).</summary>
public class OkrIndexViewModel
{
    public bool HayCicloActivo { get; set; }
    public string? CicloNombre { get; set; }
    public List<OkrResponse> Items { get; set; } = new();
}

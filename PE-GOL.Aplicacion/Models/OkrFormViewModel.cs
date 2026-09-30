using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel del formulario Crear/Editar OKR (Spec HU-024 § UI).</summary>
public class OkrFormViewModel
{
    public Guid OkrId { get; set; }
    public string? Codigo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public Guid PilarId { get; set; }
    public List<PilarResponse> Pilares { get; set; } = new();
    public bool EsEdicion { get; set; }
}

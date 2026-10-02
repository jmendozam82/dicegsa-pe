namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel del formulario Crear/Editar Key Result (Spec HU-025 § UI).</summary>
public class KeyResultFormViewModel
{
    public Guid OkrId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public Guid? KeyResultId { get; set; }
    public string? Codigo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public decimal SumaPesosActual { get; set; }
    public decimal PesoRestanteSugerido { get; set; }
    public bool EsEdicion { get; set; }
}

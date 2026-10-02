namespace PE_GOL.Aplicacion.Models;

/// <summary>ViewModel del bloque «Repartir pesos» (Spec HU-025 § UI · F0 aprobado).</summary>
public class KeyResultPesosViewModel
{
    public Guid OkrId { get; set; }
    public string OkrCodigo { get; set; } = string.Empty;
    public List<KeyResultPesoFilaViewModel> Pesos { get; set; } = new();
    public decimal SumaPesos { get; set; }
}

/// <summary>Fila individual del reparto de pesos.</summary>
public class KeyResultPesoFilaViewModel
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Peso { get; set; }
}

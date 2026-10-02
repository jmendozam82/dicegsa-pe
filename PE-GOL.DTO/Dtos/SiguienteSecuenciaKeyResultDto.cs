namespace PE_GOL.DTO.Dtos;

/// <summary>Siguiente secuencia del OKR para KR (Spec HU-025 § Queries DAL — una sola query,
/// patrón SiguienteSecuenciaOkrDto de HU-024 / SiguienteSecuenciaPilarDto de HU-013).
/// SiguienteN: MAX(N del codigo 'KR.N')+1 (CA #1, F4). SiguienteOrden: MAX(orden)+1 del OKR.</summary>
public class SiguienteSecuenciaKeyResultDto
{
    public int SiguienteN { get; set; }
    public int SiguienteOrden { get; set; }
}

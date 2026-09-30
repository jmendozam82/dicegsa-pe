namespace PE_GOL.DTO.Dtos;

/// <summary>Siguiente secuencia del área para OKR (Spec HU-024 § Queries DAL — una sola query,
/// patrón SiguienteSecuenciaPilarDto de HU-013/DAL-P5). SiguienteN: MAX(N del codigo 'OKR.N')+1
/// (CA #1, F4). SiguienteOrden: MAX(orden)+1 del área en el ciclo.</summary>
public class SiguienteSecuenciaOkrDto
{
    public int SiguienteN { get; set; }
    public int SiguienteOrden { get; set; }
}

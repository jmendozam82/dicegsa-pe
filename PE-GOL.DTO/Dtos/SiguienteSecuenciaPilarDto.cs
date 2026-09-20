namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Siguiente secuencia del ciclo para pilar (Spec HU-013 § DTOs — DAL-P5, una sola query).
/// SiguienteN: siguiente número del código PEC-N (CA #1, D-B) — MAX(regexp_match(codigo,'^PEC-(\d+)$'))+1.
/// SiguienteOrden: siguiente orden secuencial (D-H) — MAX(orden)+1 del ciclo.
/// </summary>
public class SiguienteSecuenciaPilarDto
{
    /// <summary>Siguiente número del código PEC-N (CA #1).</summary>
    public int SiguienteN { get; set; }

    /// <summary>Siguiente orden secuencial (D-H).</summary>
    public int SiguienteOrden { get; set; }
}
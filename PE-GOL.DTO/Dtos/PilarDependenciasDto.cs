namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Conteos de dependencias del pilar (Spec HU-013 § DTOs — DAL-P9, CA #3).
/// TotalObjetivosCg/TotalOkrs: subqueries COUNT sobre objetivo_cg/okr por pilar_id.
/// </summary>
public class PilarDependenciasDto
{
    /// <summary>CA #3 — conteo de objetivo_cg asociados al pilar.</summary>
    public int TotalObjetivosCg { get; set; }

    /// <summary>CA #3 — conteo de okr asociados al pilar.</summary>
    public int TotalOkrs { get; set; }
}
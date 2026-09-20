namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO DAL de la escritura de objetivos trimestrales (Spec HU-014 § DTOs — DAL-P10, D-A).
/// El UPDATE solo toca objetivo_q1..q4 + updated_at — NO incluye codigo/nombre/
/// estrategia_victoria/orden (contrato HU-013 intacto, D-A).
/// TenantId proviene del TenantContext (SEC-06), nunca del body.
/// </summary>
public class PilarObjetivosTrimestralesUpdateDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public Guid PilarId { get; set; }

    /// <summary>Objetivo del trimestre Q1 (null = sin contenido, D-H).</summary>
    public string? ObjetivoQ1 { get; set; }

    /// <summary>Objetivo del trimestre Q2 (null = sin contenido, D-H).</summary>
    public string? ObjetivoQ2 { get; set; }

    /// <summary>Objetivo del trimestre Q3 (null = sin contenido, D-H).</summary>
    public string? ObjetivoQ3 { get; set; }

    /// <summary>Objetivo del trimestre Q4 (null = sin contenido, D-H).</summary>
    public string? ObjetivoQ4 { get; set; }
}
namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de Objetivos de Área por Trimestre (Spec HU-014 § UI — ObjetivosTrimestrales.cshtml).
/// Los 4 trimestres son OPCIONALES (CA #2): null o string vacío tras trim se persiste null (D-H).
/// Texto plano (D-D), máx 2000 chars por trimestre (D-C).
/// EsGerente → formulario editable (PUT); AdminTenant/JefeArea → solo lectura (RN-006/RN-007).
/// </summary>
public class ObjetivosTrimestralesViewModel
{
    public Guid CicloId { get; set; }
    public Guid PilarId { get; set; }
    public string PilarCodigo { get; set; } = string.Empty;
    public string PilarNombre { get; set; } = string.Empty;
    public string? ObjetivoQ1 { get; set; }
    public string? ObjetivoQ2 { get; set; }
    public string? ObjetivoQ3 { get; set; }
    public string? ObjetivoQ4 { get; set; }

    /// <summary>true → formulario editable (PUT); false → solo lectura (RN-006/RN-007).</summary>
    public bool EsGerente { get; set; }
}
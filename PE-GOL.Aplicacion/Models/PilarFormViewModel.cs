namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de Pilar Estratégico (Spec HU-013 § UI — Crear/Editar.cshtml).
/// Codigo "PEC-N" es auto-generado por la BLL (CA #1, D-B) — nunca viaja en el request;
/// en Editar se muestra como badge de solo lectura. Orden opcional (D-H): null → default secuencial.
/// </summary>
public class PilarFormViewModel
{
    public Guid CicloId { get; set; }
    public Guid PilarId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? EstrategiaVictoria { get; set; }
    public int? Orden { get; set; }

    /// <summary>true en Editar: el pilar existe (para mostrar código PEC-N).</summary>
    public bool EsEdicion { get; set; }

    /// <summary>"PEC-N" auto-generado (CA #1) — solo lectura.</summary>
    public string Codigo { get; set; } = string.Empty;
}
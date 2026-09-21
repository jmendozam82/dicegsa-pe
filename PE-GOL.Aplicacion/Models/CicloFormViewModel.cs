namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de ciclo (Spec HU-007 § UI — Crear/Editar/Clonar.cshtml).
/// Estado NO es editable (RC-12/RN-004): solo se edita un ciclo en 'Borrador'; en Editar se
/// muestra como badge. OrigenNombre solo aplica al modo Clonar (identidad del ciclo origen).
/// </summary>
public class CicloFormViewModel
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Requerido, 2000..2100 (regla de negocio HU-007).</summary>
    public int AñoFiscal { get; set; }

    /// <summary>Requerido, 1..12 (espejo del CHECK del DDL).</summary>
    public int MesInicio { get; set; }

    /// <summary>'Borrador' | 'Activo' | 'Cerrado' — solo lectura (badge).</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Nombre del ciclo origen (modo Clonar).</summary>
    public string? OrigenNombre { get; set; }
}
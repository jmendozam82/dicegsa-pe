namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// CA #1 — un mes de la escala del Gantt, derivado de ciclo.año_fiscal + ciclo.mes_inicio.
/// Con mes_inicio ≠ 1 la escala cruza de año (p. ej. JUN 2026 … MAY 2027), por eso la
/// etiqueta siempre lleva el año (Spec HU-021 § Lógica BLL paso 9).
/// </summary>
public class GanttMesResponse
{
    public int Anio { get; set; }

    /// <summary>1..12</summary>
    public int Mes { get; set; }

    /// <summary>Etiqueta para la escala del Gantt — "ENE 2026" (abreviatura 3 letras + año).</summary>
    public string Etiqueta { get; set; } = string.Empty;
}

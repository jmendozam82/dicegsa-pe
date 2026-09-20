namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero del Gerente (Spec HU-016 § DTOs — DAL-D7). No es una entidad (D-B).
/// Totales consolidados del ciclo: COUNT(accion_plan) y AVG GLOBAL de okr.puntuacion_final (D-P —
/// promedio real sobre TODOS los OKRs del ciclo, ponderado naturalmente por OKRs por área).
/// </summary>
public class TotalesConsolidadosDalDto
{
    public int TotalAcciones { get; set; }                // COUNT(accion_plan) del ciclo
    public decimal PromedioOkrs { get; set; }             // 0..1 — AVG(okr.puntuacion_final) global del ciclo
}
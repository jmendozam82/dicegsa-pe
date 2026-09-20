namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Totales consolidados del ciclo del tablero del Gerente (Spec HU-016 § DTOs — CA #2).
/// TotalAcciones: COUNT(accion_plan) del ciclo (DAL-D7).
/// AccionesAtrasadas: recalculado en BLL con RN-017 regla 4 (D-D) — misma fuente que el
/// per-área (DAL-D8) → consistencia garantizada.
/// PromedioOkrs: 0..1 — AVG(okr.puntuacion_final) GLOBAL del ciclo (D-P, DAL-D7).
/// </summary>
public class TotalesConsolidadosResponse
{
    public int TotalAcciones { get; set; }                    // CA #2 — COUNT(accion_plan) del ciclo (DAL-D7)
    public int AccionesAtrasadas { get; set; }                // CA #2 — recalculado en BLL con RN-017 regla 4 (D-D)
    public decimal PromedioOkrs { get; set; }                 // CA #2 — 0..1, AVG(okr.puntuacion_final) GLOBAL del ciclo (D-P)
}
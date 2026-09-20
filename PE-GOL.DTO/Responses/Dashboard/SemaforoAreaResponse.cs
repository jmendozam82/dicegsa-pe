namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Semáforos del tablero del JEF (Spec HU-015 § DTOs — CA #2, D-E).
/// Okrs: SemaforoHelper.Evaluar(PromedioPuntuacionOkrs, umbralKpi.Verde, umbralKpi.Amarillo).
/// PlanAccion: SemaforoHelper.Evaluar(AvancePlanAccion, umbralPlanAccion.Verde, umbralPlanAccion.Amarillo).
/// Global: SemaforoHelper.Evaluar((PromedioPuntuacionOkrs + AvancePlanAccion) / 2, umbralKpi.Verde, umbralKpi.Amarillo).
/// PromedioPuntuacionOkrs: 0..1 — componente OKR del semáforo global (trazabilidad, D-E).
/// Datos vacíos → "Rojo" (fórmula pura, D-N).
/// </summary>
public class SemaforoAreaResponse
{
    public string Okrs { get; set; } = "Rojo";            // "Verde" | "Amarillo" | "Rojo"
    public string PlanAccion { get; set; } = "Rojo";
    public string Global { get; set; } = "Rojo";          // CA #2 — promedio OKRs + plan de acción (D-E)
    public decimal PromedioPuntuacionOkrs { get; set; }   // 0..1 — componente OKR del semáforo global (trazabilidad)
}
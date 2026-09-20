namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Panel por área del tablero del Gerente (Spec HU-016 § DTOs — CA #1).
/// SemaforoGlobal: D-E — (AvanceOkrs + AvancePlanAccion) / 2 evaluado contra umbrales KPI
/// (SemaforoHelper, bordes inclusivos). Datos vacíos (0) → "Rojo" (fórmula pura, D-N).
/// AvanceOkrs/AvancePlanAccion: 0..1 — campos calculados PERSISTIDOS leídos por DAL-D6 (D-C).
/// AccionesAtrasadas: recalculado en BLL con RN-017 regla 4 (D-D) — no usa status persistido.
/// AlertaActiva: CA #4 — true ⇔ SemaforoGlobal == "Rojo" (D-Ñ).
/// </summary>
public class PanelAreaResponse
{
    public Guid AreaId { get; set; }
    public string AreaCodigo { get; set; } = string.Empty;    // "GOL1"
    public string AreaNombre { get; set; } = string.Empty;    // "CEDIS FARMA"
    public string SemaforoGlobal { get; set; } = "Rojo";      // CA #1 — D-E: (AvanceOkrs + AvancePlanAccion) / 2 vs umbrales KPI
    public decimal AvanceOkrs { get; set; }                   // CA #1 — 0..1, AVG(okr.puntuacion_final) del área (D-C)
    public decimal AvancePlanAccion { get; set; }             // CA #1 — 0..1, AVG(objetivo_cg.progreso) del área (D-C)
    public int AccionesAtrasadas { get; set; }                // CA #1 — recalculado en BLL con RN-017 regla 4 (D-D)
    public bool AlertaActiva { get; set; }                    // CA #4 — true ⇔ SemaforoGlobal == "Rojo" (D-Ñ)
}
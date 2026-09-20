namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Tarjetas resumen del tablero del JEF (Spec HU-015 § DTOs — CA #1).
/// TotalOkrs/OkrsAlcanzados: COUNT(okr) y COUNT(okr.semaforo='Verde') del área (DAL-D3, D-C).
/// AvancePlanAccion: 0..1, AVG(objetivo_cg.progreso) del área (DAL-D4, D-C).
/// AccionesAtrasadas: recalculado en BLL con RN-017 regla 4 (D-D — NO se lee status persistido).
/// DiasAlVencimientoMasCercano: min sobre pendientes (Progreso &lt; 100) de
/// (FechaVencimiento.Date - DateTime.Today).Days; null si no hay pendientes; negativo = vencida.
/// </summary>
public class TarjetasResumenResponse
{
    public int TotalOkrs { get; set; }                    // CA #1 — COUNT(okr) del área
    public int OkrsAlcanzados { get; set; }               // CA #1 — COUNT(okr.semaforo = 'Verde')
    public decimal AvancePlanAccion { get; set; }         // CA #1 — 0..1, AVG(objetivo_cg.progreso)
    public int AccionesAtrasadas { get; set; }            // CA #1 — recalculado en BLL con RN-017 (D-D)
    public int? DiasAlVencimientoMasCercano { get; set; } // CA #1 — null si no hay acciones pendientes;
                                                          //          negativo = vencida hace N días
}
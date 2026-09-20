namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero del Gerente (Spec HU-016 § DTOs — DAL-D8). No es una entidad (D-B).
/// Variante de AccionTableroDto (HU-015) que AÑADE AreaId (para agrupar atrasadas por área en BLL,
/// D-D) — el DTO de HU-015 no se modifica (aditividad). Fila ligera de accion_plan del ciclo: solo
/// lo que el tablero necesita para recalcular atrasadas en BLL (RN-017 regla 4). NO incluye status
/// persistido (puede estar desactualizado vs. el batch nocturno RN-040; el tablero recalcula con
/// fecha actual).
/// </summary>
public class AccionCicloTableroDto
{
    public Guid Id { get; set; }
    public Guid AreaId { get; set; }                      // para agrupar atrasadas por área en BLL (D-D)
    public decimal Progreso { get; set; }                 // 0..100 (%)
    public DateTime FechaVencimiento { get; set; }        // DATE
}
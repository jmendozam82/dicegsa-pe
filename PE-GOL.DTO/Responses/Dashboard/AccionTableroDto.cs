namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero (Spec HU-015 § DTOs — DAL-D5). No es una entidad (D-B).
/// Fila ligera de accion_plan del área: solo lo que el tablero necesita para recalcular
/// atrasadas y días al vencimiento en BLL (D-D — RN-017 regla 4). NO incluye status persistido
/// (puede estar desactualizado vs. el batch nocturno RN-040; el tablero recalcula con fecha actual).
/// </summary>
public class AccionTableroDto
{
    public Guid Id { get; set; }
    public decimal Progreso { get; set; }          // 0..100 (%)
    public DateTime FechaInicio { get; set; }      // DATE
    public DateTime FechaVencimiento { get; set; } // DATE
}
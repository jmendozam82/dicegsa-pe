namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero (Spec HU-015 § DTOs — DAL-D4). No es una entidad (D-B).
/// Agregación de lectura sobre objetivo_cg del área: COALESCE(AVG(ocg.progreso), 0).
/// Lee el progreso PERSISTIDO (D-C — mantenido por la BLL de HU-017+, DB-04).
/// </summary>
public class ResumenPlanAccionAreaDto
{
    public decimal AvancePlanAccion { get; set; }
}
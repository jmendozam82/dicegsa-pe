namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero del Gerente (Spec HU-016 § DTOs — DAL-D6). No es una entidad (D-B).
/// Fila ligera de área ACTIVA del ciclo con sus campos calculados PERSISTIDOS (D-C — mantenidos
/// por las BLL de HU-017+/HU-024+, DB-04): promedio de puntuación de OKRs y avance del plan de
/// acción. El semáforo (Verde/Amarillo/Rojo) se calcula en BLL (DB-04), nunca en SQL.
/// </summary>
public class ResumenAreaTableroDto
{
    public Guid AreaId { get; set; }
    public string AreaCodigo { get; set; } = string.Empty;
    public string AreaNombre { get; set; } = string.Empty;
    public decimal PromedioPuntuacionOkrs { get; set; }   // 0..1 — AVG(okr.puntuacion_final) del área
    public decimal AvancePlanAccion { get; set; }         // 0..1 — AVG(objetivo_cg.progreso) del área
}
namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// DTO DAL interno del tablero (Spec HU-015 § DTOs — DAL-D3). No es una entidad (D-B).
/// Agregación de lectura sobre okr del área: COUNT(o.id), COUNT(o.id) FILTER (semaforo='Verde')
/// y COALESCE(AVG(o.puntuacion_final), 0). Lee campos calculados PERSISTIDOS (D-C — mantenidos
/// por la BLL de HU-024+, DB-04). Sin TenantId: el response lo toma del TenantContext (SEC-06).
/// </summary>
public class ResumenOkrsAreaDto
{
    public int TotalOkrs { get; set; }
    public int OkrsAlcanzados { get; set; }
    public decimal PromedioPuntuacionOkrs { get; set; }
}
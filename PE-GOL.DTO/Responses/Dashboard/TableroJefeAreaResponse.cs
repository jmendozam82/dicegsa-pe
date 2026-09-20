namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Tablero de inicio del Jefe de Área (Spec HU-015 § DTOs — GET /api/v1/dashboard/jefe-area).
/// Contrato de datos estable (D-B): no cambia cuando lleguen HU-017+/HU-024+.
/// CicloId/CicloNombre/AñoFiscal: ciclo ACTIVO del tenant (CA #4, D-G — resuelto en BLL vía DAL-D1).
/// AreaId/AreaCodigo/AreaNombre: área del JEF desde TenantContext.AreaId (SEC-07).
/// Tarjetas: CA #1 (Total OKRs · OKRs alcanzados · % avance plan · atrasadas · días al vencimiento).
/// Semaforo: CA #2 (promedio OKRs + plan de acción, D-E).
/// </summary>
public class TableroJefeAreaResponse
{
    public Guid CicloId { get; set; }
    public string CicloNombre { get; set; } = string.Empty;   // "PE 2026"
    public int AñoFiscal { get; set; }
    public Guid AreaId { get; set; }
    public string AreaCodigo { get; set; } = string.Empty;    // "GOL1"
    public string AreaNombre { get; set; } = string.Empty;    // "CEDIS FARMA"
    public TarjetasResumenResponse Tarjetas { get; set; } = new();
    public SemaforoAreaResponse Semaforo { get; set; } = new();
}
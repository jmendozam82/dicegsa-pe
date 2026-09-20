namespace PE_GOL.DTO.Responses.Dashboard;

/// <summary>
/// Tablero de inicio del Gerente (Spec HU-016 § DTOs — GET /api/v1/dashboard/gerente).
/// Contrato de datos estable (D-B): no cambia cuando lleguen HU-017+/HU-024+.
/// CicloId/CicloNombre/AñoFiscal: ciclo ACTIVO del tenant (CA #4, D-G — resuelto en BLL vía DAL-D1).
/// Paneles: CA #1 — uno por área ACTIVA del ciclo (DAL-D6, orden a.orden ASC, a.codigo ASC).
/// Totales: CA #2 — total acciones, atrasadas (recalculadas en BLL, D-D) y promedio OKRs global (D-P).
/// AreasConAlertaActiva: CA #4 — count de paneles con AlertaActiva=true (DB-04, D-Ñ).
/// </summary>
public class TableroGerenteResponse
{
    public Guid CicloId { get; set; }
    public string CicloNombre { get; set; } = string.Empty;   // "PE 2026"
    public int AñoFiscal { get; set; }
    public List<PanelAreaResponse> Paneles { get; set; } = new();   // CA #1 — uno por área ACTIVA del ciclo
    public TotalesConsolidadosResponse Totales { get; set; } = new(); // CA #2
    public int AreasConAlertaActiva { get; set; }              // CA #4 — count de paneles con AlertaActiva=true (DB-04, D-Ñ)
}
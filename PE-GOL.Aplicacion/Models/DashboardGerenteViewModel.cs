using PE_GOL.DTO.Responses.Dashboard;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del tablero de inicio del Gerente (Spec HU-016 § UI — Gerente.cshtml).
/// Datos: GET /api/v1/dashboard/gerente (solo Gerente — RN-006; SEC-07 NO APLICA, D-Q).
/// Renderiza .kpi-card (DS § 5.1) para totales y paneles por área (CA #1/#2/#4).
/// </summary>
public class DashboardGerenteViewModel
{
    /// <summary>
    /// Null cuando no hay ciclo activo o la API devuelve error → la vista muestra el estado vacío (UX-05).
    /// </summary>
    public TableroGerenteResponse? Tablero { get; set; }
}
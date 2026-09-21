using PE_GOL.DTO.Responses.Dashboard;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del tablero de inicio del Jefe de Área (Spec HU-015 § UI — JefeArea.cshtml).
/// Datos: GET /api/v1/dashboard/jefe-area (solo JefeArea — SEC-07: la API filtra por AreaId del JWT).
/// Renderiza .kpi-card (DS § 5.1) para las tarjetas resumen (CA #1) y los semáforos del área (CA #2).
/// </summary>
public class DashboardJefeAreaViewModel
{
    /// <summary>
    /// Null cuando no hay ciclo activo o la API devuelve error → la vista muestra el estado vacío (UX-05).
    /// </summary>
    public TableroJefeAreaResponse? Tablero { get; set; }
}
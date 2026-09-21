using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del historial de progreso de una acción (Spec HU-020 § UI — Historial.cshtml).
/// Datos: GET /api/v1/acciones/{id}/historial (JefeArea, Gerente — HU-020; el Gerente lo usa
/// para su tablero HU-016 y vistas consolidadas HU-018). Ordenado por created_at DESC (BLL).
/// </summary>
public class AccionPlanHistorialViewModel
{
    public Guid AccionId { get; set; }
    public Guid ObjetivoCgId { get; set; }

    /// <summary>Contexto del CG (solo lectura; vacío si el rol no puede leer el detalle del CG).</summary>
    public string ObjetivoCgCodigo { get; set; } = string.Empty;

    /// <summary>"{CG}.NN" auto-generado (CA #1).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public List<HistorialProgresoResponse> Items { get; set; } = [];
}
using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de planes de suscripción (HU-002 § UI).
/// El listado de planes NO es paginado (D1 del spec: catálogo acotado,
/// GET /api/v1/planes retorna List&lt;PlanResponse&gt; completo).
/// </summary>
public class PlanesIndexViewModel
{
    public List<PlanResponse> Items { get; set; } = [];
    public int Total { get; set; }
}
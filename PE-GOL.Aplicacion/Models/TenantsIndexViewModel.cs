using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de tenants (Spec HU-045 § Vistas Razor / ViewModels).
/// Mapea PagedResult&lt;TenantResponse&gt; + filtros activos + catálogo de planes.
/// </summary>
public class TenantsIndexViewModel
{
    public List<TenantResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public string? EstadoFiltro { get; set; }
    public Guid? PlanIdFiltro { get; set; }
    public List<PlanResponse> Planes { get; set; } = [];
}
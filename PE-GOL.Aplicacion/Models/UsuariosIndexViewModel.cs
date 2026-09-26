using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de usuarios de tenant (HU-003 § UI del SA).
/// Mapea PagedResult&lt;UsuarioResponse&gt; + filtros y catálogo de tenants (dropdown de filtro).
/// </summary>
public class UsuariosIndexViewModel
{
    public List<UsuarioResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public string? RolFiltro { get; set; }
    public string? EstadoFiltro { get; set; }
    public Guid? TenantIdFiltro { get; set; }
    public List<TenantResponse> Tenants { get; set; } = [];
}
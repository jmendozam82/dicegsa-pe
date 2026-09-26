using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado del Log de Auditoría (HU-005 § UI).
/// Mapea PagedResult&lt;LogAuditoriaResponse&gt; + filtros activos + catálogo de
/// tenants (dropdown de filtro del SA). Sin valor_anterior/valor_nuevo (D3:
/// el listado es resumen; el detalle GET /{id} los expone).
/// </summary>
public class LogAuditoriaIndexViewModel
{
    public List<LogAuditoriaResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public string? AccionFiltro { get; set; }
    public DateTimeOffset? DesdeFiltro { get; set; }
    public DateTimeOffset? HastaFiltro { get; set; }
    public Guid? TenantIdFiltro { get; set; }
    public List<TenantResponse> Tenants { get; set; } = [];
}
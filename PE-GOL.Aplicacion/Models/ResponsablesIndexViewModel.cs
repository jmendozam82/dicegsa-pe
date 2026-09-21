using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de responsables (Spec HU-010 § UI — Index.cshtml).
/// El ciclo se selecciona con un dropdown (catálogo GET /api/v1/ciclos); sin ciclo seleccionado
/// se muestra un estado vacío guiando a elegir uno. JefeArea ve solo su responsable (SEC-07).
/// </summary>
public class ResponsablesIndexViewModel
{
    public List<CicloResponse> Ciclos { get; set; } = [];

    /// <summary>Ciclo seleccionado (query param cicloId).</summary>
    public Guid? CicloId { get; set; }

    public string? CicloNombre { get; set; }
    public string? CicloEstado { get; set; }

    public List<ResponsableResponse> Items { get; set; } = [];

    /// <summary>true → botones crear/editar/desactivar visibles (AdminTenant).</summary>
    public bool EsAdmin { get; set; }
}
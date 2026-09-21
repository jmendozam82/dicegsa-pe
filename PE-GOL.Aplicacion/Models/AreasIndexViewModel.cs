using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de áreas (Spec HU-009 § UI — Index.cshtml).
/// El ciclo se selecciona con un dropdown (catálogo GET /api/v1/ciclos); sin ciclo seleccionado
/// se muestra un estado vacío guiando a elegir uno. JefeArea ve solo su área (SEC-07 — la API filtra).
/// </summary>
public class AreasIndexViewModel
{
    public List<CicloResponse> Ciclos { get; set; } = [];

    /// <summary>Ciclo seleccionado (query param cicloId).</summary>
    public Guid? CicloId { get; set; }

    public string? CicloNombre { get; set; }
    public string? CicloEstado { get; set; }

    public List<AreaResponse> Items { get; set; } = [];

    /// <summary>true → botones crear/editar/desactivar visibles (AdminTenant).</summary>
    public bool EsAdmin { get; set; }
}
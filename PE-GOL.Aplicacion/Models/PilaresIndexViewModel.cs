using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de Pilares Estratégicos (Spec HU-013 § UI — Index.cshtml).
/// El ciclo se selecciona con un dropdown (catálogo GET /api/v1/ciclos); sin ciclo seleccionado
/// se muestra un estado vacío guiando a elegir uno.
/// Items: GET /api/v1/ciclos/{cicloId}/pilares (multi-rol ADM/GER/JEF — SEC-07 NO APLICA, D-E).
/// EsGerente → botones crear/editar/eliminar/objetivos visibles (RN-006); ADM/JEF solo lectura (RN-007).
/// </summary>
public class PilaresIndexViewModel
{
    public List<CicloResponse> Ciclos { get; set; } = [];

    /// <summary>Ciclo seleccionado (query param cicloId).</summary>
    public Guid? CicloId { get; set; }

    public string? CicloNombre { get; set; }
    public string? CicloEstado { get; set; }

    public List<PilarResponse> Items { get; set; } = [];

    /// <summary>true → botones crear/editar/eliminar/objetivos visibles (Gerente).</summary>
    public bool EsGerente { get; set; }
}
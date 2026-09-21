using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de Filosofía Corporativa (Spec HU-011 § UI — Index.cshtml; HU-012 § UI — Valores.cshtml).
/// El ciclo se selecciona con un dropdown (catálogo GET /api/v1/ciclos); sin ciclo seleccionado
/// se muestra un estado vacío guiando a elegir uno.
/// Vision/Mision viajan como HTML sanitizado por la BLL (allowlist HU-011 D-C) → render seguro con
/// @Html.Raw (el servidor es la fuente de verdad, UX-04). Valores: lista ordenada (HU-012 D-C).
/// EsGerente → formulario editable (PUT); AdminTenant/JefeArea → solo lectura (RN-006/RN-007).
/// </summary>
public class FilosofiaViewModel
{
    public List<CicloResponse> Ciclos { get; set; } = [];

    /// <summary>Ciclo seleccionado (query param cicloId).</summary>
    public Guid? CicloId { get; set; }

    public string? CicloNombre { get; set; }
    public string? CicloEstado { get; set; }

    /// <summary>HTML sanitizado por la BLL (HU-011 D-C) — render seguro con @Html.Raw.</summary>
    public string Vision { get; set; } = string.Empty;

    /// <summary>HTML sanitizado por la BLL (HU-011 D-C) — render seguro con @Html.Raw.</summary>
    public string Mision { get; set; } = string.Empty;

    /// <summary>Lista ordenada de Valores Corporativos (HU-012 D-C).</summary>
    public List<string> Valores { get; set; } = [];

    /// <summary>JOIN a usuario (nombre del último editor) — trazabilidad (HU-011 CA #4).</summary>
    public string? UpdatedByNombre { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>true → formularios editables (PUT); false → solo lectura (RN-006/RN-007).</summary>
    public bool EsGerente { get; set; }
}
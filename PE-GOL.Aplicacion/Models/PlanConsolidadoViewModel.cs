using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de la Vista Consolidada del Plan (Spec HU-023 § UI — Consolidado.cshtml).
/// Fuente única: GET /api/v1/planes/consolidado (Gerente only). La capa MVC no calcula
/// nada de negocio: solo proyecta el payload de la API a la vista.
///
/// Empty states (UX-05):
///   · Sin ciclo activo  → HayCicloActivo = false (la API respondió 404).
///   · Ciclo sin acciones → HayCicloActivo = true, HayAcciones = false.
///   · Filtros sin coincidencias → HayAcciones = true, Items vacío (el JS muestra el empty state).
/// </summary>
public class PlanConsolidadoViewModel
{
    /// <summary>Hay un ciclo Activo en el tenant (RC-01).</summary>
    public bool HayCicloActivo { get; set; }

    /// <summary>El ciclo tiene al menos una acción (sin filtros).</summary>
    public bool HayAcciones { get; set; }

    /// <summary>Nombre del ciclo activo — "PE 2026".</summary>
    public string? CicloNombre { get; set; }

    /// <summary>Resumen de conteos por status (CA #3).</summary>
    public ResumenConsolidado? Resumen { get; set; }

    /// <summary>Lista paginada de acciones consolidadas.</summary>
    public List<ConsolidadoItemResponse> Items { get; set; } = new();

    /// <summary>Información de paginación.</summary>
    public PaginacionInfo? Paginacion { get; set; }

    /// <summary>Token JWT para llamadas fetch desde el frontend.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Lista de áreas para el filtro (Gerente ve todas).</summary>
    public List<AreaResponse> Areas { get; set; } = new();

    /// <summary>Lista de objetivos CG para el filtro.</summary>
    public List<ObjetivoCgResponse> ObjetivosCg { get; set; } = new();
}

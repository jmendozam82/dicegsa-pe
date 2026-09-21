using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de la Vista Consolidada de CGs del Gerente (Spec HU-018 § UI — Consolidado.cshtml).
/// Datos: GET /api/v1/objetivos-cg/consolidado (solo Gerente; ciclo activo resuelto server-side, D-B).
/// Filtros dinámicos (ObjetivoCgFilterRequest vía [FromQuery]): AreaId, PilarId, Trimestre, Semaforo.
/// Areas/Pilares: catálogos del ciclo activo para poblar los selects de filtro (multi-rol).
/// </summary>
public class ObjetivoCgConsolidadoViewModel
{
    public List<ObjetivoCgConsolidadoResponse> Items { get; set; } = [];

    /// <summary>Catálogo de áreas del ciclo activo (filtro).</summary>
    public List<AreaResponse> Areas { get; set; } = [];

    /// <summary>Catálogo de pilares del ciclo activo (filtro).</summary>
    public List<PilarResponse> Pilares { get; set; } = [];

    /// <summary>Filtros aplicados (query string).</summary>
    public ObjetivoCgFilterRequest Filtros { get; set; } = new();

    /// <summary>Nombre del ciclo activo (contexto del encabezado).</summary>
    public string? CicloNombre { get; set; }

    /// <summary>true si el tenant tiene un ciclo activo (si no, estado vacío).</summary>
    public bool HayCicloActivo { get; set; }
}
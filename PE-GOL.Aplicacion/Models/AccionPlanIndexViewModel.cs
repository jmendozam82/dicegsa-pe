using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del Plan de Acción del Jefe de Área (Spec HU-019 § UI — Index.cshtml).
/// El Objetivo CG se selecciona con un dropdown (catálogo GET /api/v1/objetivos-cg — SEC-07);
/// sin CG seleccionado se muestra un estado vacío guiando a elegir uno.
/// Items: GET /api/v1/objetivos-cg/{objetivoCgId}/acciones (JefeArea, Gerente — HU-019).
/// </summary>
public class AccionPlanIndexViewModel
{
    /// <summary>Catálogo de CGs del área del JEF (selector).</summary>
    public List<ObjetivoCgResponse> ObjetivosCg { get; set; } = [];

    /// <summary>CG seleccionado (query param objetivoCgId).</summary>
    public Guid? ObjetivoCgId { get; set; }

    /// <summary>Contexto del CG seleccionado (encabezado).</summary>
    public string? ObjetivoCgCodigo { get; set; }
    public string? ObjetivoCgDescripcion { get; set; }
    public decimal ObjetivoCgProgreso { get; set; }
    public string? ObjetivoCgSemaforo { get; set; }

    public List<AccionPlanResponse> Items { get; set; } = [];

    /// <summary>Nombre del ciclo activo (contexto del encabezado).</summary>
    public string? CicloNombre { get; set; }

    /// <summary>true si el tenant tiene un ciclo activo (si no, estado vacío).</summary>
    public bool HayCicloActivo { get; set; }
}
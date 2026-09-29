namespace PE_GOL.DTO.Requests.PlanOperativo;

/// <summary>
/// Filtros opcionales para la vista consolidada del Plan — Spec HU-023.
/// Todos los filtros son opcionales y combinables (AND lógico).
/// SEC-06: nunca incluye TenantId — proviene del JWT.
/// </summary>
public class FiltrosConsolidadoRequest
{
    /// <summary>Filtrar por área estratégica (GUID).</summary>
    public Guid? AreaId { get; set; }

    /// <summary>Filtrar por Objetivo CG (GUID).</summary>
    public Guid? CgId { get; set; }

    /// <summary>Filtrar por status: NoIniciado | EnProgreso | Terminado | Atrasado.</summary>
    public string? Status { get; set; }

    /// <summary>Filtrar por clasificación: Proyecto | Iniciativa | Operativa.</summary>
    public string? Clasificacion { get; set; }

    /// <summary>Filtrar por tipo de presupuesto: OPEX | CAPEX.</summary>
    public string? Tipo { get; set; }

    /// <summary>Fecha de inicio mínima (inclusiva).</summary>
    public DateTime? FechaDesde { get; set; }

    /// <summary>Fecha de inicio máxima (inclusiva).</summary>
    public DateTime? FechaHasta { get; set; }

    /// <summary>Número de página (1-based). Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Tamaño de página. Default: 25. Max: 100.</summary>
    public int PageSize { get; set; } = 25;
}

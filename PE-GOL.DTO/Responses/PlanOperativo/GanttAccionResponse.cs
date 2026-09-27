using System;

namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// Barra del Gantt = una acción del plan (CA #2, coloreada por Status). Lista plana; cada
/// acción referencia su grupo por ObjetivoCgId. Los enums viajan como Enum::text de
/// PostgreSQL tal cual los expone el código real: Status "NoIniciado|EnProgreso|Terminado|Atrasado",
/// Clasificacion "Proyecto|Iniciativa|Operativa", TipoPresupuesto "OPEX|CAPEX".
/// NO expone TenantId (SEC-06 — test 23).
/// </summary>
public class GanttAccionResponse
{
    public Guid Id { get; set; }

    /// <summary>Grupo (Objetivo CG) al que pertenece la acción — CA #3.</summary>
    public Guid ObjetivoCgId { get; set; }

    public Guid AreaId { get; set; }

    /// <summary>Código de la acción — "GOL1.CG1.03".</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Inicio de la barra (columna DATE → medianoche).</summary>
    public DateTime FechaInicio { get; set; }

    /// <summary>Vencimiento de la barra (columna DATE → medianoche; inclusivo en DHTMLX).</summary>
    public DateTime FechaVencimiento { get; set; }

    /// <summary>LÍDO de accion_plan.progreso (DB-04, D-M) — escala 0..100 (DECIMAL(5,2)).</summary>
    public decimal Progreso { get; set; }

    /// <summary>LÍDO de accion_plan.status (DB-04, D-M / F5): el Gantt NO reimplementa RN-017.</summary>
    public string Status { get; set; } = string.Empty;

    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;

    /// <summary>LÍDO (DECIMAL(4,3)) — es un factor 0.001..1, NO un porcentaje.</summary>
    public decimal Peso { get; set; }

    /// <summary>LEFT JOIN usuario: null si la acción no tiene responsable.</summary>
    public string? ResponsableNombre { get; set; }

    public int Orden { get; set; }
}

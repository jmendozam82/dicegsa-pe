using System;

namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// CA #3 — grupo del Gantt = un Objetivo CG **que tenga al menos una acción** del ciclo activo.
/// El frontend lo usa como tarea padre (type:"project"); las acciones la referencian por ObjetivoCgId.
/// NO expone TenantId (SEC-06 — test 23).
/// </summary>
public class GanttGrupoResponse
{
    public Guid ObjetivoCgId { get; set; }

    /// <summary>Código del CG — "GOL1.CG1".</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;
    public Guid AreaId { get; set; }

    /// <summary>Código del área — "GOL1" (etiqueta del selector de área del Gerente, F6).</summary>
    public string AreaCodigo { get; set; } = string.Empty;

    /// <summary>Nombre del área — "CEDIS FARMA".</summary>
    public string AreaNombre { get; set; } = string.Empty;

    /// <summary>
    /// LÍDO de objetivo_cg.progreso (DB-04) — escala 0..1 (DECIMAL(5,4)), NO 0..100.
    /// El front formatea: Math.round(progreso * 100) + '%'.
    /// </summary>
    public decimal Progreso { get; set; }

    /// <summary>LÍDO de objetivo_cg.semaforo (DB-04) — "Verde" | "Amarillo" | "Rojo".</summary>
    public string Semaforo { get; set; } = string.Empty;

    /// <summary>Calculado en BLL contando las acciones del grupo (DB-04, D-K: no hay COUNT en SQL).</summary>
    public int TotalAcciones { get; set; }
}

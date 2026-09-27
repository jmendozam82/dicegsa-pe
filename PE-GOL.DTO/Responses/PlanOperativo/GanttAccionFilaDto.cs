using System;

namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// Fila plana de DAL-G1 (acción + metadatos de CG y área). Solo DAL: nunca sale por la API;
/// la BLL la mapea a GanttAccionResponse / GanttGrupoResponse (Spec HU-021 § Queries DAL).
/// El mapeo Dapper resuelve los alias snake_case por DefaultTypeMap.MatchNamesWithUnderscores
/// (DbConnectionFactory L20) — mismo mecanismo que AccionPlanEntity.
/// </summary>
public class GanttAccionFilaDto
{
    // --- accion_plan ---
    public Guid Id { get; set; }
    public Guid ObjetivoCgId { get; set; }
    public Guid AreaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public decimal Progreso { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Orden { get; set; }

    /// <summary>LEFT JOIN usuario: null cuando la acción no tiene responsable (responsable_id NULL).</summary>
    public string? ResponsableNombre { get; set; }

    // --- objetivo_cg (JOIN, para el agrupado) ---
    public int ObjetivoOrden { get; set; }
    public string ObjetivoCodigo { get; set; } = string.Empty;
    public string ObjetivoDescripcion { get; set; } = string.Empty;

    /// <summary>LÍDO de objetivo_cg.progreso (DECIMAL(5,4) → escala 0..1). DB-04: no se recalcula.</summary>
    public decimal ObjetivoProgreso { get; set; }

    /// <summary>LÍDO de objetivo_cg.semaforo (enum ::text). DB-04: no se recalcula.</summary>
    public string ObjetivoSemaforo { get; set; } = string.Empty;

    // --- area (JOIN) ---
    public string AreaCodigo { get; set; } = string.Empty;
    public string AreaNombre { get; set; } = string.Empty;
}

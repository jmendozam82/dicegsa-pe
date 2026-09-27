using System;
using System.Collections.Generic;

namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// Payload del Gantt del Plan de Acción del **ciclo activo** del tenant (Spec HU-021, solo lectura).
/// Sin TenantId por diseño (SEC-06 — test 23): el tenant sale del JWT y el ciclo activo se
/// resuelve en BLL (RC-01, DAL-D1). CicloId es dato de SALIDA, nunca una entrada del cliente.
/// </summary>
public class GanttPlanResponse
{
    public Guid CicloId { get; set; }

    /// <summary>Nombre del ciclo activo — "PE 2026".</summary>
    public string CicloNombre { get; set; } = string.Empty;

    /// <summary>ciclo.año_fiscal (columna con acento: llega por alias snake_case desde DAL-D1).</summary>
    public int AñoFiscal { get; set; }

    /// <summary>ciclo.mes_inicio (1..12) — mes en que arranca la escala.</summary>
    public int MesInicio { get; set; }

    /// <summary>Primer día del ciclo — base de la escala y de gantt.config.min_date.</summary>
    public DateTime FechaInicioCiclo { get; set; }

    /// <summary>Último día del ciclo — gantt.config.max_date (CicloFechaHelper.ObtenerRango).</summary>
    public DateTime FechaFinCiclo { get; set; }

    /// <summary>CA #1 — exactamente 12 meses, desde MesInicio inclusive (cruza de año si MesInicio ≠ 1).</summary>
    public List<GanttMesResponse> Escala { get; set; } = new();

    /// <summary>CA #3 — un grupo por Objetivo CG que tenga al menos una acción.</summary>
    public List<GanttGrupoResponse> Grupos { get; set; } = new();

    /// <summary>Acciones planas; cada una referencia su grupo por ObjetivoCgId.</summary>
    public List<GanttAccionResponse> Acciones { get; set; } = new();

    /// <summary>
    /// RF-031 parcial — totales por status. Las 4 claves (NoIniciado, EnProgreso, Terminado,
    /// Atrasado) están SIEMPRE presentes en 0 si no hay acciones de ese tipo: el front no adivina.
    /// </summary>
    public Dictionary<string, int> ConteoPorStatus { get; set; } = new();
}

namespace PE_GOL.DTO.Responses.PlanOperativo;

/// <summary>
/// Respuesta de la vista consolidada del Plan — Spec HU-023.
/// Sin TenantId (SEC-06): el tenant sale del JWT.
/// </summary>
public class ConsolidadoResponse
{
    /// <summary>Lista paginada de acciones consolidadas.</summary>
    public List<ConsolidadoItemResponse> Items { get; set; } = new();

    /// <summary>Resumen de conteos por status (CA #3).</summary>
    public ResumenConsolidado Resumen { get; set; } = new();

    /// <summary>Paginación.</summary>
    public PaginacionInfo Paginacion { get; set; } = new();
}

/// <summary>
/// Resumen de conteos por status — calculado en BLL (DB-04).
/// </summary>
public class ResumenConsolidado
{
    public int Total { get; set; }
    public int NoIniciado { get; set; }
    public int EnProgreso { get; set; }
    public int Terminado { get; set; }
    public int Atrasado { get; set; }
}

/// <summary>
/// Información de paginación.
/// </summary>
public class PaginacionInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Fila de la tabla consolidada — Spec HU-023.
/// </summary>
public class ConsolidadoItemResponse
{
    // --- accion_plan ---
    public Guid AccionId { get; set; }
    public string AccionCodigo { get; set; } = string.Empty;
    public string AccionDescripcion { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;
    public decimal Progreso { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public string? ResponsableNombre { get; set; }

    // --- objetivo_cg ---
    public Guid ObjetivoCgId { get; set; }
    public string ObjetivoCodigo { get; set; } = string.Empty;
    public string ObjetivoDescripcion { get; set; } = string.Empty;
    public decimal ObjetivoProgreso { get; set; }
    public string ObjetivoSemaforo { get; set; } = string.Empty;

    // --- pilar ---
    public string PilarCodigo { get; set; } = string.Empty;
    public string PilarNombre { get; set; } = string.Empty;

    // --- area ---
    public Guid AreaId { get; set; }
    public string AreaCodigo { get; set; } = string.Empty;
    public string AreaNombre { get; set; } = string.Empty;

    // --- ciclo ---
    public Guid CicloId { get; set; }
    public string CicloNombre { get; set; } = string.Empty;
    public int CicloAnioFiscal { get; set; }
}

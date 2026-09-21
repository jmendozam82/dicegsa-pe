using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de Acción del Plan (Spec HU-019 § UI — Crear/Editar.cshtml).
/// Codigo "{CG}.NN" es auto-generado por la BLL (CA #1, D-B) — nunca viaja en el request;
/// en Editar se muestra como badge de solo lectura. El CG es contexto de solo lectura
/// (AccionPlanUpdateRequest NO incluye ObjetivoCgId — el hidden objetivoCgId solo sirve
/// para el redirect/re-render tras el POST).
/// Responsables: GET /api/v1/ciclos/{cicloId}/responsables (JEF: solo el suyo — SEC-07).
/// </summary>
public class AccionPlanFormViewModel
{
    public Guid AccionId { get; set; }
    public Guid ObjetivoCgId { get; set; }

    /// <summary>Contexto del CG (solo lectura).</summary>
    public string ObjetivoCgCodigo { get; set; } = string.Empty;
    public string ObjetivoCgDescripcion { get; set; } = string.Empty;

    /// <summary>"{CG}.NN" auto-generado (CA #1) — solo lectura en Editar.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionEntregable { get; set; }
    public Guid? ResponsableId { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaVencimiento { get; set; }

    /// <summary>"Proyecto" | "Iniciativa" | "Operativa".</summary>
    public string Clasificacion { get; set; } = string.Empty;

    /// <summary>"OPEX" | "CAPEX".</summary>
    public string TipoPresupuesto { get; set; } = string.Empty;

    /// <summary>0..1 — suma de pesos del CG ≤ 1 (RN-016).</summary>
    public decimal? Peso { get; set; }

    public string? Aclaraciones { get; set; }

    /// <summary>Catálogo de responsables del ciclo activo (JEF: solo el suyo — SEC-07).</summary>
    public List<ResponsableResponse> Responsables { get; set; } = [];

    /// <summary>true en Editar: la acción existe (para mostrar código).</summary>
    public bool EsEdicion { get; set; }
}
namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de actualización de progreso de una acción (Spec HU-020 § UI — Progreso.cshtml).
/// Datos: GET /api/v1/acciones/{id} (JefeArea, Gerente — HU-019) + PATCH /api/v1/acciones/{id}/progreso
/// (solo JefeArea — HU-020). El status se recalcula en la BLL con RN-017 (4 reglas) — la vista
/// solo muestra el status actual y el nuevo % propuesto (DB-04: sin lógica de cálculo en la vista).
/// </summary>
public class AccionPlanProgresoViewModel
{
    public Guid AccionId { get; set; }
    public Guid ObjetivoCgId { get; set; }

    /// <summary>Contexto del CG (solo lectura).</summary>
    public string ObjetivoCgCodigo { get; set; } = string.Empty;

    /// <summary>"{CG}.NN" auto-generado (CA #1).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>% de progreso actual (0..100).</summary>
    public decimal Progreso { get; set; }

    /// <summary>"NoIniciado" | "EnProgreso" | "Atrasado" | "Terminado" (RN-017).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }

    /// <summary>0..1 — peso de la acción en el CG (RN-016).</summary>
    public decimal Peso { get; set; }

    /// <summary>peso * progreso / 100 (RN-017 paso 6).</summary>
    public decimal PuntuacionPonderada { get; set; }
}
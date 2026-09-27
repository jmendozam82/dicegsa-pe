using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de la Vista Gantt del Plan de Acción (Spec HU-021 § UI — Gantt.cshtml).
/// Fuente única: GET /api/v1/acciones/gantt (JefeArea, Gerente — F7). La capa MVC no calcula
/// nada de negocio: solo proyecta el payload de la API al .empty-state, a la escala y al
/// &lt;script type="application/json"&gt; que consume gantt-plan.js.
///
/// Empty states (UX-05):
///   · Sin ciclo activo  → HayCicloActivo = false, HayAcciones = false, JsonPlan = "{}" (la API
///     respondió 404). El 404 NO es un error: no se pone TempData["Error"].
///   · Ciclo sin acciones → HayCicloActivo = true, HayAcciones = false; la escala de 12 meses y los
///     4 conteos en 0 siguen viajando para que la vista pueda dibujar los ejes.
/// </summary>
public class AccionPlanGanttViewModel
{
    /// <summary>Hay un ciclo Activo en el tenant (RC-01) y por tanto escala de 12 meses.</summary>
    public bool HayCicloActivo { get; set; }

    /// <summary>El ciclo tiene al menos una acción que dibujar. Determina el .empty-state.</summary>
    public bool HayAcciones { get; set; }

    /// <summary>Nombre del ciclo activo — "PE 2026".</summary>
    public string? CicloNombre { get; set; }

    /// <summary>CA #1 — 12 meses desde ciclo.mes_inicio (cruza de año si mes_inicio ≠ 1).</summary>
    public List<GanttMesResponse> Escala { get; set; } = new();

    /// <summary>RF-031 parcial — 4 claves siempre presentes (NoIniciado, EnProgreso, Terminado, Atrasado).</summary>
    public Dictionary<string, int> ConteoPorStatus { get; set; } = new();

    /// <summary>
    /// GanttPlanResponse serializado con <see cref="System.Text.Encodings.Web.JavaScriptEncoder.Default"/>
    /// (SEC-05: el '<' viaja escapado y nunca crudo — nada de UnsafeRelaxedJsonEscaping, que
    /// permitiría cerrar el &lt;script&gt; con el contenido de una descripción).
    /// Vacío sin ciclo activo: "{}".
    /// </summary>
    public string JsonPlan { get; set; } = "{}";
}

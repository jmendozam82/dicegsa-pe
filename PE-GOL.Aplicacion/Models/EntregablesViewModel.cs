using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel para la vista de Entregables Adjuntos (HU-022).
/// Contiene la acción actual y la lista de adjuntos.
/// </summary>
public class EntregablesViewModel
{
    /// <summary>Id de la acción (para la URL de la API).</summary>
    public Guid AccionId { get; set; }

    /// <summary>Código de la acción (para mostrar en el título).</summary>
    public string AccionCodigo { get; set; } = string.Empty;

    /// <summary>Nombre/descripción de la acción.</summary>
    public string AccionNombre { get; set; } = string.Empty;

    /// <summary>Lista de adjuntos de la acción.</summary>
    public List<EntregableAdjuntoResponse> Adjuntos { get; set; } = new();

    /// <summary>Indica si el ciclo está activo (para habilitar/deshabilitar subida).</summary>
    public bool CicloActivo { get; set; }

    /// <summary>true si el rol actual puede subir (JefeArea — la API de subida es JEF-only,
    /// API AccionPlanController.cs:174). Calculado en el controller; el JS no reimplementa la regla.</summary>
    public bool PuedeSubir { get; set; }
}

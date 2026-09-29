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

    /// <summary>Token JWT para llamadas fetch desde el frontend (se inyecta desde el servidor).</summary>
    public string AccessToken { get; set; } = string.Empty;
}

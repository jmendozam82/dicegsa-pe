using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de área (Spec HU-009 § UI — Crear/Editar.cshtml).
/// Candidatos: GET /api/v1/ciclos/{cicloId}/areas/responsables (usuarios JefeArea Activos, puente HU-010).
/// ResponsableId requerido (RN-011: área Activa con responsable). Codigo/Orden son auto-generados
/// por la BLL (CA #1) — nunca viajan en el formulario.
/// </summary>
public class AreaFormViewModel
{
    public Guid CicloId { get; set; }
    public Guid AreaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Comentarios { get; set; }
    public Guid ResponsableId { get; set; }

    /// <summary>true en Editar: el área existe (para mostrar código GOL y estado).</summary>
    public bool EsEdicion { get; set; }

    public string Codigo { get; set; } = string.Empty;
    public bool Activa { get; set; }

    public List<ResponsableCandidatoResponse> Candidatos { get; set; } = [];
}
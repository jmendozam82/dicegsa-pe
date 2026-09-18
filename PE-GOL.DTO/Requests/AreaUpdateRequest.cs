namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de edición de Área Estratégica (Spec HU-009 § DTOs — PUT /api/v1/ciclos/{cicloId}/areas/{areaId}).
/// Mismos campos que AreaCreateRequest (nombre, comentarios, responsable). codigo/orden inmutables
/// (auto-generados por la BLL, DB-04).
/// </summary>
public class AreaUpdateRequest
{
    /// <summary>Requerido, máx 150 chars (RN: VARCHAR(150)).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT, sin límite en DDL).</summary>
    public string? Comentarios { get; set; }

    /// <summary>Requerido (RN-011): usuario del tenant con rol JefeArea y estado Activo.</summary>
    public Guid ResponsableId { get; set; }
}
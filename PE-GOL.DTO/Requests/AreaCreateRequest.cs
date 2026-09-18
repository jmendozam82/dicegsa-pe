namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de creación de Área Estratégica (Spec HU-009 § DTOs — POST /api/v1/ciclos/{cicloId}/areas).
/// codigo y orden NUNCA viajan en requests: son auto-generados por la BLL (CA #1, DB-04).
/// ResponsableId requerido (RN-011: área Activa con responsable — Jefe de Área).
/// </summary>
public class AreaCreateRequest
{
    /// <summary>Requerido, máx 150 chars (RN: VARCHAR(150)).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT, sin límite en DDL).</summary>
    public string? Comentarios { get; set; }

    /// <summary>Requerido (RN-011): usuario del tenant con rol JefeArea y estado Activo (CA #2, puente HU-010).</summary>
    public Guid ResponsableId { get; set; }
}
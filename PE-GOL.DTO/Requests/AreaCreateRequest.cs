namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de creación de Área Estratégica (Spec HU-009 § DTOs — POST /api/v1/ciclos/{cicloId}/areas).
/// codigo y orden NUNCA viajan en requests: son auto-generados por la BLL (CA #1, DB-04).
/// ResponsableId opcional en creación (RN-011: área ACTIVA con responsable — se valida al
/// activar el ciclo; el área en Borrador puede crearse sin responsable para romper el
/// círculo área⇄usuario JefeArea). Null → área sin responsable asignado.
/// </summary>
public class AreaCreateRequest
{
    /// <summary>Requerido, máx 150 chars (RN: VARCHAR(150)).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT, sin límite en DDL).</summary>
    public string? Comentarios { get; set; }

    /// <summary>Opcional en creación (RN-011 2026-09-21): usuario del tenant con rol JefeArea y
    /// estado Activo. Si se omite, el área queda en Borrador sin responsable y la activación
    /// del ciclo emitirá 422 hasta asignarlo.</summary>
    public Guid? ResponsableId { get; set; }
}
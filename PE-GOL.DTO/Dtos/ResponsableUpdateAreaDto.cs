namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de reasignación de área del responsable (Spec HU-010 § DTOs — ReasignarAsync).
/// AreaAnteriorId: para sync SEC-07 (liberar origen con null) y advertencia de origen sin responsable.
/// </summary>
public class ResponsableUpdateAreaDto
{
    public Guid UsuarioId { get; set; }
    public Guid TenantId { get; set; }
    public Guid NuevaAreaId { get; set; }

    /// <summary>Área actual del responsable (para sync SEC-07 y advertencia).</summary>
    public Guid? AreaAnteriorId { get; set; }
}
namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de datos generales del usuario (DAL-U7 del spec HU-003).
/// El UPDATE no toca password_hash/estado (se usan ResetContrasenaAsync/UpdateEstadoAsync).
/// </summary>
public class UsuarioUpdateDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;     // normalizado a LOWER en BLL (D2)
    public string Rol { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid? AreaId { get; set; }
}
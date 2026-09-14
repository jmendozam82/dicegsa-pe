namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción del usuario para la DAL (DAL-U1 del spec HU-003).
/// password_hash: hash BCrypt cost ≥ 12 (SEC-02) calculado en BLL — NUNCA se expone
/// en responses ni en log_auditoria (ADR-003; tests @QA casos #9, #11, #30).
/// requiere_cambio_pwd SIEMPRE TRUE en creación (D5, CA #3).
/// </summary>
public class UsuarioInsertDto
{
    public Guid? TenantId { get; set; }                    // NULL ⇔ SuperAdmin (solo seed)
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;     // normalizado a LOWER en BLL (D2)
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public Guid? AreaId { get; set; }
    public string Estado { get; set; } = "Activo";         // estado inicial (DDL DEFAULT 'Activo')
    public bool RequiereCambioPwd { get; set; } = true;    // D5: siempre TRUE en creación
}
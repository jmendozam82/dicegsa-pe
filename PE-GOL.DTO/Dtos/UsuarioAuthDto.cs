namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO interno de auth (Spec HU-004 DAL-A1): usuario CON password_hash e intentos/bloqueo
/// + tenant_estado del JOIN. SOLO uso interno del DAL-BLL de auth (SEC-02/ADR-003: el hash
/// nunca sale de esta frontera). TenantEstado es NULL para SuperAdmin (sin tenant) → la BLL
/// omite la validación de tenant (D15).
/// </summary>
public class UsuarioAuthDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;   // BCrypt cost ≥ 12 (SEC-02)
    public string Rol { get; set; } = string.Empty;
    public Guid? AreaId { get; set; }
    public string Estado { get; set; } = string.Empty;         // Activo | Inactivo | Bloqueado
    public int IntentosFallidos { get; set; }
    public DateTimeOffset? BloqueadoHasta { get; set; }
    public bool RequiereCambioPwd { get; set; }
    public DateTimeOffset? UltimoLogin { get; set; }
    public string? TenantEstado { get; set; }                  // NULL ⇔ SuperAdmin (D15)
}
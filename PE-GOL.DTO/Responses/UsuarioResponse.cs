namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de usuario (Spec HU-003 § DTOs).
/// password/password_hash NUNCA viajan en responses (SEC-02/RNF);
/// intentos_fallidos/bloqueado_hasta son internos del DAL-BLL (HU-004).
/// </summary>
public class UsuarioResponse
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;         // 'SuperAdmin' | 'AdminTenant' | 'Gerente' | 'JefeArea'
    public Guid? AreaId { get; set; }
    public string Estado { get; set; } = string.Empty;      // 'Activo' | 'Inactivo' | 'Bloqueado'
    public bool RequiereCambioPwd { get; set; }
    public DateTimeOffset? UltimoLogin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
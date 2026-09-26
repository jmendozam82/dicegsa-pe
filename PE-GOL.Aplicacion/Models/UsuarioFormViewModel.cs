using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario create/edit de usuario de tenant (HU-003 § UI del SA).
/// Password solo en alta (requiereCambioPwd siempre TRUE — D5); en edición se usa
/// reset-contrasena. AreaId requerido SOLO si rol='JefeArea' (RN-011).
/// </summary>
public class UsuarioFormViewModel
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Rol { get; set; } = "AdminTenant";
    public Guid TenantId { get; set; }
    public Guid? AreaId { get; set; }
    public bool EsEdicion { get; set; }
    public List<TenantResponse> Tenants { get; set; } = [];
}
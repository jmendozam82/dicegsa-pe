namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request PUT /api/v1/usuarios/{id} (Spec HU-003 § DTOs).
/// El campo estado NO se modifica por este endpoint (se usa activar/desactivar).
/// Cambiar tenantId re-valida límites del plan contra el DESTINO (D4/D6).
/// </summary>
public class UsuarioUpdateRequest
{
    public string Nombre { get; set; } = string.Empty;      // requerido, máx 150
    public string Correo { get; set; } = string.Empty;      // requerido, email válido, máx 200, único excluyendo self
    public string Rol { get; set; } = string.Empty;         // reglas iguales a creación (prohibido SuperAdmin)
    public Guid TenantId { get; set; }                      // cambio de tenant re-valida límites (D4)
    public Guid? AreaId { get; set; }                       // requerido si rol='JefeArea', null en caso contrario
}
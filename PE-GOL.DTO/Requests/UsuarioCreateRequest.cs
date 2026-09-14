namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request POST /api/v1/usuarios (Spec HU-003 § DTOs).
/// SEC-06: el SuperAdmin es GLOBAL → declara tenantId/areaId explícitamente (única excepción
/// documentada; el resto de roles jamás llama a estos endpoints — 403).
/// rol ∈ {AdminTenant, Gerente, JefeArea} — NUNCA 'SuperAdmin' (se crea solo por seed).
/// areaId requerido SOLO si rol='JefeArea' (RN-011).
/// </summary>
public class UsuarioCreateRequest
{
    public string Nombre { get; set; } = string.Empty;      // requerido, máx 150
    public string Correo { get; set; } = string.Empty;      // requerido, email válido, máx 200, único GLOBAL (D2)
    public string Password { get; set; } = string.Empty;    // requerido, mín 8 (temporal; se fuerza cambio en primer login)
    public string Rol { get; set; } = string.Empty;
    public Guid TenantId { get; set; }                      // requerido (todo usuario de tenant pertenece a uno)
    public Guid? AreaId { get; set; }
    public bool RequiereCambioPwd { get; set; } = true;     // D5: SIEMPRE true en creación; se ignora el valor enviado
}
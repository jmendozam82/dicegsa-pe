namespace PE_GOL.Utility.Security;

/// <summary>
/// Contexto multi-tenant scoped (Spec HU-004 § Alcance 3, D6/D17; 04_ARQUITECTURA.md § 4.2).
/// Poblado por TenantMiddleware desde los claims del JWT (SEC-06, ARCH-04).
/// D17 (Jorge, 2026-09-13): TenantId es Guid? (nullable) — el SuperAdmin tiene tenant_id=NULL
/// en BD (DDL 06 L97) y su JWT NO lleva claim tenant_id → el middleware deja TenantId=null
/// sin lanzar error de parseo. UserId es Guid? porque un request anónimo no tiene claims.
/// </summary>
public class TenantContext
{
    public Guid? TenantId { get; set; }   // D17: null ⇔ SuperAdmin (sin tenant)
    public Guid? UserId { get; set; }     // null ⇔ request anónimo (sin JWT)
    public string? Rol { get; set; }      // SuperAdmin | AdminTenant | Gerente | JefeArea
    public Guid? AreaId { get; set; }     // solo para rol JefeArea
}
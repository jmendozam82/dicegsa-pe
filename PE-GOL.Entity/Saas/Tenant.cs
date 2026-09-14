namespace PE_GOL.Entity.Saas;

/// <summary>
/// Entidad que mapea la tabla tenant + join a plan (DAL-2/DAL-3a del spec HU-001).
/// Reubicada desde PE-GOL.DAL/Entities por @BackendDev (Paso 1.5): ahora vive en
/// PE-GOL.Entity/Saas/ conforme a 04_ARQUITECTURA.md § 3 (carpetas por domino).
/// La tabla tenant es la raíz del multitenancy: NO lleva TenantId (excepción a DB-03).
/// </summary>
public class TenantEntity
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string PlanNombre { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
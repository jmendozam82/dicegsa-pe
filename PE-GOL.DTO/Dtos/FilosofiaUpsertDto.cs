namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de UPSERT de filosofía (Spec HU-011 § DTOs — DAL-F2).
/// Vision/Mision ya sanitizadas por la BLL (D-C). UpdatedBy = TenantContext.UserId (SEC-06).
/// El UPSERT usa ON CONFLICT (tenant_id, ciclo_id) DO UPDATE (DB-06, D-B): crea la fila en el
/// primer guardado (sin fila default en CicloService.CrearAsync — D-B).
/// </summary>
public class FilosofiaUpsertDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>Ya sanitizada por la BLL (D-C).</summary>
    public string Vision { get; set; } = string.Empty;

    /// <summary>Ya sanitizada por la BLL (D-C).</summary>
    public string Mision { get; set; } = string.Empty;

    /// <summary>TenantContext.UserId (SEC-06).</summary>
    public Guid UpdatedBy { get; set; }
}
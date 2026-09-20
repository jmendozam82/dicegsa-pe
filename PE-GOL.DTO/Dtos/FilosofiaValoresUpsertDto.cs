namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de UPSERT de valores corporativos (Spec HU-012 § DTOs — DAL-F3).
/// Valores = string JSON serializado por la BLL (List&lt;string&gt; → JSON) que se pasa con el
/// cast ::jsonb en el SQL (SEC-05: parametrizado, sin concatenación). SIN Vision/Mision: el
/// INSERT con vision=''/mision='' es responsabilidad del SQL de DAL-F3, no del DTO (D-B).
/// UpdatedBy = TenantContext.UserId (SEC-06).
/// </summary>
public class FilosofiaValoresUpsertDto
{
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>JSON serializado por la BLL (List&lt;string&gt; → JSON) — se pasa con ::jsonb.</summary>
    public string Valores { get; set; } = "[]";

    /// <summary>TenantContext.UserId (SEC-06).</summary>
    public Guid UpdatedBy { get; set; }
}
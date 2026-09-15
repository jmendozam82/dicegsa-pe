namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de creación de ciclo anual (Spec HU-007 § DTOs — POST /api/v1/ciclos).
/// estado NO forma parte del request: siempre 'Borrador' al crear (CA #1).
/// created_by se resuelve del TenantContext.UserId (SEC-06) — nunca del body.
/// La unicidad de añoFiscal se valida en BLL (DAL-C6) + capa 2 BD (UNIQUE del DDL L141).
/// </summary>
public class CicloCreateRequest
{
    /// <summary>Requerido, máx 100 chars (ej: "PE 2026").</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Requerido, 2000..2100 (regla de negocio; el DDL no tiene CHECK sobre año_fiscal).</summary>
    public int AñoFiscal { get; set; }

    /// <summary>Requerido, 1..12 (espejo del CHECK del DDL L134).</summary>
    public int MesInicio { get; set; }
}
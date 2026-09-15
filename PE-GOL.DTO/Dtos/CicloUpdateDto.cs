namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de actualización de datos generales del ciclo (DAL-C4 del spec HU-007).
/// El UPDATE no toca estado/activated_at/closed_at (se usan UpdateEstadoAsync).
/// NOTA de implementación: se añade TenantId (no declarado en el spec) porque DAL-C4 exige
/// WHERE tenant_id = @TenantId como primera condición (SEC-06) — el filtro de tenant es
/// obligatorio en toda query de la DAL y la BLL lo resuelve del TenantContext.
/// </summary>
public class CicloUpdateDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int AñoFiscal { get; set; }
    public int MesInicio { get; set; }
}
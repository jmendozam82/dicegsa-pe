namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción del ciclo para la DAL (DAL-C1 del spec HU-007).
/// estado SIEMPRE 'Borrador' al crear (CA #1 — el DDL lo fija por default; la query lo
/// explicita). CreatedBy = TenantContext.UserId (SEC-06, wiring D6 HU-004).
/// </summary>
public class CicloInsertDto
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int AñoFiscal { get; set; }
    public int MesInicio { get; set; }
    public Guid CreatedBy { get; set; }
}
namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción del plan para la DAL (DAL-P1 del spec HU-002).
/// La tabla plan es GLOBAL del SaaS: no lleva tenant_id (excepción a DB-03).
/// </summary>
public class PlanInsertDto
{
    public Guid? Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
}
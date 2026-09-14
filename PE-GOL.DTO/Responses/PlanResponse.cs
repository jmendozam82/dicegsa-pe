namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de plan (Spec HU-002 § DTOs).
/// SIN updatedAt y SIN estado: el DDL de plan no tiene updated_at ni columna de estado
/// (verificado en 06_MODELO_DATOS.md líneas 52-60; decisiones D2/D3 del spec).
/// </summary>
public class PlanResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
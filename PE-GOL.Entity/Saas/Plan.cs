namespace PE_GOL.Entity.Saas;

/// <summary>
/// Entidad que mapea la tabla plan (Spec HU-002 § Entidad).
/// Tabla GLOBAL del SaaS: NO lleva tenant_id (excepción explícita a DB-03, confirmada en
/// 06_MODELO_DATOS.md líneas 52-60). El DDL no tiene updated_at ni columna de estado →
/// no se exponen aquí (decisiones D2/D3 del spec HU-002).
/// </summary>
public class PlanEntity
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; }
    public int MaxUsuarios { get; set; }
    public int MaxCiclosActivos { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
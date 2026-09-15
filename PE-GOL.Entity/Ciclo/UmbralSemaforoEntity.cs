namespace PE_GOL.Entity.Ciclo;

/// <summary>
/// Entidad que mapea la tabla umbral_semaforo (Spec HU-007 § DTOs/Entidades;
/// 06_MODELO_DATOS.md L145-157). Tipo: 'KPI' | 'PlanAccion' (enum tipo_umbral, 06 L36).
/// Los umbrales se configuran en HU-008; esta HU los inserta por defecto al crear el ciclo
/// (0.90/0.70, defaults del DDL L150-151) y los copia al clonar (D-B, CA #5 parcial).
/// </summary>
public class UmbralSemaforoEntity
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }
    public Guid TenantId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public decimal UmbralVerde { get; set; }
    public decimal UmbralAmarillo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
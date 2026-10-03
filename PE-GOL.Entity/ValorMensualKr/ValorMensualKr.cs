namespace PE_GOL.Entity.ValorMensualKr;

/// <summary>
/// Entidad que mapea la tabla valor_mensual_kr (Spec HU-026 § Entidades;
/// 06_MODELO_DATOS.md L269-278). Un registro por (key_result_id, mes) — el UNIQUE del DDL
/// garantiza la unicidad y el UPSERT (ON CONFLICT) la reutiliza (RC-07 / DB-06).
/// Valor en escala 0.0–1.0 con 1 decimal (DECIMAL(2,1), RN-024).
/// </summary>
public class ValorMensualKr
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid KeyResultId { get; set; }
    public int Mes { get; set; }           // 1-12 (calendario)
    public decimal Valor { get; set; }     // 0.0-1.0, 1 decimal (DECIMAL(2,1))
    public Guid RegistradoPor { get; set; }
    public DateTime UpdatedAt { get; set; }
}

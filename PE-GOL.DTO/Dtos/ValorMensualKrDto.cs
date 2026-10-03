namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Proyección DAL de valor_mensual_kr (Spec HU-026 § DTOs). La usa el repositorio para
/// materializar las filas de la tabla; la BLL las consume para el recálculo (DB-04).
/// </summary>
public class ValorMensualKrDto
{
    public Guid Id { get; set; }
    public Guid KeyResultId { get; set; }
    public int Mes { get; set; }
    public decimal Valor { get; set; }
    public Guid? RegistradoPor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

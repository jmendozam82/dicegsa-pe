namespace PE_GOL.DTO.Dtos;

/// <summary>Suma de pesos de los KRs de un OKR (CA #3 / RC-06). La aritmética de «excluir el KR
/// que se está editando y sumar el nuevo» se hace en la BLL con decimal exacto (F6) — por eso
/// esta query NO necesita parámetro de exclusión y evita el null-param contra columna tipada
/// (lección ADR-015: DynamicParameters con DbType explícito).</summary>
public class SumaPesosKeyResultDto
{
    public decimal SumaPesos { get; set; }
}

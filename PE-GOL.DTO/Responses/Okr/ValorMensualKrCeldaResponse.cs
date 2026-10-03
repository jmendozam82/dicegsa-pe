namespace PE_GOL.DTO.Responses.Okr;

/// <summary>
/// Una celda de la grilla. SIEMPRE hay 12 por KR, en orden 1..12 (CA #1).
/// </summary>
public class ValorMensualKrCeldaResponse
{
    public int Mes { get; set; }                 // 1 = ENE … 12 = DIC
    public string Nombre { get; set; } = string.Empty;  // "ENE" … "DIC"
    public bool Registrado { get; set; }         // ¿existe fila en valor_mensual_kr?
    public decimal? Valor { get; set; }          // null si no hay fila
    public bool Editable { get; set; }           // CA #2 / CA #3 (reflejo; el servidor manda)
    public string? MotivoBloqueo { get; set; }   // "Mes anterior al inicio del ciclo" | "Mes aún no ocurrido"
}

namespace PE_GOL.DTO.Common;

/// <summary>
/// Wrapper global de respuestas de la API (ARCH-07).
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
}
namespace PE_GOL.DTO.Common;

/// <summary>
/// Wrapper estándar de listados paginados (Spec HU-001 § DTOs).
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
}
namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Parámetros del DAL para el listado de auditoría (Spec HU-005 § DTOs, D9).
/// Evita firmas de 8 argumentos; consistente con el patrón de DTOs de capa
/// (TenantInsertDto, CicloInsertDto). Page/PageSize ya saneados por la BLL
/// (page ≥ 1; pageSize 1..100). El DAL calcula offset = (Page - 1) * PageSize.
/// </summary>
public class LogAuditoriaFiltrosDto
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    /// <summary>Filtro opcional del SA (null = todas las entradas, tabla global fuera de RLS).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Filtro opcional del SA (null = todos los usuarios).</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Filtro opcional por acción (enum accion_auditoria).</summary>
    public string? Accion { get; set; }

    /// <summary>created_at &gt;= @Desde (inclusive).</summary>
    public DateTimeOffset? Desde { get; set; }

    /// <summary>created_at &lt;= @Hasta (inclusive).</summary>
    public DateTimeOffset? Hasta { get; set; }
}
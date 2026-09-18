namespace PE_GOL.DTO.Requests;

/// <summary>
/// Filtros del listado de auditoría — GET /api/v1/log-auditoria (query params, Spec HU-005 § DTOs).
/// SEC-06: tenantId/usuarioId NUNCA viajan en el body; son query params declarados explícitamente
/// por el SuperAdmin (misma excepción documentada que HU-001/HU-003).
/// page/pageSize: opcionales, saneados en BLL (defaults 1/10; pageSize &gt; 100 → trunca a 100).
/// accion ∈ enum accion_auditoria: CREATE|UPDATE|DELETE|LOGIN|LOGOUT|ACTIVATE|DEACTIVATE.
/// desde/hasta: ISO 8601, inclusive en ambos extremos (created_at &gt;= @Desde AND created_at &lt;= @Hasta).
/// </summary>
public class LogAuditoriaFiltrosRequest
{
    public int? Page { get; set; }

    public int? PageSize { get; set; }

    /// <summary>Filtro opcional del SA: entradas de un tenant (null = todos).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Filtro opcional del SA: entradas de un usuario (null = todos).</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Filtro opcional por tipo de acción (enum accion_auditoria).</summary>
    public string? Accion { get; set; }

    /// <summary>Filtro opcional: created_at &gt;= @Desde (inclusive).</summary>
    public DateTimeOffset? Desde { get; set; }

    /// <summary>Filtro opcional: created_at &lt;= @Hasta (inclusive). No puede estar en el futuro.</summary>
    public DateTimeOffset? Hasta { get; set; }
}
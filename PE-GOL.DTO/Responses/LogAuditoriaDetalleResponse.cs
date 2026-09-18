namespace PE_GOL.DTO.Responses;

/// <summary>
/// Detalle de una entrada de auditoría — GET /api/v1/log-auditoria/{id} (Spec HU-005 § DTOs, D3).
/// valorAnterior/valorNuevo viajan como string (JSON crudo serializado con
/// UnsafeRelaxedJsonEscaping, ADR-003). El frontend los renderiza con escapado seguro
/// (nunca @Html.Raw sin sanitización). No se parsean a objeto en la API: contrato
/// "JSON opaco legible" (RNF-023).
/// </summary>
public class LogAuditoriaDetalleResponse
{
    public Guid Id { get; set; }

    /// <summary>Null para acciones SaaS-level del SuperAdmin.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>LEFT JOIN a tenant; null si tenant_id es null.</summary>
    public string? TenantNombre { get; set; }

    /// <summary>Null para LOGIN fallido con correo inexistente.</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>LEFT JOIN a usuario; null si usuario_id es null.</summary>
    public string? UsuarioNombre { get; set; }

    /// <summary>'CREATE'|'UPDATE'|'DELETE'|'LOGIN'|'LOGOUT'|'ACTIVATE'|'DEACTIVATE'.</summary>
    public string Accion { get; set; } = string.Empty;

    /// <summary>'Tenant'|'Plan'|'Usuario'|'Auth'|'Ciclo'|'UmbralSemaforo'|...</summary>
    public string Entidad { get; set; } = string.Empty;

    /// <summary>PK del registro afectado (texto).</summary>
    public string? EntidadId { get; set; }

    /// <summary>JSON crudo (JSONB → string); null en CREATE.</summary>
    public string? ValorAnterior { get; set; }

    /// <summary>JSON crudo (JSONB → string).</summary>
    public string? ValorNuevo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
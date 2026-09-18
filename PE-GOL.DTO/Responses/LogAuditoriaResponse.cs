namespace PE_GOL.DTO.Responses;

/// <summary>
/// Item del listado de auditoría — GET /api/v1/log-auditoria (Spec HU-005 § DTOs, D3).
/// Resumen SIN valor_anterior/valor_nuevo (los JSONB pueden ser grandes; el detalle
/// GET /{id} los expone para inspección forense). tenantNombre/usuarioNombre provienen
/// del LEFT JOIN (DAL-L1); null si el id es null (acciones SaaS-level / LOGIN fallido).
/// </summary>
public class LogAuditoriaResponse
{
    public Guid Id { get; set; }

    /// <summary>Null para acciones SaaS-level del SuperAdmin (tenant_id nullable del DDL).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>LEFT JOIN a tenant; null si tenant_id es null.</summary>
    public string? TenantNombre { get; set; }

    /// <summary>Null para LOGIN fallido con correo inexistente (usuario_id nullable).</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>LEFT JOIN a usuario; null si usuario_id es null.</summary>
    public string? UsuarioNombre { get; set; }

    /// <summary>'CREATE'|'UPDATE'|'DELETE'|'LOGIN'|'LOGOUT'|'ACTIVATE'|'DEACTIVATE'.</summary>
    public string Accion { get; set; } = string.Empty;

    /// <summary>'Tenant'|'Plan'|'Usuario'|'Auth'|'Ciclo'|'UmbralSemaforo'|...</summary>
    public string Entidad { get; set; } = string.Empty;

    /// <summary>PK del registro afectado (texto).</summary>
    public string? EntidadId { get; set; }

    /// <summary>Fecha/hora de la entrada (ORDER BY created_at DESC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
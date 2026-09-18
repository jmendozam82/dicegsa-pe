namespace PE_GOL.Entity.Saas;

/// <summary>
/// Entidad de log_auditoria (Spec HU-005 § DTOs, D4 — ubicada en Entity/Saas/).
/// Tabla GLOBAL fuera de RLS (decisión de Jorge, HANDOFF L156): el SuperAdmin lee todas
/// las entradas; tenant_id es NULLABLE y CLASIFICA la entrada (null = acciones SaaS-level
/// del SA, p. ej. gestión de planes HU-002 DAL-P9). usuario_id también nullable
/// (LOGIN fallido con correo inexistente, HU-004 DAL-A10).
/// TenantNombre/UsuarioNombre: proyecciones del LEFT JOIN (DAL-L1/L3), solo lectura.
/// ValorAnterior/ValorNuevo: JSONB leído como string por Dapper (ADR-003).
/// </summary>
public class LogAuditoriaEntity
{
    public Guid Id { get; set; }

    /// <summary>Nullable: acciones SaaS-level del SuperAdmin registran tenant_id = NULL.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Proyección del LEFT JOIN a tenant (solo lectura).</summary>
    public string? TenantNombre { get; set; }

    /// <summary>Nullable: LOGIN fallido sin usuario registra usuario_id = NULL.</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Proyección del LEFT JOIN a usuario (solo lectura).</summary>
    public string? UsuarioNombre { get; set; }

    /// <summary>Mapea el enum accion_auditoria: CREATE|UPDATE|DELETE|LOGIN|LOGOUT|ACTIVATE|DEACTIVATE.</summary>
    public string Accion { get; set; } = string.Empty;

    public string Entidad { get; set; } = string.Empty;

    /// <summary>PK del registro afectado (texto).</summary>
    public string? EntidadId { get; set; }

    /// <summary>JSONB leído como string por Dapper (null en CREATE).</summary>
    public string? ValorAnterior { get; set; }

    /// <summary>JSONB leído como string por Dapper.</summary>
    public string? ValorNuevo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
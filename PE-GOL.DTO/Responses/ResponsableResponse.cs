namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de Responsable (Spec HU-010 § DTOs — GET/POST/PUT/desactivar).
/// tenantId proviene del TenantContext (SEC-06), nunca del body.
/// AreaId/AreaCodigo/AreaNombre vienen del JOIN a area (DAL-R2/R3).
/// Advertencia (CONTRATO #25, Jorge): mensaje de negocio no bloqueante — p. ej. "El área origen
/// quedará sin responsable asignado" en ReasignarAsync (spec §4 paso 8). Null si no aplica.
/// </summary>
public class ResponsableResponse
{
    /// <summary>usuario.id.</summary>
    public Guid Id { get; set; }

    public Guid CicloId { get; set; }

    /// <summary>Del TenantContext, nunca del body (SEC-06).</summary>
    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;

    /// <summary>Fijo a 'JefeArea' (D-B).</summary>
    public string Rol { get; set; } = "JefeArea";

    /// <summary>Activo | Inactivo | Bloqueado.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Área asignada (usuario.area_id — sync SEC-07, D-J).</summary>
    public Guid? AreaId { get; set; }

    /// <summary>JOIN area.codigo (GOL1, GOL2...).</summary>
    public string? AreaCodigo { get; set; }

    /// <summary>JOIN area.nombre.</summary>
    public string? AreaNombre { get; set; }

    /// <summary>usuario.requiere_cambio_pwd.</summary>
    public bool RequiereCambioPwd { get; set; }

    public DateTimeOffset? UltimoLogin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Advertencia de negocio (CONTRATO #25): null si no aplica.</summary>
    public string? Advertencia { get; set; }
}
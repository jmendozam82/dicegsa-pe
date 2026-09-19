namespace PE_GOL.Entity.Estrategia;

/// <summary>
/// Entidad que mapea la vista de responsable (Spec HU-010 § DTOs/Entidades).
/// Responsable = usuario del tenant con rol='JefeArea' (puente HU-009 → HU-010, D-B).
/// AreaCodigo/AreaNombre vienen del JOIN a area (DAL-R2/R3) — LEFT JOIN con la condición
/// del ciclo en el JOIN (observación de Jorge): si usuario.area_id apunta a un área de otro
/// ciclo (clonado/reasignado), el responsable se devuelve con area_codigo/area_nombre null
/// en lugar de perderse la fila.
/// </summary>
public class ResponsableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;

    /// <summary>Fijo a 'JefeArea' (D-B): el responsable es un usuario del tenant con rol JefeArea.</summary>
    public string Rol { get; set; } = "JefeArea";

    /// <summary>Activo | Inactivo | Bloqueado (estado_usuario).</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Área asignada (usuario.area_id — sync SEC-07, D-J).</summary>
    public Guid? AreaId { get; set; }

    /// <summary>JOIN a area (código GOL1, GOL2...).</summary>
    public string? AreaCodigo { get; set; }

    /// <summary>JOIN a area (nombre).</summary>
    public string? AreaNombre { get; set; }

    public bool RequiereCambioPwd { get; set; }
    public DateTimeOffset? UltimoLogin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
namespace PE_GOL.Entity.Estrategia;

/// <summary>
/// Entidad que mapea la tabla area (Spec HU-009 § DTOs/Entidades; 06_MODELO_DATOS.md L194-212).
/// Carpeta Estrategia/ según dominio (04_ARQUITECTURA.md § 3).
/// ResponsableNombre/ResponsableCorreo vienen del JOIN a usuario (DAL-A2/A3).
/// ResponsableId null solo si el área está inactiva (RN-011).
/// </summary>
public class AreaEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>Código GOL auto-generado: "GOL1", "GOL2", ... (CA #1, DB-04).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Comentarios { get; set; }
    public Guid? ResponsableId { get; set; }

    /// <summary>JOIN a usuario (nombre del responsable).</summary>
    public string? ResponsableNombre { get; set; }

    /// <summary>JOIN a usuario (correo del responsable).</summary>
    public string? ResponsableCorreo { get; set; }

    public int Orden { get; set; }
    public bool Activa { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
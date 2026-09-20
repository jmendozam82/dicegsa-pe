namespace PE_GOL.Entity.Estrategia;

/// <summary>
/// Entidad que mapea la tabla filosofia (Spec HU-011 § DTOs/Entidades; 06_MODELO_DATOS.md L164-174).
/// Carpeta Estrategia/ según dominio (04_ARQUITECTURA.md § 3).
/// Vision/Mision: HTML sanitizado con allowlist (D-C) — texto enriquecido del CA #1.
/// Valores (JSONB) NO se expone en esta HU (responsabilidad de HU-012 — Valores Corporativos).
/// UpdatedByNombre viene del JOIN a usuario (DAL-F1) — trazabilidad (CA #4).
/// SEC-07 NO APLICA (D-E): filosofia es corporativa (una fila por ciclo por tenant, sin area_id;
/// RLS solo por tenant) → el JefeArea lee la filosofía completa del ciclo (RN-007: solo lectura).
/// </summary>
public class FilosofiaEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>HTML sanitizado con allowlist (p, br, b, strong, i, em, ul, ol, li; sin atributos — D-C).</summary>
    public string Vision { get; set; } = string.Empty;

    /// <summary>HTML sanitizado con allowlist (D-C).</summary>
    public string Mision { get; set; } = string.Empty;

    /// <summary>JSONB — NO se expone en esta HU (HU-012).</summary>
    public string Valores { get; set; } = "[]";

    /// <summary>Último usuario que editó (TenantContext.UserId — SEC-06). Null si nunca se editó.</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>JOIN a usuario (nombre del último editor) — trazabilidad (CA #4).</summary>
    public string? UpdatedByNombre { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
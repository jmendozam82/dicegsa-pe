namespace PE_GOL.Entity.Estrategia;

/// <summary>
/// Entidad que mapea la tabla pilar (Spec HU-013 § DTOs/Entidades; 06_MODELO_DATOS.md L176-192).
/// Carpeta Estrategia/ según dominio (04_ARQUITECTURA.md § 3).
/// Codigo: "PEC-N" auto-generado por la BLL (CA #1, D-B) — NO editable, NO viaja en requests.
/// EstrategiaVictoria: TEXT opcional, máx 2000 chars (D-C).
/// Orden: INT DEFAULT 0 — opcional en create/update con default secuencial (D-H).
/// ObjetivoQ1..Q4: TEXT nullable (HU-014, DDL L183-186) — objetivos de área por trimestre como
/// texto de referencia (CA #1/#2); null o string vacío tras trim se persiste null (D-H).
/// SEC-07 NO APLICA (D-E): pilar es corporativa (sin area_id; RLS pilar_policy solo por tenant)
/// → el JefeArea lee TODOS los pilares del ciclo (RN-007: solo lectura).
/// </summary>
public class PilarEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }

    /// <summary>"PEC-N" auto-generado por la BLL (CA #1, D-B): "PEC-1", "PEC-2", ...</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Requerido, máx 150 chars (VARCHAR(150)).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Opcional (TEXT), máx 2000 chars (D-C).</summary>
    public string? EstrategiaVictoria { get; set; }

    /// <summary>HU-014 — objetivo del trimestre Q1 (TEXT nullable, máx 2000 chars, D-C/D-H).</summary>
    public string? ObjetivoQ1 { get; set; }

    /// <summary>HU-014 — objetivo del trimestre Q2 (TEXT nullable, máx 2000 chars, D-C/D-H).</summary>
    public string? ObjetivoQ2 { get; set; }

    /// <summary>HU-014 — objetivo del trimestre Q3 (TEXT nullable, máx 2000 chars, D-C/D-H).</summary>
    public string? ObjetivoQ3 { get; set; }

    /// <summary>HU-014 — objetivo del trimestre Q4 (TEXT nullable, máx 2000 chars, D-C/D-H).</summary>
    public string? ObjetivoQ4 { get; set; }

    /// <summary>Orden de presentación (D-H): default secuencial MAX(orden)+1 del ciclo.</summary>
    public int Orden { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
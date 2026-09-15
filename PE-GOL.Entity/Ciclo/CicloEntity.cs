namespace PE_GOL.Entity.Ciclo;

/// <summary>
/// Entidad que mapea la tabla ciclo (Spec HU-007 § DTOs/Entidades; 06_MODELO_DATOS.md L129-143).
/// Carpeta Ciclo/ según dominio (04_ARQUITECTURA.md § 3). El DDL usa año_fiscal/mes_inicio
/// (identificadores Unicode válidos en PostgreSQL); Dapper mapea snake_case → PascalCase
/// (DefaultTypeMap.MatchNamesWithUnderscores = true en DbConnectionFactory).
/// Estado: 'Borrador' | 'Activo' | 'Cerrado' (enum estado_ciclo, 06 L35).
/// </summary>
public class CicloEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int AñoFiscal { get; set; }
    public int MesInicio { get; set; }
    public string Estado { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
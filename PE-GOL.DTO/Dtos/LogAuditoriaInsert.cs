namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción en log_auditoria (DAL-7 del spec HU-001 / DAL-P9 del spec HU-002).
/// valor_anterior / valor_nuevo: JSON serializado (null en CREATE para valor_anterior).
/// accion ∈ CREATE | UPDATE | DELETE | ACTIVATE | DEACTIVATE (enum accion_auditoria).
/// TenantId es NULLABLE: las acciones GLOBALES del SuperAdmin (p. ej. gestión de planes,
/// HU-002 DAL-P9) registran log_auditoria.tenant_id = NULL (columna nullable del DDL, línea 108).
/// </summary>
public class LogAuditoriaInsert
{
    public Guid? TenantId { get; set; }
    public Guid? UsuarioId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string Entidad { get; set; } = "Tenant";
    public string? EntidadId { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
}
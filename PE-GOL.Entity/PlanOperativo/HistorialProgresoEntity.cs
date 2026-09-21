using System;

namespace PE_GOL.Entity.PlanOperativo;

public class HistorialProgresoEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccionId { get; set; }
    public decimal ProgresoAnterior { get; set; }
    public decimal ProgresoNuevo { get; set; }
    public string StatusAnterior { get; set; } = string.Empty;
    public string StatusNuevo { get; set; } = string.Empty;
    public Guid RegistradoPor { get; set; }
    public DateTime CreatedAt { get; set; }
}

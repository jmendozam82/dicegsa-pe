using System;

namespace PE_GOL.Entity.PlanOperativo;

public class AccionPlanEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public Guid AreaId { get; set; }
    public Guid ObjetivoCgId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionEntregable { get; set; }
    public Guid? ResponsableId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public string? Aclaraciones { get; set; }
    public decimal Progreso { get; set; }
    public decimal PuntuacionPonderada { get; set; }
    public string Status { get; set; } = "NoIniciado";
    public bool AlertaEnviada { get; set; }
    public int Orden { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
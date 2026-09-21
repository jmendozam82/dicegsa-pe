using System;

namespace PE_GOL.DTO.Requests.Objetivos;

public class AccionPlanUpdateRequest
{
    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionEntregable { get; set; }
    public Guid? ResponsableId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public string? Aclaraciones { get; set; }
}
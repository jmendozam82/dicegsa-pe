using System;

namespace PE_GOL.DTO.Responses.Objetivos;

public class AccionPlanResponse
{
    public Guid Id { get; set; }
    public Guid ObjetivoCgId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? DescripcionEntregable { get; set; }
    public Guid? ResponsableId { get; set; }
    public string? ResponsableNombre { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public string Clasificacion { get; set; } = string.Empty;
    public string TipoPresupuesto { get; set; } = string.Empty;
    public decimal Peso { get; set; }
    public string? Aclaraciones { get; set; }
    public decimal Progreso { get; set; }
    public decimal PuntuacionPonderada { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool AlertaEnviada { get; set; }
    public int Orden { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
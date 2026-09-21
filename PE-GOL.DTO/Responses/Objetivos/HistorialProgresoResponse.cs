using System;

namespace PE_GOL.DTO.Responses.Objetivos;

public class HistorialProgresoResponse
{
    public Guid Id { get; set; }
    public Guid AccionId { get; set; }
    public decimal ProgresoAnterior { get; set; }
    public decimal ProgresoNuevo { get; set; }
    public string StatusAnterior { get; set; } = string.Empty;
    public string StatusNuevo { get; set; } = string.Empty;
    public Guid RegistradoPor { get; set; }
    public string? RegistradoPorNombre { get; set; }
    public DateTime CreatedAt { get; set; }
}

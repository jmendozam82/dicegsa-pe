using System;

namespace PE_GOL.DTO.Requests.Objetivos;

public class ObjetivoCgFilterRequest
{
    public Guid? AreaId { get; set; }
    public Guid? PilarId { get; set; }
    public string? Trimestre { get; set; }
    public string? Semaforo { get; set; }
}

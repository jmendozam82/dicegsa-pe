using System;

namespace PE_GOL.DTO.Requests.Objetivos;

public class ActualizarProgresoRequest
{
    /// <summary>% de progreso de la acción. Rango 0.00 – 100.00 (RN-017).</summary>
    public decimal Progreso { get; set; }
}

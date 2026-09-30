namespace PE_GOL.DTO.Responses.Okr;

/// <summary>Respuesta de OKR — Spec HU-024 (CA #4: puntuación final y semáforo).
/// Espejo de ObjetivoCgResponse con PuntuacionFinal en lugar de Progreso/Trimestre.</summary>
public class OkrResponse
{
    public Guid Id { get; set; }
    public Guid CicloId { get; set; }
    public Guid AreaId { get; set; }
    public Guid PilarId { get; set; }
    public string PilarNombre { get; set; } = string.Empty;   // INNER JOIN pilar
    public string Codigo { get; set; } = string.Empty;       // "OKR.1" (CA #1)
    public string Descripcion { get; set; } = string.Empty;
    public decimal PuntuacionFinal { get; set; }              // 0.000 - 1.000 (CA #4)
    public string Semaforo { get; set; } = string.Empty;     // "Verde" | "Amarillo" | "Rojo" (CA #4)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

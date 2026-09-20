namespace PE_GOL.DTO.Responses.Objetivos;

public class ObjetivoCgResponse
{
    public Guid Id { get; set; }
    public Guid PilarId { get; set; }
    public string PilarNombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string TrimestreObjetivo { get; set; } = string.Empty;
    public decimal Progreso { get; set; }
    public string Semaforo { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

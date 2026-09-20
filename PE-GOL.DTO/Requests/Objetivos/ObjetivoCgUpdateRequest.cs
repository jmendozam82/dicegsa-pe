namespace PE_GOL.DTO.Requests.Objetivos;

public class ObjetivoCgUpdateRequest
{
    public string Descripcion { get; set; } = string.Empty;
    public Guid PilarId { get; set; }
    public string TrimestreObjetivo { get; set; } = string.Empty;
}

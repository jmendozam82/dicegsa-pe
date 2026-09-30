namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Payload de actualización de OKR — Spec HU-024. Mismos campos que el create;
/// el codigo/puntuacion_final/semaforo/orden no son editables (CA #1, DB-04).</summary>
public class OkrUpdateRequest
{
    /// <summary>Pilar estratégico asociado (GUID). Se valida que exista en el ciclo activo del tenant.</summary>
    public Guid PilarId { get; set; }

    /// <summary>Descripción del objetivo (1-500 chars).</summary>
    public string Descripcion { get; set; } = string.Empty;
}

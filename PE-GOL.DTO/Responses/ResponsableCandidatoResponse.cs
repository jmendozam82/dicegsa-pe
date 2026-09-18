namespace PE_GOL.DTO.Responses;

/// <summary>
/// Candidato a responsable de área (Spec HU-009 § DTOs — GET /api/v1/ciclos/{cicloId}/areas/responsables).
/// Usuarios del tenant con rol JefeArea y estado Activo (CA #2, puente hasta HU-010).
/// YaAsignado: true si el usuario ya es responsable de otra área ACTIVA del ciclo (RN-012, DAL-A10).
/// </summary>
public class ResponsableCandidatoResponse
{
    /// <summary>usuario.id</summary>
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;

    /// <summary>true si es responsable de otra área ACTIVA del ciclo (RN-012).</summary>
    public bool YaAsignado { get; set; }
}
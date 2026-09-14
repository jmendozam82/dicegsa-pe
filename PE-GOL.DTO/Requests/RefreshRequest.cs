namespace PE_GOL.DTO.Requests;

/// <summary>POST /api/v1/auth/refresh (Spec HU-004 § DTOs).</summary>
public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;  // requerido
}
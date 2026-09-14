namespace PE_GOL.DTO.Requests;

/// <summary>POST /api/v1/auth/logout (Spec HU-004 § DTOs).</summary>
public class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;  // requerido
}
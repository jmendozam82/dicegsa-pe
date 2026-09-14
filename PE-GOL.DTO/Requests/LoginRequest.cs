namespace PE_GOL.DTO.Requests;

/// <summary>POST /api/v1/auth/login (Spec HU-004 § DTOs).</summary>
public class LoginRequest
{
    public string Correo { get; set; } = string.Empty;    // requerido, email válido, máx 200 (trim + LOWER)
    public string Password { get; set; } = string.Empty;  // requerido, máx 128 (anti-DoS de hashing)
}
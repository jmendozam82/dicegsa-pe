namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de login (Spec HU-004 § DTOs).
/// password/password_hash NUNCA viajan en responses (SEC-02).
/// </summary>
public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiraEn { get; set; }        // NOW + 60 min (SEC-01)
    public DateTimeOffset RefreshExpiraEn { get; set; } // NOW + 7 días (SEC-01)
    public bool RequiereCambioPwd { get; set; }         // D5: true si debe cambiar contraseña
    public UsuarioResponse Usuario { get; set; } = new();  // reutiliza DTO de HU-003
}
namespace PE_GOL.DTO.Responses;

/// <summary>
/// Respuesta de refresh (Spec HU-004 § DTOs).
/// El refreshToken es NUEVO (rotación D1); el presentado queda revocado.
/// </summary>
public class RefreshResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset RefreshExpiraEn { get; set; }
    public bool RequiereCambioPwd { get; set; }
}
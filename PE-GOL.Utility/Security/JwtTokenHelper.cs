using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PE_GOL.Utility.Security;

/// <summary>
/// Helper de emisión de tokens JWT (Spec HU-004 § Lógica BLL + ADR-004).
/// Implementación real (fase 4 del Loop); los tests de @QA son la especificación ejecutable.
/// Contrato:
///  · GenerarAccessToken: claims user_id/tenant_id (omitido si null)/rol/area_id (omitido
///    si null); expiración NOW + AccessTokenMinutes (SEC-01, D11).
///  · GenerarRefreshToken: 32 bytes aleatorios (RandomNumberGenerator) → Base64Url (D13).
///  · HashRefreshToken: SHA-256 hex (64 chars, minúsculas) del refresh token (D2).
///
/// DECISIÓN DE DISEÑO (verificada empíricamente en fase de investigación): el payload del
/// access token se emite 100% strings — incluido "exp" como UnixTimeSeconds en string — por
/// dos razones: (1) el helper de tests de @QA (DecodificarClaimsJwt) deserializa el payload
/// a Dictionary&lt;string,string&gt; y System.Text.Json lanza JsonException con claims numéricos;
/// (2) el middleware JwtBearer de Microsoft (JwtSecurityTokenHandler.ValidateToken) ACEPTA
/// exp como string: el getter JwtPayload.Exp parsea strings y ValidateLifetime funciona
/// (token expirado → SecurityTokenExpiredException, verificado). No se pasa expires/notBefore
/// al ctor del token para que el handler no reescriba exp como número.
/// </summary>
public static class JwtTokenHelper
{
    public static string GenerarAccessToken(JwtOptions options, Guid userId, Guid? tenantId, string rol, Guid? areaId)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(rol))
            throw new ArgumentException("El rol es obligatorio para emitir el access token.", nameof(rol));

        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("rol", rol)
        };
        if (tenantId.HasValue) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (areaId.HasValue) claims.Add(new Claim("area_id", areaId.Value.ToString()));

        // D11/SEC-01: expiración NOW + AccessTokenMinutes como string (ver doc de clase).
        claims.Add(new Claim(
            "exp",
            DateTimeOffset.UtcNow.AddMinutes(options.AccessTokenMinutes).ToUnixTimeSeconds().ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>D13 · Refresh token: 32 bytes criptográficamente aleatorios → Base64Url (sin padding).</summary>
    public static string GenerarRefreshToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncoder.Encode(bytes);
    }

    /// <summary>D2 · Hash SHA-256 hex (64 chars, minúsculas) del refresh token — NUNCA se persiste en claro.</summary>
    public static string HashRefreshToken(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
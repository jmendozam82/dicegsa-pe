namespace PE_GOL.Utility.Security;

/// <summary>
/// Opciones de emisión JWT (Spec HU-004 § Lógica BLL + ADR-004). Se enlaza desde la
/// sección "Jwt" de appsettings (PE-GOL.API/appsettings.json L11-17):
/// AccessTokenMinutes=60 (SEC-01) · RefreshTokenDays=7 (SEC-01).
/// Registrado como Singleton en IOC (fase @BackendDev).
/// </summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "pe-gol-saas";
    public string Audience { get; set; } = "pe-gol-users";

    /// <summary>
    /// Clave HMAC-SHA256 de firma. ⚠️ El valor por defecto es SOLO para desarrollo y tests
    /// (@QA construye JwtOptions directamente con new JwtOptions()); en producción el IOC la
    /// SOBRESCRIBE desde la sección "Jwt" de appsettings (Jwt:Key) y Program.cs lanza si falta.
    /// 36 chars ≥ 32 → SymmetricSecurityKey válido (verificado empíricamente, IDX10703 si vacía).
    /// </summary>
    public string Key { get; set; } = "pe-gol-dev-key-32-caracteres-minimo!!";

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 7;
}
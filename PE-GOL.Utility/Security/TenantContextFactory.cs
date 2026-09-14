using System.Security.Claims;

namespace PE_GOL.Utility.Security;

/// <summary>
/// Fábrica de TenantContext (Spec HU-004 § Alcance 3, D6/D17). Centraliza el mapeo
/// claims → TenantContext para compartirlo entre:
///  · El middleware REAL (PE-GOL.API/Middleware/TenantMiddleware.cs): recibe los claims YA
///    validados por JwtBearer (UseAuthentication corre antes, ARCH-04) vía HttpContext.User.
///  · El stub de contrato de @QA (PE-GOL.Tests/Stubs/TenantMiddleware.cs): decodifica el token
///    del header sin validar firma (test-only; el JwtBearer real ya rechazó la request si el
///    token era inválido) y usa esta misma fábrica → la lógica de mapeo es única y testeable.
/// D17: TenantId/UserId son Guid? — un claim ausente o no parseable deja el campo en null
/// sin lanzar error (SuperAdmin sin tenant_id; request anónimo sin claims).
/// </summary>
public static class TenantContextFactory
{
    /// <summary>Puebla el TenantContext desde una colección de claims (D6/D17).</summary>
    public static void PoblarDesdeClaims(TenantContext context, IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lista = claims?.ToList() ?? [];
        context.UserId = ParseGuid(ValorClaim(lista, "user_id"));
        context.Rol = ValorClaim(lista, "rol");
        context.TenantId = ParseGuid(ValorClaim(lista, "tenant_id"));
        context.AreaId = ParseGuid(ValorClaim(lista, "area_id"));
    }

    private static string? ValorClaim(List<Claim> claims, string tipo)
        => claims.FirstOrDefault(c => string.Equals(c.Type, tipo, StringComparison.Ordinal))?.Value;

    private static Guid? ParseGuid(string? valor)
        => Guid.TryParse(valor, out var guid) ? guid : null;
}
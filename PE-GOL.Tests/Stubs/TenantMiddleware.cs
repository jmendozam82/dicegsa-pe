using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests.Stubs;

/// <summary>
/// Stub del TenantMiddleware (Spec HU-004 § Alcance 3, D6/D17) para los casos #39/#40.
/// El middleware REAL vive en PE-GOL.API/Middleware/TenantMiddleware.cs (proyecto Web SDK no
/// referenciado por tests) y corre DESPUÉS de UseAuthentication → lee los claims YA validados
/// de HttpContext.User. Este stub NO puede depender de JwtBearer (no hay pipeline HTTP en el
/// test): decodifica el token del header con JwtSecurityTokenHandler.ReadJwtToken (SIN validar
/// firma — test-only; el JwtBearer real ya rechazó la request si el token era inválido) y
/// delega el mapeo claims → TenantContext en TenantContextFactory (lógica ÚNICA y compartida
/// con el middleware real → la misma que se testea aquí).
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth) &&
            auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth["Bearer ".Length..].Trim();
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                TenantContextFactory.PoblarDesdeClaims(tenantContext, jwt.Claims);
            }
            catch
            {
                // Token malformado → contexto vacío (el JwtBearer real rechazaría con 401 antes).
            }
        }

        return _next(context);
    }
}
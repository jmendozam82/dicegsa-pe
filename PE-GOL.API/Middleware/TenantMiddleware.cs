using PE_GOL.Utility.Security;

namespace PE_GOL.API.Middleware;

/// <summary>
/// Middleware multi-tenant (04_ARQUITECTURA.md § 4.2 — TenantMiddleware, ARCH-04).
/// Se registra DESPUÉS de UseAuthentication(): para cuando este middleware corre, el
/// JwtBearer YA validó el token (si era inválido, la request se rechazó con 401 antes) →
/// HttpContext.User contiene los claims YA validados. El middleware solo mapea esos claims
/// al TenantContext scoped (SEC-06) usando la fábrica compartida TenantContextFactory
/// (misma lógica que el stub de contrato de @QA, PE-GOL.Tests/Stubs/TenantMiddleware.cs).
/// D17: TenantId/UserId son Guid? — request anónimo (sin JWT) deja el contexto vacío sin error.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            TenantContextFactory.PoblarDesdeClaims(tenantContext, context.User.Claims);
        }

        await _next(context);
    }
}
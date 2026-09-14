using System.Net;
using System.Text.Json;
using PE_GOL.DTO.Common;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.API.Middleware;

/// <summary>
/// Middleware global de excepciones (04_ARQUITECTURA.md § 3 — ExceptionMiddleware).
/// Traduce excepciones de dominio al wrapper estándar ApiResponse&lt;T&gt; (ARCH-07):
/// ValidacionException → 422 · NotFoundException → 404 · UnauthorizedException → 401 (HU-004) ·
/// resto → 500.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidacionException ex)
        {
            _logger.LogWarning(ex, "Validación de negocio fallida. Modulo=Saas, Mensaje={Message}", ex.Message);
            await EscribirErrorAsync(context, HttpStatusCode.UnprocessableEntity, ex.Message);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Recurso no encontrado. Modulo=Saas, Mensaje={Message}", ex.Message);
            await EscribirErrorAsync(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (UnauthorizedException ex)
        {
            // HU-004: credenciales inválidas / sesión no autorizada → 401 (SEC-01).
            _logger.LogWarning(ex, "Autenticación fallida. Modulo=Saas, Mensaje={Message}", ex.Message);
            await EscribirErrorAsync(context, HttpStatusCode.Unauthorized, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado. Modulo=Saas, Mensaje={Message}", ex.Message);
            await EscribirErrorAsync(context, HttpStatusCode.InternalServerError, "Error interno del servidor.");
        }
    }

    private static async Task EscribirErrorAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        var body = new ApiResponse<object>
        {
            Success = false,
            Message = message,
            Data = null,
            Errors = [message]
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
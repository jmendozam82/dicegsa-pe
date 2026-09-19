using Microsoft.Extensions.Logging;

namespace PE_GOL.Utility.Email;

/// <summary>
/// Implementación mínima de IEmailService (STACK-10 — MailKit/SMTP diferido).
/// Registra el envío en el log estructurado (Serilog, STACK-11) y retorna Task.CompletedTask:
/// el correo de activación es fire-and-forget (D-C) — un error de envío no revierte la creación.
/// La contraseña temporal se loguea SOLO en esta implementación de desarrollo (no hay canal SMTP
/// aún); la integración real con MailKit + plantilla HTML (enlace /auth/cambiar-contrasena, HU-004)
/// se difiere a HU-045+ y NO debe loguear credenciales.
/// </summary>
public sealed class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public Task EnviarActivacionResponsableAsync(string correo, string nombre, string passwordTemporal, CancellationToken ct = default)
    {
        // TODO(HU-045+): integrar MailKit (STACK-10) + plantilla HTML con enlace
        // /auth/cambiar-contrasena (HU-004). Stub de desarrollo: loguea la credencial temporal
        // porque no existe otro canal de entrega; la implementación real NO debe loguearla.
        _logger.LogInformation(
            "Correo de activación generado para responsable. Correo={Correo}, Nombre={Nombre}, PasswordTemporal={PasswordTemporal}",
            correo, nombre, passwordTemporal);
        return Task.CompletedTask;
    }
}
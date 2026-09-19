namespace PE_GOL.Utility.Email;

/// <summary>
/// Servicio de envío de correos (STACK-10 — MailKit/SMTP; Spec HU-010 D-C).
/// El correo de activación es fire-and-forget: un error de envío NO revierte la creación
/// (la BLL captura la excepción, loguea y continúa — logging + alerta interna).
/// </summary>
public interface IEmailService
{
    /// <summary>Envía el correo de activación con la contraseña temporal al nuevo responsable
    /// (plantilla con enlace a /auth/cambiar-contrasena — HU-004 — y credenciales temporales).</summary>
    Task EnviarActivacionResponsableAsync(string correo, string nombre, string passwordTemporal, CancellationToken ct = default);
}
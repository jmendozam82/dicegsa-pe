namespace PE_GOL.DTO.Requests;

/// <summary>POST /api/v1/auth/cambiar-contrasena (Spec HU-004 § DTOs).</summary>
public class CambiarContrasenaRequest
{
    public string ContrasenaActual { get; set; } = string.Empty;  // requerido, máx 128
    public string NuevaContrasena { get; set; } = string.Empty;   // mín 8, máx 128, 1 mayúsc + 1 minúsc + 1 dígito (D4)
}
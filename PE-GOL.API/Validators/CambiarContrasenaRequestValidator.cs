using System.Text.RegularExpressions;
using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/auth/cambiar-contrasena (Spec HU-004 §
/// Validaciones FluentValidation). La política de complejidad (D4) se define aquí y se
/// re-valida en la BLL (fuente de verdad, UX-04): mín 8, máx 128, al menos 1 mayúscula,
/// 1 minúscula y 1 dígito; la nueva no puede ser igual a la actual.
/// </summary>
public class CambiarContrasenaRequestValidator : AbstractValidator<CambiarContrasenaRequest>
{
    private static readonly Regex RegexPolitica = new(
        @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d).{8,128}$",
        RegexOptions.Compiled);

    public CambiarContrasenaRequestValidator()
    {
        RuleFor(x => x.ContrasenaActual)
            .NotEmpty().WithMessage("La contraseña actual es obligatoria")
            .MaximumLength(128).WithMessage("La contraseña actual no puede superar 128 caracteres");

        RuleFor(x => x.NuevaContrasena)
            .NotEmpty().WithMessage("La nueva contraseña es obligatoria")
            .MinimumLength(8).WithMessage("La nueva contraseña debe tener al menos 8 caracteres")
            .MaximumLength(128).WithMessage("La nueva contraseña no puede superar 128 caracteres")
            .Must(p => RegexPolitica.IsMatch(p))
            .WithMessage("La nueva contraseña debe incluir una mayúscula, una minúscula y un dígito")
            .NotEqual(x => x.ContrasenaActual)
            .WithMessage("La nueva contraseña no puede ser igual a la actual");
    }
}
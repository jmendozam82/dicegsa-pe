using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/auth/login (Spec HU-004 § Validaciones
/// FluentValidation). La existencia del usuario, el estado/bloqueo y la contraseña NO se
/// validan aquí (requieren BD): son reglas de negocio que re-valida la BLL (fuente de
/// verdad, UX-04). El correo se normaliza (trim + LOWER) en la BLL.
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Correo)
            .NotEmpty().WithMessage("El correo es obligatorio")
            .EmailAddress().WithMessage("El correo debe ser una dirección de correo válida")
            .MaximumLength(200).WithMessage("El correo no puede superar 200 caracteres");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria")
            .MaximumLength(128).WithMessage("La contraseña no puede superar 128 caracteres");
    }
}
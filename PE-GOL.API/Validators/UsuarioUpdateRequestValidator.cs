using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/usuarios/{id} (Spec HU-003 § Validaciones
/// FluentValidation). Iguales reglas de forma que el Create; la unicidad excluyendo self,
/// la existencia del tenant/área y la re-validación de límites SOLO al cambiar tenant
/// se resuelven en la BLL (fuente de verdad, D4/D6).
/// </summary>
public class UsuarioUpdateRequestValidator : AbstractValidator<UsuarioUpdateRequest>
{
    public UsuarioUpdateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio")
            .MaximumLength(150).WithMessage("El nombre no puede superar 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre no puede contener solo espacios");

        RuleFor(x => x.Correo)
            .NotEmpty().WithMessage("El correo es obligatorio")
            .EmailAddress().WithMessage("El correo debe ser una dirección de correo válida")
            .MaximumLength(200).WithMessage("El correo no puede superar 200 caracteres");

        RuleFor(x => x.Rol)
            .NotEmpty().WithMessage("El rol es obligatorio")
            .Must(r => r is "AdminTenant" or "Gerente" or "JefeArea")
            .WithMessage("El rol debe ser AdminTenant, Gerente o JefeArea");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("El tenant es obligatorio");

        RuleFor(x => x.AreaId)
            .NotNull().WithMessage("El rol JefeArea requiere un área asignada")
            .When(x => x.Rol == "JefeArea");
    }
}
using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/usuarios (Spec HU-003 § Validaciones
/// FluentValidation). La unicidad de correo, la existencia del tenant/área y los límites
/// del plan NO se validan aquí (requieren BD): son reglas de negocio que re-valida la BLL
/// (fuente de verdad, UX-04). El rol SuperAdmin se re-valida en la BLL ("solo seed").
/// </summary>
public class UsuarioCreateRequestValidator : AbstractValidator<UsuarioCreateRequest>
{
    public UsuarioCreateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio")
            .MaximumLength(150).WithMessage("El nombre no puede superar 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre no puede contener solo espacios");

        RuleFor(x => x.Correo)
            .NotEmpty().WithMessage("El correo es obligatorio")
            .EmailAddress().WithMessage("El correo debe ser una dirección de correo válida")
            .MaximumLength(200).WithMessage("El correo no puede superar 200 caracteres");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria")
            .MinimumLength(8).WithMessage("La contraseña temporal debe tener al menos 8 caracteres");

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
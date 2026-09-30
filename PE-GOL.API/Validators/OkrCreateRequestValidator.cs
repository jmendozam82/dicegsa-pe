using FluentValidation;
using PE_GOL.DTO.Requests.Okr;

namespace PE_GOL.API.Validators;

/// <summary>Validación de forma del request POST /api/v1/okrs (Spec HU-024 § Validaciones FluentValidation).</summary>
public class OkrCreateRequestValidator : AbstractValidator<OkrCreateRequest>
{
    public OkrCreateRequestValidator()
    {
        RuleFor(x => x.Descripcion)
            .NotEmpty().WithMessage("La descripción del objetivo es obligatoria.")
            .MaximumLength(500).WithMessage("La descripción no puede exceder los 500 caracteres.");

        RuleFor(x => x.PilarId)
            .NotEmpty().WithMessage("El pilar estratégico es obligatorio.");
    }
}

using FluentValidation;
using PE_GOL.DTO.Requests.Okr;

namespace PE_GOL.API.Validators;

/// <summary>Validación de forma del request PUT /api/v1/okrs/{id} (Spec HU-024 § Validaciones FluentValidation).</summary>
public class OkrUpdateRequestValidator : AbstractValidator<OkrUpdateRequest>
{
    public OkrUpdateRequestValidator()
    {
        RuleFor(x => x.Descripcion)
            .NotEmpty().WithMessage("La descripción del objetivo es obligatoria.")
            .MaximumLength(500).WithMessage("La descripción no puede exceder los 500 caracteres.");

        RuleFor(x => x.PilarId)
            .NotEmpty().WithMessage("El pilar estratégico es obligatorio.");
    }
}

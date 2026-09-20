using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/filosofia (Spec HU-011 §
/// Validaciones FluentValidation). UX-04: la BLL re-valida y es la fuente de verdad — sanitiza
/// (D-C), exige texto visible no vacío (CA #2) y longitud ≤ 5000 tras sanitizar.
/// </summary>
public class FilosofiaUpdateRequestValidator : AbstractValidator<FilosofiaUpdateRequest>
{
    public FilosofiaUpdateRequestValidator()
    {
        RuleFor(x => x.Vision)
            .NotEmpty().WithMessage("La Visión es requerida")
            .MaximumLength(5000).WithMessage("La Visión no puede exceder 5000 caracteres");

        RuleFor(x => x.Mision)
            .NotEmpty().WithMessage("La Misión es requerida")
            .MaximumLength(5000).WithMessage("La Misión no puede exceder 5000 caracteres");
    }
}
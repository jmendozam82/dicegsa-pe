using FluentValidation;
using PE_GOL.DTO.Requests.Objetivos;

namespace PE_GOL.API.Validators;

public class ObjetivoCgCreateRequestValidator : AbstractValidator<ObjetivoCgCreateRequest>
{
    public ObjetivoCgCreateRequestValidator()
    {
        RuleFor(x => x.Descripcion)
            .NotEmpty().WithMessage("La descripción es obligatoria.")
            .MaximumLength(500).WithMessage("La descripción no puede exceder los 500 caracteres.");

        RuleFor(x => x.PilarId)
            .NotEmpty().WithMessage("El pilar estratégico es obligatorio.");

        RuleFor(x => x.TrimestreObjetivo)
            .NotEmpty().WithMessage("El trimestre objetivo es obligatorio.")
            .Must(t => new[] { "Q1", "Q2", "Q3", "Q4" }.Contains(t))
            .WithMessage("El trimestre debe ser Q1, Q2, Q3 o Q4.");
    }
}

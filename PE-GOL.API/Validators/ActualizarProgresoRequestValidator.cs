using FluentValidation;
using PE_GOL.DTO.Requests.Objetivos;

namespace PE_GOL.API.Validators;

/// <summary>Validador FluentValidation para ActualizarProgresoRequest (HU-020).</summary>
public class ActualizarProgresoRequestValidator : AbstractValidator<ActualizarProgresoRequest>
{
    public ActualizarProgresoRequestValidator()
    {
        RuleFor(x => x.Progreso)
            .NotNull().WithMessage("El progreso es obligatorio.")
            .InclusiveBetween(0m, 100m).WithMessage("El progreso debe estar entre 0 y 100.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage("El progreso admite un máximo de 2 decimales.");
    }
}

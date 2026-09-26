using FluentValidation;
using PE_GOL.DTO.Requests.Objetivos;

namespace PE_GOL.API.Validators;

public class AccionPlanCreateRequestValidator : AbstractValidator<AccionPlanCreateRequest>
{
    public AccionPlanCreateRequestValidator()
    {
        RuleFor(x => x.Descripcion)
            .NotEmpty().WithMessage("La descripción es obligatoria.")
            .MaximumLength(1000).WithMessage("La descripción no puede exceder 1000 caracteres.");

        RuleFor(x => x.FechaInicio)
            .NotEmpty().WithMessage("La fecha de inicio es obligatoria.");

        RuleFor(x => x.FechaVencimiento)
            .NotEmpty().WithMessage("La fecha de vencimiento es obligatoria.");

        RuleFor(x => x.Peso)
            .GreaterThan(0).WithMessage("El peso debe ser mayor a 0.")
            .LessThanOrEqualTo(1).WithMessage("El peso máximo por acción es 1.0 (100%).");

        RuleFor(x => x.Clasificacion)
            .NotEmpty().WithMessage("La clasificación es obligatoria.")
            .Must(x => x == "Proyecto" || x == "Iniciativa" || x == "Operativa")
            .WithMessage("Clasificación no válida.");

        RuleFor(x => x.TipoPresupuesto)
            .NotEmpty().WithMessage("El tipo de presupuesto es obligatorio.")
            .Must(x => x == "OPEX" || x == "CAPEX")
            .WithMessage("Tipo de presupuesto no válido.");
    }
}
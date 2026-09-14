using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/planes/{id} (Spec HU-002 § Validaciones
/// FluentValidation). Iguales reglas de forma que el Create; la unicidad excluyendo self
/// y la re-validación de rangos se resuelven en la BLL (fuente de verdad).
/// </summary>
public class PlanUpdateRequestValidator : AbstractValidator<PlanUpdateRequest>
{
    public PlanUpdateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio")
            .MaximumLength(50).WithMessage("El nombre no puede superar 50 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre no puede contener solo espacios");

        RuleFor(x => x.Descripcion)
            .MaximumLength(2000).WithMessage("La descripción no puede superar 2000 caracteres");

        RuleFor(x => x.MaxAreas)
            .NotEmpty().WithMessage("maxAreas es obligatorio")
            .InclusiveBetween(1, 20).WithMessage("maxAreas debe estar entre 1 y 20");

        RuleFor(x => x.MaxUsuarios)
            .NotEmpty().WithMessage("maxUsuarios es obligatorio")
            .InclusiveBetween(1, 1000).WithMessage("maxUsuarios debe estar entre 1 y 1000");

        RuleFor(x => x.MaxCiclosActivos)
            .NotEmpty().WithMessage("maxCiclosActivos es obligatorio")
            .InclusiveBetween(1, 10).WithMessage("maxCiclosActivos debe estar entre 1 y 10");
    }
}
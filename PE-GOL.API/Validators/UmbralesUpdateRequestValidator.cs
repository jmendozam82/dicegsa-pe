using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del PUT /api/v1/ciclos/{cicloId}/umbrales (Spec HU-008 § Validaciones
/// FluentValidation). UPSERT CONJUNTO de ambos tipos (D1): kpi y planAccion son obligatorios y
/// cada uno se valida con UmbralCategoriaRequestValidator (rango 0.00..1.00 + verde > amarillo
/// ESTRICTO). La existencia/estado del ciclo NO se valida aquí (requiere BD) → BLL (UX-04).
/// </summary>
public class UmbralesUpdateRequestValidator : AbstractValidator<UmbralesUpdateRequest>
{
    public UmbralesUpdateRequestValidator()
    {
        RuleFor(x => x.Kpi)
            .NotNull().WithMessage("Los umbrales de KPI son obligatorios")
            .SetValidator(new UmbralCategoriaRequestValidator());

        RuleFor(x => x.PlanAccion)
            .NotNull().WithMessage("Los umbrales de Plan de Acción son obligatorios")
            .SetValidator(new UmbralCategoriaRequestValidator());
    }
}
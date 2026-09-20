using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId} (Spec HU-013 §
/// Validaciones FluentValidation — CA #1, D-C, D-H).
/// Mismas reglas que PilarCreateRequestValidator (nombre, estrategia, orden). El código PEC-N
/// NO es editable (CA #1) — no viaja en el request.
/// UX-04: la BLL re-valida y es la fuente de verdad.
/// </summary>
public class PilarUpdateRequestValidator : AbstractValidator<PilarUpdateRequest>
{
    public PilarUpdateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del pilar es requerido")
            .MaximumLength(150).WithMessage("El nombre no puede exceder 150 caracteres");

        RuleFor(x => x.EstrategiaVictoria)
            .MaximumLength(2000).WithMessage("La estrategia de victoria no puede exceder 2000 caracteres")
            .When(x => x.EstrategiaVictoria is not null);

        RuleFor(x => x.Orden)
            .GreaterThanOrEqualTo(0).WithMessage("El orden no puede ser negativo")
            .When(x => x.Orden.HasValue);
    }
}
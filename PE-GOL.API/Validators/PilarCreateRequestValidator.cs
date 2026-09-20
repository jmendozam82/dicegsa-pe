using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/ciclos/{cicloId}/pilares (Spec HU-013 §
/// Validaciones FluentValidation — CA #1, D-C, D-H).
/// UX-04: la BLL re-valida y es la fuente de verdad — trim, nombre requerido ≤ 150 chars,
/// estrategia ≤ 2000 chars (D-C), orden no negativo (D-H). Las reglas de negocio (RC-12, rol GER,
/// CA #2 máximo 8, CA #1 código PEC-N, CA #3 dependencias) se validan en BLL, no aquí.
/// </summary>
public class PilarCreateRequestValidator : AbstractValidator<PilarCreateRequest>
{
    public PilarCreateRequestValidator()
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
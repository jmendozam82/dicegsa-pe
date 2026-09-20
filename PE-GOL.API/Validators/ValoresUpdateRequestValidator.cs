using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/filosofia/valores
/// (Spec HU-012 § Validaciones FluentValidation — CA #4, D-G).
/// UX-04: la BLL re-valida y es la fuente de verdad — trim, min 1, max 15, no vacío tras trim,
/// ≤ 100 chars por valor y sin duplicados case-insensitive (paso 6 de ActualizarValoresAsync).
/// </summary>
public class ValoresUpdateRequestValidator : AbstractValidator<ValoresUpdateRequest>
{
    public ValoresUpdateRequestValidator()
    {
        RuleFor(x => x.Valores)
            .NotEmpty().WithMessage("Debe registrar al menos 1 valor corporativo")
            .Must(v => v.Count <= 15).WithMessage("Máximo 15 valores corporativos")
            .Must(v => v.Select(valor => valor?.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count() == v.Count)
                .WithMessage("No se permiten valores duplicados");

        RuleForEach(x => x.Valores)
            .Must(v => !string.IsNullOrWhiteSpace(v?.Trim())).WithMessage("Cada valor es requerido")
            .MaximumLength(100).WithMessage("Cada valor no puede exceder 100 caracteres");
    }
}
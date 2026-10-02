using FluentValidation;
using PE_GOL.DTO.Requests.Okr;

namespace PE_GOL.API.Validators;

/// <summary>Validación de forma del request PUT /api/v1/okrs/{okrId}/key-results/{id} (Spec HU-025 § Validaciones FluentValidation).
/// NO valida la suma de pesos (F0, Opción B): el invariante S = 1.000 vive solo en la BLL (ActualizarPesosAsync).</summary>
public class KeyResultUpdateRequestValidator : AbstractValidator<KeyResultUpdateRequest>
{
    public KeyResultUpdateRequestValidator()
    {
        RuleFor(x => x.Descripcion)
            .NotEmpty().WithMessage("La descripción del Key Result es obligatoria.")
            .MaximumLength(500).WithMessage("La descripción no puede exceder los 500 caracteres.");

        RuleFor(x => x.Peso)
            .GreaterThan(0.000m).WithMessage("El peso debe ser mayor que 0.")
            .LessThanOrEqualTo(1.000m).WithMessage("El peso no puede ser mayor que 1.000 (100%).")
            .Must(p => decimal.Round(p, 3) == p).WithMessage("El peso admite hasta 3 decimales (por ejemplo 0.250).");
    }
}

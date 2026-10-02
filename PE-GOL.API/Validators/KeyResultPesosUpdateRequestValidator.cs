using FluentValidation;
using PE_GOL.DTO.Requests.Okr;

namespace PE_GOL.API.Validators;

/// <summary>Validación de forma del request PUT /api/v1/okrs/{okrId}/key-results/pesos (Spec HU-025 § Validaciones FluentValidation).
/// Valida la FORMA del vector; la suma S = 1.000 y la cobertura exacta del conjunto son solo BLL
/// (requieren conocer los KRs existentes del OKR).</summary>
public class KeyResultPesosUpdateRequestValidator : AbstractValidator<KeyResultPesosUpdateRequest>
{
    public KeyResultPesosUpdateRequestValidator()
    {
        RuleFor(x => x.Pesos)
            .NotNull().WithMessage("Debe indicar el reparto de pesos de los Key Results del OKR.")
            .NotEmpty().WithMessage("Debe indicar el reparto de pesos de los Key Results del OKR.")
            .Must(l => l != null && l.Count >= 1 && l.Count <= 5)
                .WithMessage("El reparto de pesos debe contener entre 1 y 5 elementos (máximo 5 Key Results por OKR).")
            .Must(l => l != null && l.Select(p => p.Id).Distinct().Count() == l.Count)
                .WithMessage("El reparto de pesos no puede contener el mismo Key Result más de una vez.");

        RuleForEach(x => x.Pesos).ChildRules(p =>
        {
            p.RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Cada elemento del reparto debe identificar un Key Result.");

            p.RuleFor(x => x.Peso)
                .GreaterThan(0.000m).WithMessage("El peso debe ser mayor que 0.")
                .LessThanOrEqualTo(1.000m).WithMessage("El peso no puede ser mayor que 1.000 (100%).")
                .Must(v => decimal.Round(v, 3) == v).WithMessage("El peso admite hasta 3 decimales (por ejemplo 0.250).");
        });
    }
}

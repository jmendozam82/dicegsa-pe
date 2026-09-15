using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/ciclos (Spec HU-007 § Validaciones FluentValidation).
/// UX-04: la validación del servidor es la fuente de verdad; la BLL re-valida mesInicio (1..12)
/// y la unicidad de añoFiscal (DAL-C6) — el validator cubre la FORMA (obligatorios/rangos).
/// </summary>
public class CicloCreateRequestValidator : AbstractValidator<CicloCreateRequest>
{
    public CicloCreateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del ciclo es obligatorio")
            .MaximumLength(100).WithMessage("El nombre del ciclo no puede superar 100 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre del ciclo no puede contener solo espacios");

        RuleFor(x => x.AñoFiscal)
            .NotEmpty().WithMessage("El año fiscal es obligatorio")
            .InclusiveBetween(2000, 2100).WithMessage("El año fiscal debe estar entre 2000 y 2100");

        RuleFor(x => x.MesInicio)
            .NotEmpty().WithMessage("El mes de inicio es obligatorio")
            .InclusiveBetween(1, 12).WithMessage("El mes de inicio debe estar entre 1 y 12");
    }
}
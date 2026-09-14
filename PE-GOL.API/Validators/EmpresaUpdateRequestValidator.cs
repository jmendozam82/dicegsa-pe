using FluentValidation;
using PE_GOL.DTO.Requests;
using PE_GOL.Utility.Helpers;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/empresa (Spec HU-006 § Validaciones FluentValidation).
/// UX-04: la validación del servidor es la fuente de verdad; la BLL re-valida la unicidad del
/// nombre (DAL-E5) y la zona horaria IANA (D7) — el validator cubre la FORMA (longitudes/obligatorios).
/// </summary>
public class EmpresaUpdateRequestValidator : AbstractValidator<EmpresaUpdateRequest>
{
    public EmpresaUpdateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre de la empresa es obligatorio")
            .MaximumLength(150).WithMessage("El nombre de la empresa no puede superar 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre de la empresa no puede contener solo espacios");

        RuleFor(x => x.Eslogan)
            .MaximumLength(200).WithMessage("El eslogan no puede superar 200 caracteres");

        RuleFor(x => x.ZonaHoraria)
            .NotEmpty().WithMessage("La zona horaria es obligatoria")
            .MaximumLength(50).WithMessage("La zona horaria no puede superar 50 caracteres")
            .Must(ZonaHorariaHelper.EsValida).WithMessage("La zona horaria debe ser un identificador IANA válido (ej: America/Managua)");
    }
}
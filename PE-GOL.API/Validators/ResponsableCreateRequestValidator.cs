using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/ciclos/{cicloId}/responsables (Spec HU-010 §
/// Validaciones FluentValidation). UX-04: la validación del servidor es la fuente de verdad; la
/// BLL re-valida las reglas de negocio (RN-010/011/012, RC-12, rol, área activa/sin responsable,
/// límites de plan) — el validator cubre la FORMA (obligatorios/longitud/formato).
/// password NUNCA viaja en requests (se genera temporal en BLL, D-C).
/// </summary>
public class ResponsableCreateRequestValidator : AbstractValidator<ResponsableCreateRequest>
{
    public ResponsableCreateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del responsable es requerido")
            .MaximumLength(150).WithMessage("El nombre del responsable no puede exceder 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre del responsable no puede contener solo espacios");

        RuleFor(x => x.Correo)
            .NotEmpty().WithMessage("El correo es requerido y debe ser válido (máx 200 caracteres)")
            .EmailAddress().WithMessage("El correo es requerido y debe ser válido (máx 200 caracteres)")
            .MaximumLength(200).WithMessage("El correo es requerido y debe ser válido (máx 200 caracteres)");

        RuleFor(x => x.AreaId)
            .NotEmpty().WithMessage("Debe asignar un área al responsable");
    }
}
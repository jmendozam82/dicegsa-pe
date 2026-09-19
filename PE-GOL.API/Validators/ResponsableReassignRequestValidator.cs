using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId}
/// (Spec HU-010 § Validaciones FluentValidation). UX-04: la validación del servidor es la fuente
/// de verdad; la BLL re-valida las reglas de negocio (RN-011/012, RC-12, área activa/sin
/// responsable, advertencia de origen) — el validator cubre la FORMA (obligatorio).
/// </summary>
public class ResponsableReassignRequestValidator : AbstractValidator<ResponsableReassignRequest>
{
    public ResponsableReassignRequestValidator()
    {
        RuleFor(x => x.AreaId)
            .NotEmpty().WithMessage("Debe asignar un área al responsable");
    }
}
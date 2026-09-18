using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/ciclos/{cicloId}/areas (Spec HU-009 § Validaciones
/// FluentValidation). UX-04: la validación del servidor es la fuente de verdad; la BLL re-valida
/// responsable (RN-011: existe/JefeArea/Activo), RN-012 y RN-010 — el validator cubre la FORMA
/// (obligatorios/longitud). codigo/orden NUNCA viajan en requests (auto-generados, DB-04).
/// </summary>
public class AreaCreateRequestValidator : AbstractValidator<AreaCreateRequest>
{
    public AreaCreateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del área es requerido")
            .MaximumLength(150).WithMessage("El nombre del área no puede exceder 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre del área no puede contener solo espacios");

        RuleFor(x => x.ResponsableId)
            .NotEmpty().WithMessage("Debe asignar un Jefe de Área responsable (RN-011)");
    }
}
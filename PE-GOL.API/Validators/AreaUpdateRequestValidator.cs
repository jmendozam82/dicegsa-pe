using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/areas/{areaId} (Spec HU-009 §
/// Validaciones FluentValidation). Mismos campos que AreaCreateRequest (nombre, comentarios,
/// responsable). codigo/orden inmutables (auto-generados por la BLL, DB-04). UX-04: la BLL
/// re-valida responsable (RN-011), RN-012 excluyendo self y el estado del ciclo (RC-12).
/// ResponsableId OPCIONAL (RN-011 2026-09-21): null libera al responsable actual.
/// </summary>
public class AreaUpdateRequestValidator : AbstractValidator<AreaUpdateRequest>
{
    public AreaUpdateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del área es requerido")
            .MaximumLength(150).WithMessage("El nombre del área no puede exceder 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre del área no puede contener solo espacios");
    }
}
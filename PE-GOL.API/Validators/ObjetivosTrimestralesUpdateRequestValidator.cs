using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId}/objetivos-trimestrales
/// (Spec HU-014 § Validaciones FluentValidation — CA #2, D-C).
/// Los 4 trimestres son OPCIONALES (CA #2): NO hay reglas NotEmpty — null o string vacío tras
/// trim se persiste null (D-H). Máx 2000 chars por trimestre (D-C).
/// UX-04: la BLL re-valida y es la fuente de verdad (paso 7 de ActualizarObjetivosTrimestralesAsync).
/// </summary>
public class ObjetivosTrimestralesUpdateRequestValidator : AbstractValidator<ObjetivosTrimestralesUpdateRequest>
{
    public ObjetivosTrimestralesUpdateRequestValidator()
    {
        RuleFor(x => x.ObjetivoQ1)
            .MaximumLength(2000).WithMessage("El objetivo del trimestre Q1 no puede exceder 2000 caracteres")
            .When(x => x.ObjetivoQ1 is not null);

        RuleFor(x => x.ObjetivoQ2)
            .MaximumLength(2000).WithMessage("El objetivo del trimestre Q2 no puede exceder 2000 caracteres")
            .When(x => x.ObjetivoQ2 is not null);

        RuleFor(x => x.ObjetivoQ3)
            .MaximumLength(2000).WithMessage("El objetivo del trimestre Q3 no puede exceder 2000 caracteres")
            .When(x => x.ObjetivoQ3 is not null);

        RuleFor(x => x.ObjetivoQ4)
            .MaximumLength(2000).WithMessage("El objetivo del trimestre Q4 no puede exceder 2000 caracteres")
            .When(x => x.ObjetivoQ4 is not null);
    }
}
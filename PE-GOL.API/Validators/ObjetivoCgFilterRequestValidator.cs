using FluentValidation;
using PE_GOL.DTO.Requests.Objetivos;
using System.Linq;

namespace PE_GOL.API.Validators;

public class ObjetivoCgFilterRequestValidator : AbstractValidator<ObjetivoCgFilterRequest>
{
    public ObjetivoCgFilterRequestValidator()
    {
        RuleFor(x => x.Trimestre)
            .Must(t => new[] { "Q1", "Q2", "Q3", "Q4" }.Contains(t))
            .When(x => !string.IsNullOrWhiteSpace(x.Trimestre))
            .WithMessage("El trimestre debe ser Q1, Q2, Q3 o Q4");

        RuleFor(x => x.Semaforo)
            .Must(s => new[] { "Rojo", "Amarillo", "Verde" }.Contains(s))
            .When(x => !string.IsNullOrWhiteSpace(x.Semaforo))
            .WithMessage("El semáforo debe ser Rojo, Amarillo o Verde");
    }
}

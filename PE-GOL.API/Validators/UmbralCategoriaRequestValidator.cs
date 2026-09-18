using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma de una categoría de umbrales (Spec HU-008 § Validaciones FluentValidation).
/// Espejo de los CHECKs del DDL (06_MODELO_DATOS.md L155-157): umbralVerde/umbralAmarillo ∈
/// 0.00..1.00 y umbralVerde &gt; umbralAmarillo ESTRICTO (verde igual a amarillo se rechaza).
/// UX-04: la BLL re-valida rango y estricto sobre los valores ya normalizados (fuente de verdad);
/// este validator cubre la FORMA del request.
/// </summary>
public class UmbralCategoriaRequestValidator : AbstractValidator<UmbralCategoriaRequest>
{
    public UmbralCategoriaRequestValidator()
    {
        RuleFor(x => x.UmbralVerde)
            .NotEmpty().WithMessage("El umbral verde es obligatorio")
            .InclusiveBetween(0.00m, 1.00m).WithMessage("El umbral verde debe estar entre 0.00 y 1.00");

        RuleFor(x => x.UmbralAmarillo)
            .NotEmpty().WithMessage("El umbral amarillo es obligatorio")
            .InclusiveBetween(0.00m, 1.00m).WithMessage("El umbral amarillo debe estar entre 0.00 y 1.00");

        // Cross-field: espejo del CHECK ESTRICTO del DDL L155 (verde > amarillo; == se rechaza).
        RuleFor(x => x)
            .Must(c => c.UmbralVerde > c.UmbralAmarillo)
            .WithMessage("El umbral verde debe ser estrictamente mayor que el umbral amarillo");
    }
}
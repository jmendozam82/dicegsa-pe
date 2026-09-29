using FluentValidation;
using PE_GOL.DTO.Requests.PlanOperativo;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma de los query params de GET /api/v1/planes/consolidado (Spec HU-023
/// § Validaciones FluentValidation). FluentValidation cubre la FORMA; la BLL re-valida
/// paginación y rango de fechas (fuente de verdad, UX-04).
/// </summary>
public class FiltrosConsolidadoRequestValidator : AbstractValidator<FiltrosConsolidadoRequest>
{
    /// <summary>Enum status_accion del DDL (06_MODELO_DATOS.md L41).</summary>
    private static readonly string[] StatusValidos =
        ["NoIniciado", "EnProgreso", "Terminado", "Atrasado"];

    /// <summary>Enum clasificacion_accion del DDL (06_MODELO_DATOS.md L39).</summary>
    private static readonly string[] ClasificacionesValidas =
        ["Proyecto", "Iniciativa", "Operativa"];

    /// <summary>Enum tipo_presupuesto del DDL (06_MODELO_DATOS.md L40).</summary>
    private static readonly string[] TiposValidos = ["OPEX", "CAPEX"];

    public FiltrosConsolidadoRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("La página debe ser mayor o igual a 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("El tamaño de página debe estar entre 1 y 100");

        RuleFor(x => x.Status)
            .Must(s => StatusValidos.Contains(s, StringComparer.Ordinal))
            .WithMessage(x => $"El status '{x.Status}' no es válido. Valores permitidos: {string.Join(", ", StatusValidos)}")
            .When(x => x.Status is not null);

        RuleFor(x => x.Clasificacion)
            .Must(c => ClasificacionesValidas.Contains(c, StringComparer.Ordinal))
            .WithMessage(x => $"La clasificación '{x.Clasificacion}' no es válida. Valores permitidos: {string.Join(", ", ClasificacionesValidas)}")
            .When(x => x.Clasificacion is not null);

        RuleFor(x => x.Tipo)
            .Must(t => TiposValidos.Contains(t, StringComparer.Ordinal))
            .WithMessage(x => $"El tipo '{x.Tipo}' no es válido. Valores permitidos: {string.Join(", ", TiposValidos)}")
            .When(x => x.Tipo is not null);

        // Cross-field: FechaDesde <= FechaHasta.
        RuleFor(x => x)
            .Must(f => f.FechaDesde is null || f.FechaHasta is null || f.FechaDesde <= f.FechaHasta)
            .WithMessage("La fecha desde debe ser anterior o igual a la fecha hasta");
    }
}

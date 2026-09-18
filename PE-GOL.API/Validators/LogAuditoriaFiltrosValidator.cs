using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma de los query params de GET /api/v1/log-auditoria (Spec HU-005
/// § Validaciones FluentValidation). FluentValidation cubre la FORMA; la BLL re-valida
/// accion, rango de fechas y paginación (fuente de verdad, UX-04 — paso 3 de ListarAsync).
/// tenantId/usuarioId: el model binding valida el formato Guid (400 si malformado); no
/// requieren reglas adicionales. desde/hasta: el model binding de ASP.NET Core garantiza
/// el formato DateTimeOffset (si no parsea → 400).
/// </summary>
public class LogAuditoriaFiltrosValidator : AbstractValidator<LogAuditoriaFiltrosRequest>
{
    /// <summary>Enum accion_auditoria del DDL (06_MODELO_DATOS.md L46).</summary>
    private static readonly string[] AccionesValidas =
        ["CREATE", "UPDATE", "DELETE", "LOGIN", "LOGOUT", "ACTIVATE", "DEACTIVATE"];

    public LogAuditoriaFiltrosValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("La página debe ser mayor o igual a 1")
            .When(x => x.Page is not null);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("El tamaño de página debe estar entre 1 y 100")
            .When(x => x.PageSize is not null);

        RuleFor(x => x.Accion)
            .Must(a => AccionesValidas.Contains(a, StringComparer.Ordinal))
            .WithMessage(x => $"La acción '{x.Accion}' no es válida")
            .When(x => x.Accion is not null);

        // Cross-field: desde <= hasta (inclusive en ambos extremos).
        RuleFor(x => x)
            .Must(f => f.Desde is null || f.Hasta is null || f.Desde <= f.Hasta)
            .WithMessage("La fecha inicial debe ser anterior o igual a la fecha final");

        // Cross-field: hasta no puede estar en el futuro (el log no tiene entradas futuras).
        RuleFor(x => x)
            .Must(f => f.Hasta is null || f.Hasta <= DateTimeOffset.UtcNow)
            .WithMessage("La fecha final no puede estar en el futuro");
    }
}
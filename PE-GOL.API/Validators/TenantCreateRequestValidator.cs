using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/tenants (Spec HU-001 § Validaciones
/// FluentValidation). La unicidad de nombre y la existencia del plan NO se validan aquí
/// (requieren BD): son reglas de negocio que re-valida la BLL (fuente de verdad).
/// </summary>
public class TenantCreateRequestValidator : AbstractValidator<TenantCreateRequest>
{
    public TenantCreateRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre es obligatorio")
            .MaximumLength(150).WithMessage("El nombre no puede superar 150 caracteres")
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre no puede contener solo espacios");

        RuleFor(x => x.Descripcion)
            .MaximumLength(1000).WithMessage("La descripción no puede superar 1000 caracteres");

        RuleFor(x => x.PlanId)
            .NotEmpty().WithMessage("El plan es obligatorio");

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithMessage("El logoUrl no puede superar 500 caracteres")
            .Must(EsUrlValida).WithMessage("El logoUrl debe ser una URL absoluta http/https")
            .When(x => !string.IsNullOrWhiteSpace(x.LogoUrl));

        RuleFor(x => x.Eslogan)
            .MaximumLength(200).WithMessage("El eslogan no puede superar 200 caracteres");

        RuleFor(x => x.ZonaHoraria)
            .MaximumLength(50).WithMessage("La zona horaria no puede superar 50 caracteres");
    }

    private static bool EsUrlValida(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
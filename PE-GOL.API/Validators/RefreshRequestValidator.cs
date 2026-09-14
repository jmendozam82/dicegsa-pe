using FluentValidation;
using PE_GOL.DTO.Requests;

namespace PE_GOL.API.Validators;

/// <summary>
/// Validación de forma del request POST /api/v1/auth/refresh (Spec HU-004 § Validaciones
/// FluentValidation). La validez/revocación/expiración del token se valida en la BLL.
/// </summary>
public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("El refresh token es obligatorio");
    }
}
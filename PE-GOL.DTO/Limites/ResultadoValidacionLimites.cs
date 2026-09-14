namespace PE_GOL.DTO.Limites;

/// <summary>
/// Resultado de la validación de límites. Errores vacíos ⇒ EsValido == true.
/// Spec HU-002 § DTOs — contrato del validador de límites.
/// </summary>
public sealed record ResultadoValidacionLimites(bool EsValido, IReadOnlyList<string> Errores)
{
    public static ResultadoValidacionLimites Ok() => new(true, []);

    public static ResultadoValidacionLimites Fallo(IReadOnlyList<string> errores) => new(false, errores);
}
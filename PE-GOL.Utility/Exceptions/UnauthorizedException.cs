namespace PE_GOL.Utility.Exceptions;

/// <summary>
/// Credenciales inválidas / sesión no autorizada → HTTP 401 (Spec HU-004 § Lógica BLL).
/// Mismo patrón que ValidacionException (422) y NotFoundException (404): excepción
/// transversal en PE-GOL.Utility/Exceptions (04_ARQUITECTURA.md § 2).
/// El ExceptionMiddleware de la API la traducirá a 401 (fase @BackendDev HU-004).
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}
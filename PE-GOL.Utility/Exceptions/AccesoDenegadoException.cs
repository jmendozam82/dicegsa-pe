namespace PE_GOL.Utility.Exceptions;

/// <summary>
/// Rol sin permiso para la operación → HTTP 403 (Spec HU-006 § Lógica BLL, D12).
/// Mismo patrón que ValidacionException (422) y NotFoundException (404): excepción
/// transversal en PE-GOL.Utility/Exceptions (04_ARQUITECTURA.md § 2).
/// El ExceptionMiddleware de la API la traducirá a 403 (fase @BackendDev HU-006).
/// </summary>
public class AccesoDenegadoException : Exception
{
    public AccesoDenegadoException(string message) : base(message) { }
}
namespace PE_GOL.Utility.Exceptions;

/// <summary>
/// Fallo de infraestructura (p. ej. Supabase Storage) → HTTP 500 (Spec HU-006 § Lógica BLL,
/// RNF-014: el fallo de storage NO toca la BD — la configuración existente queda intacta).
/// Mismo patrón que ValidacionException (422) y NotFoundException (404): excepción
/// transversal en PE-GOL.Utility/Exceptions (04_ARQUITECTURA.md § 2).
/// El ExceptionMiddleware de la API la traducirá a 500 (fase @BackendDev HU-006).
/// </summary>
public class InfraestructuraException : Exception
{
    public InfraestructuraException(string message) : base(message) { }
    public InfraestructuraException(string message, Exception innerException) : base(message, innerException) { }
}
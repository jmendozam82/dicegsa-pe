namespace PE_GOL.Aplicacion.Exceptions;

/// <summary>
/// Error de la API interna traducido por el ApiClient (Spec HU-045 § Cimiento de frontend).
/// Se lanza cuando ApiResponse&lt;T&gt;.Success == false o cuando el código HTTP no es de éxito.
/// Expone StatusCode y Errors (lista de mensajes del servidor) para que el controlador MVC
/// los muestre (CA #5: errores del servidor visibles).
/// </summary>
public class ApiClientException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public ApiClientException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? [];
    }
}
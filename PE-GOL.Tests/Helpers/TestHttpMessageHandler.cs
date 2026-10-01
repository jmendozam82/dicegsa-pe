using System.Net;

namespace PE_GOL.Tests.Helpers;

/// <summary>
/// HttpMessageHandler configurable para los tests de ApiClient (HU-045) — SIN HTTP real.
/// Devuelve las respuestas encoladas en orden (cola FIFO) y registra cada request
/// recibido para poder inspeccionar headers, URI y body (p. ej. el Bearer token o el
/// body del refresh). Si la cola se agota, responde 200 OK vacío.
/// </summary>
public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>Requests recibidos por el handler, en orden de llegada.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Cuerpo ya serializado de cada request, capturado DURANTE el envío.
    ///
    /// Un <see cref="HttpClient"/> real serializa <c>request.Content</c> dentro de SendAsync
    /// (MultipartFormDataContent.CopyToAsync), es decir, con los streams del formulario
    /// aún abiertos. Los mocks que solo guardan la referencia al HttpRequestMessage nunca
    /// disparan esa ruta y, por tanto, no detectan un stream cerrado antes de tiempo
    /// (regresión HU-022-hotfix v2: ObjectDisposedException al subir entregables).
    /// Al serializar aquí, el test reproduce el comportamiento del pipeline real.
    /// </summary>
    public List<string> BodiesSerializados { get; } = [];

    /// <summary>Error lanzado al serializar el cuerpo, si lo hubo (null = sin error).</summary>
    public Exception? ErrorAlSerializar { get; private set; }

    /// <summary>Encola una respuesta que se devolverá en la próxima llamada (FIFO).</summary>
    public void AgregarRespuesta(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (request.Content is not null)
        {
            try
            {
                BodiesSerializados.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                ErrorAlSerializar = ex;
                throw;
            }
        }

        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK);
        return response;
    }
}
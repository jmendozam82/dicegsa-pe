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

    /// <summary>Encola una respuesta que se devolverá en la próxima llamada (FIFO).</summary>
    public void AgregarRespuesta(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK);
        return Task.FromResult(response);
    }
}
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Services;

/// <summary>
/// Cliente HTTP tipado que consume la API interna (Spec HU-045 § Cimiento de frontend).
/// Registrado con AddHttpClient&lt;IApiClient, ApiClient&gt; (IHttpClientFactory) en Program.cs;
/// BaseAddress desde la configuración Api:BaseUrl (nunca hardcodeada).
///
/// Comportamiento (contrato @QA):
/// 1. Bearer token desde SesionService.ObtenerAccessToken(); sin token → UnauthorizedException
///    SIN llamar a la API.
/// 2. Desenvolvimiento del wrapper (ARCH-07): Success=true → Data; Success=false →
///    ApiClientException con StatusCode y Errors.
/// 3. 401 → POST /api/v1/auth/refresh con el refresh token almacenado (rotación D1 HU-004) →
///    ActualizarTokens → reintento ÚNICO del request original. Si el refresh falla (401) →
///    CerrarSesion + UnauthorizedException.
/// 4. 500 → mensaje genérico "Error interno del servidor" (sin filtrar detalles internos).
/// 5. Deserializa con PropertyNameCaseInsensitive=true (la API produce camelCase vía MVC y
///    PascalCase vía ExceptionMiddleware — hallazgo de @QA).
/// </summary>
public class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SesionService _sesionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ApiClient(HttpClient httpClient, SesionService sesionService)
    {
        _httpClient = httpClient;
        _sesionService = sesionService;
    }

    public Task<T> GetAsync<T>(string path, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => EnviarAsync<T>(HttpMethod.Get, path, query, body: null, ct);

    public Task<TRes> PostAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Post, path, query: null, body, ct);

    public Task<TRes> PutAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Put, path, query: null, body, ct);

    public Task<TRes> PostAsync<TRes>(string path, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Post, path, query: null, body: null, ct);

    public Task<TRes> PutAsync<TRes>(string path, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Put, path, query: null, body: null, ct);

    public Task<TRes> DeleteAsync<TRes>(string path, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Delete, path, query: null, body: null, ct);

    public Task<TRes> PatchAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default)
        => EnviarAsync<TRes>(HttpMethod.Patch, path, query: null, body, ct);

    public async Task<TRes> PostMultipartAsync<TRes>(string path, IFormFile archivo, string campo, CancellationToken ct = default)
    {
        var response = await EnviarMultipartConTokenAsync(path, archivo, campo, ct);

        // SEC-01: 401 → refresh con rotación → reintento ÚNICO del request original.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var renovado = await IntentarRefreshAsync(ct);
            if (!renovado)
                throw new UnauthorizedException("La sesión expiró. Vuelva a iniciar sesión.");

            response = await EnviarMultipartConTokenAsync(path, archivo, campo, ct);
        }

        return await ProcesarRespuestaAsync<TRes>(response, ct);
    }

    // ─── Núcleo ──────────────────────────────────────────────────────────────

    private async Task<T> EnviarAsync<T>(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        object? body,
        CancellationToken ct)
    {
        var response = await EnviarConTokenAsync(method, path, query, body, ct);

        // SEC-01: 401 → refresh con rotación → reintento ÚNICO del request original.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var renovado = await IntentarRefreshAsync(ct);
            if (!renovado)
                throw new UnauthorizedException("La sesión expiró. Vuelva a iniciar sesión.");

            response = await EnviarConTokenAsync(method, path, query, body, ct);
        }

        return await ProcesarRespuestaAsync<T>(response, ct);
    }

    private async Task<HttpResponseMessage> EnviarConTokenAsync(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        object? body,
        CancellationToken ct)
    {
        var request = CrearRequest(method, path, query, body);
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// POST multipart/form-data con Bearer (logo de empresa — HU-006, campo 'archivo').
    /// El stream del archivo permanece abierto durante SendAsync (se descarta al salir del método).
    /// </summary>
    private async Task<HttpResponseMessage> EnviarMultipartConTokenAsync(
        string path,
        IFormFile archivo,
        string campo,
        CancellationToken ct)
    {
        var accessToken = _sesionService.ObtenerAccessToken();
        if (string.IsNullOrEmpty(accessToken))
            throw new UnauthorizedException("No hay sesión activa.");

        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var contenido = new MultipartFormDataContent();
        await using var stream = archivo.OpenReadStream();
        var contenidoArchivo = new StreamContent(stream);
        if (!string.IsNullOrEmpty(archivo.ContentType))
            contenidoArchivo.Headers.ContentType = new MediaTypeHeaderValue(archivo.ContentType);
        contenido.Add(contenidoArchivo, campo, archivo.FileName);
        request.Content = contenido;

        return await _httpClient.SendAsync(request, ct);
    }

    private HttpRequestMessage CrearRequest(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query,
        object? body)
    {
        var accessToken = _sesionService.ObtenerAccessToken();
        if (string.IsNullOrEmpty(accessToken))
            throw new UnauthorizedException("No hay sesión activa.");

        var uri = ConstruirUri(path, query);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    /// <summary>
    /// POST /api/v1/auth/refresh con el refresh token almacenado (rotación D1 HU-004).
    /// Éxito → ActualizarTokens(nuevos) y true. Refresh 401 → CerrarSesion() y false.
    /// </summary>
    private async Task<bool> IntentarRefreshAsync(CancellationToken ct)
    {
        var refreshToken = _sesionService.ObtenerRefreshToken();
        if (string.IsNullOrEmpty(refreshToken))
            return false;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new RefreshRequest { RefreshToken = refreshToken }, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _sesionService.CerrarSesion();
            return false;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        ApiResponse<RefreshResponse>? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize<ApiResponse<RefreshResponse>>(content, JsonOptions);
        }
        catch (JsonException)
        {
            _sesionService.CerrarSesion();
            return false;
        }

        if (wrapper is not { Success: true } || wrapper.Data is null)
        {
            _sesionService.CerrarSesion();
            return false;
        }

        _sesionService.ActualizarTokens(wrapper.Data.AccessToken, wrapper.Data.RefreshToken);
        return true;
    }

    private async Task<T> ProcesarRespuestaAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var content = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            ApiResponse<T>? wrapper;
            try
            {
                wrapper = JsonSerializer.Deserialize<ApiResponse<T>>(content, JsonOptions);
            }
            catch (JsonException)
            {
                throw new ApiClientException(statusCode, "La API devolvió una respuesta inválida.");
            }

            if (wrapper is { Success: true })
                return wrapper.Data!;

            throw new ApiClientException(statusCode, wrapper?.Message ?? "Error", wrapper?.Errors);
        }

        // 500 → mensaje genérico sin filtrar detalles internos del servidor.
        if (statusCode == 500)
            throw new ApiClientException(500, "Error interno del servidor");

        // 401 tras el reintento → sesión no válida → redirigir a login.
        if (statusCode == 401)
            throw new UnauthorizedException("La sesión expiró. Vuelva a iniciar sesión.");

        ApiResponse<object>? errorWrapper;
        try
        {
            errorWrapper = JsonSerializer.Deserialize<ApiResponse<object>>(content, JsonOptions);
        }
        catch (JsonException)
        {
            errorWrapper = null;
        }

        var message = errorWrapper?.Message ?? $"Error {statusCode}";
        var errors = errorWrapper?.Errors;
        throw new ApiClientException(statusCode, message, errors);
    }

    /// <summary>
    /// SEC-05: los query params se codifican con Uri.EscapeDataString; nunca se concatenan
    /// valores del usuario en la URL.
    /// </summary>
    private static string ConstruirUri(string path, IDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
            return path;

        var pares = query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");

        var queryString = string.Join("&", pares);
        return string.IsNullOrEmpty(queryString) ? path : $"{path}?{queryString}";
    }
}
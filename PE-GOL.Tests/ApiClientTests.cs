using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Tests.Helpers;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para ApiClient — Spec HU-045 § "Cimiento de frontend" (8 casos, tabla #1..#8).
/// TDD fase red (TEST-01): ApiClient/IApiClient/SesionService/ApiClientException NO existen aún en
/// PE-GOL.Aplicacion → esta clase NO compila hasta que @FrontendDev los implemente con la firma
/// exacta documentada en el reporte de @QA (rojo esperado).
/// Mock de HttpMessageHandler (TestHttpMessageHandler) — sin HTTP real (TEST-03).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-05).
/// Contrato fijado:
///   IApiClient.GetAsync&lt;T&gt;(string path, IDictionary&lt;string,string?&gt;? query = null, CancellationToken ct = default)
///   IApiClient.PostAsync&lt;TReq,TRes&gt;(string path, TReq body, CancellationToken ct = default)
///   IApiClient.PutAsync&lt;TReq,TRes&gt;(string path, TReq body, CancellationToken ct = default)
///   IApiClient.PostAsync&lt;TRes&gt;(string path, CancellationToken ct = default)
///   ApiClient(HttpClient httpClient, SesionService sesionService) — BaseAddress desde HttpClient
///     (configurado con Api:BaseUrl en Program.cs). Deserializa con PropertyNameCaseInsensitive=true
///     (la API produce camelCase vía MVC y PascalCase vía ExceptionMiddleware).
///   ApiClientException(int statusCode, string message, IReadOnlyList&lt;string&gt;? errors = null)
///     en PE_GOL.Aplicacion.Exceptions (StatusCode + Errors).
///   UnauthorizedException: la EXISTENTE de PE_GOL.Utility.Exceptions (401).
///   SesionService: GuardarSesion / ActualizarTokens / CerrarSesion / ObtenerAccessToken /
///     ObtenerRefreshToken / ObtenerUsuario (ver SesionServiceTests).
/// </summary>
public class ApiClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static UsuarioResponse CrearUsuario()
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Nombre = "Super Admin",
            Correo = "superadmin@pegol.app",
            Rol = "SuperAdmin",
            AreaId = null,
            Estado = "Activo"
        };

    private static SesionService CrearSesionService()
    {
        var httpContext = new DefaultHttpContext { Session = new FakeSession() };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return new SesionService(accessor.Object);
    }

    private static (ApiClient client, TestHttpMessageHandler handler, SesionService sesion) CrearCliente()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var sesion = CrearSesionService();
        var client = new ApiClient(httpClient, sesion);
        return (client, handler, sesion);
    }

    private static HttpResponseMessage RespuestaJson<T>(HttpStatusCode status, ApiResponse<T> body)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

    // ─── 1. GetAsync_ConSesionActiva_AdjuntaBearerToken ─────────────────────

    [Fact]
    public async Task GetAsync_ConSesionActiva_AdjuntaBearerToken()
    {
        // Arrange
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-abc", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.OK, new ApiResponse<string> { Success = true, Data = "ok" }));

        // Act
        var resultado = await client.GetAsync<string>("/api/v1/tenants");

        // Assert: header Authorization: Bearer {token} en el request enviado
        Assert.Equal("ok", resultado);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token-abc", request.Headers.Authorization?.Parameter);
    }

    // ─── 2. GetAsync_RespuestaExitosa_RetornaDataDesenvuelta ────────────────

    [Fact]
    public async Task GetAsync_RespuestaExitosa_RetornaDataDesenvuelta()
    {
        // Arrange: ApiResponse<T>.Success=true → el cliente desenvuelve y retorna Data
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-abc", "refresh-1", CrearUsuario());
        var tenant = new TenantResponse
        {
            Id = Guid.NewGuid(),
            Nombre = "Acme Corp",
            PlanId = Guid.NewGuid(),
            PlanNombre = "Básico",
            ZonaHoraria = "America/Managua",
            Estado = "Activo",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.OK, new ApiResponse<TenantResponse> { Success = true, Data = tenant }));

        // Act
        var resultado = await client.GetAsync<TenantResponse>("/api/v1/tenants/1");

        // Assert
        Assert.Equal(tenant.Id, resultado.Id);
        Assert.Equal(tenant.Nombre, resultado.Nombre);
        Assert.Equal(tenant.PlanNombre, resultado.PlanNombre);
    }

    // ─── 3. GetAsync_ApiResponseConErrores_LanzaApiClientException ──────────

    [Fact]
    public async Task GetAsync_ApiResponseConErrores_LanzaApiClientException()
    {
        // Arrange: Success=false → ApiClientException con StatusCode y Errors del servidor
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-abc", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.UnprocessableEntity, new ApiResponse<string>
        {
            Success = false,
            Message = "Validación fallida",
            Errors = ["El nombre ya existe en la plataforma"]
        }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiClientException>(() => client.GetAsync<string>("/api/v1/tenants"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("El nombre ya existe en la plataforma", ex.Errors);
    }

    // ─── 4. GetAsync_401ConRefreshValido_RenuevaYReintentaUnaVez ────────────

    [Fact]
    public async Task GetAsync_401ConRefreshValido_RenuevaYReintentaUnaVez()
    {
        // Arrange: 401 → POST /api/v1/auth/refresh con el refresh token almacenado →
        // sesión actualizada → reintento ÚNICO del request original → éxito
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-viejo", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.Unauthorized, new ApiResponse<string>
        {
            Success = false,
            Message = "No autorizado",
            Errors = ["No autorizado"]
        }));
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.OK, new ApiResponse<RefreshResponse>
        {
            Success = true,
            Data = new RefreshResponse
            {
                AccessToken = "token-nuevo",
                RefreshToken = "refresh-2",
                ExpiraEn = DateTimeOffset.UtcNow.AddMinutes(60),
                RefreshExpiraEn = DateTimeOffset.UtcNow.AddDays(7),
                RequiereCambioPwd = false
            }
        }));
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.OK, new ApiResponse<string> { Success = true, Data = "ok" }));

        // Act
        var resultado = await client.GetAsync<string>("/api/v1/tenants");

        // Assert: 3 requests (original 401 → refresh → reintento) y sesión con tokens nuevos
        Assert.Equal("ok", resultado);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/api/v1/auth/refresh", handler.Requests[1].RequestUri?.PathAndQuery);

        // El body del refresh lleva el refresh token almacenado (rotación D1 HU-004)
        var refreshBody = await handler.Requests[1].Content!.ReadAsStringAsync();
        var refreshReq = JsonSerializer.Deserialize<RefreshRequest>(refreshBody, JsonOptions);
        Assert.Equal("refresh-1", refreshReq?.RefreshToken);

        // Sesión actualizada y reintento con el token NUEVO
        Assert.Equal("token-nuevo", sesion.ObtenerAccessToken());
        Assert.Equal("refresh-2", sesion.ObtenerRefreshToken());
        Assert.Equal("Bearer", handler.Requests[2].Headers.Authorization?.Scheme);
        Assert.Equal("token-nuevo", handler.Requests[2].Headers.Authorization?.Parameter);
    }

    // ─── 5. GetAsync_401ConRefreshInvalido_LimpiaSesionYLanzaUnauthorized ───

    [Fact]
    public async Task GetAsync_401ConRefreshInvalido_LimpiaSesionYLanzaUnauthorized()
    {
        // Arrange: el refresh también responde 401 → limpiar sesión + UnauthorizedException
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-viejo", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.Unauthorized, new ApiResponse<string>
        {
            Success = false,
            Message = "No autorizado",
            Errors = ["No autorizado"]
        }));
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.Unauthorized, new ApiResponse<RefreshResponse>
        {
            Success = false,
            Message = "Refresh token inválido o revocado",
            Errors = ["Refresh token inválido o revocado"]
        }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => client.GetAsync<string>("/api/v1/tenants"));

        Assert.NotNull(ex);
        Assert.Null(sesion.ObtenerAccessToken());
        Assert.Null(sesion.ObtenerRefreshToken());
        Assert.Null(sesion.ObtenerUsuario());
    }

    // ─── 6. PostAsync_422_PropagaErroresDeValidacion ────────────────────────

    [Fact]
    public async Task PostAsync_422_PropagaErroresDeValidacion()
    {
        // Arrange: 422 con Errors del servidor → disponibles en la ApiClientException (CA #5)
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-abc", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.UnprocessableEntity, new ApiResponse<TenantResponse>
        {
            Success = false,
            Message = "Validación fallida",
            Errors = ["El nombre ya existe en la plataforma", "El plan no existe"]
        }));
        var request = new TenantCreateRequest { Nombre = "Acme Corp", PlanId = Guid.NewGuid() };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiClientException>(
            () => client.PostAsync<TenantCreateRequest, TenantResponse>("/api/v1/tenants", request));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("El nombre ya existe en la plataforma", ex.Errors);
        Assert.Contains("El plan no existe", ex.Errors);
    }

    // ─── 7. GetAsync_SinTokenEnSesion_EnviaSinBearerYFallaCon401 ───────────

    [Fact]
    public async Task GetAsync_SinTokenEnSesion_EnviaSinBearerYFallaCon401()
    {
        // Arrange: sin sesión → el request se envía SIN header Authorization (los endpoints
        // [AllowAnonymous] como login no requieren token) → la API responde 401 → el refresh
        // falla (no hay refresh token) → UnauthorizedException
        var (client, handler, sesion) = CrearCliente();
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.Unauthorized, new ApiResponse<string>
        {
            Success = false,
            Message = "No autorizado",
            Errors = ["No autorizado"]
        }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => client.GetAsync<string>("/api/v1/tenants"));

        Assert.NotNull(ex);
        Assert.Single(handler.Requests); // el request SÍ se envió (sin Bearer)
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Null(sesion.ObtenerAccessToken());
        Assert.Null(sesion.ObtenerRefreshToken());
    }

    // ─── 9. PostAsync_LoginSinSesion_NoExigeBearerYRetornaData ─────────────

    [Fact]
    public async Task PostAsync_LoginSinSesion_NoExigeBearerYRetornaData()
    {
        // Arrange: POST /api/v1/auth/login es [AllowAnonymous] → sin sesión → el request se
        // envía SIN Bearer → la API responde 200 con el LoginResponse (fix "No hay sesión activa")
        var (client, handler, _) = CrearCliente();
        var loginResponse = new LoginResponse
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiraEn = DateTimeOffset.UtcNow.AddMinutes(60),
            RefreshExpiraEn = DateTimeOffset.UtcNow.AddDays(7),
            RequiereCambioPwd = true,
            Usuario = CrearUsuario()
        };
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.OK, new ApiResponse<LoginResponse>
        {
            Success = true,
            Data = loginResponse
        }));

        // Act
        var resultado = await client.PostAsync<LoginRequest, LoginResponse>(
            "/api/v1/auth/login",
            new LoginRequest { Correo = "superadmin@pegol.app", Password = "SuperAdmin@2026" });

        // Assert: 1 request, sin Bearer, y la respuesta desenvuelta
        Assert.NotNull(resultado);
        Assert.Equal("access-1", resultado.AccessToken);
        Assert.True(resultado.RequiereCambioPwd);
        Assert.Single(handler.Requests);
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    // ─── 8. GetAsync_500_RetornaErrorGenerico ───────────────────────────────

    [Fact]
    public async Task GetAsync_500_RetornaErrorGenerico()
    {
        // Arrange: 500 → mensaje genérico "Error interno del servidor" SIN filtrar detalles
        var (client, handler, sesion) = CrearCliente();
        sesion.GuardarSesion("token-abc", "refresh-1", CrearUsuario());
        handler.AgregarRespuesta(RespuestaJson(HttpStatusCode.InternalServerError, new ApiResponse<string>
        {
            Success = false,
            Message = "NullReferenceException: detalle interno de conexión a BD",
            Errors = ["NullReferenceException: detalle interno de conexión a BD"]
        }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ApiClientException>(() => client.GetAsync<string>("/api/v1/tenants"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("Error interno del servidor", ex.Message);
        Assert.DoesNotContain("NullReferenceException", ex.Message); // no filtra detalles internos
    }
}
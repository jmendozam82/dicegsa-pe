using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para AuthService — Spec HU-004 § "Tests requeridos" (41 casos, tabla #1..#41).
/// TDD fase red (TEST-01): el stub AuthService lanza NotImplementedException en TODOS los métodos,
/// por lo que los 41 tests fallan deliberadamente EN RUNTIME hasta que @BackendDev implemente la
/// lógica en fase 4 (spec § Lógica BLL + § Queries DAL A1-A10 + decisiones D1..D17).
/// Moq sobre IAuthRepository + JwtOptions (TEST-03). Patrón Arrange/Act/Assert + nombre
/// [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-05). Nombres EXACTOS de la tabla del spec.
/// Ctor de AuthService: (IAuthRepository, JwtOptions) + overload con ILogger (spec § Lógica BLL).
/// Los casos #36-38 ejercitan JwtTokenHelper (Utility/Security, ADR-004); #39-40 el stub de
/// TenantMiddleware (PE_GOL.Tests.Stubs — el real vive en PE-GOL.API, no referenciado por tests);
/// #41 verifica el wiring D6 (TenantContext.UserId → log_auditoria.usuario_id en UsuarioService).
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<IAuthRepository> _mockRepo;
    private readonly JwtOptions _jwtOptions;
    private readonly IAuthService _service;

    public AuthServiceTests()
    {
        _mockRepo = new Mock<IAuthRepository>();
        _jwtOptions = new JwtOptions(); // AccessTokenMinutes=60 · RefreshTokenDays=7 (appsettings L11-17)
        _service = new AuthService(_mockRepo.Object, _jwtOptions);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string CorreoUnico(string prefijo = "auth")
        => $"{prefijo}{Guid.NewGuid():N}@empresa.com";

    /// <summary>Hash BCrypt REAL cost 12 (SEC-02) para los flujos que verifican contraseña.</summary>
    private static string HashDePrueba(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    /// <summary>Serializa el coste de un hash BCrypt ("$2a$12$..." → 12). Devuelve -1 si no es válido.</summary>
    private static int CosteBcrypt(string hash)
    {
        var partes = hash.Split('$');
        return partes.Length >= 3 && int.TryParse(partes[2], out var coste) ? coste : -1;
    }

    /// <summary>UsuarioAuthDto (DAL-A1: con hash + tenant_estado). TenantEstado null ⇔ SuperAdmin (D15).</summary>
    private static UsuarioAuthDto CrearUsuarioAuth(
        Guid? id = null, Guid? tenantId = null, string? correo = null, string? passwordHash = null,
        string? rol = "AdminTenant", Guid? areaId = null, string estado = "Activo",
        int intentosFallidos = 0, DateTimeOffset? bloqueadoHasta = null,
        bool requiereCambioPwd = false, string? tenantEstado = "Activo")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? (rol == "SuperAdmin" ? null : Guid.NewGuid()),
            Nombre = "Ana Pérez",
            Correo = correo ?? CorreoUnico("auth"),
            PasswordHash = passwordHash ?? HashDePrueba("Password#123"),
            Rol = rol ?? "AdminTenant",
            AreaId = areaId,
            Estado = estado,
            IntentosFallidos = intentosFallidos,
            BloqueadoHasta = bloqueadoHasta,
            RequiereCambioPwd = requiereCambioPwd,
            UltimoLogin = null,
            TenantEstado = tenantEstado
        };

    /// <summary>UsuarioEntity (DAL-A4/A1b). PasswordHash vacío por defecto (DAL-A4 no trae hash, SEC-02).</summary>
    private static UsuarioEntity CrearUsuario(
        Guid? id = null, Guid? tenantId = null, string? rol = "AdminTenant",
        Guid? areaId = null, string estado = "Activo", bool requiereCambioPwd = false,
        string? passwordHash = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            Nombre = "Ana Pérez",
            Correo = CorreoUnico("usuario"),
            PasswordHash = passwordHash ?? string.Empty,
            Rol = rol ?? "AdminTenant",
            AreaId = areaId,
            Estado = estado,
            RequiereCambioPwd = requiereCambioPwd,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static RefreshTokenEntity CrearRefreshToken(
        Guid? id = null, Guid? usuarioId = null, string? tokenHash = null,
        DateTimeOffset? expiraEn = null, bool revocado = false)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            UsuarioId = usuarioId ?? Guid.NewGuid(),
            Token = tokenHash ?? Guid.NewGuid().ToString("N"), // hash simulado (D2)
            ExpiraEn = expiraEn ?? DateTimeOffset.UtcNow.AddDays(7),
            Revocado = revocado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

    private static LoginRequest CrearLoginRequest(string? correo = null, string? password = "Password#123")
        => new() { Correo = correo ?? CorreoUnico("login"), Password = password ?? "Password#123" };

    /// <summary>Flujo feliz de login: DAL-A1 devuelve el usuario, DAL-A8/DAL-A2/DAL-A10 responden Ok.</summary>
    private static void ConfigurarLoginExitoso(Mock<IAuthRepository> repo, UsuarioAuthDto usuario)
    {
        repo.Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        repo.Setup(r => r.RegistrarLoginExitosoAsync(usuario.Id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        repo.Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Flujo feliz de refresh: token válido + usuario Activo + tx para la rotación (D1).</summary>
    private static void ConfigurarRefreshFeliz(Mock<IAuthRepository> repo, RefreshTokenEntity token, UsuarioEntity usuario)
    {
        repo.Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        repo.Setup(r => r.ObtenerUsuarioPorIdAsync(token.UsuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        repo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        repo.Setup(r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    /// <summary>Flujo feliz de cambio de contraseña: usuario con hash + tx (DAL-A9 + DAL-A6 + auditoría).</summary>
    private static void ConfigurarCambioContrasenaFeliz(Mock<IAuthRepository> repo, UsuarioEntity usuario)
    {
        repo.Setup(r => r.ObtenerUsuarioPorIdConHashAsync(usuario.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        repo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        repo.Setup(r => r.CambiarContrasenaAsync(usuario.Id, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.RevocarTodosLosRefreshTokensAsync(usuario.Id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        repo.Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Decodifica el payload de un JWT (segmento central Base64Url) a claims planos.</summary>
    private static Dictionary<string, string> DecodificarClaimsJwt(string token)
    {
        var partes = token.Split('.');
        Assert.True(partes.Length == 3, "El JWT debe tener 3 segmentos (header.payload.signature).");
        var payload = partes[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>JWT HS256 firmado a mano con la misma Key que usará el middleware (JwtOptions).</summary>
    private static string CrearJwtValido(JwtOptions options, Guid userId, Guid? tenantId, string rol, Guid? areaId)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var claims = new Dictionary<string, object>
        {
            ["user_id"] = userId.ToString(),
            ["rol"] = rol,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds()
        };
        if (tenantId.HasValue) claims["tenant_id"] = tenantId.Value.ToString();
        if (areaId.HasValue) claims["area_id"] = areaId.Value.ToString();

        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Key));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    // ─── Caso 1-16. LoginAsync ──────────────────────────────────────────────

    // Caso 1 ─ correo inexistente → 401 + auditoría LOGIN fallido con usuario_id=null; no se genera token
    [Fact]
    public async Task Login_CorreoInexistente_Retorna401YAuditaFallo()
    {
        // Arrange: DAL-A1 null (correo no registrado)
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioAuthDto?)null);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401 "Credenciales inválidas" (D16: dummy BCrypt para anti-enumeración)
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest()));

        Assert.Contains("credenciales", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: LOGIN fallido con usuario_id=null y motivo (D3/ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "LOGIN" &&
                    l.Entidad == "Auth" &&
                    l.UsuarioId == null &&
                    l.TenantId == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("credenciales_invalidas")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: no se genera token (DAL-A2 nunca se invoca)
        _mockRepo.Verify(
            r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ usuario Inactivo → 401 sin verificar contraseña (BCrypt.Verify nunca se invoca)
    [Fact]
    public async Task Login_UsuarioInactivo_Retorna401()
    {
        // Arrange: estado Inactivo (SEC-03/D8)
        var usuario = CrearUsuarioAuth(estado: "Inactivo");
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401 con mensaje específico
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo)));

        Assert.Contains("inactivo", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: BCrypt.Verify nunca se invoca → ni intentos (DAL-A7) ni tokens (DAL-A2)
        _mockRepo.Verify(
            r => r.RegistrarIntentoFallidoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ usuario Bloqueado con bloqueo vigente → 401 "bloqueada" sin verificación
    [Fact]
    public async Task Login_UsuarioBloqueado_Retorna401ConMensaje()
    {
        // Arrange: Bloqueado + bloqueado_hasta en el futuro (10 min)
        var usuario = CrearUsuarioAuth(estado: "Bloqueado", bloqueadoHasta: DateTimeOffset.UtcNow.AddMinutes(10));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401 con mensaje específico de bloqueo (D8)
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo)));

        Assert.Contains("bloqueada", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: sin verificación de contraseña (ni DAL-A7 ni DAL-A8)
        _mockRepo.Verify(
            r => r.RegistrarIntentoFallidoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.RegistrarLoginExitosoAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ bloqueo expirado → desbloqueo automático (DAL-A8) y continúa la verificación
    [Fact]
    public async Task Login_BloqueoExpirado_LimpiaBloqueoYContinua()
    {
        // Arrange: Bloqueado + bloqueado_hasta en el pasado → desbloqueo automático (paso 3)
        var usuario = CrearUsuarioAuth(estado: "Bloqueado", bloqueadoHasta: DateTimeOffset.UtcNow.AddMinutes(-1));
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act: login con la contraseña correcta → éxito tras el desbloqueo
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: DAL-A8 invocado (desbloqueo) y la verificación continúa → tokens emitidos
        Assert.False(string.IsNullOrEmpty(resultado.AccessToken));
        _mockRepo.Verify(
            r => r.RegistrarLoginExitosoAsync(usuario.Id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // Caso 5 ─ password incorrecto → DAL-A7 con intentos_fallidos=+1, 401
    [Fact]
    public async Task Login_PasswordIncorrecto_IncrementaIntentos()
    {
        // Arrange: 2 intentos previos; el hash NO corresponde a la contraseña enviada
        var usuario = CrearUsuarioAuth(intentosFallidos: 2, passwordHash: HashDePrueba("Otra#Contrasena"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.RegistrarIntentoFallidoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo, password: "Password#123")));

        // Assert: DAL-A7 con intentos_fallidos = 2+1 = 3, estado Activo, sin bloqueo (D9)
        _mockRepo.Verify(
            r => r.RegistrarIntentoFallidoAsync(
                usuario.Id, 3, null, "Activo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 6 ─ 5º intento fallido → Bloqueado + bloqueado_hasta=NOW+15min + intentos=0, 401 (SEC-03)
    [Fact]
    public async Task Login_QuintoIntentoFallido_BloqueaCuenta15Min()
    {
        // Arrange: 4 intentos previos → este es el 5º → bloqueo (SEC-03/D9)
        var usuario = CrearUsuarioAuth(intentosFallidos: 4, passwordHash: HashDePrueba("Otra#Contrasena"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.RegistrarIntentoFallidoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401 con mensaje de bloqueo
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo, password: "Password#123")));

        Assert.Contains("bloqueada", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: DAL-A7 con intentos=0, estado Bloqueado y bloqueado_hasta = NOW+15min (SEC-03/D9)
        _mockRepo.Verify(
            r => r.RegistrarIntentoFallidoAsync(
                usuario.Id,
                It.Is<int>(i => i == 0),
                It.Is<DateTimeOffset?>(b => b.HasValue && Math.Abs((b.Value - DateTimeOffset.UtcNow).TotalMinutes - 15) < 2),
                It.Is<string>(e => e == "Bloqueado"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 7 ─ password correcto → 200 con tokens no vacíos y usuario con tenantId/rol/areaId
    [Fact]
    public async Task Login_PasswordCorrecto_RetornaTokensYUsuario()
    {
        // Arrange: flujo feliz
        var tenantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var usuario = CrearUsuarioAuth(tenantId: tenantId, areaId: areaId, rol: "JefeArea");
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: 200 con tokens no vacíos y usuario con tenantId/rol/areaId
        Assert.False(string.IsNullOrEmpty(resultado.AccessToken));
        Assert.False(string.IsNullOrEmpty(resultado.RefreshToken));
        Assert.Equal(tenantId, resultado.Usuario.TenantId);
        Assert.Equal("JefeArea", resultado.Usuario.Rol);
        Assert.Equal(areaId, resultado.Usuario.AreaId);
    }

    // Caso 8 ─ login exitoso → DAL-A8 (intentos=0, bloqueado_hasta=NULL, ultimo_login=NOW())
    [Fact]
    public async Task Login_PasswordCorrecto_ReseteaIntentosYActualizaUltimoLogin()
    {
        // Arrange: usuario con intentos previos (2) → el login exitoso resetea (DAL-A8)
        var usuario = CrearUsuarioAuth(intentosFallidos: 2);
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: DAL-A8 invocado con el id del usuario
        _mockRepo.Verify(
            r => r.RegistrarLoginExitosoAsync(usuario.Id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 9 ─ requiere_cambio_pwd=true → flag en la respuesta, tokens emitidos igual (D5)
    [Fact]
    public async Task Login_RequiereCambioPwd_RetornaFlagTrue()
    {
        // Arrange: requiere_cambio_pwd=true (D5: se emiten tokens + flag)
        var usuario = CrearUsuarioAuth(requiereCambioPwd: true);
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: flag true y tokens emitidos igual (D5)
        Assert.True(resultado.RequiereCambioPwd);
        Assert.False(string.IsNullOrEmpty(resultado.AccessToken));
        Assert.False(string.IsNullOrEmpty(resultado.RefreshToken));
    }

    // Caso 10 ─ DAL-A2 recibe el SHA-256 del token (64 hex), NUNCA el token en claro (D2)
    [Fact]
    public async Task Login_RefreshTokenSePersisteConHash()
    {
        // Arrange: capturar el hash que el servicio entrega al repositorio (DAL-A2)
        var usuario = CrearUsuarioAuth();
        ConfigurarLoginExitoso(_mockRepo, usuario);
        string? hashCapturado = null;
        _mockRepo
            .Setup(r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTimeOffset, IDbTransaction?, CancellationToken>((_, hash, _, _, _) => hashCapturado = hash)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: hash SHA-256 hex ≠ token en claro (D2)
        Assert.NotNull(hashCapturado);
        Assert.NotEqual(resultado.RefreshToken, hashCapturado);
        Assert.Matches("^[0-9a-f]{64}$", hashCapturado!);
    }

    // Caso 11 ─ login exitoso → auditoría LOGIN con usuario_id y valor_nuevo.resultado="exitoso" (ADR-003)
    [Fact]
    public async Task Login_Exitoso_AuditaLogin()
    {
        // Arrange
        var usuario = CrearUsuarioAuth();
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: LOGIN exitoso con usuario_id y resultado="exitoso" (D3/ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "LOGIN" &&
                    l.Entidad == "Auth" &&
                    l.UsuarioId == usuario.Id &&
                    l.TenantId == usuario.TenantId &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("exitoso")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 12 ─ login fallido → auditoría LOGIN con motivo (D3: éxito Y fallo)
    [Fact]
    public async Task Login_Fallido_AuditaLoginConMotivo()
    {
        // Arrange: password incorrecto → motivo "password_incorrecto"
        var usuario = CrearUsuarioAuth(passwordHash: HashDePrueba("Otra#Contrasena"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.RegistrarIntentoFallidoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo, password: "Password#123")));

        // Assert: LOGIN fallido con motivo en valor_nuevo (D3/ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "LOGIN" &&
                    l.Entidad == "Auth" &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("password_incorrecto")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 13 ─ SuperAdmin sin tenant → claim tenant_id AUSENTE en el JWT (D11/D17)
    [Fact]
    public async Task Login_SuperAdmin_SinTenant_OmiteClaimTenant()
    {
        // Arrange: SuperAdmin sin tenant (TenantId=null, D17) — el JWT NO lleva claim tenant_id (D11)
        var usuario = CrearUsuarioAuth(tenantId: null, rol: "SuperAdmin", tenantEstado: null);
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: el claim tenant_id está AUSENTE en el JWT (D11/D17)
        var claims = DecodificarClaimsJwt(resultado.AccessToken);
        Assert.False(claims.ContainsKey("tenant_id"), "SuperAdmin sin tenant → claim tenant_id ausente (D11/D17).");
        Assert.Equal("SuperAdmin", claims["rol"]);
    }

    // Caso 14 ─ tenant Inactivo → 401 (D15, CA HU-001: "Al desactivar un tenant, sus usuarios no pueden iniciar sesión")
    [Fact]
    public async Task Login_UsuarioDeTenantInactivo_Retorna401()
    {
        // Arrange: tenant_estado=Inactivo (DAL-A1 JOIN) → 401 (D15)
        var usuario = CrearUsuarioAuth(tenantEstado: "Inactivo");
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorCorreoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 401 con mensaje de tenant
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo)));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 15 ─ expiraEn = NOW + 60 min (config Jwt:AccessTokenMinutes, SEC-01)
    [Fact]
    public async Task Login_AccessTokenExpiraEn60Min()
    {
        // Arrange
        var usuario = CrearUsuarioAuth();
        ConfigurarLoginExitoso(_mockRepo, usuario);

        // Act
        var resultado = await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: expiraEn ≈ NOW + 60 min (SEC-01)
        var minutos = (resultado.ExpiraEn - DateTimeOffset.UtcNow).TotalMinutes;
        Assert.InRange(minutos, 58, 62);
    }

    // Caso 16 ─ expira_en del refresh = NOW + 7 días (config Jwt:RefreshTokenDays, SEC-01)
    [Fact]
    public async Task Login_RefreshTokenExpiraEn7Dias()
    {
        // Arrange: capturar expiraEn entregado a DAL-A2
        var usuario = CrearUsuarioAuth();
        ConfigurarLoginExitoso(_mockRepo, usuario);
        DateTimeOffset? expiraEnCapturado = null;
        _mockRepo
            .Setup(r => r.InsertarRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTimeOffset, IDbTransaction?, CancellationToken>((_, _, expira, _, _) => expiraEnCapturado = expira)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _service.LoginAsync(CrearLoginRequest(correo: usuario.Correo));

        // Assert: expira_en ≈ NOW + 7 días (SEC-01)
        Assert.NotNull(expiraEnCapturado);
        var dias = (expiraEnCapturado!.Value - DateTimeOffset.UtcNow).TotalDays;
        Assert.InRange(dias, 6.9, 7.1);
    }

    // ─── Caso 17-24. RefreshAsync ───────────────────────────────────────────

    // Caso 17 ─ token válido → 200 con nuevo access token y nuevo refresh token (rotación D1)
    [Fact]
    public async Task Refresh_TokenValido_RenuevaAccessToken()
    {
        // Arrange: token válido (no revocado, no expirado) + usuario Activo
        var usuarioId = Guid.NewGuid();
        var token = CrearRefreshToken(usuarioId: usuarioId);
        var usuario = CrearUsuario(usuarioId);
        ConfigurarRefreshFeliz(_mockRepo, token, usuario);

        // Act
        var resultado = await _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-presentado" });

        // Assert: 200 con nuevo access token y nuevo refresh token (D1)
        Assert.False(string.IsNullOrEmpty(resultado.AccessToken));
        Assert.False(string.IsNullOrEmpty(resultado.RefreshToken));
    }

    // Caso 18 ─ rotación: token viejo revocado (DAL-A5) + nuevo insertado (DAL-A2) en la misma tx (D1)
    [Fact]
    public async Task Refresh_TokenValido_RotaRefreshToken()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var token = CrearRefreshToken(usuarioId: usuarioId);
        var usuario = CrearUsuario(usuarioId);
        ConfigurarRefreshFeliz(_mockRepo, token, usuario);

        // Act
        var resultado = await _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-presentado" });

        // Assert: DAL-A5 (revocar viejo) + DAL-A2 (insertar nuevo) invocados (D1)
        _mockRepo.Verify(
            r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarRefreshTokenAsync(token.UsuarioId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.False(string.IsNullOrEmpty(resultado.RefreshToken));
    }

    // Caso 19 ─ token inexistente → 401 (DAL-A3 null)
    [Fact]
    public async Task Refresh_TokenInexistente_Retorna401()
    {
        // Arrange: DAL-A3 null
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act & Assert: 401 "Sesión inválida o expirada"
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-desconocido" }));
    }

    // Caso 20 ─ replay: token ya revocado → 401 + revoca TODA la familia (DAL-A6, D1)
    [Fact]
    public async Task Refresh_TokenRevocado_Retorna401YRevocaTodos()
    {
        // Arrange: replay — token ya revocado (D1)
        var usuarioId = Guid.NewGuid();
        var token = CrearRefreshToken(usuarioId: usuarioId, revocado: true);
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _mockRepo
            .Setup(r => r.RevocarTodosLosRefreshTokensAsync(usuarioId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-replay" }));

        // Assert: se revoca TODA la familia del usuario (DAL-A6, D1)
        _mockRepo.Verify(
            r => r.RevocarTodosLosRefreshTokensAsync(usuarioId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 21 ─ token expirado → 401 (expira_en <= NOW)
    [Fact]
    public async Task Refresh_TokenExpirado_Retorna401()
    {
        // Arrange: expira_en en el pasado
        var token = CrearRefreshToken(expiraEn: DateTimeOffset.UtcNow.AddMinutes(-1));
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-expirado" }));
    }

    // Caso 22 ─ usuario Inactivo → 401 (D24, defensa en profundidad)
    [Fact]
    public async Task Refresh_UsuarioInactivo_Retorna401()
    {
        // Arrange: token válido pero usuario Inactivo (D24)
        var usuarioId = Guid.NewGuid();
        var token = CrearRefreshToken(usuarioId: usuarioId);
        var usuario = CrearUsuario(usuarioId, estado: "Inactivo");
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-valido" }));
    }

    // Caso 23 ─ usuario inexistente → 401 (DAL-A4 null)
    [Fact]
    public async Task Refresh_UsuarioInexistente_Retorna401()
    {
        // Arrange: DAL-A4 null
        var token = CrearRefreshToken();
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdAsync(token.UsuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert: 401
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-valido" }));
    }

    // Caso 24 ─ requiereCambioPwd en la respuesta del refresh
    [Fact]
    public async Task Refresh_RequiereCambioPwd_RetornaFlag()
    {
        // Arrange: usuario con requiere_cambio_pwd=true
        var usuarioId = Guid.NewGuid();
        var token = CrearRefreshToken(usuarioId: usuarioId);
        var usuario = CrearUsuario(usuarioId, requiereCambioPwd: true);
        ConfigurarRefreshFeliz(_mockRepo, token, usuario);

        // Act
        var resultado = await _service.RefreshAsync(new RefreshRequest { RefreshToken = "token-presentado" });

        // Assert: flag en la respuesta
        Assert.True(resultado.RequiereCambioPwd);
    }

    // ─── Caso 25-28. LogoutAsync ────────────────────────────────────────────

    // Caso 25 ─ token válido → DAL-A5 invocado, 200 con Data null (D10)
    [Fact]
    public async Task Logout_TokenValido_RevocaToken()
    {
        // Arrange: token existe y no está revocado
        var token = CrearRefreshToken();
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.LogoutAsync(new LogoutRequest { RefreshToken = "token-activo" });

        // Assert: 200 con Data null (D10) y DAL-A5 invocado
        Assert.True(resultado);
        _mockRepo.Verify(
            r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 26 ─ token inexistente → 200 idempotente sin error ni auditoría (D10: no revelar existencia)
    [Fact]
    public async Task Logout_TokenInexistente_Retorna200Idempotente()
    {
        // Arrange: DAL-A3 null
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act: no debe lanzar
        var resultado = await _service.LogoutAsync(new LogoutRequest { RefreshToken = "token-inexistente" });

        // Assert: 200 idempotente, sin revocación ni auditoría (D10)
        Assert.True(resultado);
        _mockRepo.Verify(
            r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 27 ─ token ya revocado → 200 idempotente sin error (D10)
    [Fact]
    public async Task Logout_TokenYaRevocado_Retorna200Idempotente()
    {
        // Arrange: token ya revocado
        var token = CrearRefreshToken(revocado: true);
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act: no debe lanzar
        var resultado = await _service.LogoutAsync(new LogoutRequest { RefreshToken = "token-ya-revocado" });

        // Assert: 200 idempotente, sin re-revocar (D10)
        Assert.True(resultado);
        _mockRepo.Verify(
            r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 28 ─ logout → auditoría LOGOUT con usuario_id del token (D3/ADR-003)
    [Fact]
    public async Task Logout_AuditaLogout()
    {
        // Arrange
        var token = CrearRefreshToken();
        _mockRepo
            .Setup(r => r.ObtenerRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokenPorHashAsync(It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.LogoutAsync(new LogoutRequest { RefreshToken = "token-activo" });

        // Assert: LOGOUT con usuario_id del token (D3/ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "LOGOUT" &&
                    l.Entidad == "Auth" &&
                    l.UsuarioId == token.UsuarioId &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("logout")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 29-35. CambiarContrasenaAsync ────────────────────────────────

    // Caso 29 ─ contraseña actual incorrecta → 422 sin UPDATE (DAL-A9 nunca se invoca)
    [Fact]
    public async Task CambiarContrasena_PasswordActualIncorrecto_LanzaValidacion()
    {
        // Arrange: el hash NO corresponde a contrasenaActual
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Otra#Contrasena"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdConHashAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        // Act & Assert: 422 sin UPDATE (DAL-A9 nunca se invoca)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
            {
                ContrasenaActual = "Password#123",
                NuevaContrasena = "Nueva#Contrasena1"
            }));

        Assert.Contains("actual", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CambiarContrasenaAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 30 ─ política débil (sin mayúscula/dígito) → 422 (D4)
    [Fact]
    public async Task CambiarContrasena_PoliticaDebil_LanzaValidacion()
    {
        // Arrange: contrasenaActual correcta pero nueva sin mayúscula ni dígito (D4)
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Password#123"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdConHashAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        // Act & Assert: 422 (política: mín 8 + mayúsc + minúsc + dígito, D4)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
            {
                ContrasenaActual = "Password#123",
                NuevaContrasena = "solominusculas" // sin mayúscula ni dígito
            }));

        Assert.Contains("contraseña", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CambiarContrasenaAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 31 ─ nueva == actual → 422 (D26: no reutilizar la contraseña actual)
    [Fact]
    public async Task CambiarContrasena_NuevaIgualActual_LanzaValidacion()
    {
        // Arrange: nueva == actual (D26)
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Password#123"));
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdConHashAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        // Act & Assert: 422 (D26)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
            {
                ContrasenaActual = "Password#123",
                NuevaContrasena = "Password#123" // igual a la actual
            }));

        Assert.Contains("igual", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CambiarContrasenaAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 32 ─ éxito → DAL-A9 (limpia flag) + DAL-A6 (revoca TODOS los tokens, D14), 200
    [Fact]
    public async Task CambiarContrasena_Exitoso_LimpiaFlagYRevocaTokens()
    {
        // Arrange: flujo feliz — actual correcta, nueva válida y distinta
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Password#123"), requiereCambioPwd: true);
        ConfigurarCambioContrasenaFeliz(_mockRepo, usuario);

        // Act
        await _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
        {
            ContrasenaActual = "Password#123",
            NuevaContrasena = "Nueva#Contrasena1"
        });

        // Assert: 200 + DAL-A9 (requiere_cambio_pwd=FALSE) + DAL-A6 (revoca TODOS, D14)
        _mockRepo.Verify(
            r => r.CambiarContrasenaAsync(usuarioId, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.RevocarTodosLosRefreshTokensAsync(usuarioId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 33 ─ el nuevo hash verifica con BCrypt y cost ≥ 12 (SEC-02)
    [Fact]
    public async Task CambiarContrasena_Exitoso_HasheaConBCrypt12()
    {
        // Arrange: capturar el hash entregado a DAL-A9
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Password#123"));
        ConfigurarCambioContrasenaFeliz(_mockRepo, usuario);
        string? hashCapturado = null;
        _mockRepo
            .Setup(r => r.CambiarContrasenaAsync(usuarioId, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IDbTransaction?, CancellationToken>((_, hash, _, _) => hashCapturado = hash)
            .ReturnsAsync(1);

        // Act
        await _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
        {
            ContrasenaActual = "Password#123",
            NuevaContrasena = "Nueva#Contrasena1"
        });

        // Assert: hash BCrypt REAL que verifica la NUEVA contraseña, cost ≥ 12 (SEC-02)
        Assert.NotNull(hashCapturado);
        Assert.True(BCrypt.Net.BCrypt.Verify("Nueva#Contrasena1", hashCapturado!),
            "El hash almacenado debe verificar la NUEVA contraseña (BCrypt).");
        Assert.True(CosteBcrypt(hashCapturado!) >= 12,
            "BCrypt work factor debe ser ≥ 12 (SEC-02).");
    }

    // Caso 34 ─ auditoría UPDATE con pwd_cambiada=true y SIN hash en el JSON (ADR-003/SEC-02)
    [Fact]
    public async Task CambiarContrasena_Exitoso_AuditaUpdateSinHash()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var usuario = CrearUsuario(usuarioId, passwordHash: HashDePrueba("Password#123"), requiereCambioPwd: true);
        ConfigurarCambioContrasenaFeliz(_mockRepo, usuario);

        // Act
        await _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
        {
            ContrasenaActual = "Password#123",
            NuevaContrasena = "Nueva#Contrasena1"
        });

        // Assert: UPDATE con pwd_cambiada=true y SIN hash (ADR-003/SEC-02)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Usuario" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("requiere_cambio_pwd") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("pwd_cambiada") &&
                    !l.ValorNuevo.Contains("password", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 35 ─ usuario inexistente → 404 (DAL-A1b null, defensivo)
    [Fact]
    public async Task CambiarContrasena_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange: DAL-A1b null (defensivo: el usuario autenticado existe)
        var usuarioId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerUsuarioPorIdConHashAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CambiarContrasenaAsync(usuarioId, new CambiarContrasenaRequest
            {
                ContrasenaActual = "Password#123",
                NuevaContrasena = "Nueva#Contrasena1"
            }));
    }

    // ─── Caso 36-38. JwtTokenHelper (Utility/Security, ADR-004) ─────────────

    // Caso 36 ─ claims user_id/tenant_id/rol/area_id presentes en el access token (D11)
    [Fact]
    public void GenerarAccessToken_ContieneClaimsEsperados()
    {
        // Arrange
        var options = new JwtOptions { Key = "clave-secreta-de-prueba-min-32-caracteres!!" };
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();

        // Act
        var token = JwtTokenHelper.GenerarAccessToken(options, userId, tenantId, "JefeArea", areaId);

        // Assert: claims user_id/tenant_id/rol/area_id presentes (D11)
        var claims = DecodificarClaimsJwt(token);
        Assert.Equal(userId.ToString(), claims["user_id"]);
        Assert.Equal(tenantId.ToString(), claims["tenant_id"]);
        Assert.Equal("JefeArea", claims["rol"]);
        Assert.Equal(areaId.ToString(), claims["area_id"]);
    }

    // Caso 37 ─ dos llamadas → tokens distintos (D13: 32 bytes aleatorios → Base64Url)
    [Fact]
    public void GenerarRefreshToken_EsUnico()
    {
        // Act: dos llamadas → tokens distintos (D13)
        var t1 = JwtTokenHelper.GenerarRefreshToken();
        var t2 = JwtTokenHelper.GenerarRefreshToken();

        // Assert
        Assert.False(string.IsNullOrEmpty(t1));
        Assert.False(string.IsNullOrEmpty(t2));
        Assert.NotEqual(t1, t2);
    }

    // Caso 38 ─ hash SHA-256: 64 chars hex y ≠ token original (D2)
    [Fact]
    public void HashRefreshToken_GeneraHashSha256()
    {
        // Arrange
        const string refreshToken = "token-en-claro-de-prueba";

        // Act
        var hash = JwtTokenHelper.HashRefreshToken(refreshToken);

        // Assert: 64 chars hex y ≠ token original (D2)
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.NotEqual(refreshToken, hash);
    }

    // ─── Caso 39-40. TenantMiddleware (stub de contrato, D6/D17) ────────────

    // Caso 39 ─ con JWT válido → TenantContext poblado (TenantId/UserId/Rol/AreaId)
    [Fact]
    public async Task TenantMiddleware_ConJwtValido_PopulaTenantContext()
    {
        // Arrange: JWT HS256 firmado con la misma Key que usará el middleware (JwtOptions)
        var options = new JwtOptions { Key = "clave-secreta-de-prueba-min-32-caracteres!!" };
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var token = CrearJwtValido(options, userId, tenantId, "JefeArea", areaId);

        var tenantContext = new TenantContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {token}";

        var middleware = new Stubs.TenantMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert: claims → TenantContext (D6/D17)
        Assert.Equal(tenantId, tenantContext.TenantId);
        Assert.Equal(userId, tenantContext.UserId);
        Assert.Equal("JefeArea", tenantContext.Rol);
        Assert.Equal(areaId, tenantContext.AreaId);
    }

    // Caso 40 ─ sin JWT → TenantContext vacío (UserId=null; D17: TenantId null sin error)
    [Fact]
    public async Task TenantMiddleware_SinJwt_DejaTenantContextVacio()
    {
        // Arrange: request anónimo sin header Authorization
        var tenantContext = new TenantContext();
        var httpContext = new DefaultHttpContext();
        var middleware = new Stubs.TenantMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert: sin JWT → UserId=null (y TenantId=null, D17)
        Assert.Null(tenantContext.UserId);
        Assert.Null(tenantContext.TenantId);
    }

    // ─── Caso 41. Wiring D6 — TenantContext.UserId → log_auditoria.usuario_id ─

    // Caso 41 ─ UsuarioService.Crear con TenantContext poblado → InsertLog.UsuarioId = TenantContext.UserId (D6)
    [Fact]
    public async Task UsuarioService_Crear_ConTenantContext_RegistraUsuarioIdEnAuditoria()
    {
        // Arrange: TenantContext poblado (D6) → la auditoría de HU-003 debe registrar UsuarioId
        var tenantContext = new TenantContext { UserId = Guid.NewGuid() };
        var mockRepo = new Mock<IUsuarioRepository>();
        var mockPlan = new Mock<IPlanService>();
        mockPlan
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        var request = new UsuarioCreateRequest
        {
            Nombre = "Ana Pérez",
            Correo = CorreoUnico("wiring"),
            Password = "Temporal#123",
            Rol = "AdminTenant",
            TenantId = Guid.NewGuid(),
            AreaId = null,
            RequiereCambioPwd = true
        };
        var nuevoId = Guid.NewGuid();
        var entidad = new UsuarioEntity
        {
            Id = nuevoId,
            TenantId = request.TenantId,
            Nombre = "Ana Pérez",
            Correo = request.Correo,
            PasswordHash = "$2a$12$abcdefghijklmnopqrstuv",
            Rol = "AdminTenant",
            Estado = "Activo",
            RequiereCambioPwd = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        mockRepo.Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mockRepo.Setup(r => r.ExisteTenantAsync(request.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockRepo.Setup(r => r.ObtenerPlanIdTenantAsync(request.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        mockRepo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Mock<IDbTransaction>().Object);
        mockRepo.Setup(r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)nuevoId);
        mockRepo.Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>())).ReturnsAsync(entidad);

        var service = new UsuarioService(mockRepo.Object, mockPlan.Object, tenantContext);

        // Act
        await service.CrearAsync(request);

        // Assert: InsertLog.UsuarioId = TenantContext.UserId (D6 — wiring HU-004)
        mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l => l.UsuarioId == tenantContext.UserId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
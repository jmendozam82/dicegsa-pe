using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de autenticación (Spec HU-004 § Lógica BLL). Endpoints: login/refresh/logout/
/// cambiar-contrasena. Implementación real (fase 4 del Loop); los tests de @QA son la
/// especificación ejecutable.
/// Reglas:
///  · Login: normaliza correo (trim + LOWER, D2), valida estado/bloqueo/tenant (SEC-03/D8/D15),
///    BCrypt.Verify (SEC-02), bloqueo al 5º intento (SEC-03/D9), dummy BCrypt anti-enumeración
///    (D16), emite access token (60 min) + refresh token (7 días, hash SHA-256 D2) y audita
///    LOGIN éxito/fallo (D3/ADR-003). requiere_cambio_pwd → flag en respuesta (D5).
///  · Refresh: valida token (replay D1 → revoca familia DAL-A6), rota (DAL-A5 + DAL-A2 en una
///    tx), emite nuevo access token. NO se audita (D3).
///  · Logout: revoca el refresh token presentado (idempotente D10) y audita LOGOUT.
///  · CambiarContrasena: verifica actual, re-valida política (D4), rechaza igual a la actual
///    (D26), BCrypt 12 (SEC-02), limpia flag + revoca TODOS los tokens (D14) y audita UPDATE
///    sin hash (ADR-003). usuarioId SIEMPRE del JWT (SEC-06), nunca del body.
/// Ctor aprobado por spec: (IAuthRepository, JwtOptions) + overload con ILogger (RNF-023).
/// </summary>
public class AuthService : IAuthService
{
    private const string EntidadAuditoriaAuth = "Auth";
    private const string EntidadAuditoriaUsuario = "Usuario";
    private const int WorkFactorBcrypt = 12;        // SEC-02: coste ≥ 12
    private const int MaxIntentosFallidos = 5;      // SEC-03: bloqueo al 5º intento
    private const int MinutosBloqueo = 15;          // SEC-03: bloqueo de 15 minutos

    /// <summary>Política de contraseña (D4): mín 8, máx 128, al menos 1 mayúscula, 1 minúscula y 1 dígito.</summary>
    private static readonly Regex RegexPoliticaContrasena = new(
        @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d).{8,128}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Opciones de serialización para la auditoría (log_auditoria.valor_anterior/valor_nuevo).
    /// UnsafeRelaxedJsonEscaping: NO escapa caracteres no-ASCII → JSON legible (RNF-023/ADR-003).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// D16: hash BCrypt fijo para el Verify dummy cuando el correo NO existe — iguala el timing
    /// con un usuario existente (anti-enumeración por timing). Se calcula UNA vez por AppDomain.
    /// </summary>
    private static readonly string HashDummyBcrypt =
        BCrypt.Net.BCrypt.HashPassword("dummy", workFactor: WorkFactorBcrypt);

    private readonly IAuthRepository _repository;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public AuthService(IAuthRepository repository, JwtOptions jwtOptions)
        : this(repository, jwtOptions, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public AuthService(IAuthRepository repository, JwtOptions jwtOptions, ILogger<AuthService>? logger)
    {
        _repository = repository;
        _jwtOptions = jwtOptions;
        _logger = logger;
    }

    /// <summary>Spec §1 · LoginAsync: normaliza correo, valida estado/bloqueo/tenant, BCrypt.Verify,
    /// bloqueo al 5º intento (SEC-03), emite access token (60 min) + refresh token (7 días, hash D2)
    /// y audita LOGIN éxito/fallo (D3/ADR-003).</summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var correo = (request.Correo?.Trim() ?? string.Empty).ToLowerInvariant();

        // DAL-A1: usuario con hash + tenant_estado (SOLO uso interno de auth).
        var usuario = await _repository.ObtenerUsuarioPorCorreoAsync(correo, ct);
        if (usuario is null)
        {
            // D16: Verify dummy contra un hash fijo → mismo timing que un usuario existente.
            BCrypt.Net.BCrypt.Verify(request.Password, HashDummyBcrypt);

            await AuditarLoginFallidoAsync(correo, null, null, "credenciales_invalidas", null, ct);
            throw new UnauthorizedException("Credenciales inválidas.");
        }

        // Estado del usuario (SEC-03, D8).
        if (string.Equals(usuario.Estado, "Inactivo", StringComparison.Ordinal))
        {
            await AuditarLoginFallidoAsync(correo, usuario.Id, usuario.TenantId, "usuario_inactivo", null, ct);
            throw new UnauthorizedException("El usuario está inactivo. Contacte al administrador.");
        }

        if (string.Equals(usuario.Estado, "Bloqueado", StringComparison.Ordinal))
        {
            if (usuario.BloqueadoHasta is not null && usuario.BloqueadoHasta.Value > DateTimeOffset.UtcNow)
            {
                var minutos = Math.Max(1, (int)Math.Ceiling((usuario.BloqueadoHasta.Value - DateTimeOffset.UtcNow).TotalMinutes));
                await AuditarLoginFallidoAsync(correo, usuario.Id, usuario.TenantId, "cuenta_bloqueada", null, ct);
                throw new UnauthorizedException($"Cuenta bloqueada temporalmente. Intente nuevamente en {minutos} minutos.");
            }

            // Bloqueo expirado → desbloqueo automático (DAL-A8) y continuar la verificación.
            await _repository.RegistrarLoginExitosoAsync(usuario.Id, null, ct);
        }

        // Tenant activo (D15): tenant_estado NULL ⇔ SuperAdmin (sin tenant) → se omite.
        if (string.Equals(usuario.TenantEstado, "Inactivo", StringComparison.Ordinal))
        {
            await AuditarLoginFallidoAsync(correo, usuario.Id, usuario.TenantId, "tenant_inactivo", null, ct);
            throw new UnauthorizedException("El tenant está inactivo.");
        }

        // Verificar contraseña (SEC-02).
        if (!BCrypt.Net.BCrypt.Verify(request.Password, usuario.PasswordHash))
        {
            var nuevosIntentos = usuario.IntentosFallidos + 1;

            if (nuevosIntentos >= MaxIntentosFallidos)
            {
                // 5º intento → bloqueo (SEC-03/D9): intentos=0, estado Bloqueado, +15 min.
                var bloqueadoHasta = DateTimeOffset.UtcNow.AddMinutes(MinutosBloqueo);
                await _repository.RegistrarIntentoFallidoAsync(usuario.Id, 0, bloqueadoHasta, "Bloqueado", null, ct);
                await AuditarLoginFallidoAsync(correo, usuario.Id, usuario.TenantId, "cuenta_bloqueada", 5, ct);
                throw new UnauthorizedException("Cuenta bloqueada temporalmente. Intente nuevamente en 15 minutos.");
            }

            await _repository.RegistrarIntentoFallidoAsync(usuario.Id, nuevosIntentos, null, "Activo", null, ct);
            await AuditarLoginFallidoAsync(correo, usuario.Id, usuario.TenantId, "password_incorrecto", nuevosIntentos, ct);
            throw new UnauthorizedException("Credenciales inválidas.");
        }

        // Login exitoso: reset de intentos/bloqueo + ultimo_login (DAL-A8).
        await _repository.RegistrarLoginExitosoAsync(usuario.Id, null, ct);

        // Emisión de tokens (SEC-01): access 60 min + refresh 7 días (hash SHA-256, D2).
        var accessToken = JwtTokenHelper.GenerarAccessToken(_jwtOptions, usuario.Id, usuario.TenantId, usuario.Rol, usuario.AreaId);
        var refreshToken = JwtTokenHelper.GenerarRefreshToken();
        var refreshExpiraEn = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);
        await _repository.InsertarRefreshTokenAsync(usuario.Id, JwtTokenHelper.HashRefreshToken(refreshToken), refreshExpiraEn, null, ct);

        // Auditoría LOGIN exitoso (D3/ADR-003).
        await AuditarLoginExitosoAsync(correo, usuario.Id, usuario.TenantId, ct);

        _logger?.LogInformation(
            "Login exitoso. UsuarioId={UsuarioId}, Rol={Rol}. Modulo=Saas, Accion=LOGIN, Entidad={Entidad}",
            usuario.Id, usuario.Rol, EntidadAuditoriaAuth);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiraEn = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            RefreshExpiraEn = refreshExpiraEn,
            RequiereCambioPwd = usuario.RequiereCambioPwd, // D5: flag + tokens emitidos igual
            Usuario = MapToResponse(usuario)
        };
    }

    /// <summary>Spec §2 · RefreshAsync: valida token (replay D1 → revoca familia DAL-A6), rota
    /// (DAL-A5 + DAL-A2 en una tx), emite nuevo access token. NO se audita (D3).</summary>
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = JwtTokenHelper.HashRefreshToken(request.RefreshToken);

        // DAL-A3: token por hash (D2).
        var token = await _repository.ObtenerRefreshTokenPorHashAsync(hash, ct);
        if (token is null)
            throw new UnauthorizedException("Sesión inválida o expirada.");

        // Replay detection (D1): token ya rotado/usado → revocar TODA la familia del usuario.
        if (token.Revocado)
        {
            await _repository.RevocarTodosLosRefreshTokensAsync(token.UsuarioId, null, ct);
            throw new UnauthorizedException("Sesión inválida o expirada.");
        }

        // Expiración del refresh token.
        if (token.ExpiraEn <= DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Sesión inválida o expirada.");

        // Usuario activo (D24, defensa en profundidad).
        var usuario = await _repository.ObtenerUsuarioPorIdAsync(token.UsuarioId, ct);
        if (usuario is null || !string.Equals(usuario.Estado, "Activo", StringComparison.Ordinal))
            throw new UnauthorizedException("Sesión inválida o expirada.");

        // Rotación (D1): revocar el presentado + insertar el nuevo en UNA transacción.
        var nuevoRefresh = JwtTokenHelper.GenerarRefreshToken();
        var nuevoHash = JwtTokenHelper.HashRefreshToken(nuevoRefresh);
        var refreshExpiraEn = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.RevocarRefreshTokenPorHashAsync(hash, tx, ct);
            await _repository.InsertarRefreshTokenAsync(token.UsuarioId, nuevoHash, refreshExpiraEn, tx, ct);
            tx.Commit();
        }
        catch
        {
            // Rollback: la rotación (revocar viejo + insertar nuevo) es atómica.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en RefreshAsync. Modulo=Saas"); }
            throw;
        }

        var accessToken = JwtTokenHelper.GenerarAccessToken(_jwtOptions, usuario.Id, usuario.TenantId, usuario.Rol, usuario.AreaId);

        return new RefreshResponse
        {
            AccessToken = accessToken,
            RefreshToken = nuevoRefresh,
            ExpiraEn = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            RefreshExpiraEn = refreshExpiraEn,
            RequiereCambioPwd = usuario.RequiereCambioPwd
        };
    }

    /// <summary>Spec §3 · LogoutAsync: revoca el refresh token presentado (idempotente D10) y audita LOGOUT.
    /// Retorna true siempre (200 idempotente). ARCH-02: el wrapper ApiResponse&lt;T&gt; lo construye el controller.</summary>
    public async Task<bool> LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var hash = JwtTokenHelper.HashRefreshToken(request.RefreshToken);

        // DAL-A3: token inexistente o ya revocado → 200 idempotente (D10: no revelar existencia).
        var token = await _repository.ObtenerRefreshTokenPorHashAsync(hash, ct);
        if (token is null || token.Revocado)
        {
            return true;
        }

        await _repository.RevocarRefreshTokenPorHashAsync(hash, null, ct);

        // Auditoría LOGOUT (D3/ADR-003): tenant_id del usuario (DAL-A4; null defensivo).
        var usuario = await _repository.ObtenerUsuarioPorIdAsync(token.UsuarioId, ct);
        await _repository.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = usuario?.TenantId,
            UsuarioId = token.UsuarioId,
            Accion = "LOGOUT",
            Entidad = EntidadAuditoriaAuth,
            EntidadId = token.UsuarioId.ToString(),
            ValorAnterior = null,
            ValorNuevo = JsonSerializer.Serialize(new { resultado = "logout" }, JsonOpcionesAuditoria)
        }, null, ct);

        _logger?.LogInformation(
            "Logout. UsuarioId={UsuarioId}. Modulo=Saas, Accion=LOGOUT, Entidad={Entidad}",
            token.UsuarioId, EntidadAuditoriaAuth);

        return true;
    }

    /// <summary>Spec §4 · CambiarContrasenaAsync: verifica actual, re-valida política (D4), rechaza
    /// igual a la actual (D26), BCrypt 12 (SEC-02), limpia flag + revoca TODOS los tokens (D14) y
    /// audita UPDATE sin hash (ADR-003). usuarioId SIEMPRE del JWT (SEC-06), nunca del body.
    /// ARCH-02: sin retorno — el wrapper ApiResponse&lt;T&gt; lo construye el controller.</summary>
    public async Task CambiarContrasenaAsync(Guid usuarioId, CambiarContrasenaRequest request, CancellationToken ct = default)
    {
        // DAL-A1b: usuario con hash (defensivo: el usuario autenticado existe).
        var usuario = await _repository.ObtenerUsuarioPorIdConHashAsync(usuarioId, ct)
            ?? throw new NotFoundException($"El usuario '{usuarioId}' no existe");

        // Verificar contraseña actual.
        if (!BCrypt.Net.BCrypt.Verify(request.ContrasenaActual, usuario.PasswordHash))
            throw new ValidacionException("La contraseña actual es incorrecta");

        // Re-validar política (D4) — la BLL es la fuente de verdad (UX-04).
        if (!RegexPoliticaContrasena.IsMatch(request.NuevaContrasena))
            throw new ValidacionException("La nueva contraseña debe tener mínimo 8 caracteres e incluir una mayúscula, una minúscula y un dígito");

        // D26: no reutilizar la contraseña actual.
        if (BCrypt.Net.BCrypt.Verify(request.NuevaContrasena, usuario.PasswordHash))
            throw new ValidacionException("La nueva contraseña no puede ser igual a la actual");

        var nuevoHash = BCrypt.Net.BCrypt.HashPassword(request.NuevaContrasena, workFactor: WorkFactorBcrypt);

        // Transacción única (patrón HU-003): DAL-A9 + DAL-A6 + auditoría.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.CambiarContrasenaAsync(usuarioId, nuevoHash, tx, ct);
            await _repository.RevocarTodosLosRefreshTokensAsync(usuarioId, tx, ct);

            // Auditoría UPDATE sin hash (ADR-003/SEC-02): solo flags.
            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = usuario.TenantId,
                UsuarioId = usuarioId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoriaUsuario,
                EntidadId = usuarioId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(
                    new { requiere_cambio_pwd = usuario.RequiereCambioPwd }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(
                    new { requiere_cambio_pwd = false, pwd_cambiada = true }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();
        }
        catch
        {
            // Rollback: cambio de hash + revocación + auditoría no se persisten parcialmente.
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras error en CambiarContrasenaAsync. Modulo=Saas"); }
            throw;
        }

        _logger?.LogInformation(
            "Contraseña del usuario {UsuarioId} cambiada. Modulo=Saas, Accion=UPDATE, Entidad={Entidad}",
            usuarioId, EntidadAuditoriaUsuario);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Auditoría LOGIN fallido (D3/ADR-003): Entidad="Auth", usuario_id/tenant_id null
    /// SOLO para correo inexistente (credenciales_invalidas); motivo + intento en valor_nuevo.</summary>
    private async Task AuditarLoginFallidoAsync(string correo, Guid? usuarioId, Guid? tenantId, string motivo, int? intento, CancellationToken ct)
    {
        var valorNuevo = intento.HasValue
            ? JsonSerializer.Serialize(new { correo, resultado = "fallido", motivo, intento = intento.Value }, JsonOpcionesAuditoria)
            : JsonSerializer.Serialize(new { correo, resultado = "fallido", motivo }, JsonOpcionesAuditoria);

        await _repository.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Accion = "LOGIN",
            Entidad = EntidadAuditoriaAuth,
            EntidadId = usuarioId?.ToString(),
            ValorAnterior = null,
            ValorNuevo = valorNuevo
        }, null, ct);
    }

    /// <summary>Auditoría LOGIN exitoso (D3/ADR-003): Entidad="Auth", usuario_id/tenant_id del usuario.</summary>
    private async Task AuditarLoginExitosoAsync(string correo, Guid usuarioId, Guid? tenantId, CancellationToken ct)
    {
        await _repository.InsertLogAsync(new LogAuditoriaInsert
        {
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Accion = "LOGIN",
            Entidad = EntidadAuditoriaAuth,
            EntidadId = usuarioId.ToString(),
            ValorAnterior = null,
            ValorNuevo = JsonSerializer.Serialize(new { correo, resultado = "exitoso" }, JsonOpcionesAuditoria)
        }, null, ct);
    }

    /// <summary>Mapeo UsuarioAuthDto → UsuarioResponse (SEC-02: sin hash, sin intentos/bloqueo).
    /// CreatedAt/UpdatedAt no viajan en DAL-A1 (DTO interno de auth) → default.</summary>
    private static UsuarioResponse MapToResponse(UsuarioAuthDto u) => new()
    {
        Id = u.Id,
        TenantId = u.TenantId,
        Nombre = u.Nombre,
        Correo = u.Correo,
        Rol = u.Rol,
        AreaId = u.AreaId,
        Estado = u.Estado,
        RequiereCambioPwd = u.RequiereCambioPwd,
        UltimoLogin = u.UltimoLogin,
        CreatedAt = default,
        UpdatedAt = default
    };
}
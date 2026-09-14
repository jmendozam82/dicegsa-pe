using System.Data;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para UsuarioService — Spec HU-003 § "Tests requeridos" (30 casos).
/// TDD fase red (TEST-01): el stub UsuarioService lanza NotImplementedException en TODOS los
/// métodos, por lo que los 30 tests fallan deliberadamente EN RUNTIME hasta que @BackendDev
/// implemente la lógica en fase 4 (spec § Lógica BLL + § Queries DAL U1-U10).
/// Moq sobre IUsuarioRepository + Mock&lt;IPlanService&gt; (CA #2 HU-002: la validación de
/// límites SIEMPRE vía ValidarLimitesParaTenantAsync, nunca duplicada). El hash BCrypt se
/// verifica capturando el DTO que el servicio entrega al repositorio (nunca contra BD).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de UsuarioService: (IUsuarioRepository, IPlanService) + overload con ILogger — el
/// aprobado por el spec § Lógica BLL (idéntico al patrón PlanService HU-002).
/// </summary>
public class UsuarioServiceTests
{
    private readonly Mock<IUsuarioRepository> _mockRepo;
    private readonly Mock<IPlanService> _mockPlanService;
    private readonly IUsuarioService _service;

    public UsuarioServiceTests()
    {
        _mockRepo = new Mock<IUsuarioRepository>();
        _mockPlanService = new Mock<IPlanService>();

        // Default Ok() para la validación de límites: los tests del flujo feliz no necesitan
        // configurar el validador; los casos de límites (7, 16, 26) lo sobreescriben.
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        _service = new UsuarioService(_mockRepo.Object, _mockPlanService.Object);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string CorreoUnico(string prefijo = "usuario")
        => $"{prefijo}{Guid.NewGuid():N}@empresa.com";

    private static UsuarioEntity CrearUsuario(
        Guid? id = null, Guid? tenantId = null, string? nombre = null, string? correo = null,
        string? rol = "AdminTenant", Guid? areaId = null, string estado = "Activo",
        bool requiereCambioPwd = false, string? passwordHash = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new UsuarioEntity
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            Nombre = nombre ?? "Ana Pérez",
            Correo = correo ?? CorreoUnico("ana"),
            PasswordHash = passwordHash ?? "$2a$12$abcdefghijklmnopqrstuv",
            Rol = rol ?? "AdminTenant",
            AreaId = areaId,
            Estado = estado,
            RequiereCambioPwd = requiereCambioPwd,
            CreatedAt = createdAt ?? now.AddDays(-10),
            UpdatedAt = updatedAt ?? now
        };
    }

    private static UsuarioCreateRequest CrearCreateRequestValido(
        string? nombre = "Ana Pérez", string? correo = null, string? password = "Temporal#123",
        string? rol = "AdminTenant", Guid? tenantId = null, Guid? areaId = null)
        => new()
        {
            Nombre = nombre ?? "Ana Pérez",
            Correo = correo ?? CorreoUnico("crear"),
            Password = password ?? "Temporal#123",
            Rol = rol ?? "AdminTenant",
            TenantId = tenantId ?? Guid.NewGuid(),
            AreaId = areaId,
            RequiereCambioPwd = true
        };

    private static UsuarioUpdateRequest CrearUpdateRequestValido(
        string? nombre = "Nuevo Nombre", string? correo = null, string? rol = "AdminTenant",
        Guid? tenantId = null, Guid? areaId = null)
        => new()
        {
            Nombre = nombre ?? "Nuevo Nombre",
            Correo = correo ?? CorreoUnico("update"),
            Rol = rol ?? "AdminTenant",
            TenantId = tenantId ?? Guid.NewGuid(),
            AreaId = areaId
        };

    /// <summary>Serializa el coste de un hash BCrypt ("$2a$12$..." → 12). Devuelve -1 si no es válido.</summary>
    private static int CosteBcrypt(string hash)
    {
        var partes = hash.Split('$');
        return partes.Length >= 3 && int.TryParse(partes[2], out var coste) ? coste : -1;
    }

    private static void ConfigurarFlujoFelizCrear(
        Mock<IUsuarioRepository> repo, UsuarioCreateRequest request, Guid nuevoId, UsuarioEntity entidadCreada)
    {
        repo.Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.ExisteTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repo.Setup(r => r.PerteneceAreaAlTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repo.Setup(r => r.ObtenerPlanIdTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        repo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        repo.Setup(r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        repo.Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadCreada);
    }

    // ─── Caso 1-11. CrearAsync ──────────────────────────────────────────────

    // Caso 1 ─ correo malformado → 422; el INSERT nunca se ejecuta
    [Fact]
    public async Task Crear_CorreoInvalido_LanzaValidacion()
    {
        // Arrange: correo sin '@' ni dominio (la BLL re-valida la forma — fuente de verdad, UX-04)
        var request = CrearCreateRequestValido(correo: "correo-invalido");

        // Act & Assert: 422 y Insert nunca se llama
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("correo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ rol SuperAdmin → 422 ("solo seed"); Insert no se llama
    [Fact]
    public async Task Crear_RolSuperAdmin_LanzaValidacion()
    {
        // Arrange
        var request = CrearCreateRequestValido(rol: "SuperAdmin");

        // Act & Assert: mensaje del spec "El SuperAdmin se crea solo mediante seed"
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("seed", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ correo duplicado → 422; Insert no se llama
    [Fact]
    public async Task Crear_CorreoDuplicado_LanzaValidacion()
    {
        // Arrange
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(
                It.Is<string>(c => c.Equals(request.Correo, StringComparison.OrdinalIgnoreCase)),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("correo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ tenant inexistente → 422; Insert no se llama
    [Fact]
    public async Task Crear_TenantInexistente_LanzaValidacion()
    {
        // Arrange
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ JefeArea sin área → 422 (RN-011); Insert no se llama
    [Fact]
    public async Task Crear_JefeAreaSinArea_LanzaValidacion()
    {
        // Arrange: rol JefeArea + areaId = null
        var request = CrearCreateRequestValido(rol: "JefeArea", areaId: null);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: "El rol JefeArea requiere un área asignada"
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("requiere", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 6 ─ área de otro tenant → 422; Insert no se llama
    [Fact]
    public async Task Crear_AreaDeOtroTenant_LanzaValidacion()
    {
        // Arrange: rol JefeArea + área que no pertenece al tenant
        var areaDeOtroTenant = Guid.NewGuid();
        var request = CrearCreateRequestValido(rol: "JefeArea", areaId: areaDeOtroTenant);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.PerteneceAreaAlTenantAsync(areaDeOtroTenant, request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert: "El área seleccionada no pertenece al tenant"
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("pertenece", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ tenant al tope de max_usuarios → 422 vía IPlanService (CA #2 HU-002); Insert no se llama
    [Fact]
    public async Task Crear_TenantExcedeMaxUsuarios_LanzaValidacion()
    {
        // Arrange: consumimos el validador centralizado de HU-002 (PROHIBIDO duplicar lógica)
        var request = CrearCreateRequestValido();
        var planId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdTenantAsync(request.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(request.TenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de usuarios: 26 > 25"]));

        // Act & Assert: 422 y el INSERT no se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("usuarios", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(request.TenantId, planId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 8 ─ datos válidos → 201, UsuarioResponse con requiereCambioPwd=true y correo normalizado (D2)
    [Fact]
    public async Task Crear_DatosValidos_RetornaUsuarioCreado()
    {
        // Arrange: nombre/correo con espacios y mayúsculas → se normalizan (paso 1)
        var tenantId = Guid.NewGuid();
        var request = CrearCreateRequestValido(
            nombre: "  Ana   Pérez  ",
            correo: "  Ana.Perez@Empresa.COM  ",
            tenantId: tenantId);
        var nuevoId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);
        var entidad = CrearUsuario(nuevoId, tenantId, "Ana Pérez", "ana.perez@empresa.com",
            "AdminTenant", null, "Activo", requiereCambioPwd: true, createdAt: createdAt);

        ConfigurarFlujoFelizCrear(_mockRepo, request, nuevoId, entidad);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert: 201 con la entidad persistida; correo LOWER (D2); requiereCambioPwd=true (D5)
        Assert.NotNull(resultado);
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal("Ana Pérez", resultado.Nombre);
        Assert.Equal("ana.perez@empresa.com", resultado.Correo);
        Assert.Equal("AdminTenant", resultado.Rol);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("Activo", resultado.Estado);
        Assert.True(resultado.RequiereCambioPwd, "requiere_cambio_pwd debe ser TRUE en creación (D5)");
        Assert.Equal(createdAt, resultado.CreatedAt);

        _mockRepo.Verify(
            r => r.InsertAsync(
                It.Is<UsuarioInsertDto>(d =>
                    d.Nombre == "Ana Pérez" &&
                    d.Correo == "ana.perez@empresa.com" &&
                    d.TenantId == tenantId &&
                    d.Rol == "AdminTenant" &&
                    d.Estado == "Activo" &&
                    d.RequiereCambioPwd),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ExisteCorreoAsync("ana.perez@empresa.com", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 9 ─ la contraseña se hashea con BCrypt cost ≥ 12 (SEC-02); el hash llega al DTO de inserción
    [Fact]
    public async Task Crear_ContrasenaSeHasheaConBCrypt_WorkFactor12()
    {
        // Arrange: capturar el DTO que el servicio entrega al repositorio (nunca contra BD)
        var request = CrearCreateRequestValido(password: "Temporal#123");
        var nuevoId = Guid.NewGuid();
        var entidad = CrearUsuario(nuevoId, request.TenantId, request.Nombre, request.Correo);
        UsuarioInsertDto? dtoCapturado = null;

        ConfigurarFlujoFelizCrear(_mockRepo, request, nuevoId, entidad);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<UsuarioInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<UsuarioInsertDto, IDbTransaction?, CancellationToken>((dto, _, _) => dtoCapturado = dto)
            .ReturnsAsync((Guid?)nuevoId);

        // Act
        await _service.CrearAsync(request);

        // Assert: hash BCrypt REAL verificado y coste ≥ 12 (SEC-02).
        // NOTA (lib): con `using BCrypt.Net;` el compilador enlaza `BCrypt` al NAMESPACE padre
        // (CS0234/CS0118) — quirk documentado en el README del paquete → se usa el nombre
        // totalmente cualificado BCrypt.Net.BCrypt.* (misma regla aplicará en BLL fase 4).
        Assert.NotNull(dtoCapturado);
        Assert.True(BCrypt.Net.BCrypt.Verify(request.Password, dtoCapturado!.PasswordHash),
            "El hash almacenado debe verificar la contraseña original (BCrypt).");
        Assert.True(CosteBcrypt(dtoCapturado.PasswordHash) >= 12,
            "BCrypt work factor debe ser ≥ 12 (SEC-02).");
    }

    // Caso 10 ─ auditoría CREATE con JSON relajado (ADR-003): sin escapes Unicode, correo legible
    [Fact]
    public async Task Crear_RegistraAuditoriaCreateConJsonRelajado()
    {
        // Arrange: nombre con acento → el JSON auditado debe contener literal "María" (no \u00ED)
        var request = CrearCreateRequestValido(nombre: "María López", correo: "maria@empresa.com");
        var nuevoId = Guid.NewGuid();
        var entidad = CrearUsuario(nuevoId, request.TenantId, "María López", "maria@empresa.com");

        ConfigurarFlujoFelizCrear(_mockRepo, request, nuevoId, entidad);

        // Act
        await _service.CrearAsync(request);

        // Assert: accion=CREATE, entidad='Usuario', tenant del usuario gestionado,
        // valor_anterior=null y JSON con UnsafeRelaxedJsonEscaping (ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Usuario" &&
                    l.EntidadId == nuevoId.ToString() &&
                    l.TenantId == request.TenantId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("María") &&
                    !l.ValorNuevo.Contains("\\u")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 11 ─ el JSON de auditoría NUNCA contiene el hash ni la contraseña (SEC-02/ADR-003)
    [Fact]
    public async Task Crear_RegistraAuditoriaSinHashEnValorNuevo()
    {
        // Arrange
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var entidad = CrearUsuario(nuevoId, request.TenantId, request.Nombre, request.Correo);

        ConfigurarFlujoFelizCrear(_mockRepo, request, nuevoId, entidad);

        // Act
        await _service.CrearAsync(request);

        // Assert: el valor_nuevo se serializa con la forma del UsuarioResponse SIN password
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.ValorNuevo != null &&
                    !l.ValorNuevo.Contains("password", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 12-19. ActualizarAsync ────────────────────────────────────────

    // Caso 12 ─ usuario inexistente → 404
    [Fact]
    public async Task Actualizar_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(id, request));
    }

    // Caso 13 ─ correo duplicado de otro usuario → 422 (excluyendo self); Update no se llama
    [Fact]
    public async Task Actualizar_CorreoDuplicadoDeOtroUsuario_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var original = CrearUsuario(id, tenantId: Guid.NewGuid(), correo: "original@empresa.com");
        var request = CrearUpdateRequestValido(correo: "otra@empresa.com");

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(
                It.Is<string>(c => c.Equals("otra@empresa.com", StringComparison.OrdinalIgnoreCase)),
                id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y Update nunca se llama (DAL-U2b con excludeId = id)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("correo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 14 ─ rol no permitido → 422; Update no se llama
    [Fact]
    public async Task Actualizar_RolInvalido_LanzaValidacion()
    {
        // Arrange: rol fuera de {AdminTenant, Gerente, JefeArea}
        var id = Guid.NewGuid();
        var original = CrearUsuario(id, tenantId: Guid.NewGuid());
        var request = CrearUpdateRequestValido(rol: "Invitado", tenantId: original.TenantId);

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("rol", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 15 ─ JefeArea con área de otro tenant (y cambio de tenant) → 422; Update no se llama
    [Fact]
    public async Task Actualizar_JefeAreaAreaDeOtroTenant_LanzaValidacion()
    {
        // Arrange: el nuevo rol exige área y el área no pertenece al NUEVO tenant
        var id = Guid.NewGuid();
        var tenantDestino = Guid.NewGuid();
        var areaDeOtroTenant = Guid.NewGuid();
        var original = CrearUsuario(id, tenantId: Guid.NewGuid(), rol: "AdminTenant");
        var request = CrearUpdateRequestValido(
            rol: "JefeArea", tenantId: tenantDestino, areaId: areaDeOtroTenant);

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(tenantDestino, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.PerteneceAreaAlTenantAsync(areaDeOtroTenant, tenantDestino, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("pertenece", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 16 ─ cambio de tenant → re-valida límites contra el DESTINO (D4/D6); tope → 422; Update no se llama
    [Fact]
    public async Task Actualizar_CambioTenant_ValidaLimitesDelDestino()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantOrigen = Guid.NewGuid();
        var tenantDestino = Guid.NewGuid();
        var planIdDestino = Guid.NewGuid();
        var original = CrearUsuario(id, tenantId: tenantOrigen);
        var request = CrearUpdateRequestValido(tenantId: tenantDestino);

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(tenantDestino, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdTenantAsync(tenantDestino, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planIdDestino);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantDestino, planIdDestino, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de usuarios: 26 > 25"]));

        // Act & Assert: 422 contra el DESTINO; Update nunca se llama
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("usuarios", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(tenantDestino, planIdDestino, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 17 ─ sin cambio de tenant → el validador de límites NUNCA se invoca (D4)
    [Fact]
    public async Task Actualizar_SinCambioDeTenant_NoValidaLimites()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var original = CrearUsuario(id, tenantId: tenantId, updatedAt: now.AddHours(-1));
        var actualizado = CrearUsuario(id, tenantId: tenantId, nombre: "Nuevo Nombre", updatedAt: now);
        var request = CrearUpdateRequestValido(nombre: "Nuevo Nombre", tenantId: tenantId);

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)
            .ReturnsAsync(actualizado);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.ActualizarAsync(id, request);

        // Assert: el update sí ocurre, pero IPlanService nunca se toca
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 18 ─ update válido → 200 con usuario actualizado + auditoría UPDATE
    [Fact]
    public async Task Actualizar_DatosValidos_RetornaUsuarioActualizado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var original = CrearUsuario(id, tenantId: tenantId, updatedAt: now.AddHours(-1));
        var actualizado = CrearUsuario(id, tenantId: tenantId, nombre: "Nuevo Nombre", updatedAt: now);
        var request = CrearUpdateRequestValido(nombre: "Nuevo Nombre", tenantId: tenantId);

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)
            .ReturnsAsync(actualizado);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert
        Assert.Equal("Nuevo Nombre", resultado.Nombre);
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<UsuarioUpdateDto>(d => d.Id == id && d.Nombre == "Nuevo Nombre" && d.TenantId == tenantId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l => l.Accion == "UPDATE" && l.Entidad == "Usuario"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 19 ─ auditoría UPDATE con snapshot previo (valor_anterior) y posterior (valor_nuevo)
    [Fact]
    public async Task Actualizar_RegistraAuditoriaConValorAnterior()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var original = CrearUsuario(id, tenantId: tenantId, nombre: "Ana Pérez");
        var actualizado = CrearUsuario(id, tenantId: tenantId, nombre: "Nuevo Nombre");
        var request = CrearUpdateRequestValido(nombre: "Nuevo Nombre", tenantId: tenantId);

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)
            .ReturnsAsync(actualizado);
        _mockRepo
            .Setup(r => r.ExisteCorreoAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExisteTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<UsuarioUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.ActualizarAsync(id, request);

        // Assert: snapshot previo en valor_anterior, estado posterior en valor_nuevo
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Usuario" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Ana Pérez") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Nuevo Nombre")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 20-23. DesactivarAsync ───────────────────────────────────────

    // Caso 20 ─ usuario inexistente → 404
    [Fact]
    public async Task Desactivar_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DesactivarAsync(id));
    }

    // Caso 21 ─ ya inactivo → 422 sin auditoría duplicada
    [Fact]
    public async Task Desactivar_YaInactivo_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var inactivo = CrearUsuario(id, estado: "Inactivo");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactivo);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.DesactivarAsync(id));

        Assert.Contains("inactivo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 22 ─ desactivar revoca los refresh tokens del usuario (DAL-U9)
    [Fact]
    public async Task Desactivar_RevocaRefreshTokens()
    {
        // Arrange
        var id = Guid.NewGuid();
        var activo = CrearUsuario(id, estado: "Activo");
        var inactivo = CrearUsuario(id, estado: "Inactivo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activo)
            .ReturnsAsync(inactivo);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(id, "Inactivo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var resultado = await _service.DesactivarAsync(id);

        // Assert: DAL-U9 invocado con el id del usuario; el usuario queda Inactivo
        Assert.Equal("Inactivo", resultado.Estado);
        _mockRepo.Verify(
            r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 23 ─ desactivar retorna UsuarioResponse con estado Inactivo y audita DEACTIVATE
    [Fact]
    public async Task Desactivar_RetornaUsuarioInactivo()
    {
        // Arrange
        var id = Guid.NewGuid();
        var activo = CrearUsuario(id, estado: "Activo");
        var inactivo = CrearUsuario(id, estado: "Inactivo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activo)
            .ReturnsAsync(inactivo);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(id, "Inactivo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.DesactivarAsync(id);

        // Assert
        Assert.Equal("Inactivo", resultado.Estado);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(id, "Inactivo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l => l.Accion == "DEACTIVATE" && l.Entidad == "Usuario"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 24-27. ActivarAsync ───────────────────────────────────────────

    // Caso 24 ─ usuario inexistente → 404
    [Fact]
    public async Task Activar_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActivarAsync(id));
    }

    // Caso 25 ─ ya activo → 422
    [Fact]
    public async Task Activar_YaActivo_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var activo = CrearUsuario(id, estado: "Activo");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activo);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("activo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 26 ★ ─ activar con el tenant AL TOPE → 422 (decisión D4/D7 aprobada por Jorge)
    [Fact]
    public async Task Activar_TenantAlTopeDeUsuarios_LanzaValidacion()
    {
        // Arrange: el usuario Inactivo/Bloqueado ya ocupa plaza (D5 HU-002 cuenta TODOS);
        // la validación defensiva D7 bloquea el activar con 422 — NUNCA UpdateEstado
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var bloqueado = CrearUsuario(id, tenantId: tenantId, estado: "Bloqueado");

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bloqueado);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de usuarios: 26 > 25"]));

        // Act & Assert: 422 con el mensaje del validador reutilizado
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("usuarios", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 27 ─ dentro de límites → estado Activo + auditoría ACTIVATE
    [Fact]
    public async Task Activar_DentroDeLimites_RetornaUsuarioActivo()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var inactivo = CrearUsuario(id, tenantId: tenantId, estado: "Inactivo");
        var activo = CrearUsuario(id, tenantId: tenantId, estado: "Activo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactivo)
            .ReturnsAsync(activo);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(id, "Activo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActivarAsync(id);

        // Assert
        Assert.Equal("Activo", resultado.Estado);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(id, "Activo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l => l.Accion == "ACTIVATE" && l.Entidad == "Usuario"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 28-30. ResetearContrasenaAsync ───────────────────────────────

    // Caso 28 ─ usuario inexistente → 404
    [Fact]
    public async Task ResetearContrasena_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResetearContrasenaAsync(id));
    }

    // Caso 29 ─ reset genera un hash BCrypt cost 12 nuevo, marca requiere_cambio_pwd y revoca tokens
    [Fact]
    public async Task ResetearContrasena_GeneraNuevoHashYRequiereCambio()
    {
        // Arrange: la contraseña temporal es interna (no se devuelve por HTTP — SEC/RNF);
        // se captura el hash entregado al repositorio y se verifica su forma BCrypt (cost 12)
        var id = Guid.NewGuid();
        var original = CrearUsuario(id, requiereCambioPwd: false);
        var reseteado = CrearUsuario(id, requiereCambioPwd: true);
        string? hashCapturado = null;

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)
            .ReturnsAsync(reseteado);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.ResetContrasenaAsync(id, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IDbTransaction?, CancellationToken>((_, hash, _, _) => hashCapturado = hash)
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ResetearContrasenaAsync(id);

        // Assert: hash BCrypt nuevo (cost ≥ 12), requiere_cambio_pwd=true y tokens revocados
        Assert.True(resultado.RequiereCambioPwd, "requiere_cambio_pwd debe marcarse TRUE en reset (D5).");
        Assert.NotNull(hashCapturado);
        Assert.StartsWith("$2", hashCapturado!);
        Assert.True(CosteBcrypt(hashCapturado!) >= 12, "BCrypt work factor debe ser ≥ 12 (SEC-02).");
        _mockRepo.Verify(
            r => r.ResetContrasenaAsync(id, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 30 ─ la auditoría del reset refleja requiere_cambio_pwd=true y NUNCA expone el hash
    [Fact]
    public async Task ResetearContrasena_RegistraAuditoriaSinExponerHash()
    {
        // Arrange
        var id = Guid.NewGuid();
        var original = CrearUsuario(id, requiereCambioPwd: false);
        var reseteado = CrearUsuario(id, requiereCambioPwd: true);

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)
            .ReturnsAsync(reseteado);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.ResetContrasenaAsync(id, It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.ResetearContrasenaAsync(id);

        // Assert: accion=UPDATE con las claves del spec en valor_nuevo
        // (requiere_cambio_pwd=true, __pwd_reseteado=true) y sin hash en ningún JSON
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Usuario" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("requiere_cambio_pwd") &&
                    !l.ValorAnterior.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("requiere_cambio_pwd") &&
                    l.ValorNuevo.Contains("pwd_reseteado") &&
                    !l.ValorNuevo.Contains("password", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 31+. REV (fase 5) — ListarAsync / ObtenerPorIdAsync (U6b/U6c) ──────
    // Brecha cerrada en REVIEW: el spec § Tests requeridos no cubría GET /api/v1/usuarios
    // (listado paginado con filtros tenantId/rol/estado) ni GET /api/v1/usuarios/{id}.
    // Son tests de REGRESIÓN que fijan el comportamiento ya implementado (la fase TDD
    // roja de los flujos principales se cumplió con los 30 casos del spec).

    // Caso 31 ─ GET /api/v1/usuarios con datos → PagedResult<UsuarioResponse> mapeado (sin hash)
    [Fact]
    public async Task Listar_DatosValidos_RetornaListaPaginadaMapeada()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var u1 = CrearUsuario(tenantId: tenantId, nombre: "Ana Pérez", rol: "AdminTenant", estado: "Activo");
        var u2 = CrearUsuario(tenantId: tenantId, nombre: "Luis Gómez", rol: "Gerente", estado: "Inactivo");
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _mockRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new[] { u1, u2 });

        // Act
        var resultado = await _service.ListarAsync(1, 10, tenantId, null, null);

        // Assert: entidad → UsuarioResponse (SEC-02: el tipo NO expone PasswordHash)
        Assert.Equal(2, resultado.Items.Count);
        Assert.Equal(u1.Id, resultado.Items[0].Id);
        Assert.Equal(u1.TenantId, resultado.Items[0].TenantId);
        Assert.Equal("Ana Pérez", resultado.Items[0].Nombre);
        Assert.Equal(u1.Correo, resultado.Items[0].Correo);
        Assert.Equal("AdminTenant", resultado.Items[0].Rol);
        Assert.Equal("Activo", resultado.Items[0].Estado);
        Assert.Equal(u2.Id, resultado.Items[1].Id);
        Assert.Equal(1, resultado.Page);
        Assert.Equal(10, resultado.PageSize);
        Assert.Equal(2, resultado.Total);
        Assert.Equal(1, resultado.TotalPages);
        Assert.True(resultado.Items.All(i => i.GetType().GetProperty("PasswordHash") is null),
            "UsuarioResponse NUNCA expone password_hash (SEC-02).");
    }

    // Caso 32 ─ los filtros (tenantId/rol/estado) se propagan al COUNT (U6c) y al SELECT (U6b)
    [Fact]
    public async Task Listar_FiltrosSePropaganAlRepositorio()
    {
        // Arrange
        var tenantFiltro = Guid.NewGuid();
        const string rolFiltro = "Gerente";
        const string estadoFiltro = "Activo";
        _mockRepo
            .Setup(r => r.CountAsync(tenantFiltro, rolFiltro, estadoFiltro, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 10, tenantFiltro, rolFiltro, estadoFiltro, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new[]
            {
                CrearUsuario(tenantId: tenantFiltro, rol: rolFiltro, estado: estadoFiltro)
            });

        // Act
        var resultado = await _service.ListarAsync(1, 10, tenantFiltro, rolFiltro, estadoFiltro);

        // Assert: los filtros llegan exactos al DAL (D1: filtro explícito por tenant/rol/estado)
        Assert.Single(resultado.Items);
        Assert.Equal(tenantFiltro, resultado.Items[0].TenantId);
        _mockRepo.Verify(
            r => r.CountAsync(tenantFiltro, rolFiltro, estadoFiltro, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 10, tenantFiltro, rolFiltro, estadoFiltro, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 33 ─ page < 1 se sanea a 1 antes de llegar al repo
    [Fact]
    public async Task Listar_PaginaMenorQueUno_SeSaneaAUno()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new[] { CrearUsuario() });

        // Act: page=0 (invalida) → debe sanearse a 1
        var resultado = await _service.ListarAsync(0, 10, null, null, null);

        // Assert: el repo solo se consulta con la página saneada
        Assert.Equal(1, resultado.Page);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 10, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.GetPagedAsync(0, It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 34 ─ pageSize < 1 usa el default 10
    [Fact]
    public async Task Listar_PageSizeCero_UsaDefaultDiez()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new[] { CrearUsuario() });

        // Act: pageSize=0 (invalida) → default 10
        var resultado = await _service.ListarAsync(1, 0, null, null, null);

        // Assert
        Assert.Equal(10, resultado.PageSize);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 10, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), 0, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 35 ─ pageSize > 100 trunca a 100 (mismo contrato que HU-001/HU-002)
    [Fact]
    public async Task Listar_PageSizeMayorQueCien_TruncaACien()
    {
        // Arrange: pageSize=500 debe truncarse a 100 ANTES del repo (U6b)
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 100, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new[] { CrearUsuario() });

        // Act
        var resultado = await _service.ListarAsync(1, 500, null, null, null);

        // Assert
        Assert.Equal(100, resultado.PageSize);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 100, null, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 500, null, null, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 36 ─ total==0 → página vacía sin consultar datos (U6c evita U6b)
    [Fact]
    public async Task Listar_TotalCero_RetornaPaginaVaciaConTotalCero()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var resultado = await _service.ListarAsync(1, 10, null, null, null);

        // Assert: Items vacíos + Total 0 + TotalPages 0 (props del PagedResult conservadas)
        Assert.Empty(resultado.Items);
        Assert.Equal(0, resultado.Total);
        Assert.Equal(0, resultado.TotalPages);
        Assert.Equal(1, resultado.Page);
        Assert.Equal(10, resultado.PageSize);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 37 ─ Total/TotalPages expuestos y calculados con ceil (25 usuarios / 10 → 3 páginas)
    [Fact]
    public async Task Listar_TotalYTotalPages_CalculaCeilDePaginas()
    {
        // Arrange: Count=25 y pageSize=10 → TotalPages = ceil(25/10) = 3
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(25);
        _mockRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UsuarioEntity>)new List<UsuarioEntity>());

        // Act
        var resultado = await _service.ListarAsync(1, 10, null, null, null);

        // Assert: Total y TotalPages viajan en el PagedResult para el cliente de la API
        Assert.Equal(25, resultado.Total);
        Assert.Equal(3, resultado.TotalPages);
    }

    // Caso 38 ─ GET /api/v1/usuarios/{id} con usuario existente → UsuarioResponse sin hash
    [Fact]
    public async Task ObtenerPorId_UsuarioExistente_RetornaResponseSinHash()
    {
        // Arrange (DAL-U6: SELECT por id sin password_hash)
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var entidad = CrearUsuario(id, tenantId, "Ana Pérez", "ana.perez@empresa.com",
            rol: "Gerente", estado: "Activo", requiereCambioPwd: true);
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerPorIdAsync(id);

        // Assert: mapeo completo entidad → response; el tipo NO expone password_hash (SEC-02)
        Assert.Equal(id, resultado.Id);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("Ana Pérez", resultado.Nombre);
        Assert.Equal("ana.perez@empresa.com", resultado.Correo);
        Assert.Equal("Gerente", resultado.Rol);
        Assert.Null(resultado.AreaId);
        Assert.Equal("Activo", resultado.Estado);
        Assert.True(resultado.RequiereCambioPwd);
        Assert.Equal(entidad.CreatedAt, resultado.CreatedAt);
        Assert.Equal(entidad.UpdatedAt, resultado.UpdatedAt);
        Assert.True(resultado.GetType().GetProperty("PasswordHash") is null,
            "UsuarioResponse NUNCA expone password_hash (SEC-02).");
    }

    // Caso 39 ─ GET /api/v1/usuarios/{id} con usuario inexistente → 404
    [Fact]
    public async Task ObtenerPorId_UsuarioInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert: NotFoundException → 404 (spec § Endpoints: "404 si no existe")
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));
    }
}
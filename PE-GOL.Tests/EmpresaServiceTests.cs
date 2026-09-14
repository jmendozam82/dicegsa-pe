using System.Data;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;
using PE_GOL.Utility.Storage;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para EmpresaService — Spec HU-006 § "Tests requeridos" (18 casos de la tabla)
/// + 4 casos adicionales exigidos por @Orquestador (tenant inexistente en GET, lectura multi-rol
/// sin restricción en BLL, nombre vacío/too long, no tocar campos del SuperAdmin) = 22 casos.
/// TDD fase red (TEST-01): el stub EmpresaService lanza NotImplementedException en TODOS los
/// métodos, por lo que los 22 tests fallan deliberadamente EN RUNTIME hasta que @BackendDev
/// implemente la lógica en fase 4 (spec § Lógica BLL + § Queries DAL DAL-E1..E5 + ADR-005).
/// Moq sobre ITenantRepository + IStorageHelper (interfaz definida en el stub, ADR-005) +
/// TenantContext real (D17: TenantId nullable). Patrón Arrange/Act/Assert + nombre
/// [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de EmpresaService: (ITenantRepository, TenantContext, IStorageHelper) + overload con
/// ILogger — NOTA: el spec declara StorageHelper concreto; se usa IStorageHelper (interfaz)
/// para mockeabilidad con Moq (autorizado por @Orquestador; StorageHelper la implementa).
/// </summary>
public class EmpresaServiceTests
{
    private readonly Mock<ITenantRepository> _mockRepo;
    private readonly Mock<IStorageHelper> _mockStorage;
    private readonly TenantContext _tenantContext;
    private readonly IEmpresaService _service;

    public EmpresaServiceTests()
    {
        _mockRepo = new Mock<ITenantRepository>();
        _mockStorage = new Mock<IStorageHelper>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "AdminTenant"
        };
        _service = new EmpresaService(_mockRepo.Object, _tenantContext, _mockStorage.Object);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TenantEntity CrearEntidad(
        Guid? id = null, string nombre = "Acme Corp", string? eslogan = null,
        string? logoUrl = null, string zonaHoraria = "America/Managua",
        DateTimeOffset? updatedAt = null)
    {
        var baseUpdated = updatedAt ?? DateTimeOffset.UtcNow;
        return new TenantEntity
        {
            Id = id ?? Guid.NewGuid(),
            Nombre = nombre,
            Descripcion = "Empresa demo",
            PlanId = Guid.NewGuid(),
            PlanNombre = "Plan Pro",
            LogoUrl = logoUrl,
            Eslogan = eslogan,
            ZonaHoraria = zonaHoraria,
            Estado = "Activo",
            CreatedAt = baseUpdated.AddDays(-30),
            UpdatedAt = baseUpdated
        };
    }

    private static EmpresaUpdateRequest CrearRequestValido(
        string? nombre = "Acme Corp", string? eslogan = "Líder en logística",
        string zonaHoraria = "America/Managua")
        => new()
        {
            Nombre = nombre ?? "Acme Corp",
            Eslogan = eslogan,
            ZonaHoraria = zonaHoraria
        };

    /// <summary>Stream con firma de bytes PNG válida (89 50 4E 47 — D14) y tamaño dado.</summary>
    private static MemoryStream CrearStreamPng(long length = 1024)
    {
        var bytes = new byte[length];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        return new MemoryStream(bytes);
    }

    /// <summary>Configura el flujo feliz de ActualizarAsync (GetById x2 + unicidad + tx + DAL-E2).</summary>
    private void ConfigurarFlujoFelizActualizar(TenantEntity original, TenantEntity actualizada, string nombreNormalizado)
    {
        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)      // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(actualizada);  // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.ExisteNombreAsync(nombreNormalizado, _tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Configura el flujo feliz de SubirLogoAsync (storage + tx + DAL-E3 + re-lectura).</summary>
    private void ConfigurarFlujoFelizSubirLogo(string path, string urlFirmada, TenantEntity entidadConLogo)
    {
        _mockStorage
            .Setup(s => s.SubirLogoAsync(_tenantContext.TenantId!.Value, It.IsAny<Stream>(), ".png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(path);
        _mockStorage
            .Setup(s => s.ObtenerUrlFirmadaAsync(path, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()))
            .ReturnsAsync(urlFirmada);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.ActualizarLogoUrlAsync(_tenantContext.TenantId!.Value, path, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.GetByIdAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadConLogo);
    }

    // ─── 1-7. ObtenerAsync ──────────────────────────────────────────────────

    // Caso 1 ─ TenantContext.TenantId = null → 404; el repo NUNCA se consulta (defensivo, D17)
    [Fact]
    public async Task Obtener_SinTenantEnContexto_LanzaNoEncontrado()
    {
        // Arrange: TenantId = null (D17 — SuperAdmin sin tenant; defensivo: los roles
        // autorizados siempre tienen tenant_id; el SA queda fuera por [Authorize])
        var contexto = new TenantContext { TenantId = null, Rol = "AdminTenant", UserId = Guid.NewGuid() };
        var service = new EmpresaService(_mockRepo.Object, contexto, _mockStorage.Object);

        // Act & Assert: 404 y el repo NUNCA se consulta
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.ObtenerAsync());

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ GetByIdAsync devuelve tenant → EmpresaResponse con nombre/eslogan/zonaHoraria mapeados
    [Fact]
    public async Task Obtener_ConfiguracionExistente_RetornaEmpresaResponse()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId, "Acme Corp", "Líder en logística", logoUrl: null);
        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerAsync();

        // Assert: nombre/eslogan/zonaHoraria/descripcion/updatedAt mapeados (DAL-E1 = DAL-2 HU-001)
        Assert.NotNull(resultado);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("Acme Corp", resultado.Nombre);
        Assert.Equal("Líder en logística", resultado.Eslogan);
        Assert.Equal("America/Managua", resultado.ZonaHoraria);
        Assert.Equal(entidad.Descripcion, resultado.Descripcion);
        Assert.Equal(entidad.UpdatedAt, resultado.UpdatedAt);
    }

    // Caso 3 ─ LogoUrl presente → ObtenerUrlFirmadaAsync con expiración 24 h y el path; logoUrl = URL firmada
    [Fact]
    public async Task Obtener_ConLogo_GeneraUrlFirmada()
    {
        // Arrange: logo_url = path de storage → URL firmada 24 h (D5/ARCH-06)
        var tenantId = _tenantContext.TenantId!.Value;
        var path = $"{tenantId}/logo.png";
        var entidad = CrearEntidad(tenantId, logoUrl: path);
        const string urlFirmada = "https://supabase.example/storage/v1/object/sign/logos-tenant/xxx?token=abc";

        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockStorage
            .Setup(s => s.ObtenerUrlFirmadaAsync(path, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()))
            .ReturnsAsync(urlFirmada);

        // Act
        var resultado = await _service.ObtenerAsync();

        // Assert: ObtenerUrlFirmadaAsync invocado con el path y expiración 24 h; logoUrl = URL firmada
        Assert.Equal(urlFirmada, resultado.LogoUrl);
        _mockStorage.Verify(
            s => s.ObtenerUrlFirmadaAsync(path, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 4 ─ LogoUrl null → logoUrl = null; el storage NUNCA se invoca
    [Fact]
    public async Task Obtener_SinLogo_RetornaLogoUrlNull()
    {
        // Arrange: sin logo → sin URL firmada (D5)
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId, logoUrl: null);
        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerAsync();

        // Assert: logoUrl = null y el storage NUNCA se invoca
        Assert.Null(resultado.LogoUrl);
        _mockStorage.Verify(
            s => s.ObtenerUrlFirmadaAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ ObtenerUrlFirmadaAsync lanza → 200 con logoUrl=null (RNF-014 graceful) + Warning
    [Fact]
    public async Task Obtener_FalloStorage_RetornaLogoNullYNoFalla()
    {
        // Arrange: el storage falla → RNF-014 (graceful): la configuración NO debe fallar
        // por un problema de storage secundario; se loguea Warning y se devuelve logoUrl=null
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId, logoUrl: $"{tenantId}/logo.png");
        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockStorage
            .Setup(s => s.ObtenerUrlFirmadaAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Supabase Storage caído"));

        // Act: NO debe lanzar (RNF-014)
        var resultado = await _service.ObtenerAsync();

        // Assert: 200 con logoUrl=null y el resto de la configuración intacta
        Assert.NotNull(resultado);
        Assert.Null(resultado.LogoUrl);
        Assert.Equal("Acme Corp", resultado.Nombre);
        Assert.Equal("America/Managua", resultado.ZonaHoraria);
    }

    // Caso 6 (EXTRA @Orquestador) ─ tenant inexistente → 404 (DAL-2 null)
    [Fact]
    public async Task Obtener_TenantInexistente_LanzaNoEncontrado()
    {
        // Arrange: GetByIdAsync null → 404
        var tenantId = _tenantContext.TenantId!.Value;
        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantEntity?)null);

        // Act & Assert: 404 con el id del tenant en el mensaje
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerAsync());

        Assert.Contains(tenantId.ToString(), ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Caso 7 (EXTRA @Orquestador) ─ AdminTenant/Gerente/JefeArea leen; la BLL NO restringe por rol
    [Fact]
    public async Task Obtener_NoRestringeLecturaPorRol()
    {
        // Arrange: RN-006/RN-007 — AdminTenant/Gerente/JefeArea tienen acceso de lectura a
        // configuración; la restricción de roles es del [Authorize(Roles = "AdminTenant,Gerente,
        // JefeArea")] en el controller (D10), NO del servicio (SEC-07 no aplica: la tabla tenant
        // es la raíz del multitenancy, no una entidad de área).
        foreach (var rol in new[] { "AdminTenant", "Gerente", "JefeArea" })
        {
            var tenantId = Guid.NewGuid();
            var contexto = new TenantContext { TenantId = tenantId, Rol = rol, UserId = Guid.NewGuid() };
            var repo = new Mock<ITenantRepository>();
            repo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CrearEntidad(tenantId));
            var storage = new Mock<IStorageHelper>();
            var service = new EmpresaService(repo.Object, contexto, storage.Object);

            // Act: el servicio NO valida rol en lectura
            var resultado = await service.ObtenerAsync();

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(tenantId, resultado.TenantId);
        }
    }

    // ─── 8-16. ActualizarAsync ──────────────────────────────────────────────

    // Caso 8 ─ rol ≠ AdminTenant → AccesoDenegadoException (403); el UPDATE nunca se llama (D12)
    [Fact]
    public async Task Actualizar_RolNoAdminTenant_LanzaAccesoDenegado()
    {
        // Arrange: rol Gerente → 403 (D12: re-validación defensiva en BLL; el [Authorize]
        // del controller es la primera capa, la BLL es la fuente de verdad)
        var contexto = new TenantContext { TenantId = Guid.NewGuid(), Rol = "Gerente", UserId = Guid.NewGuid() };
        var service = new EmpresaService(_mockRepo.Object, contexto, _mockStorage.Object);

        // Act & Assert: 403 y el UPDATE nunca se llama
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => service.ActualizarAsync(CrearRequestValido()));

        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ TenantId = null → 404 (defensivo)
    [Fact]
    public async Task Actualizar_SinTenantEnContexto_LanzaNoEncontrado()
    {
        // Arrange: TenantId = null → 404 (defensivo, D17)
        var contexto = new TenantContext { TenantId = null, Rol = "AdminTenant", UserId = Guid.NewGuid() };
        var service = new EmpresaService(_mockRepo.Object, contexto, _mockStorage.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.ActualizarAsync(CrearRequestValido()));
    }

    // Caso 10 ─ nombre duplicado de OTRO tenant → 422 (D9/DAL-E5 con excludeId); Update no se llama
    [Fact]
    public async Task Actualizar_NombreDuplicadoDeOtroTenant_LanzaValidacion()
    {
        // Arrange: ExisteNombreAsync(nombre, excludeId = tenantId) true → 422
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId, "Acme Corp");
        var request = CrearRequestValido(nombre: "Otra Corp");

        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Otra Corp", tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y el UPDATE nunca se llama
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 11 ─ zona horaria IANA inválida → 422 (D7); Update no se llama
    [Fact]
    public async Task Actualizar_ZonaHorariaInvalida_LanzaValidacion()
    {
        // Arrange: zonaHoraria "Foo/Bar" no es IANA → 422 (ZonaHorariaHelper.EsValida)
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId);
        var request = CrearRequestValido(zonaHoraria: "Foo/Bar");

        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert: 422 y el UPDATE nunca se llama
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(request));

        Assert.Contains("zona horaria", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 12 ─ update OK → EmpresaResponse con los nuevos valores y updatedAt renovado
    [Fact]
    public async Task Actualizar_ConDatosValidos_RetornaConfiguracionActualizada()
    {
        // Arrange: flujo feliz — update OK + re-lectura post-commit
        var tenantId = _tenantContext.TenantId!.Value;
        var now = DateTimeOffset.UtcNow;
        var original = CrearEntidad(tenantId, "Acme Corp", "Antiguo eslogan", updatedAt: now.AddHours(-1));
        var actualizada = CrearEntidad(tenantId, "Nuevo Nombre", "Nuevo eslogan", updatedAt: now);
        var request = CrearRequestValido(nombre: "Nuevo Nombre", eslogan: "Nuevo eslogan");

        ConfigurarFlujoFelizActualizar(original, actualizada, "Nuevo Nombre");

        // Act
        var resultado = await _service.ActualizarAsync(request);

        // Assert: EmpresaResponse con los nuevos valores y updatedAt renovado (DAL-E2)
        Assert.Equal("Nuevo Nombre", resultado.Nombre);
        Assert.Equal("Nuevo eslogan", resultado.Eslogan);
        Assert.Equal("America/Managua", resultado.ZonaHoraria);
        Assert.True(resultado.UpdatedAt > original.UpdatedAt, "updatedAt debe renovarse en el UPDATE");
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(
                It.Is<TenantConfiguracionUpdateDto>(d =>
                    d.Id == tenantId &&
                    d.Nombre == "Nuevo Nombre" &&
                    d.Eslogan == "Nuevo eslogan" &&
                    d.ZonaHoraria == "America/Managua"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 13 ─ auditoría UPDATE con snapshot previo/posterior y JSON legible (ADR-003)
    [Fact]
    public async Task Actualizar_RegistraAuditoriaConValorAnterior()
    {
        // Arrange: snapshot previo en valor_anterior, estado posterior en valor_nuevo
        var tenantId = _tenantContext.TenantId!.Value;
        var original = CrearEntidad(tenantId, "Acme Corp", "Antiguo eslogan");
        var actualizada = CrearEntidad(tenantId, "Nuevo Nombre", "Nuevo eslogan");
        var request = CrearRequestValido(nombre: "Nuevo Nombre", eslogan: "Nuevo eslogan");

        ConfigurarFlujoFelizActualizar(original, actualizada, "Nuevo Nombre");

        // Act
        await _service.ActualizarAsync(request);

        // Assert: accion=UPDATE, entidad='Tenant' (D6), valor_anterior con el estado previo,
        // valor_nuevo con el posterior, JSON sin escapes Unicode (ADR-003: UnsafeRelaxedJsonEscaping)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Tenant" &&
                    l.EntidadId == tenantId.ToString() &&
                    l.TenantId == tenantId &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Acme Corp") &&
                    l.ValorAnterior.Contains("Antiguo eslogan") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Nuevo Nombre") &&
                    l.ValorNuevo.Contains("Nuevo eslogan") &&
                    !l.ValorNuevo.Contains("\\u")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 14 ─ "  Gerencia   A " → se persiste "Gerencia A" (trim + colapso espacios)
    [Fact]
    public async Task Actualizar_NombreConEspacios_SeNormaliza()
    {
        // Arrange: "  Gerencia   A " → se guarda/retorna "Gerencia A" (mismo helper que TenantService)
        var tenantId = _tenantContext.TenantId!.Value;
        var original = CrearEntidad(tenantId, "Acme Corp");
        var actualizada = CrearEntidad(tenantId, "Gerencia A");
        var request = CrearRequestValido(nombre: "  Gerencia   A ");

        ConfigurarFlujoFelizActualizar(original, actualizada, "Gerencia A");

        // Act
        var resultado = await _service.ActualizarAsync(request);

        // Assert: la unicidad (DAL-E5) y el UPDATE usan el nombre normalizado
        Assert.Equal("Gerencia A", resultado.Nombre);
        _mockRepo.Verify(
            r => r.ExisteNombreAsync("Gerencia A", tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(
                It.Is<TenantConfiguracionUpdateDto>(d => d.Nombre == "Gerencia A"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 15 (EXTRA @Orquestador) ─ nombre vacío/solo espacios o > 150 chars → 422; Update no se llama
    [Fact]
    public async Task Actualizar_NombreVacioOLargo_LanzaValidacion()
    {
        // Arrange: nombre solo espacios → 422 (NotEmpty + trim no vacío, UX-04: BLL fuente de verdad)
        var tenantId = _tenantContext.TenantId!.Value;
        var entidad = CrearEntidad(tenantId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        var requestVacio = CrearRequestValido(nombre: "   ");
        var ex1 = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(requestVacio));
        Assert.Contains("nombre", ex1.Message, StringComparison.OrdinalIgnoreCase);

        // Arrange: nombre > 150 chars → 422 (MaximumLength(150))
        var requestLargo = CrearRequestValido(nombre: new string('a', 151));
        var ex2 = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(requestLargo));
        Assert.Contains("nombre", ex2.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: el UPDATE nunca se llama
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 16 (EXTRA @Orquestador) ─ D8: descripcion/planId/estado son del SuperAdmin (HU-001);
    // el ADM solo configura los 4 campos del CA → NUNCA se invoca DAL-4 (UpdateAsync de HU-001)
    [Fact]
    public async Task Actualizar_NoTocaCamposGestionadosPorSuperAdmin()
    {
        // Arrange: flujo feliz
        var tenantId = _tenantContext.TenantId!.Value;
        var original = CrearEntidad(tenantId, "Acme Corp");
        var actualizada = CrearEntidad(tenantId, "Nuevo Nombre");
        var request = CrearRequestValido(nombre: "Nuevo Nombre");

        ConfigurarFlujoFelizActualizar(original, actualizada, "Nuevo Nombre");

        // Act
        await _service.ActualizarAsync(request);

        // Assert: solo DAL-E2 (ActualizarConfiguracionAsync); NUNCA DAL-4 de HU-001 (UpdateAsync)
        _mockRepo.Verify(
            r => r.ActualizarConfiguracionAsync(It.IsAny<TenantConfiguracionUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 17-22. SubirLogoAsync ──────────────────────────────────────────────

    // Caso 17 ─ archivo null o stream vacío → 422 (D14 paso 3a); el storage no se invoca
    [Fact]
    public async Task SubirLogo_ArchivoNulo_LanzaValidacion()
    {
        // Arrange: archivo null o length 0 → 422 "Debe seleccionar un archivo de imagen"
        // Act & Assert: stream null
        var ex1 = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.SubirLogoAsync(null!, "logo.png", "image/png", 0));
        Assert.Contains("archivo", ex1.Message, StringComparison.OrdinalIgnoreCase);

        // Act & Assert: length 0
        var ex2 = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.SubirLogoAsync(Stream.Null, "logo.png", "image/png", 0));
        Assert.Contains("archivo", ex2.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: el storage NUNCA se invoca
        _mockStorage.Verify(
            s => s.SubirLogoAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 18 ─ content-type no permitido o extensión no permitida → 422 (D14); storage no se invoca
    [Fact]
    public async Task SubirLogo_FormatoInvalido_LanzaValidacion()
    {
        // Arrange: content-type image/gif (fuera de {image/png, image/jpeg}) → 422
        var ex1 = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.SubirLogoAsync(CrearStreamPng(), "logo.gif", "image/gif", 1024));
        Assert.Contains("png", ex1.Message, StringComparison.OrdinalIgnoreCase);

        // Arrange: extensión .txt (fuera de {.png, .jpg, .jpeg}) → 422
        var ex2 = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.SubirLogoAsync(CrearStreamPng(), "logo.txt", "image/png", 1024));
        Assert.Contains("png", ex2.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: el storage NUNCA se invoca
        _mockStorage.Verify(
            s => s.SubirLogoAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 19 ─ length > 2 MB → 422 (CA #1); storage no se invoca
    [Fact]
    public async Task SubirLogo_TamanoExcede2MB_LanzaValidacion()
    {
        // Arrange: length = 2*1024*1024 + 1 → 422 "El logo no puede superar 2 MB"
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.SubirLogoAsync(CrearStreamPng(2 * 1024 * 1024 + 1), "logo.png", "image/png", 2 * 1024 * 1024 + 1));

        Assert.Contains("2 MB", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockStorage.Verify(
            s => s.SubirLogoAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 20 ─ PNG válido (firma de bytes) → SubirLogoAsync (bucket logos-tenant, ADR-005) +
    // DAL-E3 persiste el path; respuesta con URL firmada fresca
    [Fact]
    public async Task SubirLogo_Exitoso_SubeArchivoYActualizaLogoUrl()
    {
        // Arrange: PNG válido (firma de bytes 89 50 4E 47 — D14) ≤ 2 MB
        var tenantId = _tenantContext.TenantId!.Value;
        var path = $"{tenantId}/logo.png";
        const string urlFirmada = "https://supabase.example/storage/v1/object/sign/logos-tenant/xxx?token=fresh";
        using var stream = CrearStreamPng(2048);
        var entidadConLogo = CrearEntidad(tenantId, logoUrl: path);

        ConfigurarFlujoFelizSubirLogo(path, urlFirmada, entidadConLogo);

        // Act
        var resultado = await _service.SubirLogoAsync(stream, "logo.png", "image/png", 2048);

        // Assert: SubirLogoAsync invocado con tenantId + extensión ".png" (ruta {tenant_id}/logo.{ext},
        // D4/ADR-005 — el bucket logos-tenant se resuelve DENTRO de StorageHelper vía Supabase:LogoBucket,
        // NUNCA Supabase:StorageBucket); DAL-E3 persiste el path; respuesta con URL firmada fresca
        _mockStorage.Verify(
            s => s.SubirLogoAsync(tenantId, It.IsAny<Stream>(), ".png", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarLogoUrlAsync(tenantId, path, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(urlFirmada, resultado.LogoUrl);
    }

    // Caso 21 ─ auditoría UPDATE con valor_nuevo = solo { logoUrl: path }, SIN bytes (ADR-003)
    [Fact]
    public async Task SubirLogo_Exitoso_RegistraAuditoriaSinContenidoBinario()
    {
        // Arrange: flujo feliz de subida
        var tenantId = _tenantContext.TenantId!.Value;
        var path = $"{tenantId}/logo.png";
        const string urlFirmada = "https://supabase.example/storage/v1/object/sign/logos-tenant/xxx?token=fresh";
        using var stream = CrearStreamPng(2048);
        var entidadConLogo = CrearEntidad(tenantId, logoUrl: path);

        ConfigurarFlujoFelizSubirLogo(path, urlFirmada, entidadConLogo);

        // Act
        await _service.SubirLogoAsync(stream, "logo.png", "image/png", 2048);

        // Assert: accion=UPDATE, entidad='Tenant' (D6), valor_nuevo con el path de storage
        // y NUNCA el contenido binario del archivo (ADR-003: la auditoría no guarda bytes)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Tenant" &&
                    l.EntidadId == tenantId.ToString() &&
                    l.TenantId == tenantId &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains(path) &&
                    !l.ValorNuevo.Contains("base64", StringComparison.OrdinalIgnoreCase) &&
                    !l.ValorNuevo.Contains("data:image", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 22 ─ el storage lanza → InfraestructuraException (500) SIN tocar la BD (RNF-014)
    [Fact]
    public async Task SubirLogo_FalloStorage_NoActualizaLogoUrl()
    {
        // Arrange: el storage falla → 500 (InfraestructuraException); la configuración
        // existente queda intacta (el logo anterior sigue vigente — RNF-014)
        var tenantId = _tenantContext.TenantId!.Value;
        using var stream = CrearStreamPng(2048);
        _mockStorage
            .Setup(s => s.SubirLogoAsync(tenantId, It.IsAny<Stream>(), ".png", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Supabase Storage caído"));

        // Act & Assert: 500
        await Assert.ThrowsAsync<InfraestructuraException>(() =>
            _service.SubirLogoAsync(stream, "logo.png", "image/png", 2048));

        // Assert: DAL-E3 NUNCA se invoca (ni la auditoría) — la BD no se toca
        _mockRepo.Verify(
            r => r.ActualizarLogoUrlAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
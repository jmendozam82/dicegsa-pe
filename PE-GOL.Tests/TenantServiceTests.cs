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
/// Tests de contrato para TenantService — Spec HU-001 § "Tests requeridos" (20 casos)
/// + Spec HU-002 §9.1 (3 casos nuevos: validación de límites al cambiar de plan).
/// Contrato HU-002: el ctor ahora recibe IPlanService (validación centralizada D4/D3).
/// TDD fase red por contrato (TEST-01): el ctor nuevo de producción aún NO existe (@BackendDev
/// lo implementa en fase 4) → esta clase NO compila hasta entonces (rojo esperado).
/// Los 20 tests de HU-001 se ajustan SOLO en el Arrange del ctor (se pasa un Mock&lt;IPlanService&gt;);
/// asserts y nombres permanecen intactos. Patrón Arrange/Act/Assert · xUnit + Moq (TEST-03/04/05).
/// </summary>
public class TenantServiceTests
{
    private readonly Mock<ITenantRepository> _mockRepo;
    private readonly Mock<IPlanService> _mockPlanService;
    private readonly ITenantService _service;

    public TenantServiceTests()
    {
        _mockRepo = new Mock<ITenantRepository>();
        _mockPlanService = new Mock<IPlanService>();

        // HU-002 §9.1: el nuevo paso de ActualizarAsync invoca ValidarLimitesParaTenantAsync
        // SOLO cuando request.PlanId != original.PlanId. Default Ok() para que los 20 tests de
        // HU-001 que cambian de plan (p. ej. Actualizar_ConDatosValidos_RetornaTenantActualizado)
        // sigan VERDES en fase 4 SIN tocar sus cuerpos — el cambio es exclusivamente el ctor.
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        _service = new TenantService(_mockRepo.Object, _mockPlanService.Object);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TenantEntity CrearEntidad(
        Guid? id = null, string nombre = "Acme Corp", string estado = "Activo",
        Guid? planId = null, DateTimeOffset? updatedAt = null)
    {
        var baseUpdated = updatedAt ?? DateTimeOffset.UtcNow;
        return new TenantEntity
        {
            Id = id ?? Guid.NewGuid(),
            Nombre = nombre,
            Descripcion = "Empresa demo",
            PlanId = planId ?? Guid.NewGuid(),
            PlanNombre = "Plan Pro",
            LogoUrl = null,
            Eslogan = null,
            ZonaHoraria = "America/Managua",
            Estado = estado,
            CreatedAt = baseUpdated.AddDays(-30),
            UpdatedAt = baseUpdated
        };
    }

    private static TenantCreateRequest CrearRequestValido(string? nombre = "Acme Corp")
    {
        return new TenantCreateRequest
        {
            Nombre = nombre ?? "Acme Corp",
            Descripcion = "Empresa demo",
            PlanId = Guid.NewGuid(),
            ZonaHoraria = null // la BLL aplica el default 'America/Managua'
        };
    }

    private static TenantUpdateRequest CrearUpdateRequestValido(Guid planId, string? nombre = "Nuevo Nombre")
    {
        return new TenantUpdateRequest
        {
            Nombre = nombre ?? "Nuevo Nombre",
            Descripcion = "Descripción actualizada",
            PlanId = planId,
            ZonaHoraria = "America/Managua"
        };
    }

    // ─── 1-5. CrearAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_ConNombreDuplicado_LanzaValidacion()
    {
        // Arrange
        var request = CrearRequestValido();
        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<TenantInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Crear_ConPlanInexistente_LanzaValidacion()
    {
        // Arrange
        var request = CrearRequestValido();
        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(request.PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("plan", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Crear_ConDatosValidos_RetornaTenantCreado()
    {
        // Arrange
        var request = CrearRequestValido();
        var nuevoId = Guid.NewGuid();
        var entidad = CrearEntidad(nuevoId, request.Nombre, "Activo", request.PlanId);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(request.PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<TenantInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal(request.Nombre, resultado.Nombre);
        Assert.Equal(request.PlanId, resultado.PlanId);
        Assert.Equal("Plan Pro", resultado.PlanNombre);
        Assert.Equal("Activo", resultado.Estado);
        Assert.Equal("America/Managua", resultado.ZonaHoraria); // default del dominio
    }

    [Fact]
    public async Task Crear_RegistraAuditoria()
    {
        // Arrange
        var request = CrearRequestValido();
        var nuevoId = Guid.NewGuid();
        var entidad = CrearEntidad(nuevoId, request.Nombre, "Activo", request.PlanId);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(request.PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<TenantInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        await _service.CrearAsync(request);

        // Assert: accion=CREATE, valor_anterior=null, valor_nuevo serializado, entidad_id = id creado
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Tenant" &&
                    l.EntidadId == nuevoId.ToString() &&
                    l.ValorAnterior == null &&
                    !string.IsNullOrEmpty(l.ValorNuevo)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Crear_NombreConEspacios_SeNormaliza()
    {
        // Arrange: "  Gerencia   A  " → se guarda/retorna "Gerencia A" (trim + colapso espacios)
        var request = CrearRequestValido("  Gerencia   A  ");
        var nuevoId = Guid.NewGuid();
        var entidad = CrearEntidad(nuevoId, "Gerencia A", "Activo", request.PlanId);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(request.PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<TenantInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert
        Assert.Equal("Gerencia A", resultado.Nombre);
        _mockRepo.Verify(
            r => r.ExisteNombreAsync("Gerencia A", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 6-9. ActualizarAsync ───────────────────────────────────────────────

    [Fact]
    public async Task Actualizar_ConNombreDuplicadoDeOtroTenant_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidad = CrearEntidad(id, "Acme Corp", "Activo");
        var request = CrearUpdateRequestValido(entidad.PlanId, "Otra Corp");

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Otra Corp", id, It.IsAny<CancellationToken>())) // excludeId = id (excluye self)
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Actualizar_TenantInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = CrearUpdateRequestValido(Guid.NewGuid());
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(id, request));
    }

    [Fact]
    public async Task Actualizar_ConDatosValidos_RetornaTenantActualizado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var nuevoPlanId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entidadOriginal = CrearEntidad(id, "Acme Corp", "Activo", updatedAt: now.AddHours(-1));
        var entidadActualizada = CrearEntidad(id, "Nuevo Nombre", "Activo", nuevoPlanId, now);
        var request = CrearUpdateRequestValido(nuevoPlanId, "Nuevo Nombre");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadOriginal)    // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(entidadActualizada); // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Nuevo Nombre", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(nuevoPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert
        Assert.Equal("Nuevo Nombre", resultado.Nombre);
        Assert.Equal(nuevoPlanId, resultado.PlanId);
        Assert.True(resultado.UpdatedAt > entidadOriginal.UpdatedAt, "updatedAt debe renovarse en el UPDATE");
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<TenantUpdateDto>(d => d.Id == id && d.Nombre == "Nuevo Nombre" && d.PlanId == nuevoPlanId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Actualizar_RegistraAuditoriaConValorAnterior()
    {
        // Arrange
        var id = Guid.NewGuid();
        var nuevoPlanId = Guid.NewGuid();
        var entidadOriginal = CrearEntidad(id, "Acme Corp", "Activo");
        var request = CrearUpdateRequestValido(nuevoPlanId, "Nuevo Nombre");

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadOriginal);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Nuevo Nombre", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(nuevoPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.ActualizarAsync(id, request);

        // Assert: snapshot previo en valor_anterior, estado posterior en valor_nuevo
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Acme Corp") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Nuevo Nombre")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 10-12. DesactivarAsync ─────────────────────────────────────────────

    [Fact]
    public async Task Desactivar_RegistraAuditoria()
    {
        // Arrange: transición Activo → Inactivo (accion=DEACTIVATE)
        var id = Guid.NewGuid();
        var entidadActiva = CrearEntidad(id, "Acme Corp", "Activo");
        var entidadInactiva = CrearEntidad(id, "Acme Corp", "Inactivo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadActiva)
            .ReturnsAsync(entidadInactiva);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(id, "Inactivo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var resultado = await _service.DesactivarAsync(id);

        // Assert
        Assert.Equal("Inactivo", resultado.Estado);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l => l.Accion == "DEACTIVATE" && l.Entidad == "Tenant"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Desactivar_RevocaRefreshTokensDeUsuarios()
    {
        // Arrange: DAL-5 debe invocarse con el tenant id
        var id = Guid.NewGuid();
        var entidadActiva = CrearEntidad(id, "Acme Corp", "Activo");
        var entidadInactiva = CrearEntidad(id, "Acme Corp", "Inactivo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadActiva)
            .ReturnsAsync(entidadInactiva);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(id, "Inactivo", It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var resultado = await _service.DesactivarAsync(id);

        // Assert
        _mockRepo.Verify(
            r => r.RevocarRefreshTokensAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("Inactivo", resultado.Estado);
    }

    [Fact]
    public async Task Desactivar_TenantYaInactivo_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidadInactiva = CrearEntidad(id, "Acme Corp", "Inactivo");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadInactiva);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.DesactivarAsync(id));

        Assert.Contains("desactivado", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Sin auditoría duplicada
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 13-14. ActivarAsync ────────────────────────────────────────────────

    [Fact]
    public async Task Activar_TenantYaActivo_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidadActiva = CrearEntidad(id, "Acme Corp", "Activo");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadActiva);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("activo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activar_ConTenantInactivo_RetornaTenantActivo()
    {
        // Arrange: transición Inactivo → Activo (accion=ACTIVATE)
        var id = Guid.NewGuid();
        var entidadInactiva = CrearEntidad(id, "Acme Corp", "Inactivo");
        var entidadActiva = CrearEntidad(id, "Acme Corp", "Activo");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadInactiva)
            .ReturnsAsync(entidadActiva);
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
                It.Is<LogAuditoriaInsert>(l => l.Accion == "ACTIVATE"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 15-19. ListarAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task Listar_FiltroPorEstado_RetornaSoloActivos()
    {
        // Arrange
        var activos = new List<TenantEntity>
        {
            CrearEntidad(estado: "Activo"),
            CrearEntidad(estado: "Activo")
        };
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 10, "Activo", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantEntity>)activos);
        _mockRepo
            .Setup(r => r.CountAsync("Activo", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var resultado = await _service.ListarAsync(1, 10, "Activo", null);

        // Assert: el filtro se pasa al repo y solo se retornan activos
        Assert.Equal(2, resultado.Items.Count);
        Assert.All(resultado.Items, item => Assert.Equal("Activo", item.Estado));
        Assert.Equal(2, resultado.Total);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 10, "Activo", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Listar_FiltroPorPlan_RetornaSoloDelPlan()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var items = new List<TenantEntity>
        {
            CrearEntidad(planId: planId),
            CrearEntidad(planId: planId)
        };
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 10, null, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantEntity>)items);
        _mockRepo
            .Setup(r => r.CountAsync(null, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var resultado = await _service.ListarAsync(1, 10, null, planId);

        // Assert
        Assert.Equal(2, resultado.Items.Count);
        Assert.All(resultado.Items, item => Assert.Equal(planId, item.PlanId));
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 10, null, planId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Listar_SinFiltros_RetornaTodosPaginados()
    {
        // Arrange
        var items = new List<TenantEntity> { CrearEntidad(), CrearEntidad(), CrearEntidad() };
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantEntity>)items);
        _mockRepo
            .Setup(r => r.CountAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var resultado = await _service.ListarAsync(1, 10, null, null);

        // Assert: total y totalPages calculados (ceil(3/10) = 1)
        Assert.Equal(3, resultado.Items.Count);
        Assert.Equal(1, resultado.Page);
        Assert.Equal(10, resultado.PageSize);
        Assert.Equal(3, resultado.Total);
        Assert.Equal(1, resultado.TotalPages);
    }

    [Fact]
    public async Task Listar_PageSizeMayor100_TruncaA100()
    {
        // Arrange: pageSize=500 debe truncarse a 100 antes de llegar al repo
        _mockRepo
            .Setup(r => r.GetPagedAsync(1, 100, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantEntity>)new List<TenantEntity>());
        _mockRepo
            .Setup(r => r.CountAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1); // >0 para que el SELECT de datos sí se ejecute

        // Act
        var resultado = await _service.ListarAsync(1, 500, null, null);

        // Assert
        Assert.Equal(100, resultado.PageSize);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 100, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.GetPagedAsync(1, 500, null, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Listar_TotalCero_RetornaPaginaVacia()
    {
        // Arrange: total==0 → NO se ejecuta el SELECT de datos
        _mockRepo
            .Setup(r => r.CountAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var resultado = await _service.ListarAsync(1, 10, null, null);

        // Assert
        Assert.Empty(resultado.Items);
        Assert.Equal(0, resultado.Total);
        Assert.Equal(0, resultado.TotalPages);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 20. ObtenerPorIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorId_TenantInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));
    }

    // ─── 28-30. HU-002 §9.1 — Cambio de plan en ActualizarAsync (validación de límites) ──

    [Fact]
    public async Task Actualizar_CambiaPlanAOtroConLimitesExcedidos_LanzaValidacion()
    {
        // Arrange: request.PlanId != original.PlanId y el validador devuelve EsValido=false
        var id = Guid.NewGuid();
        var planViejo = Guid.NewGuid();
        var planNuevo = Guid.NewGuid();
        var entidadOriginal = CrearEntidad(id, "Acme Corp", "Activo", planViejo);
        var request = CrearUpdateRequestValido(planNuevo, "Nuevo Nombre");

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadOriginal);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Nuevo Nombre", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(planNuevo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(id, planNuevo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de áreas: 12 > 10"]));

        // Act & Assert: 422 sin llegar al UPDATE ni a la auditoría
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("áreas", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Actualizar_CambiaPlanADentroDeLimites_RetornaTenantActualizado()
    {
        // Arrange: request.PlanId != original.PlanId pero el validador devuelve Ok (default del ctor)
        var id = Guid.NewGuid();
        var planViejo = Guid.NewGuid();
        var planNuevo = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entidadOriginal = CrearEntidad(id, "Acme Corp", "Activo", planViejo, now.AddHours(-1));
        var entidadActualizada = CrearEntidad(id, "Nuevo Nombre", "Activo", planNuevo, now);
        var request = CrearUpdateRequestValido(planNuevo, "Nuevo Nombre");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadOriginal)
            .ReturnsAsync(entidadActualizada);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Nuevo Nombre", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(planNuevo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert: update + auditoría normales (el validador Ok es el default configurado en el ctor)
        Assert.Equal("Nuevo Nombre", resultado.Nombre);
        Assert.Equal(planNuevo, resultado.PlanId);
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<TenantUpdateDto>(d => d.Id == id && d.Nombre == "Nuevo Nombre" && d.PlanId == planNuevo),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Actualizar_MismoPlanNoLlamaAlValidador()
    {
        // Arrange: request.PlanId == original.PlanId → no hay cambio de plan, no se valida (HU-002 §9.1)
        var id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var entidadOriginal = CrearEntidad(id, "Acme Corp", "Activo", planId);
        var entidadActualizada = CrearEntidad(id, "Mismo Plan", "Activo", planId);
        var request = CrearUpdateRequestValido(planId, "Mismo Plan");

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadOriginal)
            .ReturnsAsync(entidadActualizada);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Mismo Plan", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ExistePlanAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<TenantUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.ActualizarAsync(id, request);

        // Assert: el validador NUNCA se invoca (compatibilidad total con HU-001)
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
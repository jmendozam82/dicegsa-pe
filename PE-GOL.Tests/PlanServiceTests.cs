using System.Data;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para PlanService — Spec HU-002 § "Tests requeridos" (casos 8-27, CRUD +
/// validación de límites). TDD fase red (TEST-01): el stub PlanService lanza NotImplementedException
/// (y el PlanLimitValidator stub también en los casos que lo alcanzan), por lo que TODOS los tests
/// fallan deliberadamente hasta que @BackendDev implemente la lógica en fase 4.
/// Moq sobre IPlanRepository + PlanLimitValidator REAL (stateless, puro). Patrón Arrange/Act/Assert
/// (TEST-03/05).
/// </summary>
public class PlanServiceTests
{
    private readonly Mock<IPlanRepository> _mockRepo;
    private readonly PlanLimitValidator _validator = new();
    private readonly IPlanService _service;

    public PlanServiceTests()
    {
        _mockRepo = new Mock<IPlanRepository>();
        _service = new PlanService(_mockRepo.Object, _validator);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static PlanEntity CrearPlan(
        Guid? id = null, string nombre = "Estándar", string? descripcion = null,
        int maxAreas = 10, int maxUsuarios = 25, int maxCiclosActivos = 1,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Nombre = nombre,
        Descripcion = descripcion,
        MaxAreas = maxAreas,
        MaxUsuarios = maxUsuarios,
        MaxCiclosActivos = maxCiclosActivos,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    private static PlanCreateRequest CrearRequestValido(
        string? nombre = "Estándar", string? descripcion = null,
        int maxAreas = 10, int maxUsuarios = 25, int maxCiclosActivos = 1) => new()
    {
        Nombre = nombre ?? "Estándar",
        Descripcion = descripcion,
        MaxAreas = maxAreas,
        MaxUsuarios = maxUsuarios,
        MaxCiclosActivos = maxCiclosActivos
    };

    private static PlanUpdateRequest CrearUpdateRequestValido(
        string? nombre = "Estándar", string? descripcion = null,
        int maxAreas = 10, int maxUsuarios = 25, int maxCiclosActivos = 1) => new()
    {
        Nombre = nombre ?? "Estándar",
        Descripcion = descripcion,
        MaxAreas = maxAreas,
        MaxUsuarios = maxUsuarios,
        MaxCiclosActivos = maxCiclosActivos
    };

    // ─── 8-12. CrearAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Crear_ConNombreDuplicado_LanzaValidacion()
    {
        // Arrange
        var request = CrearRequestValido();
        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: ValidacionException y Insert nunca se llama (DAL-P4 sin exclude)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<PlanInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Crear_ConRangoInvalido_LanzaValidacion()
    {
        // Arrange: maxAreas = 0 (fuera de 1..20) → re-validación BLL, fuente de verdad (D9)
        var request = CrearRequestValido(maxAreas: 0);

        // Act & Assert: ValidacionException antes de Insert (DAL-P1)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("maxAreas", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<PlanInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Crear_ConDatosValidos_RetornaPlanCreado()
    {
        // Arrange
        var request = CrearRequestValido();
        var nuevoId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var entidad = CrearPlan(nuevoId, request.Nombre, request.Descripcion,
            request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos, createdAt);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<PlanInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert: PlanResponse con límites y createdAt del RETURNING (re-lectura GetById post-commit)
        Assert.NotNull(resultado);
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal(request.Nombre, resultado.Nombre);
        Assert.Equal(request.MaxAreas, resultado.MaxAreas);
        Assert.Equal(request.MaxUsuarios, resultado.MaxUsuarios);
        Assert.Equal(request.MaxCiclosActivos, resultado.MaxCiclosActivos);
        Assert.Equal(createdAt, resultado.CreatedAt);
    }

    [Fact]
    public async Task Crear_RegistraAuditoriaCreate()
    {
        // Arrange
        var request = CrearRequestValido();
        var nuevoId = Guid.NewGuid();
        var entidad = CrearPlan(nuevoId, request.Nombre, request.Descripcion,
            request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(request.Nombre, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<PlanInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        await _service.CrearAsync(request);

        // Assert: accion=CREATE, entidad='Plan', valor_anterior=null, valor_nuevo serializado, entidad_id = id creado
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Plan" &&
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
        // Arrange: "  Estándar   XL  " → se guarda/retorna "Estándar XL" (trim + colapso espacios)
        var request = CrearRequestValido("  Estándar   XL  ");
        var nuevoId = Guid.NewGuid();
        var entidad = CrearPlan(nuevoId, "Estándar XL", request.Descripcion,
            request.MaxAreas, request.MaxUsuarios, request.MaxCiclosActivos);

        _mockRepo
            .Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<PlanInsertDto>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.GetByIdAsync(nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert: la unicidad (DAL-P4) y el resultado usan el nombre normalizado
        Assert.Equal("Estándar XL", resultado.Nombre);
        _mockRepo.Verify(
            r => r.ExisteNombreAsync("Estándar XL", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 13-17. ActualizarAsync ─────────────────────────────────────────────

    [Fact]
    public async Task Actualizar_PlanInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanEntity?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(id, request));
    }

    [Fact]
    public async Task Actualizar_ConNombreDuplicadoDeOtroPlan_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidad = CrearPlan(id, "Estándar", maxAreas: 10);
        var request = CrearUpdateRequestValido("Premium", maxAreas: 10); // nombre que ya usa otro plan

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Premium", id, It.IsAny<CancellationToken>())) // excludeId = id (excluye self)
            .ReturnsAsync(true);

        // Act & Assert: 422
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Actualizar_ConLimitesMenoresQueViolanTenantExistente_LanzaValidacion()
    {
        // Arrange: plan Premium (20 áreas) se baja a 10; el tenant 'Operaciones Logística' tiene 12 áreas
        var id = Guid.NewGuid();
        var planOriginal = CrearPlan(id, "Premium", maxAreas: 20, maxUsuarios: 100, maxCiclosActivos: 2);
        var request = CrearUpdateRequestValido("Premium", maxAreas: 10, maxUsuarios: 100, maxCiclosActivos: 2);
        var tenantIncumple = new TenantPlanDto { TenantId = Guid.NewGuid(), Nombre = "Operaciones Logística" };

        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planOriginal);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Premium", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ObtenerTenantsPorPlanAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantPlanDto>)new List<TenantPlanDto> { tenantIncumple });
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(tenantIncumple.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 12, UsuariosActuales: 50, CiclosActivosActuales: 1));

        // Act & Assert: 422 con mensaje del tenant infractor; Update nunca se llama
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("Operaciones Logística", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<PlanUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Actualizar_ConLimitesMenoresSinTenantsExcedidos_RetornaPlanActualizado()
    {
        // Arrange: mismo escenario de bajada de límites pero el tenant queda DENTRO de los nuevos
        var id = Guid.NewGuid();
        var planOriginal = CrearPlan(id, "Premium", maxAreas: 20, maxUsuarios: 100, maxCiclosActivos: 2);
        var planActualizado = CrearPlan(id, "Premium", maxAreas: 10, maxUsuarios: 100, maxCiclosActivos: 2);
        var request = CrearUpdateRequestValido("Premium", maxAreas: 10, maxUsuarios: 100, maxCiclosActivos: 2);
        var tenantOk = new TenantPlanDto { TenantId = Guid.NewGuid(), Nombre = "Logística Norte" };

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planOriginal)
            .ReturnsAsync(planActualizado);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Premium", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ObtenerTenantsPorPlanAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantPlanDto>)new List<TenantPlanDto> { tenantOk });
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(tenantOk.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 50, CiclosActivosActuales: 1));
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<PlanUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert: update OK con los nuevos límites
        Assert.NotNull(resultado);
        Assert.Equal("Premium", resultado.Nombre);
        Assert.Equal(10, resultado.MaxAreas);
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<PlanUpdateDto>(d => d.Id == id && d.MaxAreas == 10),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Actualizar_ConDatosValidos_RetornaPlanActualizado()
    {
        // Arrange: límites SIN cambios (nombre/descripción) → no requiere validación vs tenants
        var id = Guid.NewGuid();
        var planOriginal = CrearPlan(id, "Estándar", maxAreas: 10, maxUsuarios: 25, maxCiclosActivos: 1);
        var planActualizado = CrearPlan(id, "Estándar XL", maxAreas: 10, maxUsuarios: 25, maxCiclosActivos: 1);
        var request = CrearUpdateRequestValido("Estándar XL", maxAreas: 10, maxUsuarios: 25, maxCiclosActivos: 1);

        _mockRepo
            .SetupSequence(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planOriginal)
            .ReturnsAsync(planActualizado);
        _mockRepo
            .Setup(r => r.ExisteNombreAsync("Estándar XL", id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<PlanUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert: update OK + auditoría UPDATE con snapshot previo (valor_anterior)
        Assert.Equal("Estándar XL", resultado.Nombre);
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<PlanUpdateDto>(d => d.Id == id && d.Nombre == "Estándar XL"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Plan" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Estándar") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Estándar XL")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── 18-20. EliminarAsync ───────────────────────────────────────────────

    [Fact]
    public async Task Eliminar_PlanEnUsoPorTenant_LanzaValidacion()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidad = CrearPlan(id, "Estándar");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ContarTenantsUsoAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 por plan en uso; Delete nunca se llama (DAL-P6)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.EliminarAsync(id));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Eliminar_PlanSinUsos_RetornaTrueYAuditaDelete()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidad = CrearPlan(id, "Estándar", descripcion: "Plan base");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);
        _mockRepo
            .Setup(r => r.ContarTenantsUsoAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.DeleteAsync(id, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.EliminarAsync(id);

        // Assert: true + auditoría DELETE con valor_anterior snapshot y valor_nuevo null
        Assert.True(resultado);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "DELETE" &&
                    l.Entidad == "Plan" &&
                    l.ValorAnterior != null &&
                    l.ValorNuevo == null),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Eliminar_PlanInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanEntity?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() => _service.EliminarAsync(id));
    }

    // ─── 21. ListarAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_RetornaTodosLosPlanesOrdenadosPorNombre()
    {
        // Arrange: la DAL (DAL-P3) ya ordena por nombre ASC; el servicio mapea sin paginación (D1)
        var planes = new List<PlanEntity>
        {
            CrearPlan(nombre: "Alfa"),
            CrearPlan(nombre: "Beta"),
            CrearPlan(nombre: "Gamma")
        };
        _mockRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<PlanEntity>)planes);

        // Act
        var resultado = await _service.ListarAsync();

        // Assert: listado completo, orden preservado, sin paginación
        Assert.Equal(3, resultado.Count);
        Assert.Equal("Alfa", resultado[0].Nombre);
        Assert.Equal("Beta", resultado[1].Nombre);
        Assert.Equal("Gamma", resultado[2].Nombre);
    }

    // ─── 22-23. ObtenerPorIdAsync ───────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorId_PlanInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanEntity?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));
    }

    [Fact]
    public async Task ObtenerPorId_PlanExistente_RetornaPlan()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entidad = CrearPlan(id, "Premium", maxAreas: 20, maxUsuarios: 100, maxCiclosActivos: 2);
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerPorIdAsync(id);

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(id, resultado!.Id);
        Assert.Equal("Premium", resultado.Nombre);
        Assert.Equal(20, resultado.MaxAreas);
        Assert.Equal(100, resultado.MaxUsuarios);
        Assert.Equal(2, resultado.MaxCiclosActivos);
        Assert.Equal(entidad.CreatedAt, resultado.CreatedAt);
    }

    // ─── 24-26. ValidarLimitesParaTenantAsync ───────────────────────────────

    [Fact]
    public async Task ValidarLimitesParaTenant_TenantExcedeLimite_RetornaInvalido()
    {
        // Arrange: plan con max 10 áreas; tenant con 11 áreas activas (DAL-P8)
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan = CrearPlan(planId, "Estándar", maxAreas: 10, maxUsuarios: 25, maxCiclosActivos: 1);

        _mockRepo
            .Setup(r => r.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 11, UsuariosActuales: 10, CiclosActivosActuales: 1));

        // Act
        var resultado = await _service.ValidarLimitesParaTenantAsync(tenantId, planId);

        // Assert: EsValido=false con error de áreas (NO lanza excepción por límites excedidos)
        Assert.False(resultado.EsValido);
        Assert.Contains(resultado.Errores, e => e.Contains("áreas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidarLimitesParaTenant_TenantDentroDeLimites_RetornaValido()
    {
        // Arrange: tenant con 5 áreas / 10 usuarios / 1 ciclo dentro de límites
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan = CrearPlan(planId, "Estándar", maxAreas: 10, maxUsuarios: 25, maxCiclosActivos: 1);

        _mockRepo
            .Setup(r => r.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 10, CiclosActivosActuales: 1));

        // Act
        var resultado = await _service.ValidarLimitesParaTenantAsync(tenantId, planId);

        // Assert
        Assert.True(resultado.EsValido);
        Assert.Empty(resultado.Errores);
    }

    [Fact]
    public async Task ValidarLimitesParaTenant_PlanInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanEntity?)null);

        // Act & Assert: 404 defensivo (los llamadores ya validan existencia; spec paso 1)
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ValidarLimitesParaTenantAsync(tenantId, planId));
    }

    // ─── 27. ValidarLimitesParaTenantsDelPlanAsync ──────────────────────────

    [Fact]
    public async Task ValidarLimitesParaTenantsDelPlan_DosTenantsUnoIncumple_RetornaInvalidoConEseTenant()
    {
        // Arrange: 2 tenants usan el plan; solo 'Excede SA' incumple los nuevos límites (12 áreas > 10)
        var planId = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var nuevosLimites = new PlanLimits(10, 25, 1);

        _mockRepo
            .Setup(r => r.ObtenerTenantsPorPlanAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<TenantPlanDto>)new List<TenantPlanDto>
            {
                new() { TenantId = t1, Nombre = "Cumple SA" },
                new() { TenantId = t2, Nombre = "Excede SA" }
            });
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(t1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 10, CiclosActivosActuales: 1));
        _mockRepo
            .Setup(r => r.ObtenerConteosUsoAsync(t2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConteosUsoPlan(AreasActuales: 12, UsuariosActuales: 10, CiclosActivosActuales: 1));

        // Act
        var resultado = await _service.ValidarLimitesParaTenantsDelPlanAsync(planId, nuevosLimites);

        // Assert: inválido, solo se acumula el mensaje del infractor (prefijado con su nombre)
        Assert.False(resultado.EsValido);
        Assert.Single(resultado.Errores);
        Assert.Contains("Excede SA", resultado.Errores[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cumple SA", resultado.Errores[0], StringComparison.OrdinalIgnoreCase);
    }
}
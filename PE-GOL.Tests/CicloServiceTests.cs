using System.Data;
using Moq;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para CicloService — Spec HU-007 § "Tests requeridos" (35 casos de la tabla).
/// TDD fase red por contrato (TEST-01): ICicloService/CicloService/ICicloRepository/CicloEntity/
/// UmbralSemaforoEntity/CicloCreateRequest/CicloUpdateRequest/ClonarCicloRequest/CicloResponse/
/// CicloInsertDto/CicloUpdateDto/UmbralSemaforoDto AÚN NO existen (@BackendDev los implementa en
/// fase 4) → esta clase NO compila hasta entonces (rojo esperado por compilación).
/// Moq sobre ICicloRepository + IPlanService (D-C: la validación de límites SIEMPRE vía
/// ValidarLimitesParaTenantAsync, patrón UsuarioService HU-003) + TenantContext real
/// (D17: TenantId nullable; D12: re-validación de rol en BLL).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de CicloService: (ICicloRepository, IPlanService, TenantContext) + overload con ILogger
/// (D13) — se usa el ctor de 3 args, idéntico al patrón EmpresaService HU-006.
/// Escrituras (INSERT/UPDATE/estado/umbrales + auditoría) en UNA sola transacción IDbTransaction
/// (patrón HU-001/HU-003); captura SQLSTATE 23505 → ValidacionException + rollback (ADR-001).
/// Auditoría (ADR-003): Entidad="Ciclo", JSON legible con UnsafeRelaxedJsonEscaping; cerrar se
/// audita como UPDATE y clonar como CREATE con origenId (D11).
/// </summary>
public class CicloServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly Mock<IPlanService> _mockPlanService;
    private readonly TenantContext _tenantContext;
    private readonly ICicloService _service;

    public CicloServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _mockPlanService = new Mock<IPlanService>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "AdminTenant"
        };

        // Default Ok() para la validación de límites: los tests del flujo feliz no necesitan
        // configurar el validador; el caso 24 (límites excedidos) lo sobreescribe.
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        _service = new CicloService(_mockRepo.Object, _mockPlanService.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(
        Guid? id = null, Guid? tenantId = null, string nombre = "PE 2026", int añoFiscal = 2026,
        int mesInicio = 1, string estado = "Borrador", Guid? createdBy = null,
        DateTimeOffset? activatedAt = null, DateTimeOffset? closedAt = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CicloEntity
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            Nombre = nombre,
            AñoFiscal = añoFiscal,
            MesInicio = mesInicio,
            Estado = estado,
            CreatedBy = createdBy ?? Guid.NewGuid(),
            ActivatedAt = activatedAt,
            ClosedAt = closedAt,
            CreatedAt = createdAt ?? now.AddDays(-30),
            UpdatedAt = updatedAt ?? now
        };
    }

    private static UmbralSemaforoEntity CrearUmbral(
        string tipo, decimal umbralVerde, decimal umbralAmarillo, Guid cicloId, Guid tenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            CicloId = cicloId,
            TenantId = tenantId,
            Tipo = tipo,
            UmbralVerde = umbralVerde,
            UmbralAmarillo = umbralAmarillo,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static CicloCreateRequest CrearCreateRequestValido(
        string? nombre = "PE 2026", int añoFiscal = 2026, int mesInicio = 1)
        => new() { Nombre = nombre ?? "PE 2026", AñoFiscal = añoFiscal, MesInicio = mesInicio };

    private static CicloUpdateRequest CrearUpdateRequestValido(
        string? nombre = "PE 2026", int añoFiscal = 2026, int mesInicio = 1)
        => new() { Nombre = nombre ?? "PE 2026", AñoFiscal = añoFiscal, MesInicio = mesInicio };

    private static ClonarCicloRequest CrearClonarRequestValido(
        string? nombre = "PE 2027", int añoFiscal = 2027, int mesInicio = 1)
        => new() { Nombre = nombre ?? "PE 2027", AñoFiscal = añoFiscal, MesInicio = mesInicio };

    /// <summary>Configura el flujo feliz de CrearAsync (unicidad + tx + INSERT + 2 umbrales + log + re-lectura).</summary>
    private void ConfigurarFlujoFelizCrear(
        CicloCreateRequest request, Guid nuevoId, CicloEntity entidadCreada, Mock<IDbTransaction>? mockTx = null)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, request.AñoFiscal, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.InsertarUmbralAsync(It.IsAny<UmbralSemaforoDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidadCreada);
    }

    // ─── Caso 1-9. CrearAsync ───────────────────────────────────────────────

    // Caso 1 ─ insert OK → CicloResponse con estado='Borrador', createdBy=TenantContext.UserId,
    // activatedAt/closedAt=null (CA #1: el estado SIEMPRE Borrador al crear)
    [Fact]
    public async Task Crear_ConDatosValidos_RetornaCicloBorrador()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var entidadCreada = CrearCiclo(nuevoId, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);

        ConfigurarFlujoFelizCrear(request, nuevoId, entidadCreada);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert: 201 con la entidad persistida; Borrador; createdBy del JWT; sin fechas de estado
        Assert.NotNull(resultado);
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("PE 2026", resultado.Nombre);
        Assert.Equal(2026, resultado.AñoFiscal);
        Assert.Equal(1, resultado.MesInicio);
        Assert.Equal("Borrador", resultado.Estado);
        Assert.Equal(_tenantContext.UserId, resultado.CreatedBy);
        Assert.Null(resultado.ActivatedAt);
        Assert.Null(resultado.ClosedAt);
    }

    // Caso 2 ─ se insertan 2 filas umbral_semaforo (KPI y PlanAccion, 0.90/0.70) en la misma transacción
    [Fact]
    public async Task Crear_InsertaUmbralesPorDefecto()
    {
        // Arrange: mockTx con referencia para verificar que ambos INSERT usan la MISMA transacción
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var entidadCreada = CrearCiclo(nuevoId, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);

        ConfigurarFlujoFelizCrear(request, nuevoId, entidadCreada, mockTx);

        // Act
        await _service.CrearAsync(request);

        // Assert: 2 umbrales por defecto (defaults del DDL L150-151, HU-008 CA #4) en la misma tx
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d =>
                    d.Tipo == "KPI" && d.UmbralVerde == 0.90m && d.UmbralAmarillo == 0.70m),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d =>
                    d.Tipo == "PlanAccion" && d.UmbralVerde == 0.90m && d.UmbralAmarillo == 0.70m),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 3 ─ año fiscal duplicado → 422 (CA #2, DAL-C6); Insert nunca se llama
    [Fact]
    public async Task Crear_AñoFiscalDuplicado_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido(añoFiscal: 2026);
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2026, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("año fiscal", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ mesInicio fuera de 1..12 → 422 (re-validación BLL, espejo del CHECK del DDL L134);
    // Insert no se llama
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task Crear_MesInicioFueraDeRango_LanzaValidacion(int mesInicio)
    {
        // Arrange
        var request = CrearCreateRequestValido(mesInicio: mesInicio);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("mes", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ nombre con espacios → se persiste normalizado "PE 2026" (trim + colapso de espacios)
    [Fact]
    public async Task Crear_NombreConEspacios_SeNormaliza()
    {
        // Arrange: "  PE   2026 " → "PE 2026" (mismo helper que TenantService/UsuarioService)
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido(nombre: "  PE   2026 ");
        var nuevoId = Guid.NewGuid();
        var entidadCreada = CrearCiclo(nuevoId, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);

        ConfigurarFlujoFelizCrear(request, nuevoId, entidadCreada);

        // Act
        var resultado = await _service.CrearAsync(request);

        // Assert: el DTO de inserción y la respuesta llevan el nombre normalizado
        Assert.Equal("PE 2026", resultado.Nombre);
        _mockRepo.Verify(
            r => r.InsertAsync(
                It.Is<CicloInsertDto>(d =>
                    d.Nombre == "PE 2026" &&
                    d.AñoFiscal == 2026 &&
                    d.MesInicio == 1 &&
                    d.TenantId == tenantId &&
                    d.CreatedBy == _tenantContext.UserId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 6 ─ auditoría CREATE con JSON relajado (ADR-003): accion=CREATE, entidad='Ciclo',
    // valor_anterior=null y JSON legible sin escapes Unicode
    [Fact]
    public async Task Crear_RegistraAuditoriaCreateConJsonRelajado()
    {
        // Arrange: nombre con acento → el JSON auditado debe contener literal "PE 2026" (sin \u)
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido(nombre: "PE 2026");
        var nuevoId = Guid.NewGuid();
        var entidadCreada = CrearCiclo(nuevoId, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);

        ConfigurarFlujoFelizCrear(request, nuevoId, entidadCreada);

        // Act
        await _service.CrearAsync(request);

        // Assert: accion=CREATE, entidad='Ciclo', valor_anterior=null y JSON con
        // UnsafeRelaxedJsonEscaping (ADR-003) — sin escapes Unicode
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Ciclo" &&
                    l.EntidadId == nuevoId.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("PE 2026") &&
                    !l.ValorNuevo.Contains("\\u")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 7 ─ TenantContext.TenantId=null → NotFoundException 404; el repo nunca se consulta
    [Fact]
    public async Task Crear_SinTenantEnContexto_LanzaNoEncontrado()
    {
        // Arrange: D17 — TenantId null (SuperAdmin sin tenant)
        _tenantContext.TenantId = null;
        var request = CrearCreateRequestValido();

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CrearAsync(request));

        _mockRepo.Verify(
            r => r.ExisteAñoFiscalAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ rol distinto de AdminTenant → AccesoDenegadoException 403 (D12); Insert no se llama
    [Fact]
    public async Task Crear_RolNoAdminTenant_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO crea ciclos (solo el ADM)
        _tenantContext.Rol = "Gerente";
        var request = CrearCreateRequestValido();

        // Act & Assert: 403 y el INSERT nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CrearAsync(request));

        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ colisión concurrente 23505 (UNIQUE tenant_id+año_fiscal, DDL L141) → rollback +
    // ValidacionException 422 (patrón ADR-001)
    [Fact]
    public async Task Crear_ColisionConcurrente23505_LanzaValidacion()
    {
        // Arrange: el INSERT lanza PostgresException 23505 (carrera TOCTOU capturada por el UNIQUE)
        var tenantId = _tenantContext.TenantId!.Value;
        var request = CrearCreateRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, request.AñoFiscal, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",   // invariantSeverity (puede repetir "ERROR")
                "23505")); // sqlState ← aquí va el código 23505

        // Act & Assert: 422 con mensaje amigable y rollback explícito de la transacción
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(request));

        Assert.Contains("año fiscal", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ─── Caso 10-11. ListarAsync ────────────────────────────────────────────

    // Caso 10 ─ DAL-C3 mapeado a List<CicloResponse> (el DAL ya ordena por año_fiscal DESC)
    [Fact]
    public async Task Listar_RetornaCiclosDelTenantOrdenadosPorAñoFiscalDesc()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var c2025 = CrearCiclo(tenantId: tenantId, nombre: "PE 2025", añoFiscal: 2025);
        var c2026 = CrearCiclo(tenantId: tenantId, nombre: "PE 2026", añoFiscal: 2026);
        _mockRepo
            .Setup(r => r.ListarAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<CicloEntity>)new[] { c2026, c2025 });

        // Act
        var resultado = await _service.ListarAsync();

        // Assert: mapeo completo y orden DESC (2026 primero)
        Assert.Equal(2, resultado.Count);
        Assert.Equal(2026, resultado[0].AñoFiscal);
        Assert.Equal("PE 2026", resultado[0].Nombre);
        Assert.Equal(2025, resultado[1].AñoFiscal);
        Assert.Equal("PE 2025", resultado[1].Nombre);
        Assert.Equal(tenantId, resultado[0].TenantId);
        _mockRepo.Verify(r => r.ListarAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 11 ─ TenantId=null → 404; el repo nunca se consulta
    [Fact]
    public async Task Listar_SinTenantEnContexto_LanzaNoEncontrado()
    {
        // Arrange: D17
        _tenantContext.TenantId = null;

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ListarAsync());

        _mockRepo.Verify(
            r => r.ListarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Caso 12-14. ObtenerPorIdAsync ──────────────────────────────────────

    // Caso 12 ─ DAL-C2 devuelve null → NotFoundException 404
    [Fact]
    public async Task ObtenerPorId_CicloInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));
    }

    // Caso 13 ─ el filtro tenant_id del DAL devuelve null para un id de otro tenant → 404 (sin fuga)
    [Fact]
    public async Task ObtenerPorId_CicloDeOtroTenant_LanzaNoEncontrado()
    {
        // Arrange: el ciclo existe pero pertenece a OTRO tenant → el DAL (WHERE tenant_id = @TenantId)
        // devuelve null → 404 sin fuga de información (SEC-06)
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var cicloDeOtroTenant = CrearCiclo(id, tenantId: Guid.NewGuid(), nombre: "PE 2025");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y el repo se consulta SIEMPRE con el tenant del contexto (nunca el del ciclo)
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotEqual(tenantId, cicloDeOtroTenant.TenantId);
    }

    // Caso 14 ─ mapeo completo de campos a CicloResponse
    [Fact]
    public async Task ObtenerPorId_CicloExistente_RetornaCicloResponse()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var entidad = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Activo",
            createdBy: _tenantContext.UserId, activatedAt: activatedAt);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerPorIdAsync(id);

        // Assert: mapeo completo de campos
        Assert.Equal(id, resultado.Id);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("PE 2026", resultado.Nombre);
        Assert.Equal(2026, resultado.AñoFiscal);
        Assert.Equal(1, resultado.MesInicio);
        Assert.Equal("Activo", resultado.Estado);
        Assert.Equal(_tenantContext.UserId, resultado.CreatedBy);
        Assert.Equal(activatedAt, resultado.ActivatedAt);
        Assert.Null(resultado.ClosedAt);
        Assert.Equal(entidad.CreatedAt, resultado.CreatedAt);
        Assert.Equal(entidad.UpdatedAt, resultado.UpdatedAt);
    }

    // ─── Caso 15-20. ActualizarAsync ────────────────────────────────────────

    // Caso 15 ─ original.Estado='Activo' → 422 "Solo se puede editar un ciclo en estado Borrador"
    // (RC-12/RN-004); Update no se llama
    [Fact]
    public async Task Actualizar_CicloActivo_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var original = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Activo");
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("Borrador", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<CicloUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 16 ─ original.Estado='Cerrado' → 422 (RC-12/RN-004: Cerrado = solo lectura)
    [Fact]
    public async Task Actualizar_CicloCerrado_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var original = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Cerrado");
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("Borrador", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<CicloUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 17 ─ ciclo inexistente → 404
    [Fact]
    public async Task Actualizar_CicloInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(id, request));
    }

    // Caso 18 ─ ExisteAñoFiscalAsync(excludeId=id) true → 422 (CA #2 excluyendo self, DAL-C6)
    [Fact]
    public async Task Actualizar_AñoFiscalDuplicadoDeOtroCiclo_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var original = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        var request = CrearUpdateRequestValido(añoFiscal: 2027);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2027, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));

        Assert.Contains("año fiscal", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<CicloUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 19 ─ update OK, updatedAt renovado, auditoría UPDATE con valor_anterior snapshot (ADR-003)
    [Fact]
    public async Task Actualizar_ConDatosValidos_RetornaCicloActualizado()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var original = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId, updatedAt: now.AddHours(-1));
        var actualizado = CrearCiclo(id, tenantId, "PE 2027", 2027, 1, "Borrador",
            createdBy: _tenantContext.UserId, updatedAt: now);
        var request = CrearUpdateRequestValido(nombre: "PE 2027", añoFiscal: 2027);

        _mockRepo
            .SetupSequence(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)      // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(actualizado);  // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2027, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateAsync(It.IsAny<CicloUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActualizarAsync(id, request);

        // Assert: update OK con updatedAt renovado + auditoría UPDATE con snapshot previo
        Assert.Equal("PE 2027", resultado.Nombre);
        Assert.Equal(2027, resultado.AñoFiscal);
        Assert.True(resultado.UpdatedAt > original.UpdatedAt, "updatedAt debe renovarse en el UPDATE");
        _mockRepo.Verify(
            r => r.UpdateAsync(
                It.Is<CicloUpdateDto>(d => d.Id == id && d.Nombre == "PE 2027" && d.AñoFiscal == 2027),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Ciclo" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("PE 2026") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("PE 2027")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 20 ─ rol JefeArea → AccesoDenegadoException 403 (D12); Update no se llama
    [Fact]
    public async Task Actualizar_RolNoAdminTenant_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el JefeArea solo lee configuración del ciclo (RN-007), no edita
        _tenantContext.Rol = "JefeArea";
        var id = Guid.NewGuid();
        var request = CrearUpdateRequestValido();

        // Act & Assert: 403 y el UPDATE nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarAsync(id, request));

        _mockRepo.Verify(
            r => r.UpdateAsync(It.IsAny<CicloUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Caso 21-27. ActivarAsync ───────────────────────────────────────────

    // Caso 21 ─ estado='Activo' → 422 (evita log duplicado y auditoría imprecisa)
    [Fact]
    public async Task Activar_CicloYaActivo_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var activo = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Activo",
            activatedAt: DateTimeOffset.UtcNow);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activo);

        // Act & Assert: 422 y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("activo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 22 ─ estado='Cerrado' → 422 (RC-12/RN-004: Cerrado = solo lectura)
    [Fact]
    public async Task Activar_CicloCerrado_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var cerrado = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Cerrado",
            closedAt: DateTimeOffset.UtcNow);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cerrado);

        // Act & Assert: 422 y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 23 ─ ContarCiclosActivosAsync(excludeId)>0 → 422 (RC-01: solo un Activo por tenant)
    [Fact]
    public async Task Activar_ExisteOtroCicloActivo_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador);
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 (RC-01) y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("activo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 24 ★ ─ IPlanService.ValidarLimitesParaTenantAsync → EsValido=false → 422 con los errores
    // del plan (D-C, CA #2 HU-002); Update y InsertLog NO se llaman
    [Fact]
    public async Task Activar_PlanExcedeMaxCiclosActivos_LanzaValidacion()
    {
        // Arrange: el tenant excede max_ciclos_activos del plan → 422 sin llegar al UPDATE ni a la auditoría
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador);
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de ciclos activos: 2 > 1"]));

        // Act & Assert: 422 con los errores del plan; ni UPDATE ni auditoría
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("ciclos", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 25 ─ estado='Activo', activatedAt poblado, auditoría ACTIVATE con valor_anterior={estado:'Borrador'}
    [Fact]
    public async Task Activar_ConDatosValidos_RetornaCicloActivo()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var activatedAt = DateTimeOffset.UtcNow;
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);
        var activo = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Activo",
            createdBy: _tenantContext.UserId, activatedAt: activatedAt);

        _mockRepo
            .SetupSequence(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador)   // 1ª llamada: validación
            .ReturnsAsync(activo);    // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(tenantId, id, "Activo", It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ActivarAsync(id);

        // Assert: Activo con activatedAt poblado + auditoría ACTIVATE con snapshot previo
        Assert.Equal("Activo", resultado.Estado);
        Assert.NotNull(resultado.ActivatedAt);
        Assert.Null(resultado.ClosedAt);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(tenantId, id, "Activo", It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "ACTIVATE" &&
                    l.Entidad == "Ciclo" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Borrador")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 26 ─ rol Gerente → AccesoDenegadoException 403 (D-A: el GER no activa)
    [Fact]
    public async Task Activar_RolNoAdminTenant_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente solo cierra, no activa
        _tenantContext.Rol = "Gerente";
        var id = Guid.NewGuid();

        // Act & Assert: 403 y el UPDATE de estado nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActivarAsync(id));

        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 27 ─ TenantId=null → 404
    [Fact]
    public async Task Activar_SinTenantEnContexto_LanzaNoEncontrado()
    {
        // Arrange: D17
        _tenantContext.TenantId = null;
        var id = Guid.NewGuid();

        // Act & Assert: 404 y el repo nunca se consulta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActivarAsync(id));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Caso 28-31. CerrarAsync ────────────────────────────────────────────

    // Caso 28 ─ estado='Borrador' → 422 "Solo se puede cerrar el ciclo activo" (solo el Activo se cierra)
    [Fact]
    public async Task Cerrar_CicloNoActivo_LanzaValidacion()
    {
        // Arrange: D-A — solo el Gerente cierra (Activo → Cerrado); el ctor fija AdminTenant
        _tenantContext.Rol = "Gerente";
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador);

        // Act & Assert: 422 y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CerrarAsync(id));

        Assert.Contains("cerrar", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 29 ─ estado='Cerrado' → 422 (ya está cerrado)
    [Fact]
    public async Task Cerrar_CicloYaCerrado_LanzaValidacion()
    {
        // Arrange: D-A — solo el Gerente cierra (Activo → Cerrado); el ctor fija AdminTenant
        _tenantContext.Rol = "Gerente";
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var cerrado = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Cerrado",
            closedAt: DateTimeOffset.UtcNow);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cerrado);

        // Act & Assert: 422 y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CerrarAsync(id));

        Assert.Contains("cerrar", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 30 ─ estado='Cerrado', closedAt poblado, activatedAt conservado, auditoría UPDATE con
    // valor_nuevo={estado:'Cerrado'} (D11: el enum accion_auditoria no tiene CLOSE)
    [Fact]
    public async Task Cerrar_ConDatosValidos_RetornaCicloCerrado()
    {
        // Arrange: D-A — solo el Gerente cierra (Activo → Cerrado); el ctor fija AdminTenant
        _tenantContext.Rol = "Gerente";
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var closedAt = DateTimeOffset.UtcNow;
        var activo = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Activo",
            createdBy: _tenantContext.UserId, activatedAt: activatedAt);
        var cerrado = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Cerrado",
            createdBy: _tenantContext.UserId, activatedAt: activatedAt, closedAt: closedAt);

        _mockRepo
            .SetupSequence(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activo)    // 1ª llamada: validación
            .ReturnsAsync(cerrado);  // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.UpdateEstadoAsync(tenantId, id, "Cerrado", It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.CerrarAsync(id);

        // Assert: Cerrado con closedAt poblado y activatedAt conservado (DAL-C5)
        Assert.Equal("Cerrado", resultado.Estado);
        Assert.NotNull(resultado.ClosedAt);
        Assert.Equal(activatedAt, resultado.ActivatedAt);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(tenantId, id, "Cerrado", It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Ciclo" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Activo") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Cerrado")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 31 ─ rol AdminTenant → AccesoDenegadoException 403 (D-A: el ADM no cierra)
    [Fact]
    public async Task Cerrar_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: D-A — solo el Gerente cierra (Activo → Cerrado)
        _tenantContext.Rol = "AdminTenant";
        var id = Guid.NewGuid();

        // Act & Assert: 403 y el UPDATE de estado nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CerrarAsync(id));

        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Caso 32-35. ClonarAsync ────────────────────────────────────────────

    // Caso 32 ─ ciclo origen inexistente → 404
    [Fact]
    public async Task Clonar_CicloOrigenInexistente_LanzaNoEncontrado()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var idOrigen = Guid.NewGuid();
        var request = CrearClonarRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ClonarAsync(idOrigen, request));
    }

    // Caso 33 ─ año fiscal duplicado → 422 (CA #2, DAL-C6); Insert no se llama
    [Fact]
    public async Task Clonar_AñoFiscalDuplicado_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var idOrigen = Guid.NewGuid();
        var origen = CrearCiclo(idOrigen, tenantId, "PE 2026", 2026, 1, "Borrador");
        var request = CrearClonarRequestValido(añoFiscal: 2027);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync(origen);
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2027, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ClonarAsync(idOrigen, request));

        Assert.Contains("año fiscal", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 34 ─ se inserta el ciclo nuevo + umbrales con los valores COPIADOS del origen
    // (DAL-C10 → DAL-C9b), auditoría CREATE con origenId en valor_nuevo (D11)
    [Fact]
    public async Task Clonar_ConDatosValidos_CopiaUmbralesYRetornaNuevoCiclo()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var idOrigen = Guid.NewGuid();
        var nuevoId = Guid.NewGuid();
        var origen = CrearCiclo(idOrigen, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);
        var nuevoCiclo = CrearCiclo(nuevoId, tenantId, "PE 2027", 2027, 1, "Borrador",
            createdBy: _tenantContext.UserId);
        var request = CrearClonarRequestValido(nombre: "PE 2027", añoFiscal: 2027);
        var umbralKpi = CrearUmbral("KPI", 0.85m, 0.60m, idOrigen, tenantId);
        var umbralPlan = CrearUmbral("PlanAccion", 0.95m, 0.75m, idOrigen, tenantId);

        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync(origen);      // 1ª llamada: validación del origen
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoCiclo);  // 2ª llamada: re-lectura posterior al commit (spec §7 paso 10)
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2027, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ObtenerUmbralesAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UmbralSemaforoEntity>)new[] { umbralKpi, umbralPlan });
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.InsertarUmbralAsync(It.IsAny<UmbralSemaforoDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ClonarAsync(idOrigen, request);

        // Assert: ciclo nuevo en Borrador + umbrales COPIADOS del origen (DAL-C9b) + auditoría CREATE
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal("PE 2027", resultado.Nombre);
        Assert.Equal("Borrador", resultado.Estado);
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d => d.Tipo == "KPI" && d.UmbralVerde == 0.85m && d.UmbralAmarillo == 0.60m),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d => d.Tipo == "PlanAccion" && d.UmbralVerde == 0.95m && d.UmbralAmarillo == 0.75m),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Ciclo" &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains(idOrigen.ToString())),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 35 ─ origen con 0 umbrales → se insertan los 2 defaults (0.90/0.70) (defensivo, D-B)
    [Fact]
    public async Task Clonar_OrigenSinUmbrales_InsertaDefaults()
    {
        // Arrange: el origen no tiene umbrales → el ciclo nuevo NUNCA queda sin configuración
        var tenantId = _tenantContext.TenantId!.Value;
        var idOrigen = Guid.NewGuid();
        var nuevoId = Guid.NewGuid();
        var origen = CrearCiclo(idOrigen, tenantId, "PE 2026", 2026, 1, "Borrador",
            createdBy: _tenantContext.UserId);
        var nuevoCiclo = CrearCiclo(nuevoId, tenantId, "PE 2027", 2027, 1, "Borrador",
            createdBy: _tenantContext.UserId);
        var request = CrearClonarRequestValido(nombre: "PE 2027", añoFiscal: 2027);

        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync(origen);      // 1ª llamada: validación del origen
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoCiclo);  // 2ª llamada: re-lectura posterior al commit (spec §7 paso 10)
        _mockRepo
            .Setup(r => r.ExisteAñoFiscalAsync(tenantId, 2027, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ObtenerUmbralesAsync(tenantId, idOrigen, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<UmbralSemaforoEntity>)new List<UmbralSemaforoEntity>());
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IDbTransaction>().Object);
        _mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<CicloInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.InsertarUmbralAsync(It.IsAny<UmbralSemaforoDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _service.ClonarAsync(idOrigen, request);

        // Assert: 2 defaults (0.90/0.70) insertados para KPI y PlanAccion
        Assert.Equal(nuevoId, resultado.Id);
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d => d.Tipo == "KPI" && d.UmbralVerde == 0.90m && d.UmbralAmarillo == 0.70m),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarUmbralAsync(
                It.Is<UmbralSemaforoDto>(d => d.Tipo == "PlanAccion" && d.UmbralVerde == 0.90m && d.UmbralAmarillo == 0.70m),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
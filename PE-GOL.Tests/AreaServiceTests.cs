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
using PE_GOL.Entity.Estrategia;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para AreaService — Spec HU-009 § "Tests requeridos" (29 casos de la tabla).
/// TDD fase red por contrato (TEST-01): IAreaService/AreaService/AreaEntity/AreaCreateRequest/
/// AreaUpdateRequest/AreaResponse/ResponsableCandidatoResponse/AreaInsertDto/AreaUpdateDto y las
/// extensiones DAL-A1..A11 de ICicloRepository + IPlanService.ObtenerLimitesAsync AÚN NO existen
/// (stubs NotImplementedException) → los 29 tests FALLAN hasta que @BackendDev implemente (fase 4).
/// Moq sobre ICicloRepository + IPlanService (D-C: ObtenerLimitesAsync para el chequeo por ciclo
/// RN-010 + ValidarLimitesParaTenantAsync como guarda a nivel tenant, CA #2 HU-002) + TenantContext
/// real (D17: TenantId nullable; D12: re-validación de rol en BLL).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de AreaService: (ICicloRepository, IPlanService, TenantContext) + overload con ILogger
/// (D13) — se usa el ctor de 3 args, idéntico al patrón CicloService HU-007.
/// Escrituras (INSERT/UPDATE/desactivar + sync usuario.area_id + auditoría) en UNA sola transacción
/// IDbTransaction (patrón HU-001/HU-003); captura SQLSTATE 23505 → ValidacionException + rollback
/// (ADR-001). Auditoría (ADR-003): Entidad="Area", JSON legible con UnsafeRelaxedJsonEscaping;
/// desactivar se audita como DEACTIVATE (D-E, enum accion_auditoria L46).
/// SEC-07: area SÍ es entidad de área → ListarAsync filtra por TenantContext.AreaId si rol JefeArea;
/// ObtenerPorIdAsync lanza 403 si JefeArea pide otra área (D12).
/// </summary>
public class AreaServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly Mock<IPlanService> _mockPlanService;
    private readonly TenantContext _tenantContext;
    private readonly IAreaService _service;

    public AreaServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _mockPlanService = new Mock<IPlanService>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "AdminTenant"
        };

        // Defaults para el flujo feliz: límites del plan (D-D) y validación a nivel tenant OK.
        // Los tests de límites (casos 10/11) los sobreescriben.
        _mockPlanService
            .Setup(s => s.ObtenerLimitesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanLimits(10, 50, 1));
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        _service = new AreaService(_mockRepo.Object, _mockPlanService.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(
        Guid? id = null, Guid? tenantId = null, string nombre = "PE 2026", int añoFiscal = 2026,
        int mesInicio = 1, string estado = "Borrador", Guid? createdBy = null,
        DateTimeOffset? activatedAt = null, DateTimeOffset? closedAt = null)
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
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now
        };
    }

    private static AreaEntity CrearArea(
        Guid? id = null, Guid? tenantId = null, Guid? cicloId = null, string codigo = "GOL1",
        string nombre = "CEDIS FARMA", string? comentarios = null, Guid? responsableId = null,
        int orden = 1, bool activa = true, DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AreaEntity
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            CicloId = cicloId ?? Guid.NewGuid(),
            Codigo = codigo,
            Nombre = nombre,
            Comentarios = comentarios,
            ResponsableId = responsableId,
            ResponsableNombre = responsableId is null ? null : "Juan Pérez",
            ResponsableCorreo = responsableId is null ? null : "juan@dicegsa.com",
            Orden = orden,
            Activa = activa,
            CreatedAt = createdAt ?? now.AddDays(-30),
            UpdatedAt = updatedAt ?? now
        };
    }

    private static UsuarioEntity CrearResponsable(
        Guid id, Guid tenantId, string rol = "JefeArea", string estado = "Activo")
        => new()
        {
            Id = id,
            TenantId = tenantId,
            Nombre = "Juan Pérez",
            Correo = "juan@dicegsa.com",
            PasswordHash = "hash",
            Rol = rol,
            Estado = estado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AreaCreateRequest CrearCreateRequestValido(
        string? nombre = "CEDIS FARMA", string? comentarios = null, Guid? responsableId = null)
        => new()
        {
            Nombre = nombre ?? "CEDIS FARMA",
            Comentarios = comentarios,
            ResponsableId = responsableId ?? Guid.NewGuid()
        };

    private static AreaUpdateRequest CrearUpdateRequestValido(
        string? nombre = "CEDIS FARMA", string? comentarios = null, Guid? responsableId = null)
        => new()
        {
            Nombre = nombre ?? "CEDIS FARMA",
            Comentarios = comentarios,
            ResponsableId = responsableId ?? Guid.NewGuid()
        };

    /// <summary>Configura el flujo feliz de CrearAsync (spec §3): ciclo Borrador, responsable válido
    /// (JefeArea Activo), RN-012 libre, límites OK (por ciclo y tenant), código GOL1, tx con
    /// INSERT + sync usuario.area_id + auditoría CREATE, re-lectura post-commit.</summary>
    private void ConfigurarFlujoFelizCrear(
        Guid cicloId, Guid tenantId, AreaCreateRequest request, Guid nuevoId,
        AreaEntity areaCreada, Mock<IDbTransaction>? mockTx = null)
    {
        var planId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        if (request.ResponsableId is { } refId)
        {
            _mockRepo
                .Setup(r => r.ObtenerResponsableAsync(tenantId, refId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CrearResponsable(refId, tenantId));
            _mockRepo
                .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, refId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
        }
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.ContarAreasActivasEnCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerSiguienteOrdenAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(areaCreada);
    }

    /// <summary>Configura el flujo feliz de ActualizarAsync (spec §4): ciclo Borrador, área original,
    /// responsable válido, RN-012 excluyendo self libre, tx con UPDATE + auditoría UPDATE, re-lectura.</summary>
    private void ConfigurarFlujoFelizActualizar(
        Guid cicloId, Guid areaId, Guid tenantId, AreaUpdateRequest request,
        AreaEntity original, AreaEntity actualizada, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .SetupSequence(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original)      // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(actualizada);  // 2ª llamada: re-lectura post-commit
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.ActualizarAreaAsync(It.IsAny<AreaUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Configura el flujo feliz de DesactivarAsync (spec §5): ciclo Activo (D-H), área activa,
    /// tx con UPDATE activa=FALSE + auditoría DEACTIVATE, re-lectura. NO se configura
    /// ActualizarAreaIdUsuarioAsync (D-J: no se limpia usuario.area_id).</summary>
    private void ConfigurarFlujoFelizDesactivar(
        Guid cicloId, Guid areaId, Guid tenantId, AreaEntity area,
        AreaEntity desactivada, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .SetupSequence(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(area)          // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(desactivada);  // 2ª llamada: re-lectura post-commit
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.DesactivarAreaAsync(tenantId, cicloId, areaId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    // ─── Caso 1-14. CrearAsync ──────────────────────────────────────────────

    // Caso 1 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A); InsertarAreaAsync nunca se llama
    [Fact]
    public async Task Crear_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO crea áreas (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();

        // Act & Assert: 403 y el INSERT nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task Crear_SinTenant_LanzaNotFound()
    {
        // Arrange: D17 — TenantId null (SuperAdmin sin tenant)
        _tenantContext.TenantId = null;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404 (el filtro tenant evita fuga)
    [Fact]
    public async Task Crear_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ ciclo Cerrado → ValidacionException 422 (RC-12/RN-004: solo lectura); sin INSERT
    [Fact]
    public async Task Crear_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura (Borrador y Activo permitidos, D-H)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ nombre con espacios → se persiste normalizado "CEDIS FARMA" (trim + colapso de espacios)
    [Fact]
    public async Task Crear_NombreConEspacios_NormalizaYPersiste()
    {
        // Arrange: "  CEDIS   FARMA " → "CEDIS FARMA" (mismo helper que TenantService/CicloService)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido(nombre: "  CEDIS   FARMA ");
        var nuevoId = Guid.NewGuid();
        var areaCreada = CrearArea(nuevoId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: request.ResponsableId);

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, areaCreada);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: el DTO de inserción y la respuesta llevan el nombre normalizado
        Assert.Equal("CEDIS FARMA", resultado.Nombre);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(
                It.Is<AreaInsertDto>(d =>
                    d.Nombre == "CEDIS FARMA" &&
                    d.TenantId == tenantId &&
                    d.CicloId == cicloId &&
                    d.ResponsableId == request.ResponsableId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 6 ─ responsable inexistente (DAL-A9 null) → ValidacionException 422 (RN-011)
    [Fact]
    public async Task Crear_ResponsableInexistente_LanzaValidacion()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioEntity?)null);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("responsable", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ responsable con rol ≠ JefeArea → ValidacionException 422 (RN-011, CA #2)
    [Fact]
    public async Task Crear_ResponsableNoJefeArea_LanzaValidacion()
    {
        // Arrange: el responsable existe pero es Gerente → no puede liderar un área (RN-011)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId, rol: "Gerente"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("Jefe", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ responsable Inactivo → ValidacionException 422 (RN-011, CA #2)
    [Fact]
    public async Task Crear_ResponsableInactivo_LanzaValidacion()
    {
        // Arrange: el responsable es JefeArea pero está Inactivo → no puede liderar (CA #2)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId, estado: "Inactivo"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("Activo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ responsable ya asignado a otra área ACTIVA del ciclo → 422 (RN-012, DAL-A8 > 0)
    [Fact]
    public async Task Crear_ResponsableYaAsignadoEnCiclo_LanzaValidacion()
    {
        // Arrange: RN-012 — un responsable solo puede liderar UNA área por ciclo
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 (RN-012) y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("asignado", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 10 ─ ciclo al tope de áreas del plan → 422 (RN-010, chequeo PRECISO por ciclo: DAL-A6 >= MaxAreas)
    [Fact]
    public async Task Crear_CicloAlTopeDeAreas_LanzaValidacion()
    {
        // Arrange: D-D — el chequeo por ciclo (DAL-A6 >= MaxAreas) detecta el caso "ciclo al tope"
        // que el MAX-per-ciclo de DAL-P8 (comparación estricta >) NO detecta (Flag #2)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ObtenerLimitesAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanLimits(5, 50, 1)); // plan Básico: máx 5 áreas
        _mockRepo
            .Setup(r => r.ContarAreasActivasEnCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5); // el ciclo ya tiene 5 áreas activas → 5 >= 5 → tope

        // Act & Assert: 422 (RN-010) y el INSERT nunca se ejecuta; la guarda a nivel tenant NO se consulta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("límite", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 11 ─ tenant excede límite del plan → 422 (CA #2 HU-002, guarda a nivel tenant)
    [Fact]
    public async Task Crear_TenantExcedeLimitePlan_LanzaValidacion()
    {
        // Arrange: el chequeo por ciclo pasa (3 < 10) pero la guarda a nivel tenant falla
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.ContarAreasActivasEnCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3); // 3 < 10 → el chequeo por ciclo pasa
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de áreas: 12 > 10"]));

        // Act & Assert: 422 con los errores del plan; el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("áreas", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 12 ─ código GOL auto-generado (CA #1, DB-04): DAL-A7 → 4 → codigo="GOL4", orden=4
    [Fact]
    public async Task Crear_GeneraCodigoSecuencial_InsertaGOLn()
    {
        // Arrange: MAX(orden)+1 = 4 (incluye inactivas → sin reutilizar códigos tras desactivar)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var areaCreada = CrearArea(nuevoId, tenantId, cicloId, "GOL4", "CEDIS FARMA",
            responsableId: request.ResponsableId, orden: 4);

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, areaCreada);
        _mockRepo
            .Setup(r => r.ObtenerSiguienteOrdenAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: el DTO de inserción lleva codigo="GOL4" y orden=4 (calculados en BLL, DB-04)
        Assert.Equal("GOL4", resultado.Codigo);
        Assert.Equal(4, resultado.Orden);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(
                It.Is<AreaInsertDto>(d => d.Codigo == "GOL4" && d.Orden == 4 && d.Activa),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 13 ★ ─ éxito: INSERT + sync usuario.area_id (DAL-A11, SEC-07 D-J) + auditoría CREATE
    // en la MISMA transacción, commit, re-lectura → 201
    [Fact]
    public async Task Crear_Exito_InsertaAreaSincronizaUsuarioYAudita()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var areaCreada = CrearArea(nuevoId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: request.ResponsableId);

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, areaCreada, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: INSERT + sync usuario.area_id + auditoría CREATE con la MISMA tx (D1), commit, re-lectura
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal("GOL1", resultado.Codigo);
        Assert.Equal(request.ResponsableId, resultado.ResponsableId);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(
                It.Is<AreaInsertDto>(d =>
                    d.TenantId == tenantId && d.CicloId == cicloId &&
                    d.Codigo == "GOL1" && d.Nombre == "CEDIS FARMA" &&
                    d.ResponsableId == request.ResponsableId && d.Orden == 1 && d.Activa),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(
                request.ResponsableId!.Value, nuevoId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Area" &&
                    l.EntidadId == nuevoId.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("CEDIS FARMA")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 13b ─ área SIN responsable (RN-011 diferido: exigencia en activación, no en creación,
    // 2026-09-21) → INSERT con ResponsableId=null y SIN sync de usuario.area_id → 201
    [Fact]
    public async Task Crear_SinResponsable_InsertaAreaSinSincronizar()
    {
        // Arrange: request.ResponsableId = null (resolución chicken-egg área↔responsable)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = new AreaCreateRequest { Nombre = "CEDIS FARMA", ResponsableId = null };
        var nuevoId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var areaCreada = CrearArea(nuevoId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: null);

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, areaCreada, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: se persiste con ResponsableId=null y NO se sincroniza usuario.area_id (no hay
        // responsable que vincular); auditoría CREATE igualmente se registra en la misma tx
        Assert.Equal(nuevoId, resultado.Id);
        Assert.True(resultado.ResponsableId is null);
        _mockRepo.Verify(
            r => r.InsertarAreaAsync(
                It.Is<AreaInsertDto>(d =>
                    d.CicloId == cicloId && d.Codigo == "GOL1" &&
                    d.ResponsableId == null && d.Orden == 1 && d.Activa),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 14 ─ colisión concurrente 23505 (UNIQUE (ciclo_id, codigo), DDL L206) → rollback +
    // ValidacionException 422 (patrón ADR-001)
    [Fact]
    public async Task Crear_ColisionCodigo23505_LanzaValidacion()
    {
        // Arrange: el INSERT lanza PostgresException 23505 (carrera TOCTOU capturada por el UNIQUE)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _mockRepo
            .Setup(r => r.ContarAreasActivasEnCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerSiguienteOrdenAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.InsertarAreaAsync(It.IsAny<AreaInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",   // invariantSeverity (puede repetir "ERROR")
                "23505")); // sqlState ← aquí va el código 23505

        // Act & Assert: 422 con mensaje amigable y rollback explícito de la transacción
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("código", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ─── Caso 15-21. ActualizarAsync ────────────────────────────────────────

    // Caso 15 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A)
    [Fact]
    public async Task Actualizar_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO edita áreas (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var request = CrearUpdateRequestValido();

        // Act & Assert: 403 y el UPDATE nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarAsync(cicloId, areaId, request));

        _mockRepo.Verify(
            r => r.ActualizarAreaAsync(It.IsAny<AreaUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 16 ─ área inexistente (DAL-A2 null) → NotFoundException 404
    [Fact]
    public async Task Actualizar_AreaInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AreaEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(cicloId, areaId, request));
    }

    // Caso 17 ─ ciclo Cerrado → ValidacionException 422 (RC-12/RN-004: solo lectura)
    [Fact]
    public async Task Actualizar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — ninguna entidad hija se modifica si el ciclo está Cerrado
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, areaId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaAsync(It.IsAny<AreaUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 18 ─ responsable ya asignado a OTRA área activa del ciclo → 422 (RN-012 excluyendo self)
    [Fact]
    public async Task Actualizar_ResponsableYaAsignadoOtroArea_LanzaValidacion()
    {
        // Arrange: DAL-A8 con excludeAreaId = areaId (self excluido) > 0 → RN-012
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var request = CrearUpdateRequestValido();
        var original = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: Guid.NewGuid());
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ObtenerResponsableAsync(tenantId, request.ResponsableId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(request.ResponsableId!.Value, tenantId));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, request.ResponsableId!.Value, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 (RN-012) y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, areaId, request));

        Assert.Contains("asignado", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaAsync(It.IsAny<AreaUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 19 ★ ─ cambio de responsable: libera al anterior (DAL-A11 con null) + sincroniza al
    // nuevo (DAL-A11 con areaId) en la MISMA transacción (SEC-07, D-J)
    [Fact]
    public async Task Actualizar_CambioResponsable_LiberaAnteriorYSincronizaNuevo()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var responsableAnterior = Guid.NewGuid();
        var responsableNuevo = Guid.NewGuid();
        var request = CrearUpdateRequestValido(responsableId: responsableNuevo);
        var mockTx = new Mock<IDbTransaction>();
        var original = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsableAnterior);
        var actualizada = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsableNuevo);

        ConfigurarFlujoFelizActualizar(cicloId, areaId, tenantId, request, original, actualizada, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, areaId, request);

        // Assert: libera al anterior (area_id = null) + sincroniza al nuevo (area_id = areaId),
        // ambos con la MISMA tx (D1); el UPDATE y la auditoría también
        Assert.Equal(responsableNuevo, resultado.ResponsableId);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(
                responsableAnterior, null, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(
                responsableNuevo, areaId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaAsync(
                It.Is<AreaUpdateDto>(d => d.Id == areaId && d.ResponsableId == responsableNuevo),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 20 ─ sin cambio de responsable → DAL-A11 NUNCA se invoca (no se toca usuario.area_id)
    [Fact]
    public async Task Actualizar_SinCambioResponsable_NoTocaUsuario()
    {
        // Arrange: request.ResponsableId == original.ResponsableId → no hay sync (D-J)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var responsable = Guid.NewGuid();
        var request = CrearUpdateRequestValido(responsableId: responsable);
        var original = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsable);
        var actualizada = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsable);

        ConfigurarFlujoFelizActualizar(cicloId, areaId, tenantId, request, original, actualizada);

        // Act
        await _service.ActualizarAsync(cicloId, areaId, request);

        // Assert: DAL-A11 nunca se invoca (mismo responsable → usuario.area_id intacto)
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 21 ─ éxito: UPDATE + auditoría UPDATE con snapshot previo (ADR-003) → 200
    [Fact]
    public async Task Actualizar_Exito_ActualizaYAuditaUpdate()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var responsable = Guid.NewGuid();
        var request = CrearUpdateRequestValido(nombre: "CEDIS NORTE", comentarios: "Nuevo comentario",
            responsableId: responsable);
        var original = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            comentarios: "Comentario previo", responsableId: responsable);
        var actualizada = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS NORTE",
            comentarios: "Nuevo comentario", responsableId: responsable);

        ConfigurarFlujoFelizActualizar(cicloId, areaId, tenantId, request, original, actualizada);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, areaId, request);

        // Assert: UPDATE con el DTO correcto + auditoría UPDATE con snapshot previo (ADR-003)
        Assert.Equal("CEDIS NORTE", resultado.Nombre);
        Assert.Equal("Nuevo comentario", resultado.Comentarios);
        _mockRepo.Verify(
            r => r.ActualizarAreaAsync(
                It.Is<AreaUpdateDto>(d =>
                    d.Id == areaId && d.TenantId == tenantId && d.CicloId == cicloId &&
                    d.Nombre == "CEDIS NORTE" && d.Comentarios == "Nuevo comentario" &&
                    d.ResponsableId == responsable),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Area" &&
                    l.EntidadId == areaId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("CEDIS FARMA") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("CEDIS NORTE")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Caso 22-25. DesactivarAsync ────────────────────────────────────────

    // Caso 22 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A)
    [Fact]
    public async Task Desactivar_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO desactiva áreas (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();

        // Act & Assert: 403 y el UPDATE de desactivación nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.DesactivarAsync(cicloId, areaId));

        _mockRepo.Verify(
            r => r.DesactivarAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 23 ─ área inexistente (DAL-A2 null) → NotFoundException 404
    [Fact]
    public async Task Desactivar_AreaInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AreaEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DesactivarAsync(cicloId, areaId));
    }

    // Caso 24 ─ área ya inactiva → ValidacionException 422 (D-E: no se re-desactiva)
    [Fact]
    public async Task Desactivar_AreaYaInactiva_LanzaValidacion()
    {
        // Arrange: activa = false → 422 "El área ya está desactivada"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var areaInactiva = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: null, activa: false);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(areaInactiva);

        // Act & Assert: 422 y el UPDATE de desactivación nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.DesactivarAsync(cicloId, areaId));

        Assert.Contains("desactivada", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.DesactivarAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 25 ★ ─ éxito: activa=FALSE (DAL-A5, sin DELETE — CA #5) + auditoría DEACTIVATE;
    // NO se llama DAL-A11 (usuario.area_id conservado — D-J, el JefeArea conserva lectura)
    [Fact]
    public async Task Desactivar_Exito_ActivaFalseYAuditaDeactivate()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var responsable = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var area = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsable, activa: true);
        var desactivada = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
            responsableId: responsable, activa: false);

        ConfigurarFlujoFelizDesactivar(cicloId, areaId, tenantId, area, desactivada, mockTx);

        // Act
        var resultado = await _service.DesactivarAsync(cicloId, areaId);

        // Assert: activa=FALSE + auditoría DEACTIVATE con snapshot previo; usuario.area_id NO se toca
        Assert.False(resultado.Activa);
        _mockRepo.Verify(
            r => r.DesactivarAreaAsync(tenantId, cicloId, areaId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "DEACTIVATE" &&
                    l.Entidad == "Area" &&
                    l.EntidadId == areaId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("CEDIS FARMA")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // ─── Caso 26-29. Lecturas y candidatos ──────────────────────────────────

    // Caso 26 ─ rol JefeArea → DAL-A3 invocado con areaIdFiltro = TenantContext.AreaId (SEC-07)
    [Fact]
    public async Task Listar_RolJefeArea_FiltraSoloSuArea()
    {
        // Arrange: SEC-07 — el JefeArea solo ve SU área (AND id = @AreaId en el DAL)
        _tenantContext.Rol = "JefeArea";
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = Guid.NewGuid();
        _tenantContext.AreaId = areaId;
        var cicloId = Guid.NewGuid();
        var area = CrearArea(areaId, tenantId, cicloId, "GOL1", "CEDIS FARMA");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarAreasAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<AreaEntity>)new[] { area });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: el DAL se consulta con areaIdFiltro = TenantContext.AreaId (SEC-07)
        Assert.Single(resultado);
        Assert.Equal(areaId, resultado[0].Id);
        _mockRepo.Verify(
            r => r.ListarAreasAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 27 ─ rol AdminTenant/Gerente → DAL-A3 invocado con areaIdFiltro = null (todas las áreas)
    [Fact]
    public async Task Listar_RolAdmin_Gerente_RetornaTodas()
    {
        // Arrange: ADM (default del ctor) y GER ven TODAS las áreas del ciclo (sin filtro SEC-07)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var a1 = CrearArea(tenantId: tenantId, cicloId: cicloId, codigo: "GOL1", orden: 1);
        var a2 = CrearArea(tenantId: tenantId, cicloId: cicloId, codigo: "GOL2", orden: 2);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarAreasAsync(tenantId, cicloId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<AreaEntity>)new[] { a1, a2 });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: sin filtro de área (areaIdFiltro = null) → todas las áreas
        Assert.Equal(2, resultado.Count);
        _mockRepo.Verify(
            r => r.ListarAreasAsync(tenantId, cicloId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 28 ─ rol JefeArea pidiendo OTRA área → AccesoDenegadoException 403 (D12, SEC-07)
    [Fact]
    public async Task ObtenerPorId_RolJefeAreaOtraArea_LanzaAccesoDenegado()
    {
        // Arrange: el JefeArea solo accede a SU área (area.Id != TenantContext.AreaId → 403)
        _tenantContext.Rol = "JefeArea";
        var tenantId = _tenantContext.TenantId!.Value;
        _tenantContext.AreaId = Guid.NewGuid(); // área del JefeArea
        var cicloId = Guid.NewGuid();
        var otraAreaId = Guid.NewGuid();
        var otraArea = CrearArea(otraAreaId, tenantId, cicloId, "GOL2", "CEDIS NORTE");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, otraAreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otraArea);

        // Act & Assert: 403 (D12) — el área consultada no es la del contexto
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerPorIdAsync(cicloId, otraAreaId));
    }

    // Caso 29 ─ candidatos: solo JefeArea Activos del tenant con flag yaAsignado (DAL-A10 → mapeo)
    [Fact]
    public async Task ListarResponsables_SoloJefeAreaActivos_RetornaConYaAsignado()
    {
        // Arrange: DAL-A10 ya filtra rol=JefeArea y estado=Activo; yaAsignado = EXISTS área activa
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var libre = new ResponsableCandidatoResponse { Id = Guid.NewGuid(), Nombre = "Ana López", Correo = "ana@dicegsa.com", YaAsignado = false };
        var asignado = new ResponsableCandidatoResponse { Id = Guid.NewGuid(), Nombre = "Juan Pérez", Correo = "juan@dicegsa.com", YaAsignado = true };
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarResponsablesCandidatosAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ResponsableCandidatoResponse>)new[] { libre, asignado });

        // Act
        var resultado = await _service.ListarResponsablesCandidatosAsync(cicloId);

        // Assert: mapeo directo con yaAsignado preservado (RN-012 para el frontend)
        Assert.Equal(2, resultado.Count);
        Assert.False(resultado[0].YaAsignado);
        Assert.True(resultado[1].YaAsignado);
        Assert.Equal("Ana López", resultado[0].Nombre);
        Assert.Equal("juan@dicegsa.com", resultado[1].Correo);
        _mockRepo.Verify(
            r => r.ListarResponsablesCandidatosAsync(tenantId, cicloId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
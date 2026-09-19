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
using PE_GOL.Utility.Email;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para ResponsableService — Spec HU-010 § "Tests requeridos" (35 casos).
/// TDD fase red por contrato (TEST-01): IResponsableService/ResponsableService/ResponsableEntity/
/// ResponsableCreateRequest/ResponsableReassignRequest/ResponsableResponse/ResponsableInsertDto/
/// IEmailService (PE_GOL.Utility.Email) y las extensiones DAL-R1..R7 de ICicloRepository AÚN NO
/// existen (@BackendDev los implementa en fase 4) → esta clase NO compila hasta entonces
/// (rojo esperado por compilación, mismo patrón que AreaServiceTests HU-009).
/// Moq sobre ICicloRepository + IPlanService (D-D: ObtenerLimitesAsync + ValidarLimitesParaTenantAsync)
/// + IAuthService (D-L: en el ctor; el hash BCrypt se hace directo en BLL, spec §3 paso 9) +
/// IEmailService (D-C: correo de activación fire-and-forget) + TenantContext real
/// (D17: TenantId nullable; D12: re-validación de rol en BLL).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de ResponsableService: (ICicloRepository, IPlanService, IAuthService, IEmailService,
/// TenantContext) + overload con ILogger (D-L) — se usa el ctor de 5 args.
/// Escrituras (INSERT usuario + sync usuario.area_id + area.responsable_id + auditoría) en UNA
/// sola transacción IDbTransaction (patrón HU-001..HU-009); captura SQLSTATE 23505 →
/// ValidacionException + rollback (ADR-001). Detección de constraint por ex.ConstraintName
/// (settable vía el ctor público completo de Npgsql 8.0.5, parámetro constraintName):
/// uq_usuario_correo → correo duplicado; uq_area_responsable_unico (ADR-007) → RN-012.
/// Auditoría (ADR-003): Entidad="Responsable", JSON legible con UnsafeRelaxedJsonEscaping;
/// desactivar se audita como DEACTIVATE (D-K, enum accion_auditoria L46).
/// SEC-07: responsable SÍ es entidad de área → ListarAsync filtra por TenantContext.AreaId si rol
/// JefeArea (DAL-R3 con areaIdFiltro); ObtenerPorIdAsync lanza 403 si JefeArea pide otro
/// responsable (D12). Sync usuario.area_id al crear/reasignar (D-J); NO se limpia al desactivar.
/// CONTRATOS ADICIONALES (observaciones de Jorge): #23 Reasignar_MismaArea → no-op SIN auditoría
/// (InsertLogAsync NO se invoca); #32 Desactivar → DAL-A11 NO se invoca (usuario.area_id
/// conservado). CONTRATO #25: ResponsableResponse.Advertencia (string?) requerida por spec §4
/// paso 10 (el DTO del spec no la lista; @BackendDev debe incluirla). CONTRATO #10: Crear incluye
/// guarda RN-012 capa 1 vía DAL-A8 (la tabla de tests del spec la exige; el relato §3 no la lista
/// explícitamente — @BackendDev debe implementarla).
/// </summary>
public class ResponsableServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly Mock<IPlanService> _mockPlanService;
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly TenantContext _tenantContext;
    private readonly IResponsableService _service;

    public ResponsableServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _mockPlanService = new Mock<IPlanService>();
        _mockAuthService = new Mock<IAuthService>();
        _mockEmailService = new Mock<IEmailService>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "AdminTenant"
        };

        // Defaults para el flujo feliz: límites del plan (D-D) y validación a nivel tenant OK.
        // Los tests de límites (casos 11/12) los sobreescriben.
        _mockPlanService
            .Setup(s => s.ObtenerLimitesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanLimits(10, 50, 1));
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());

        _service = new ResponsableService(
            _mockRepo.Object, _mockPlanService.Object, _mockAuthService.Object,
            _mockEmailService.Object, _tenantContext);
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

    private static ResponsableEntity CrearResponsable(
        Guid? id = null, Guid? tenantId = null, Guid? cicloId = null, string nombre = "JUAN PEREZ",
        string correo = "juan@dicegsa.com", string rol = "JefeArea", string estado = "Activo",
        Guid? areaId = null, string? areaCodigo = null, string? areaNombre = null,
        bool requiereCambioPwd = true, DateTimeOffset? ultimoLogin = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ResponsableEntity
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            CicloId = cicloId ?? Guid.NewGuid(),
            Nombre = nombre,
            Correo = correo,
            Rol = rol,
            Estado = estado,
            AreaId = areaId,
            AreaCodigo = areaCodigo,
            AreaNombre = areaNombre,
            RequiereCambioPwd = requiereCambioPwd,
            UltimoLogin = ultimoLogin,
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now
        };
    }

    private static ResponsableCreateRequest CrearCreateRequestValido(
        string? nombre = "JUAN PEREZ", string? correo = "juan@dicegsa.com", Guid? areaId = null)
        => new()
        {
            Nombre = nombre ?? "JUAN PEREZ",
            Correo = correo ?? "juan@dicegsa.com",
            AreaId = areaId ?? Guid.NewGuid()
        };

    private static ResponsableReassignRequest CrearReassignRequestValido(Guid? areaId = null)
        => new() { AreaId = areaId ?? Guid.NewGuid() };

    /// <summary>Configura el flujo feliz de CrearAsync (spec §3): ciclo Borrador, área válida
    /// (activa, sin responsable), correo único, RN-012 capa 1 libre, límites OK (por tenant y a
    /// nivel tenant), tx con INSERT usuario + sync usuario.area_id (DAL-A11) + area.responsable_id
    /// (DAL-R6) + auditoría CREATE, re-lectura post-commit. El correo de activación se verifica
    /// en el caso 13.</summary>
    private void ConfigurarFlujoFelizCrear(
        Guid cicloId, Guid tenantId, ResponsableCreateRequest request, Guid nuevoId,
        ResponsableEntity responsableCreado, Mock<IDbTransaction>? mockTx = null)
    {
        var planId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0); // RN-012 capa 1 (guarda de Crear exigida por la tabla de tests del spec)
        _mockRepo
            .Setup(r => r.ExisteCorreoEnTenantAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.ContarUsuariosActivosEnTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)nuevoId);
        _mockRepo
            .Setup(r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsableCreado);
    }

    /// <summary>Configura el flujo feliz de ReasignarAsync (spec §4): ciclo Borrador, responsable
    /// existente, área destino válida (activa, sin responsable), RN-012 excluyendo self libre,
    /// advertencia de origen (si cambia de área), tx con sync usuario.area_id (DAL-A11) + liberar
    /// origen (DAL-R6 null) + asignar destino (DAL-R6 responsableId) + auditoría UPDATE, re-lectura.</summary>
    private void ConfigurarFlujoFelizReasignar(
        Guid cicloId, Guid responsableId, Guid tenantId, ResponsableReassignRequest request,
        ResponsableEntity responsable, ResponsableEntity reasignado, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .SetupSequence(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsable)      // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(reasignado);      // 2ª llamada: re-lectura post-commit
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL2", "CEDIS NORTE"));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0); // RN-012 excluyendo self (paso 7)
        if (responsable.AreaId is Guid areaOrigen && areaOrigen != request.AreaId)
        {
            _mockRepo
                .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, areaOrigen, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0); // el responsable es el único del área origen → advertencia (paso 8)
        }
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Configura el flujo feliz de DesactivarAsync (spec §5): ciclo Activo (D-H), responsable
    /// Activo con área, tx con estado=Inactivo (DAL-R7) + liberar area.responsable_id (DAL-R6 null)
    /// + auditoría DEACTIVATE, re-lectura. NO se configura ActualizarAreaIdUsuarioAsync (D-J: no se
    /// limpia usuario.area_id).</summary>
    private void ConfigurarFlujoFelizDesactivar(
        Guid cicloId, Guid responsableId, Guid tenantId, ResponsableEntity responsable,
        ResponsableEntity desactivado, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .SetupSequence(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsable)      // 1ª llamada: validación de existencia + snapshot
            .ReturnsAsync(desactivado);     // 2ª llamada: re-lectura post-commit
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.DesactivarUsuarioAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepo
            .Setup(r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    // ─── Caso 1-15. CrearAsync ──────────────────────────────────────────────

    // Caso 1 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A); InsertarUsuarioAsync nunca se llama
    [Fact]
    public async Task Crear_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO crea responsables (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();

        // Act & Assert: 403 y el INSERT nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
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
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
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
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
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
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ nombre con espacios → se persiste normalizado "JUAN PEREZ" (trim + colapso de espacios)
    [Fact]
    public async Task Crear_NombreConEspacios_NormalizaYPersiste()
    {
        // Arrange: "  JUAN   PEREZ " → "JUAN PEREZ" (spec §3 paso 5, mismo helper que TenantService/CicloService)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido(nombre: "  JUAN   PEREZ ");
        var nuevoId = Guid.NewGuid();
        var responsableCreado = CrearResponsable(nuevoId, tenantId, cicloId, "JUAN PEREZ",
            "juan@dicegsa.com", areaId: request.AreaId, areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, responsableCreado);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: el DTO de inserción y la respuesta llevan el nombre normalizado
        Assert.Equal("JUAN PEREZ", resultado.Nombre);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(
                It.Is<ResponsableInsertDto>(d =>
                    d.Nombre == "JUAN PEREZ" &&
                    d.Correo == "juan@dicegsa.com" &&
                    d.TenantId == tenantId &&
                    d.Rol == "JefeArea" &&
                    d.Estado == "Activo" &&
                    d.RequiereCambioPwd &&
                    d.AreaId == request.AreaId &&
                    !string.IsNullOrEmpty(d.PasswordHash)),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 6 ─ área inexistente (DAL-A2 null) → ValidacionException 422 (RN-011, CA #1)
    [Fact]
    public async Task Crear_AreaInexistente_LanzaValidacion()
    {
        // Arrange: DAL-A2 → null → 422 "El área no existe en este ciclo"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AreaEntity?)null);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("área no existe", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ área inactiva → ValidacionException 422 (RN-011, CA #1)
    [Fact]
    public async Task Crear_AreaInactiva_LanzaValidacion()
    {
        // Arrange: activa = false → 422 "El área debe estar activa para asignar un responsable"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA", activa: false));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("activa", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ área ya tiene responsable → ValidacionException 422 (RN-011)
    [Fact]
    public async Task Crear_AreaYaTieneResponsable_LanzaValidacion()
    {
        // Arrange: area.ResponsableId != null → 422 (RN-011: área Activa requiere exactamente un responsable)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA",
                responsableId: Guid.NewGuid()));

        // Act & Assert: 422 (RN-011) y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("responsable", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ correo duplicado en el tenant → ValidacionException 422 (DAL-R4 true)
    [Fact]
    public async Task Crear_CorreoDuplicadoEnTenant_LanzaValidacion()
    {
        // Arrange: DAL-R4 → true → 422 "Ya existe un usuario con el correo 'X' en este tenant"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ExisteCorreoEnTenantAsync(tenantId, "juan@dicegsa.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("correo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 10 ─ responsable ya asignado a otra área ACTIVA del ciclo → 422 (RN-012, capa 1 BLL)
    [Fact]
    public async Task Crear_ResponsableYaAsignadoOtraAreaActiva_LanzaValidacion()
    {
        // Arrange: RN-012 capa 1 — DAL-A8 > 0 → 422. CONTRATO: la tabla de tests del spec exige
        // esta guarda en Crear (el relato §3 no la lista explícitamente; @BackendDev debe implementarla).
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 (RN-012) y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("asignado", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 11 ─ tenant al tope de usuarios del plan → 422 (RN-010 a nivel usuario, D-D)
    [Fact]
    public async Task Crear_TenantAlTopeUsuarios_LanzaValidacion()
    {
        // Arrange: D-D — chequeo PRECISO por tenant: DAL-R5 (50) >= MaxUsuarios (50) → 422
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ObtenerLimitesAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanLimits(10, 50, 1)); // plan Estándar: máx 50 usuarios
        _mockRepo
            .Setup(r => r.ContarUsuariosActivosEnTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50); // 50 >= 50 → tope

        // Act & Assert: 422 (RN-010) y el INSERT nunca se ejecuta; la guarda a nivel tenant NO se consulta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("usuarios", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 12 ─ tenant excede límite del plan → 422 (CA #2 HU-002, guarda a nivel tenant)
    [Fact]
    public async Task Crear_TenantExcedeLimitePlan_LanzaValidacion()
    {
        // Arrange: el chequeo por tenant pasa (10 < 50) pero la guarda a nivel tenant falla
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockRepo
            .Setup(r => r.ContarUsuariosActivosEnTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10); // 10 < 50 → el chequeo por tenant pasa
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Fallo(["El tenant supera el límite de usuarios: 60 > 50"]));

        // Act & Assert: 422 con los errores del plan; el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("usuarios", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockPlanService.Verify(
            s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 13 ★ ─ éxito: INSERT usuario + sync usuario.area_id (DAL-A11, SEC-07 D-J) +
    // area.responsable_id (DAL-R6) + auditoría CREATE en la MISMA transacción, commit, re-lectura
    // → 201; correo de activación con pwd temporal (D-C)
    [Fact]
    public async Task Crear_Exito_InsertaUsuarioSincronizaAreaYEnviaCorreo()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var nuevoId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var responsableCreado = CrearResponsable(nuevoId, tenantId, cicloId, "JUAN PEREZ",
            "juan@dicegsa.com", areaId: request.AreaId, areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");

        ConfigurarFlujoFelizCrear(cicloId, tenantId, request, nuevoId, responsableCreado, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: INSERT usuario + sync usuario.area_id (DAL-A11) + area.responsable_id (DAL-R6) +
        // auditoría CREATE con la MISMA tx (D1), commit, re-lectura y correo de activación
        Assert.Equal(nuevoId, resultado.Id);
        Assert.Equal("JUAN PEREZ", resultado.Nombre);
        Assert.Equal("juan@dicegsa.com", resultado.Correo);
        Assert.Equal(request.AreaId, resultado.AreaId);
        Assert.Equal("JefeArea", resultado.Rol);
        Assert.Equal("Activo", resultado.Estado);
        _mockRepo.Verify(
            r => r.InsertarUsuarioAsync(
                It.Is<ResponsableInsertDto>(d =>
                    d.TenantId == tenantId &&
                    d.Nombre == "JUAN PEREZ" &&
                    d.Correo == "juan@dicegsa.com" &&
                    d.Rol == "JefeArea" &&
                    d.Estado == "Activo" &&
                    d.RequiereCambioPwd &&
                    d.AreaId == request.AreaId &&
                    !string.IsNullOrEmpty(d.PasswordHash)),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(
                nuevoId, request.AreaId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.AsignarResponsableAreaAsync(
                tenantId, cicloId, request.AreaId, nuevoId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Responsable" &&
                    l.EntidadId == nuevoId.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("JUAN PEREZ")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
        _mockEmailService.Verify(
            s => s.EnviarActivacionResponsableAsync(
                "juan@dicegsa.com", "JUAN PEREZ",
                It.Is<string>(p => !string.IsNullOrEmpty(p)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 14 ─ colisión concurrente 23505 (uq_usuario_correo, UNIQUE global DDL L85) → rollback +
    // ValidacionException 422 (patrón ADR-001/002)
    [Fact]
    public async Task Crear_ColisionCorreo23505_LanzaValidacion()
    {
        // Arrange: el INSERT lanza PostgresException 23505 con constraint uq_usuario_correo
        // (carrera TOCTOU capturada por el UNIQUE global; ConstraintName seteable vía ctor Npgsql 8.0.5)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _mockRepo
            .Setup(r => r.ContarUsuariosActivosEnTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",
                "23505",
                constraintName: "uq_usuario_correo"));

        // Act & Assert: 422 con mensaje amigable y rollback explícito de la transacción
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("correo", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // Caso 15 ─ colisión concurrente 23505 (uq_area_responsable_unico, ADR-007 RN-012) → rollback +
    // ValidacionException 422
    [Fact]
    public async Task Crear_ColisionResponsableUnico23505_LanzaValidacion()
    {
        // Arrange: DAL-R6 (UPDATE area SET responsable_id) lanza 23505 con constraint
        // uq_area_responsable_unico (ADR-007, RN-012 capa 2 BD ante carrera TOCTOU)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL1", "CEDIS FARMA"));
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _mockRepo
            .Setup(r => r.ContarUsuariosActivosEnTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.InsertarUsuarioAsync(It.IsAny<ResponsableInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)Guid.NewGuid());
        _mockRepo
            .Setup(r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",
                "23505",
                constraintName: "uq_area_responsable_unico"));

        // Act & Assert: 422 (RN-012) y rollback explícito
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("asignado a otra área activa", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ─── Caso 16-27. ReasignarAsync ─────────────────────────────────────────

    // Caso 16 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A)
    [Fact]
    public async Task Reasignar_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO reasigna responsables (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();

        // Act & Assert: 403 y el sync nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 17 ─ responsable inexistente (DAL-R2 null) → NotFoundException 404
    [Fact]
    public async Task Reasignar_ResponsableInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResponsableEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ReasignarAsync(cicloId, responsableId, request));
    }

    // Caso 18 ─ ciclo Cerrado → ValidacionException 422 (RC-12: solo lectura)
    [Fact]
    public async Task Reasignar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — el responsable existe (paso 4) antes del chequeo de Cerrado (paso 5)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));

        // Act & Assert: 422 y el sync nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 19 ─ área destino inexistente (DAL-A2 null) → ValidacionException 422
    [Fact]
    public async Task Reasignar_AreaDestinoInexistente_LanzaValidacion()
    {
        // Arrange: DAL-A2 → null → 422 "El área destino no existe en este ciclo"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AreaEntity?)null);

        // Act & Assert: 422 y el sync nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("área destino no existe", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 20 ─ área destino inactiva → ValidacionException 422
    [Fact]
    public async Task Reasignar_AreaDestinoInactiva_LanzaValidacion()
    {
        // Arrange: areaDestino.Activa = false → 422 "El área destino debe estar activa"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL2", "CEDIS NORTE", activa: false));

        // Act & Assert: 422 y el sync nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("activa", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 21 ─ área destino ya tiene responsable → ValidacionException 422 (RN-011)
    [Fact]
    public async Task Reasignar_AreaDestinoYaTieneResponsable_LanzaValidacion()
    {
        // Arrange: areaDestino.ResponsableId != null → 422 (RN-011)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL2", "CEDIS NORTE",
                responsableId: Guid.NewGuid()));

        // Act & Assert: 422 (RN-011) y el sync nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("responsable", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 22 ─ responsable ya en otra área ACTIVA del ciclo → 422 (RN-012 excluyendo self)
    [Fact]
    public async Task Reasignar_ResponsableYaEnOtraAreaActiva_LanzaValidacion()
    {
        // Arrange: DAL-A8 con excludeAreaId = request.AreaId > 0 → RN-012 (el self se excluye)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var request = CrearReassignRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(request.AreaId, tenantId, cicloId, "GOL2", "CEDIS NORTE"));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert: 422 (RN-012) y el sync nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("asignado", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 23 ★ ─ nuevaAreaId == areaActual → no-op: 200 sin UPDATE, sin sync, sin auditoría
    // (observación de Jorge: InsertLogAsync NO se invoca)
    [Fact]
    public async Task Reasignar_MismaArea_NoHaceNada_RetornaOk()
    {
        // Arrange: request.AreaId == responsable.AreaId → la BLL corta sin tocar nada
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaActual = Guid.NewGuid();
        var request = CrearReassignRequestValido(areaId: areaActual);
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaActual,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsable);

        // Act
        var resultado = await _service.ReasignarAsync(cicloId, responsableId, request);

        // Assert: 200 con la misma área y NINGUNA escritura (ni tx, ni sync, ni auditoría)
        Assert.Equal(areaActual, resultado.AreaId);
        _mockRepo.Verify(
            r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 24 ★ ─ cambio de área: sync usuario.area_id (DAL-A11) + liberar origen (DAL-R6 null) +
    // asignar destino (DAL-R6 responsableId) en la MISMA transacción (SEC-07, D-J, D-E)
    [Fact]
    public async Task Reasignar_CambioArea_LiberaOrigenAsignaDestino_SincronizaUsuario()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaOrigen = Guid.NewGuid();
        var areaDestino = Guid.NewGuid();
        var request = CrearReassignRequestValido(areaId: areaDestino);
        var mockTx = new Mock<IDbTransaction>();
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaOrigen,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        var reasignado = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaDestino,
            areaCodigo: "GOL2", areaNombre: "CEDIS NORTE");

        ConfigurarFlujoFelizReasignar(cicloId, responsableId, tenantId, request, responsable, reasignado, mockTx);

        // Act
        var resultado = await _service.ReasignarAsync(cicloId, responsableId, request);

        // Assert: sync usuario.area_id (DAL-A11) + liberar origen (DAL-R6 null) + asignar destino
        // (DAL-R6 responsableId), todos con la MISMA tx (D1); commit
        Assert.Equal(areaDestino, resultado.AreaId);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(
                responsableId, areaDestino, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.AsignarResponsableAreaAsync(
                tenantId, cicloId, areaOrigen, null, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.AsignarResponsableAreaAsync(
                tenantId, cicloId, areaDestino, responsableId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 25 ─ el área origen queda sin responsable → response con Advertencia (spec §4 paso 8)
    [Fact]
    public async Task Reasignar_OrigenQuedaSinResponsable_IncluyeAdvertencia()
    {
        // Arrange: el responsable es el ÚNICO del área origen (DAL-A8 con excludeAreaId=origen → 0)
        // → la BLL NO bloquea (decisión del ADM) pero el response incluye la advertencia
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaOrigen = Guid.NewGuid();
        var areaDestino = Guid.NewGuid();
        var request = CrearReassignRequestValido(areaId: areaDestino);
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaOrigen,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        var reasignado = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaDestino,
            areaCodigo: "GOL2", areaNombre: "CEDIS NORTE");

        ConfigurarFlujoFelizReasignar(cicloId, responsableId, tenantId, request, responsable, reasignado);

        // Act
        var resultado = await _service.ReasignarAsync(cicloId, responsableId, request);

        // Assert: advertencia incluida en el response (CONTRATO: ResponsableResponse.Advertencia)
        Assert.Equal("El área origen quedará sin responsable asignado", resultado.Advertencia);
    }

    // Caso 26 ─ éxito: UPDATE usuario.area_id + area.responsable_id (origen y destino) + auditoría
    // UPDATE con snapshot previo (ADR-003) → 200
    [Fact]
    public async Task Reasignar_Exito_ActualizaYAuditaUpdate()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaOrigen = Guid.NewGuid();
        var areaDestino = Guid.NewGuid();
        var request = CrearReassignRequestValido(areaId: areaDestino);
        var mockTx = new Mock<IDbTransaction>();
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaOrigen,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        var reasignado = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaDestino,
            areaCodigo: "GOL2", areaNombre: "CEDIS NORTE");

        ConfigurarFlujoFelizReasignar(cicloId, responsableId, tenantId, request, responsable, reasignado, mockTx);

        // Act
        var resultado = await _service.ReasignarAsync(cicloId, responsableId, request);

        // Assert: auditoría UPDATE con snapshot previo (ADR-003) en la MISMA tx
        Assert.Equal(areaDestino, resultado.AreaId);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Responsable" &&
                    l.EntidadId == responsableId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("CEDIS FARMA") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("CEDIS NORTE")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 27 ─ colisión concurrente 23505 (uq_area_responsable_unico, ADR-007) → rollback + 422
    [Fact]
    public async Task Reasignar_ColisionResponsableUnico23505_LanzaValidacion()
    {
        // Arrange: DAL-R6 (asignar destino) lanza 23505 con constraint uq_area_responsable_unico
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaOrigen = Guid.NewGuid();
        var areaDestino = Guid.NewGuid();
        var request = CrearReassignRequestValido(areaId: areaDestino);
        var mockTx = new Mock<IDbTransaction>();
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaOrigen,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsable);
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearArea(areaDestino, tenantId, cicloId, "GOL2", "CEDIS NORTE"));
        _mockRepo
            .Setup(r => r.ContarAreasConResponsableAsync(tenantId, cicloId, responsableId, request.AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.AsignarResponsableAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",
                "23505",
                constraintName: "uq_area_responsable_unico"));

        // Act & Assert: 422 (RN-012) y rollback explícito
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ReasignarAsync(cicloId, responsableId, request));

        Assert.Contains("asignado a otra área activa", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ─── Caso 28-32. DesactivarAsync ────────────────────────────────────────

    // Caso 28 ─ rol ≠ AdminTenant → AccesoDenegadoException 403 (D12, D-A)
    [Fact]
    public async Task Desactivar_RolNoAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D-A — el Gerente NO desactiva responsables (escritura ADM-only)
        _tenantContext.Rol = "Gerente";
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();

        // Act & Assert: 403 y el UPDATE de desactivación nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.DesactivarAsync(cicloId, responsableId));

        _mockRepo.Verify(
            r => r.DesactivarUsuarioAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 29 ─ responsable inexistente (DAL-R2 null) → NotFoundException 404
    [Fact]
    public async Task Desactivar_ResponsableInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResponsableEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DesactivarAsync(cicloId, responsableId));
    }

    // Caso 30 ─ responsable ya inactivo → ValidacionException 422 (D-F: no se re-desactiva)
    [Fact]
    public async Task Desactivar_YaInactivo_LanzaValidacion()
    {
        // Arrange: estado = Inactivo → 422 "El responsable ya está inactivo"
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, estado: "Inactivo",
                areaId: Guid.NewGuid()));

        // Act & Assert: 422 y el UPDATE de desactivación nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.DesactivarAsync(cicloId, responsableId));

        Assert.Contains("inactivo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.DesactivarUsuarioAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 31 ─ ciclo Cerrado → ValidacionException 422 (RC-12: solo lectura)
    [Fact]
    public async Task Desactivar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — el responsable existe (paso 4) antes del chequeo de Cerrado (paso 5)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResponsable(responsableId, tenantId, cicloId, areaId: Guid.NewGuid()));

        // Act & Assert: 422 y el UPDATE de desactivación nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.DesactivarAsync(cicloId, responsableId));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.DesactivarUsuarioAsync(It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 32 ★ ─ éxito: estado=Inactivo (DAL-R7) + liberar area.responsable_id (DAL-R6 null) +
    // auditoría DEACTIVATE; DAL-A11 NO se invoca (usuario.area_id conservado — D-J, SEC-07) → 200
    [Fact]
    public async Task Desactivar_Exito_EstadoInactivoLiberaArea_NoLimpiaAreaIdUsuario()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: areaId,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        var desactivado = CrearResponsable(responsableId, tenantId, cicloId, estado: "Inactivo",
            areaId: areaId, areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");

        ConfigurarFlujoFelizDesactivar(cicloId, responsableId, tenantId, responsable, desactivado, mockTx);

        // Act
        var resultado = await _service.DesactivarAsync(cicloId, responsableId);

        // Assert: estado=Inactivo (DAL-R7) + liberar area.responsable_id (DAL-R6 null) + auditoría
        // DEACTIVATE con snapshot previo; usuario.area_id NO se toca (DAL-A11 nunca se invoca)
        Assert.Equal("Inactivo", resultado.Estado);
        _mockRepo.Verify(
            r => r.DesactivarUsuarioAsync(responsableId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.AsignarResponsableAreaAsync(
                tenantId, cicloId, areaId, null, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.ActualizarAreaIdUsuarioAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "DEACTIVATE" &&
                    l.Entidad == "Responsable" &&
                    l.EntidadId == responsableId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("CEDIS FARMA")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // ─── Caso 33-35. Lecturas ───────────────────────────────────────────────

    // Caso 33 ─ rol JefeArea → DAL-R3 invocado con areaIdFiltro = TenantContext.AreaId (SEC-07)
    [Fact]
    public async Task Listar_RolJefeArea_FiltraSoloSuResponsable()
    {
        // Arrange: SEC-07 — el JefeArea solo ve SU responsable (AND a.id = @AreaId en el DAL)
        _tenantContext.Rol = "JefeArea";
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = Guid.NewGuid();
        _tenantContext.AreaId = areaId;
        var cicloId = Guid.NewGuid();
        var responsable = CrearResponsable(tenantId: tenantId, cicloId: cicloId, areaId: areaId,
            areaCodigo: "GOL1", areaNombre: "CEDIS FARMA");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarResponsablesAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResponsableEntity> { responsable });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: el DAL se consulta con areaIdFiltro = TenantContext.AreaId (SEC-07)
        Assert.Single(resultado);
        Assert.Equal(areaId, resultado[0].AreaId);
        _mockRepo.Verify(
            r => r.ListarResponsablesAsync(tenantId, cicloId, areaId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 34 ─ rol AdminTenant/Gerente → DAL-R3 invocado con areaIdFiltro = null (todos)
    [Fact]
    public async Task Listar_RolAdmin_Gerente_RetornaTodos()
    {
        // Arrange: ADM (default del ctor) y GER ven TODOS los responsables del ciclo (sin filtro SEC-07)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var r1 = CrearResponsable(tenantId: tenantId, cicloId: cicloId, nombre: "ANA LOPEZ",
            correo: "ana@dicegsa.com", areaId: Guid.NewGuid(), areaCodigo: "GOL1");
        var r2 = CrearResponsable(tenantId: tenantId, cicloId: cicloId, nombre: "JUAN PEREZ",
            correo: "juan@dicegsa.com", areaId: Guid.NewGuid(), areaCodigo: "GOL2");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarResponsablesAsync(tenantId, cicloId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResponsableEntity> { r1, r2 });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: sin filtro de área (areaIdFiltro = null) → todos los responsables
        Assert.Equal(2, resultado.Count);
        _mockRepo.Verify(
            r => r.ListarResponsablesAsync(tenantId, cicloId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 35 ─ rol JefeArea pidiendo OTRO responsable → AccesoDenegadoException 403 (D12, SEC-07)
    [Fact]
    public async Task ObtenerPorId_RolJefeAreaOtraArea_LanzaAccesoDenegado()
    {
        // Arrange: el JefeArea solo accede a SU responsable (responsable.AreaId != TenantContext.AreaId → 403)
        _tenantContext.Rol = "JefeArea";
        var tenantId = _tenantContext.TenantId!.Value;
        _tenantContext.AreaId = Guid.NewGuid(); // área del JefeArea
        var cicloId = Guid.NewGuid();
        var responsableId = Guid.NewGuid();
        var otraAreaId = Guid.NewGuid();
        var responsable = CrearResponsable(responsableId, tenantId, cicloId, areaId: otraAreaId,
            areaCodigo: "GOL2", areaNombre: "CEDIS NORTE");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerResponsablePorIdAsync(tenantId, cicloId, responsableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responsable);

        // Act & Assert: 403 (D12) — el responsable consultado no es del área del contexto
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerPorIdAsync(cicloId, responsableId));
    }
}
using System.Data;
using Moq;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para PilarService — Spec HU-013 § "Tests requeridos" (34 casos de la tabla,
/// L305-353). TDD fase red por contrato (TEST-01): IPilarService/PilarService/PilarEntity/
/// PilarCreateRequest/PilarUpdateRequest/PilarResponse/PilarInsertDto/PilarUpdateDto/
/// SiguienteSecuenciaPilarDto/PilarConteosDto/PilarDependenciasDto y las extensiones DAL-P1..P9
/// de ICicloRepository AÚN NO existen (@BackendDev los implementa en fase 4) → esta clase NO
/// compila hasta entonces (rojo esperado por compilación, mismo patrón que AreaServiceTests HU-009
/// y FilosofiaServiceTests HU-011).
/// Moq sobre ICicloRepository + TenantContext real (D17: TenantId nullable; D12: re-validación
/// de rol en BLL — POST/PUT/DELETE solo Gerente, RN-006/D-G; GET multi-rol ADM/GER/JEF, CA #4).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de PilarService: (ICicloRepository, TenantContext) + overload con ILogger (D13) —
/// se usa el ctor de 2 args.
/// Contrato DAL (firmas exactas que @BackendDev debe implementar en ICicloRepository):
///   · DAL-P1 Task&lt;IEnumerable&lt;PilarConteosDto&gt;&gt; ListarPilaresConConteosAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
///   · DAL-P2 Task&lt;PilarConteosDto?&gt; ObtenerPilarConConteosAsync(Guid tenantId, Guid cicloId, Guid pilarId, CancellationToken ct = default)
///   · DAL-P3 Task&lt;PilarEntity?&gt; ObtenerPilarAsync(Guid tenantId, Guid cicloId, Guid pilarId, CancellationToken ct = default)
///   · DAL-P4 Task&lt;int&gt; ContarPilaresDelCicloAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
///   · DAL-P5 Task&lt;SiguienteSecuenciaPilarDto&gt; ObtenerSiguienteSecuenciaPilarAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
///   · DAL-P6 Task&lt;Guid?&gt; InsertarPilarAsync(PilarInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)  // RETURNING id (patrón InsertarAreaAsync)
///   · DAL-P7 Task&lt;int&gt; ActualizarPilarAsync(PilarUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
///   · DAL-P8 Task&lt;int&gt; EliminarPilarAsync(Guid tenantId, Guid cicloId, Guid pilarId, IDbTransaction? tx = null, CancellationToken ct = default)
///   · DAL-P9 Task&lt;PilarDependenciasDto&gt; ContarDependenciasPilarAsync(Guid tenantId, Guid pilarId, CancellationToken ct = default)
/// Escrituras (INSERT/UPDATE/DELETE + auditoría) en UNA sola transacción IDbTransaction (patrón
/// HU-001..HU-012); captura SQLSTATE 23505 (Crear, UNIQUE (ciclo_id, codigo) DDL L190) y 23503
/// (Eliminar, FK objetivo_cg.pilar_id/okr.pilar_id sin CASCADE) → ValidacionException + rollback
/// (ADR-007 / HU-002 D2). Auditoría (ADR-003): Entidad="Pilar", snapshot SOLO del alcance HU-013
/// {"codigo","nombre","estrategia_victoria","orden"} (D-K); valor_anterior=null en CREATE,
/// valor_nuevo=null en DELETE.
/// SEC-07 NO APLICA (D-E): pilar es corporativa (sin area_id; RLS solo por tenant) → el JEF lee
/// TODOS los pilares del ciclo (RN-007: solo lectura), sin AND area_id.
/// </summary>
public class PilarServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly TenantContext _tenantContext;
    private readonly IPilarService _service;

    public PilarServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "Gerente" // D-G/RN-006: el GER es el único con escritura de pilares
        };

        _service = new PilarService(_mockRepo.Object, _tenantContext);
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

    /// <summary>Fila del listado/detalle con conteos (CA #5, D-D) — DAL-P1/P2. Sin TenantId
    /// (el response lo toma del TenantContext, SEC-06).</summary>
    private static PilarConteosDto CrearPilarConteos(
        Guid? id = null, Guid? cicloId = null, string codigo = "PEC-1", string nombre = "Crecimiento",
        string? estrategiaVictoria = null, int orden = 1, int totalObjetivosCg = 0, int totalOkrs = 0,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CicloId = cicloId ?? Guid.NewGuid(),
            Codigo = codigo,
            Nombre = nombre,
            EstrategiaVictoria = estrategiaVictoria,
            Orden = orden,
            TotalObjetivosCg = totalObjetivosCg,
            TotalOkrs = totalOkrs,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
        };

    /// <summary>Entidad pilar sin conteos — DAL-P3 (validaciones/update/delete).</summary>
    private static PilarEntity CrearPilarEntity(
        Guid? id = null, Guid? tenantId = null, Guid? cicloId = null, string codigo = "PEC-1",
        string nombre = "Crecimiento", string? estrategiaVictoria = null, int orden = 1,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            CicloId = cicloId ?? Guid.NewGuid(),
            Codigo = codigo,
            Nombre = nombre,
            EstrategiaVictoria = estrategiaVictoria,
            Orden = orden,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
        };

    private static PilarCreateRequest CrearCreateRequest(
        string? nombre = "Crecimiento", string? estrategiaVictoria = null, int? orden = null)
        => new()
        {
            Nombre = nombre ?? "Crecimiento",
            EstrategiaVictoria = estrategiaVictoria,
            Orden = orden
        };

    private static PilarUpdateRequest CrearUpdateRequest(
        string? nombre = "Crecimiento", string? estrategiaVictoria = null, int? orden = null)
        => new()
        {
            Nombre = nombre ?? "Crecimiento",
            EstrategiaVictoria = estrategiaVictoria,
            Orden = orden
        };

    /// <summary>Configura el flujo feliz de CrearAsync (spec §3): ciclo Borrador (D-F), conteo &lt; 8
    /// (CA #2), secuencia PEC-N/orden (DAL-P5), tx con INSERT (DAL-P6) + auditoría CREATE (DAL-C11),
    /// re-lectura post-commit (DAL-P2, paso 10).</summary>
    private void ConfigurarFlujoFelizCrear(
        Guid cicloId, Guid tenantId, SiguienteSecuenciaPilarDto secuencia,
        PilarConteosDto postCommit, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ContarPilaresDelCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerSiguienteSecuenciaPilarAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secuencia);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(postCommit.Id);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ObtenerPilarConConteosAsync(tenantId, cicloId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(postCommit);
    }

    /// <summary>Configura el flujo feliz de ActualizarAsync (spec §4): ciclo Borrador (D-F),
    /// pilar original (DAL-P3, paso 5), tx con UPDATE (DAL-P7) + auditoría UPDATE (DAL-C11),
    /// re-lectura post-commit (DAL-P2, paso 10).</summary>
    private void ConfigurarFlujoFelizActualizar(
        Guid cicloId, Guid tenantId, Guid pilarId, PilarEntity original,
        PilarConteosDto postCommit, Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.ActualizarPilarAsync(It.IsAny<PilarUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(postCommit);
    }

    /// <summary>Configura el flujo feliz de EliminarAsync (spec §5): ciclo Borrador (D-F),
    /// pilar original (DAL-P3, paso 5), sin dependencias (DAL-P9 → 0/0, CA #3), tx con DELETE
    /// (DAL-P8) + auditoría DELETE (DAL-C11).</summary>
    private void ConfigurarFlujoFelizEliminar(
        Guid cicloId, Guid tenantId, Guid pilarId, PilarEntity original,
        Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);
        _mockRepo
            .Setup(r => r.ContarDependenciasPilarAsync(tenantId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PilarDependenciasDto { TotalObjetivosCg = 0, TotalOkrs = 0 });
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.EliminarPilarAsync(tenantId, cicloId, pilarId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Crear (POST) — Spec HU-013 § "Tests requeridos" casos 1-14
    // ═══════════════════════════════════════════════════════════════════════

    // Caso 1 ─ rol ≠ Gerente → AccesoDenegadoException 403 (D12, D-G/RN-006); InsertarPilarAsync nunca se llama
    [Fact]
    public async Task Crear_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: RN-006 — el ADM gestiona estructura, NO escribe contenido estratégico
        _tenantContext.Rol = "AdminTenant";
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();

        // Act & Assert: 403 y el INSERT nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task Crear_SinTenant_LanzaNotFound()
    {
        // Arrange: D17 — TenantId null (SuperAdmin sin tenant)
        _tenantContext.TenantId = null;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404 (el filtro tenant evita fuga)
    [Fact]
    public async Task Crear_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y el INSERT nunca se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CrearAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ ciclo Cerrado → ValidacionException 422 (RC-12, D-F: Borrador y Activo permitidos); sin INSERT
    [Fact]
    public async Task Crear_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ Nombre=" " → 422 (trim primero; " " tras trim = vacío)
    [Fact]
    public async Task Crear_NombreVacio_LanzaValidacion()
    {
        // Arrange: paso 6 — re-validación BLL (fuente de verdad, UX-04)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(nombre: " ");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y ni transacción ni INSERT
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 6 ─ nombre de 151 chars → 422 (VARCHAR(150))
    [Fact]
    public async Task Crear_NombreExcede150_LanzaValidacion()
    {
        // Arrange: VARCHAR(150) — 151 chars → 422
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(nombre: new string('a', 151));
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("150", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ estrategia de 2001 chars → 422 (D-C: máx 2000)
    [Fact]
    public async Task Crear_EstrategiaExcede2000_LanzaValidacion()
    {
        // Arrange: D-C — máx 2000 chars para estrategia_victoria (TEXT sin límite en DDL)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(estrategiaVictoria: new string('a', 2001));
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("2000", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ Orden=-1 → 422 (D-H: el orden no puede ser negativo)
    [Fact]
    public async Task Crear_OrdenNegativo_LanzaValidacion()
    {
        // Arrange: D-H — el orden no puede ser negativo
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(orden: -1);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el INSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("orden", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ ContarPilaresDelCicloAsync → 8 → 422 (CA #2: máximo 8 pilares por ciclo, D-C)
    [Fact]
    public async Task Crear_8PilaresExistentes_LanzaValidacion()
    {
        // Arrange: CA #2 (D-C) — constante MaxPilaresPorCiclo = 8
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ContarPilaresDelCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(8);

        // Act & Assert: 422 (CA #2) y ni secuencia ni INSERT
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("8", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ObtenerSiguienteSecuenciaPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 10 ★ ─ código PEC-N auto-generado (CA #1, D-B): con PEC-1/PEC-2 existentes DAL-P5
    // devuelve SiguienteN=3 → codigo="PEC-3"; sin pilares (SiguienteN=1) → "PEC-1" (mismo path)
    [Fact]
    public async Task Crear_Exito_GeneraCodigoPECN()
    {
        // Arrange: CA #1 — el código NO viaja en el request; la BLL lo genera desde DAL-P5
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();
        var postCommit = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-3", nombre: request.Nombre);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizCrear(cicloId, tenantId,
            new SiguienteSecuenciaPilarDto { SiguienteN = 3, SiguienteOrden = 4 },
            postCommit, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: el DTO insertado lleva el código PEC-N generado por la BLL (CA #1) + commit
        Assert.Equal("PEC-3", resultado.Codigo);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(
                It.Is<PilarInsertDto>(d => d.Codigo == "PEC-3" && d.CicloId == cicloId && d.TenantId == tenantId),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 11 ─ Orden=null → orden = SiguienteOrden de DAL-P5 (D-H: default secuencial MAX(orden)+1)
    [Fact]
    public async Task Crear_Exito_OrdenDefaultSecuencial()
    {
        // Arrange: D-H — Orden=null → se persiste el orden secuencial calculado por DAL-P5
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(orden: null);
        var postCommit = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-2", nombre: request.Nombre, orden: 4);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizCrear(cicloId, tenantId,
            new SiguienteSecuenciaPilarDto { SiguienteN = 2, SiguienteOrden = 4 },
            postCommit, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: se persiste SiguienteOrden (4), no un default fijo (D-H)
        Assert.Equal(4, resultado.Orden);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(
                It.Is<PilarInsertDto>(d => d.Orden == 4),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 12 ─ Orden=7 explícito → se persiste 7 (D-H: el orden explícito del request se respeta)
    [Fact]
    public async Task Crear_ConOrdenExplicito_RespetaOrden()
    {
        // Arrange: D-H — Orden=7 explícito → se persiste 7 (no el default secuencial)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(orden: 7);
        var postCommit = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-1", nombre: request.Nombre, orden: 7);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizCrear(cicloId, tenantId,
            new SiguienteSecuenciaPilarDto { SiguienteN = 1, SiguienteOrden = 1 },
            postCommit, mockTx);

        // Act
        var resultado = await _service.CrearAsync(cicloId, request);

        // Assert: el orden explícito del request se respeta (D-H)
        Assert.Equal(7, resultado.Orden);
        _mockRepo.Verify(
            r => r.InsertarPilarAsync(
                It.Is<PilarInsertDto>(d => d.Orden == 7),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 13 ★ ─ auditoría CREATE: entidad "Pilar", valor_anterior=null, snapshot del alcance
    // HU-013 {"codigo","nombre","estrategia_victoria","orden"} (ADR-003, D-K)
    [Fact]
    public async Task Crear_Exito_RegistraAuditoriaCreate()
    {
        // Arrange: D-K/ADR-003 — se audita SOLO lo que gestiona HU-013 (sin objetivo_q1..q4)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest(nombre: "Crecimiento", estrategiaVictoria: "Expandir mercado");
        var postCommit = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-1", nombre: "Crecimiento",
            estrategiaVictoria: "Expandir mercado", orden: 1);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizCrear(cicloId, tenantId,
            new SiguienteSecuenciaPilarDto { SiguienteN = 1, SiguienteOrden = 1 },
            postCommit, mockTx);

        // Act
        await _service.CrearAsync(cicloId, request);

        // Assert: auditoría CREATE con snapshot del alcance HU-013 (D-K, ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "CREATE" &&
                    l.Entidad == "Pilar" &&
                    l.EntidadId == postCommit.Id.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("\"codigo\"") &&
                    l.ValorNuevo.Contains("\"nombre\"") &&
                    l.ValorNuevo.Contains("\"estrategia_victoria\"") &&
                    l.ValorNuevo.Contains("\"orden\"") &&
                    l.ValorNuevo.Contains("Crecimiento")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 14 ─ PostgresException 23505 (UNIQUE (ciclo_id, codigo), DDL L190) → rollback + 422
    // (ADR-007: carrera TOCTOU de código PEC-N entre dos GER en paralelo)
    [Fact]
    public async Task Crear_23505_LanzaValidacion()
    {
        // Arrange: el INSERT lanza PostgresException 23505 (capa 2 del UNIQUE (ciclo_id, codigo))
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearCreateRequest();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ContarPilaresDelCicloAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerSiguienteSecuenciaPilarAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaPilarDto { SiguienteN = 1, SiguienteOrden = 1 });
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.InsertarPilarAsync(It.IsAny<PilarInsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",   // invariantSeverity (puede repetir "ERROR")
                "23505")); // sqlState ← código 23505 (UNIQUE (ciclo_id, codigo), DDL L190)

        // Act & Assert: 422 con mensaje amigable y rollback explícito (ADR-007)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(cicloId, request));

        Assert.Contains("PEC", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Actualizar (PUT) — Spec HU-013 § "Tests requeridos" casos 15-21
    // ═══════════════════════════════════════════════════════════════════════

    // Caso 15 ─ rol ≠ Gerente → AccesoDenegadoException 403 (D12, D-G/RN-006); ActualizarPilarAsync nunca se llama
    [Fact]
    public async Task Actualizar_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: RN-007 — el JEF solo lee; no edita pilares
        _tenantContext.Rol = "JefeArea";
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest();

        // Act & Assert: 403 y el UPDATE nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarAsync(cicloId, pilarId, request));

        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(It.IsAny<PilarUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 16 ─ ciclo Cerrado → ValidacionException 422 (RC-12); sin UPDATE
    [Fact]
    public async Task Actualizar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, pilarId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(It.IsAny<PilarUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 17 ─ pilar inexistente (DAL-P3 null) → NotFoundException 404
    [Fact]
    public async Task Actualizar_PilarInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PilarEntity?)null);

        // Act & Assert: 404 y el UPDATE nunca se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(cicloId, pilarId, request));

        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(It.IsAny<PilarUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 18 ─ Nombre=" " → 422 (trim primero; mismas reglas que POST, pasos 5-6)
    [Fact]
    public async Task Actualizar_NombreVacio_LanzaValidacion()
    {
        // Arrange: re-validación BLL (fuente de verdad, UX-04)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest(nombre: " ");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearPilarEntity(pilarId, tenantId, cicloId));

        // Act & Assert: 422 y el UPDATE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, pilarId, request));

        Assert.Contains("nombre", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(It.IsAny<PilarUpdateDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 19 ★ ─ éxito: UPDATE nombre/estrategia/orden + auditoría UPDATE con
    // valor_anterior/valor_nuevo (ADR-003) + commit + re-lectura post-commit → 200
    [Fact]
    public async Task Actualizar_Exito_ActualizaCamposYAudita()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest(nombre: "Nuevo nombre", estrategiaVictoria: "Nueva estrategia", orden: 5);
        var original = CrearPilarEntity(pilarId, tenantId, cicloId, codigo: "PEC-1", nombre: "Nombre anterior",
            estrategiaVictoria: "Estrategia anterior", orden: 1);
        var postCommit = CrearPilarConteos(pilarId, cicloId, codigo: "PEC-1", nombre: "Nuevo nombre",
            estrategiaVictoria: "Nueva estrategia", orden: 5);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, pilarId, original, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, pilarId, request);

        // Assert: UPDATE con los campos nuevos + auditoría UPDATE con snapshot anterior/nuevo (ADR-003)
        Assert.Equal("Nuevo nombre", resultado.Nombre);
        Assert.Equal("Nueva estrategia", resultado.EstrategiaVictoria);
        Assert.Equal(5, resultado.Orden);
        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(
                It.Is<PilarUpdateDto>(d =>
                    d.PilarId == pilarId &&
                    d.CicloId == cicloId &&
                    d.TenantId == tenantId &&
                    d.Nombre == "Nuevo nombre" &&
                    d.EstrategiaVictoria == "Nueva estrategia" &&
                    d.Orden == 5),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Pilar" &&
                    l.EntidadId == pilarId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Nombre anterior") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Nuevo nombre")),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 20 ─ Orden=null en update → conserva el orden del pilar original (D-H)
    [Fact]
    public async Task Actualizar_OrdenNull_ConservaOrdenExistente()
    {
        // Arrange: D-H — Orden=null → se persiste el orden del original (3), no un default
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest(nombre: "Nuevo nombre", orden: null);
        var original = CrearPilarEntity(pilarId, tenantId, cicloId, codigo: "PEC-1", nombre: "Nombre anterior", orden: 3);
        var postCommit = CrearPilarConteos(pilarId, cicloId, codigo: "PEC-1", nombre: "Nuevo nombre", orden: 3);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, pilarId, original, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, pilarId, request);

        // Assert: se persiste el orden del original (3) — D-H
        Assert.Equal(3, resultado.Orden);
        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(
                It.Is<PilarUpdateDto>(d => d.Orden == 3),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 21 ─ el código PEC-N NO es editable (CA #1): DAL-P7 no incluye codigo en el UPDATE
    // y la re-lectura post-commit conserva el código original intacto
    [Fact]
    public async Task Actualizar_NoTocaCodigo()
    {
        // Arrange: CA #1 (D-B) — el código es auto-generado y no editable
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var request = CrearUpdateRequest(nombre: "Nuevo nombre");
        var original = CrearPilarEntity(pilarId, tenantId, cicloId, codigo: "PEC-2", nombre: "Nombre anterior", orden: 2);
        var postCommit = CrearPilarConteos(pilarId, cicloId, codigo: "PEC-2", nombre: "Nuevo nombre", orden: 2);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, pilarId, original, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, pilarId, request);

        // Assert: el código original se conserva intacto en la re-lectura (CA #1)
        Assert.Equal("PEC-2", resultado.Codigo);
        _mockRepo.Verify(
            r => r.ActualizarPilarAsync(
                It.Is<PilarUpdateDto>(d => d.PilarId == pilarId && d.Nombre == "Nuevo nombre"),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Eliminar (DELETE) — Spec HU-013 § "Tests requeridos" casos 22-28
    // ═══════════════════════════════════════════════════════════════════════

    // Caso 22 ─ rol ≠ Gerente → AccesoDenegadoException 403 (D12, D-G/RN-006); EliminarPilarAsync nunca se llama
    [Fact]
    public async Task Eliminar_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: RN-006 — solo el GER elimina pilares
        _tenantContext.Rol = "AdminTenant";
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();

        // Act & Assert: 403 y el DELETE nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.EliminarAsync(cicloId, pilarId));

        _mockRepo.Verify(
            r => r.EliminarPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 23 ─ ciclo Cerrado → ValidacionException 422 (RC-12); sin DELETE
    [Fact]
    public async Task Eliminar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el DELETE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.EliminarAsync(cicloId, pilarId));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.EliminarPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 24 ─ pilar inexistente (DAL-P3 null) → NotFoundException 404
    [Fact]
    public async Task Eliminar_PilarInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PilarEntity?)null);

        // Act & Assert: 404 y el DELETE nunca se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.EliminarAsync(cicloId, pilarId));

        _mockRepo.Verify(
            r => r.EliminarPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 25 ─ pilar con 1+ Objetivos CG (DAL-P9) → ValidacionException 422 (CA #3, D-C)
    [Fact]
    public async Task Eliminar_ConObjetivosCg_LanzaValidacion()
    {
        // Arrange: CA #3 (D-C) — no se elimina un pilar con objetivos CG asociados
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearPilarEntity(pilarId, tenantId, cicloId));
        _mockRepo
            .Setup(r => r.ContarDependenciasPilarAsync(tenantId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PilarDependenciasDto { TotalObjetivosCg = 2, TotalOkrs = 0 });

        // Act & Assert: 422 mencionando Objetivos CG y el DELETE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.EliminarAsync(cicloId, pilarId));

        Assert.Contains("Objetivos CG", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.EliminarPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 26 ─ pilar con 1+ OKRs (DAL-P9) → ValidacionException 422 (CA #3, D-C)
    [Fact]
    public async Task Eliminar_ConOkrs_LanzaValidacion()
    {
        // Arrange: CA #3 (D-C) — no se elimina un pilar con OKRs asociados
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearPilarEntity(pilarId, tenantId, cicloId));
        _mockRepo
            .Setup(r => r.ContarDependenciasPilarAsync(tenantId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PilarDependenciasDto { TotalObjetivosCg = 0, TotalOkrs = 3 });

        // Act & Assert: 422 mencionando OKRs y el DELETE nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.EliminarAsync(cicloId, pilarId));

        Assert.Contains("OKR", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.EliminarPilarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 27 ★ ─ éxito: DELETE físico (DAL-P8) + auditoría DELETE con valor_anterior y
    // valor_nuevo=null (ADR-003) + commit
    [Fact]
    public async Task Eliminar_Exito_EliminaYAuditaDelete()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var original = CrearPilarEntity(pilarId, tenantId, cicloId, codigo: "PEC-1", nombre: "Crecimiento", orden: 1);
        var mockTx = new Mock<IDbTransaction>();

        ConfigurarFlujoFelizEliminar(cicloId, tenantId, pilarId, original, mockTx);

        // Act
        await _service.EliminarAsync(cicloId, pilarId);

        // Assert: DELETE físico + auditoría DELETE con snapshot anterior y valor_nuevo=null (ADR-003)
        _mockRepo.Verify(
            r => r.EliminarPilarAsync(tenantId, cicloId, pilarId, mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "DELETE" &&
                    l.Entidad == "Pilar" &&
                    l.EntidadId == pilarId.ToString() &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Crecimiento") &&
                    l.ValorNuevo == null),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 28 ─ PostgresException 23503 (FK objetivo_cg.pilar_id/okr.pilar_id sin CASCADE) →
    // rollback + 422 (ADR-007: carrera TOCTOU entre el conteo DAL-P9 y el DELETE)
    [Fact]
    public async Task Eliminar_23503_LanzaValidacion()
    {
        // Arrange: el DELETE lanza PostgresException 23503 (FK sin CASCADE, DDL L190)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var mockTx = new Mock<IDbTransaction>();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearPilarEntity(pilarId, tenantId, cicloId));
        _mockRepo
            .Setup(r => r.ContarDependenciasPilarAsync(tenantId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PilarDependenciasDto { TotalObjetivosCg = 0, TotalOkrs = 0 });
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);
        _mockRepo
            .Setup(r => r.EliminarPilarAsync(tenantId, cicloId, pilarId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException(
                "update or delete on table \"pilar\" violates foreign key constraint",
                "ERROR",
                "ERROR",   // invariantSeverity (puede repetir "ERROR")
                "23503")); // sqlState ← código 23503 (FK sin CASCADE)

        // Act & Assert: 422 con mensaje amigable y rollback explícito (ADR-007)
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.EliminarAsync(cicloId, pilarId));

        Assert.Contains("eliminar", ex.Message, StringComparison.OrdinalIgnoreCase);
        mockTx.Verify(t => t.Rollback(), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Listar / Obtener (GET) — Spec HU-013 § "Tests requeridos" casos 29-34
    // ═══════════════════════════════════════════════════════════════════════

    // Caso 29 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404
    [Fact]
    public async Task Listar_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y DAL-P1 nunca se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ListarAsync(cicloId));

        _mockRepo.Verify(
            r => r.ListarPilaresConConteosAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 30 ★ ─ éxito: retorna la lista con conteos de Objetivos CG y OKRs (CA #5, D-D)
    [Fact]
    public async Task Listar_Exito_RetornaConteosCgYOkrs()
    {
        // Arrange: CA #5 (D-D) — el listado expone total_objetivos_cg y total_okrs por pilar
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilar1 = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-1", nombre: "Crecimiento",
            orden: 1, totalObjetivosCg: 3, totalOkrs: 5);
        var pilar2 = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-2", nombre: "Rentabilidad",
            orden: 2, totalObjetivosCg: 1, totalOkrs: 0);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ListarPilaresConConteosAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pilar1, pilar2 });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: 2 pilares con sus conteos (CA #5) y tenant_id del contexto (SEC-06)
        Assert.Equal(2, resultado.Count);
        Assert.Equal(3, resultado[0].TotalObjetivosCg);
        Assert.Equal(5, resultado[0].TotalOkrs);
        Assert.Equal(1, resultado[1].TotalObjetivosCg);
        Assert.Equal(0, resultado[1].TotalOkrs);
        Assert.All(resultado, p => Assert.Equal(tenantId, p.TenantId));
    }

    // Caso 31 ─ éxito: la lista se ordena por Orden asc y luego Codigo asc (D-A)
    [Fact]
    public async Task Listar_Exito_OrdenaPorOrdenYCodigo()
    {
        // Arrange: D-A — orden de presentación: Orden asc, desempate por Codigo asc
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarA = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-2", nombre: "B", orden: 2);
        var pilarB = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-1", nombre: "A", orden: 2);
        var pilarC = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-3", nombre: "C", orden: 1);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ListarPilaresConConteosAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pilarA, pilarB, pilarC });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: primero Orden=1 (PEC-3), luego Orden=2 desempatado por Codigo (PEC-1 antes que PEC-2)
        Assert.Equal("PEC-3", resultado[0].Codigo);
        Assert.Equal("PEC-1", resultado[1].Codigo);
        Assert.Equal("PEC-2", resultado[2].Codigo);
    }

    // Caso 32 ─ rol JefeArea → retorna TODOS los pilares del ciclo (SEC-07 NO aplica: pilar es
    // corporativa, sin area_id; RN-007 solo lectura)
    [Fact]
    public async Task Listar_RolJefeArea_RetornaPilaresCompletos()
    {
        // Arrange: D-E — SEC-07 NO aplica a pilar (sin area_id); el JEF lee todos los pilares
        _tenantContext.Rol = "JefeArea";
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilar1 = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-1", nombre: "Crecimiento", orden: 1);
        var pilar2 = CrearPilarConteos(cicloId: cicloId, codigo: "PEC-2", nombre: "Rentabilidad", orden: 2);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ListarPilaresConConteosAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pilar1, pilar2 });

        // Act
        var resultado = await _service.ListarAsync(cicloId);

        // Assert: el JEF ve los 2 pilares (sin filtro por area_id — D-E)
        Assert.Equal(2, resultado.Count);
        Assert.Equal("PEC-1", resultado[0].Codigo);
        Assert.Equal("PEC-2", resultado[1].Codigo);
    }

    // Caso 33 ─ pilar inexistente (DAL-P2 null) → NotFoundException 404
    [Fact]
    public async Task Obtener_PilarInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PilarConteosDto?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerAsync(cicloId, pilarId));
    }

    // Caso 34 ★ ─ éxito: retorna el detalle del pilar con sus conteos (CA #5, D-D)
    [Fact]
    public async Task Obtener_Exito_RetornaDetalleConConteos()
    {
        // Arrange: CA #5 (D-D) — el detalle expone total_objetivos_cg y total_okrs
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var pilarId = Guid.NewGuid();
        var detalle = CrearPilarConteos(pilarId, cicloId, codigo: "PEC-1", nombre: "Crecimiento",
            estrategiaVictoria: "Expandir mercado", orden: 1, totalObjetivosCg: 3, totalOkrs: 5);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerPilarConConteosAsync(tenantId, cicloId, pilarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detalle);

        // Act
        var resultado = await _service.ObtenerAsync(cicloId, pilarId);

        // Assert: detalle completo con conteos (CA #5) y tenant_id del contexto (SEC-06)
        Assert.Equal(pilarId, resultado.Id);
        Assert.Equal("PEC-1", resultado.Codigo);
        Assert.Equal("Crecimiento", resultado.Nombre);
        Assert.Equal("Expandir mercado", resultado.EstrategiaVictoria);
        Assert.Equal(1, resultado.Orden);
        Assert.Equal(3, resultado.TotalObjetivosCg);
        Assert.Equal(5, resultado.TotalOkrs);
        Assert.Equal(tenantId, resultado.TenantId);
    }
}
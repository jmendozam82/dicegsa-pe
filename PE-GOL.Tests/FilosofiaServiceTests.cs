using System.Data;
using System.Text.Json;
using Moq;
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
/// Tests de contrato para FilosofiaService — Spec HU-011 § "Tests requeridos" (15 casos de la tabla).
/// TDD fase red por contrato (TEST-01): IFilosofiaService/FilosofiaService/FilosofiaEntity/
/// FilosofiaUpdateRequest/FilosofiaResponse/FilosofiaUpsertDto y las extensiones DAL-F1/F2 de
/// ICicloRepository AÚN NO existen (@BackendDev los implementa en fase 4) → esta clase NO compila
/// hasta entonces (rojo esperado por compilación, mismo patrón que AreaServiceTests HU-009).
/// Moq sobre ICicloRepository + TenantContext real (D17: TenantId nullable; D12: re-validación
/// de rol en BLL — PUT solo Gerente, RN-006/D-A).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Ctor de FilosofiaService: (ICicloRepository, TenantContext) + overload con ILogger (D-J) —
/// se usa el ctor de 2 args.
/// Escrituras (UPSERT filosofia DAL-F2 + auditoría UPDATE DAL-C11) en UNA sola transacción
/// IDbTransaction (patrón HU-001..HU-010); captura SQLSTATE 23505 → ValidacionException +
/// rollback (ADR-001, capa 2 del UNIQUE (tenant_id, ciclo_id) del DDL L173).
/// Auditoría (ADR-003): Entidad="Filosofia", acción UPDATE (D-D), valor_anterior = snapshot
/// previo (null si no existía fila — primer guardado, sin bifurcación CREATE).
/// Sanitización HTML (D-C): allowlist p/br/b/strong/i/em/ul/ol/li, SIN atributos, máx 5000 chars;
/// los tests #9/#10/#11 verifican el COMPORTAMIENTO OBSERVABLE (el DTO que llega a
/// UpsertFilosofiaAsync y la excepción 422), no el helper interno de sanitización.
/// SEC-07 NO APLICA (D-E): filosofia es corporativa (sin area_id; RLS solo por tenant).
/// HU-012 (extensión, Spec HU-012 § "Tests requeridos" — 18 casos nuevos, tabla #1-#18):
/// ActualizarValoresAsync (16) + ObtenerAsync con mapeo de Valores (2). TDD fase red por
/// contrato: ValoresUpdateRequest/FilosofiaValoresUpsertDto/FilosofiaResponse.Valores/
/// ActualizarValoresAsync/UpsertFilosofiaValoresAsync AÚN NO existen (@BackendDev los
/// implementa en fase 4) → esta clase NO compila hasta entonces (rojo esperado por
/// compilación, mismo patrón que la fase red de HU-011). Contrato verificado contra el spec:
/// PUT /filosofia/valores solo Gerente (RN-006/D12) · 404 sin tenant/ciclo · 422 Cerrado
/// (RC-12) y CA #4 (min 1, max 15, trim, ≤100 chars, sin duplicados case-insensitive) ·
/// UPSERT DAL-F3 ON CONFLICT (tenant_id, ciclo_id) DO UPDATE solo valores (D-B, no pisa
/// vision/mision) · auditoría UPDATE entidad "Filosofia" con {"valores":[...]} (D-D, ADR-003)
/// · orden = índice del array JSONB (D-C) · GET defensivo con Valores=[] (D-H).
/// </summary>
public class FilosofiaServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly TenantContext _tenantContext;
    private readonly IFilosofiaService _service;

    public FilosofiaServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "Gerente" // D-A/RN-006: el GER es el único con escritura de filosofía
        };

        _service = new FilosofiaService(_mockRepo.Object, _tenantContext);
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

    private static FilosofiaEntity CrearFilosofia(
        Guid? id = null, Guid? tenantId = null, Guid? cicloId = null, string vision = "",
        string mision = "", string valores = "[]", Guid? updatedBy = null, string? updatedByNombre = null,
        DateTimeOffset? updatedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            CicloId = cicloId ?? Guid.NewGuid(),
            Vision = vision,
            Mision = mision,
            Valores = valores,
            UpdatedBy = updatedBy,
            UpdatedByNombre = updatedByNombre,
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
        };

    private static FilosofiaUpdateRequest CrearRequestValido(
        string? vision = null, string? mision = null)
        => new()
        {
            Vision = vision ?? "Ser el líder del mercado",
            Mision = mision ?? "Servir con excelencia"
        };

    /// <summary>Request de HU-012: lista YA ordenada (D-C — el orden del array es el orden final).</summary>
    private static ValoresUpdateRequest CrearRequestValores(params string[] valores)
        => new() { Valores = valores.ToList() };

    /// <summary>Configura el flujo feliz de ActualizarAsync (spec §2): ciclo Borrador (D-F),
    /// snapshot previo (DAL-F1 1ª llamada, paso 8), tx con UPSERT (DAL-F2) + auditoría UPDATE
    /// (DAL-C11), re-lectura post-commit (DAL-F1 2ª llamada, paso 10).</summary>
    private void ConfigurarFlujoFelizActualizar(
        Guid cicloId, Guid tenantId, FilosofiaEntity? previo, FilosofiaEntity postCommit,
        Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .SetupSequence(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previo)        // 1ª llamada: snapshot de auditoría (paso 8)
            .ReturnsAsync(postCommit);   // 2ª llamada: re-lectura post-commit (paso 10)
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    /// <summary>Configura el flujo feliz de ActualizarValoresAsync (spec HU-012 §2): ciclo Borrador
    /// (D-F), snapshot previo (DAL-F1 1ª llamada, paso 7), tx con UPSERT valores (DAL-F3) +
    /// auditoría UPDATE (DAL-C11), re-lectura post-commit (DAL-F1 2ª llamada, paso 10).</summary>
    private void ConfigurarFlujoFelizActualizarValores(
        Guid cicloId, Guid tenantId, FilosofiaEntity? previo, FilosofiaEntity postCommit,
        Mock<IDbTransaction>? mockTx = null)
    {
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .SetupSequence(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previo)        // 1ª llamada: snapshot de auditoría (paso 7)
            .ReturnsAsync(postCommit);   // 2ª llamada: re-lectura post-commit (paso 10)
        _mockRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockTx ?? new Mock<IDbTransaction>()).Object);
        _mockRepo
            .Setup(r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockRepo
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    // ─── Caso 1-4. ObtenerAsync ─────────────────────────────────────────────

    // Caso 1 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task Obtener_SinTenant_LanzaNotFound()
    {
        // Arrange: D17 — TenantId null (SuperAdmin sin tenant)
        _tenantContext.TenantId = null;
        var cicloId = Guid.NewGuid();

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerAsync(cicloId));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.ObtenerFilosofiaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404 (el filtro tenant evita fuga)
    [Fact]
    public async Task Obtener_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y DAL-F1 nunca se consulta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerAsync(cicloId));

        _mockRepo.Verify(
            r => r.ObtenerFilosofiaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ DAL-F1 → null → 200 con defaults vacíos en memoria (D-H, patrón D5 HU-008), sin escritura
    [Fact]
    public async Task Obtener_SinFilaFilosofia_RetornaDefaultsVacios()
    {
        // Arrange: D-H — el GET es de solo lectura y nunca falla por ausencia de fila
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilosofiaEntity?)null);

        // Act
        var resultado = await _service.ObtenerAsync(cicloId);

        // Assert: defaults vacíos en memoria (D-H) y NINGUNA escritura (GET solo lectura)
        Assert.Equal(Guid.Empty, resultado.Id);
        Assert.Equal(cicloId, resultado.CicloId);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("", resultado.Vision);
        Assert.Equal("", resultado.Mision);
        Assert.Null(resultado.UpdatedBy);
        Assert.Null(resultado.UpdatedByNombre);
        Assert.Null(resultado.UpdatedAt);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ mapeo completo a FilosofiaResponse con UpdatedByNombre del JOIN (DAL-F1)
    [Fact]
    public async Task Obtener_ConFila_RetornaFilosofiaResponse()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var fila = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "<p>Ser el líder del mercado</p>", mision: "<p>Servir con excelencia</p>",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General", updatedAt: updatedAt);
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fila);

        // Act
        var resultado = await _service.ObtenerAsync(cicloId);

        // Assert: mapeo completo de campos (incl. UpdatedByNombre del JOIN a usuario)
        Assert.Equal(fila.Id, resultado.Id);
        Assert.Equal(cicloId, resultado.CicloId);
        Assert.Equal(tenantId, resultado.TenantId);
        Assert.Equal("<p>Ser el líder del mercado</p>", resultado.Vision);
        Assert.Equal("<p>Servir con excelencia</p>", resultado.Mision);
        Assert.Equal(_tenantContext.UserId, resultado.UpdatedBy);
        Assert.Equal("Gerente General", resultado.UpdatedByNombre);
        Assert.Equal(updatedAt, resultado.UpdatedAt);
    }

    // ─── Caso 5-15. ActualizarAsync ─────────────────────────────────────────

    // Caso 5 ─ rol ≠ Gerente → AccesoDenegadoException 403 (D12, D-A/RN-006); UpsertFilosofiaAsync nunca se llama
    [Fact]
    public async Task Actualizar_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: D-A/RN-006 — el ADM gestiona estructura, NO escribe contenido estratégico
        _tenantContext.Rol = "AdminTenant";
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();

        // Act & Assert: 403 y el UPSERT nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 6 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task Actualizar_SinTenant_LanzaNotFound()
    {
        // Arrange: D17
        _tenantContext.TenantId = null;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404
    [Fact]
    public async Task Actualizar_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ ciclo Cerrado → ValidacionException 422 (RC-12, D-F: Borrador y Activo permitidos); sin UPSERT
    [Fact]
    public async Task Actualizar_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ "<script>alert(1)</script>Mi visión" → se persiste "Mi visión" (script eliminado CON su
    // contenido, D-C/SEC-05). Comportamiento observable: el DTO que llega a UpsertFilosofiaAsync.
    [Fact]
    public async Task Actualizar_HtmlConEtiquetaNoPermitida_SeSanitiza()
    {
        // Arrange: D-C — <script> se elimina con su contenido (XSS, SEC-05); el texto visible se conserva
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido(
            vision: "<script>alert(1)</script>Mi visión",
            mision: "Servir con excelencia");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "Mi visión", mision: "Servir con excelencia",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, previo: null, postCommit);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, request);

        // Assert: se persiste el HTML sanitizado (script eliminado con contenido) — comportamiento observable
        Assert.Equal("Mi visión", resultado.Vision);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(
                It.Is<FilosofiaUpsertDto>(d =>
                    d.Vision == "Mi visión" &&
                    d.Mision == "Servir con excelencia" &&
                    d.TenantId == tenantId &&
                    d.CicloId == cicloId &&
                    d.UpdatedBy == _tenantContext.UserId),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 10 ─ "<p onclick=\"x()\">texto</p>" → "<p>texto</p>" (la allowlist NO admite atributos, SEC-05)
    [Fact]
    public async Task Actualizar_HtmlConAtributoPeligroso_SeEliminaAtributo()
    {
        // Arrange: D-C — SIN atributos en la allowlist → onclick se elimina (SEC-05)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido(
            vision: "<p onclick=\"x()\">texto</p>",
            mision: "Servir con excelencia");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "<p>texto</p>", mision: "Servir con excelencia",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, previo: null, postCommit);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, request);

        // Assert: la etiqueta permitida se conserva pero el atributo peligroso se elimina
        Assert.Equal("<p>texto</p>", resultado.Vision);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(
                It.Is<FilosofiaUpsertDto>(d => d.Vision == "<p>texto</p>"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 11 ─ "<p></p>" → 422 (CA #2: el texto visible tras quitar etiquetas no puede quedar vacío)
    [Fact]
    public async Task Actualizar_TextoVacioTrasSanitizar_LanzaValidacion()
    {
        // Arrange: CA #2 — StripHtml("<p></p>") → "" → IsNullOrWhiteSpace → 422
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido(vision: "<p></p>", mision: "Servir con excelencia");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 (CA #2) y ni transacción ni UPSERT
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, request));

        Assert.Contains("obligatorias", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 12 ─ > 5000 caracteres → 422 (D-C: re-validación BLL, espejo de FluentValidation)
    [Fact]
    public async Task Actualizar_Excede5000Caracteres_LanzaValidacion()
    {
        // Arrange: D-C — máx 5000 chars por campo (regla de negocio; el DDL es TEXT sin límite)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido(
            vision: new string('a', 5001),
            mision: "Servir con excelencia");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(cicloId, request));

        Assert.Contains("5000", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 13 ★ ─ sin fila previa (DAL-F1 → null) → UPSERT (INSERT) + auditoría UPDATE con
    // valor_anterior=null (D-D: primer guardado sin bifurcación CREATE) → 200
    [Fact]
    public async Task Actualizar_SinFilaPrevia_InsertaConAuditoriaValorAnteriorNull()
    {
        // Arrange: D-D — el primer guardado se audita como UPDATE con valor_anterior = null
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: request.Vision, mision: request.Mision,
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, previo: null, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, request);

        // Assert: UPSERT con el DTO completo + auditoría UPDATE con valor_anterior=null (D-D) + commit
        Assert.Equal(postCommit.Id, resultado.Id);
        Assert.Equal(request.Vision, resultado.Vision);
        Assert.Equal(request.Mision, resultado.Mision);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(
                It.Is<FilosofiaUpsertDto>(d =>
                    d.TenantId == tenantId &&
                    d.CicloId == cicloId &&
                    d.Vision == request.Vision &&
                    d.Mision == request.Mision &&
                    d.UpdatedBy == _tenantContext.UserId),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Filosofia" &&
                    l.EntidadId == cicloId.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains(request.Vision)),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 14 ─ con fila previa → UPSERT (UPDATE) + auditoría con valor_anterior = snapshot (ADR-003, CA #4)
    [Fact]
    public async Task Actualizar_ConFilaPrevia_UpsertYAuditaConSnapshot()
    {
        // Arrange: ADR-003 — valor_anterior = snapshot JSON de la fila previa (CA #4)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido(vision: "Nueva visión", mision: "Nueva misión");
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "Visión anterior", mision: "Misión anterior",
            updatedBy: Guid.NewGuid(), updatedByNombre: "Gerente previo");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "Nueva visión", mision: "Nueva misión",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, previo, postCommit);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, request);

        // Assert: UPSERT (UPDATE) + auditoría con valor_anterior = snapshot previo (ADR-003)
        Assert.Equal("Nueva visión", resultado.Vision);
        Assert.Equal("Nueva misión", resultado.Mision);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(
                It.Is<FilosofiaUpsertDto>(d => d.Vision == "Nueva visión" && d.Mision == "Nueva misión"),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Filosofia" &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("Visión anterior") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("Nueva visión")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 15 ★ ─ éxito: commit + re-lectura post-commit (DAL-F1) → FilosofiaResponse con
    // UpdatedBy/UpdatedAt poblados (SEC-06: del TenantContext) → 200
    [Fact]
    public async Task Actualizar_Exito_ReLecturaPostCommit_Retorna200()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValido();
        var mockTx = new Mock<IDbTransaction>();
        var updatedAt = DateTimeOffset.UtcNow;
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "Visión anterior", mision: "Misión anterior");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: request.Vision, mision: request.Mision,
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General", updatedAt: updatedAt);

        ConfigurarFlujoFelizActualizar(cicloId, tenantId, previo, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarAsync(cicloId, request);

        // Assert: re-lectura post-commit con UpdatedBy/UpdatedAt poblados (SEC-06) + commit
        Assert.Equal(request.Vision, resultado.Vision);
        Assert.Equal(request.Mision, resultado.Mision);
        Assert.Equal(_tenantContext.UserId, resultado.UpdatedBy);
        Assert.Equal("Gerente General", resultado.UpdatedByNombre);
        Assert.Equal(updatedAt, resultado.UpdatedAt);
        mockTx.Verify(t => t.Commit(), Times.Once);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaAsync(It.IsAny<FilosofiaUpsertDto>(), mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HU-012 · Gestión de Valores Corporativos — Spec HU-012 § "Tests requeridos"
    // Casos 1-16: ActualizarValoresAsync (nuevo) · Casos 17-18: ObtenerAsync (mapeo de Valores)
    // ═══════════════════════════════════════════════════════════════════════

    // Caso 1 ─ rol ≠ Gerente → AccesoDenegadoException 403 (D12, RN-006); UpsertFilosofiaValoresAsync nunca se llama
    [Fact]
    public async Task ActualizarValores_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange: RN-006 — el ADM gestiona estructura, NO escribe contenido estratégico
        _tenantContext.Rol = "AdminTenant";
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "Excelencia");

        // Act & Assert: 403 y el UPSERT de valores nunca se ejecuta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarValoresAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 2 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task ActualizarValores_SinTenant_LanzaNotFound()
    {
        // Arrange: D17
        _tenantContext.TenantId = null;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo");

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarValoresAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 3 ─ ciclo inexistente (DAL-C2 null) → NotFoundException 404 (el filtro tenant evita fuga)
    [Fact]
    public async Task ActualizarValores_CicloInexistente_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y el UPSERT nunca se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ActualizarValoresAsync(cicloId, request));

        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 4 ─ ciclo Cerrado → ValidacionException 422 (RC-12, D-F: Borrador y Activo permitidos); sin UPSERT
    [Fact]
    public async Task ActualizarValores_CicloCerrado_LanzaValidacion()
    {
        // Arrange: RC-12 — un ciclo Cerrado es de solo lectura
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Cerrado"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("solo lectura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 5 ─ Valores=[] → 422 (CA #4: mínimo 1 valor corporativo)
    [Fact]
    public async Task ActualizarValores_ListaVacia_LanzaValidacion()
    {
        // Arrange: CA #4 — mínimo 1 valor
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y ni transacción ni UPSERT
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("al menos 1", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 6 ─ 16 valores → 422 (CA #4: máximo 15 valores corporativos)
    [Fact]
    public async Task ActualizarValores_16Valores_LanzaValidacion()
    {
        // Arrange: CA #4 — máximo 15 valores
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores(Enumerable.Range(1, 16).Select(i => $"Valor {i}").ToArray());
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("15", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 7 ─ ["Liderazgo", " "] → 422 (valor vacío tras trim, D-G)
    [Fact]
    public async Task ActualizarValores_ValorVacio_LanzaValidacion()
    {
        // Arrange: D-G — trim primero; " " tras trim = vacío → 422
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", " ");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("requerido", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 8 ─ valor de 101 chars → 422 (D-G: máx 100 caracteres por valor)
    [Fact]
    public async Task ActualizarValores_ValorExcede100Caracteres_LanzaValidacion()
    {
        // Arrange: D-G — máx 100 chars por valor corporativo
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores(new string('a', 101));
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("100", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 9 ─ ["Liderazgo", "liderazgo"] → 422 (duplicados case-insensitive, D-G/CA #4)
    [Fact]
    public async Task ActualizarValores_DuplicadosCaseInsensitive_LanzaValidacion()
    {
        // Arrange: D-G — duplicados case-insensitive rechazados (CA #4)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "liderazgo");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));

        // Act & Assert: 422 y el UPSERT nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarValoresAsync(cicloId, request));

        Assert.Contains("duplicados", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 10 ─ [" Liderazgo ", "Excelencia"] → se persiste ["Liderazgo", "Excelencia"] (D-G: trim).
    // Comportamiento observable: el JSON serializado en el DTO que llega a UpsertFilosofiaValoresAsync.
    [Fact]
    public async Task ActualizarValores_ValoresConEspacios_SeNormalizanConTrim()
    {
        // Arrange: D-G — trim de cada valor antes de persistir
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores(" Liderazgo ", "Excelencia");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo: null, postCommit);

        // Act
        var resultado = await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: el JSON del DTO contiene los valores SIN espacios (trim, D-G)
        Assert.Equal(new List<string> { "Liderazgo", "Excelencia" }, resultado.Valores);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(
                It.Is<FilosofiaValoresUpsertDto>(d =>
                    d.TenantId == tenantId &&
                    d.CicloId == cicloId &&
                    d.UpdatedBy == _tenantContext.UserId &&
                    JsonSerializer.Deserialize<List<string>>(d.Valores, (JsonSerializerOptions?)null)!.SequenceEqual(new[] { "Liderazgo", "Excelencia" })),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 11 ★ ─ sin fila previa (DAL-F1 → null) → UPSERT (INSERT con vision=''/mision='' + valores) → 200
    [Fact]
    public async Task ActualizarValores_SinFilaPrevia_InsertaConVisionMisionVacias()
    {
        // Arrange: D-B — el primer guardado crea la fila con vision=''/mision='' (DAL-F3); el GER
        // puede registrar valores sin haber escrito visión/misión aún
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "Excelencia");
        var mockTx = new Mock<IDbTransaction>();
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "", mision: "", valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo: null, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: UPSERT con el DTO de valores + respuesta con vision/mision vacías (INSERT inicial) + commit
        Assert.Equal("", resultado.Vision);
        Assert.Equal("", resultado.Mision);
        Assert.Equal(new List<string> { "Liderazgo", "Excelencia" }, resultado.Valores);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(
                It.Is<FilosofiaValoresUpsertDto>(d =>
                    d.TenantId == tenantId &&
                    d.CicloId == cicloId &&
                    d.UpdatedBy == _tenantContext.UserId &&
                    JsonSerializer.Deserialize<List<string>>(d.Valores, (JsonSerializerOptions?)null)!.SequenceEqual(new[] { "Liderazgo", "Excelencia" })),
                mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        mockTx.Verify(t => t.Commit(), Times.Once);
    }

    // Caso 12 ─ con fila previa (vision/mision existentes) → UPSERT solo valores → vision/mision intactas
    [Fact]
    public async Task ActualizarValores_ConFilaPrevia_UpsertSoloValoresSinPisarVisionMision()
    {
        // Arrange: D-B — el branch DO UPDATE de DAL-F3 solo toca valores/updated_by/updated_at
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "Excelencia");
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "<p>Ser el líder del mercado</p>", mision: "<p>Servir con excelencia</p>",
            valores: "[\"Liderazgo\"]", updatedBy: Guid.NewGuid(), updatedByNombre: "Gerente previo");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "<p>Ser el líder del mercado</p>", mision: "<p>Servir con excelencia</p>",
            valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo, postCommit);

        // Act
        var resultado = await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: vision/mision intactas en la re-lectura (el UPSERT solo actualiza valores)
        Assert.Equal("<p>Ser el líder del mercado</p>", resultado.Vision);
        Assert.Equal("<p>Servir con excelencia</p>", resultado.Mision);
        Assert.Equal(new List<string> { "Liderazgo", "Excelencia" }, resultado.Valores);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(
                It.Is<FilosofiaValoresUpsertDto>(d =>
                    JsonSerializer.Deserialize<List<string>>(d.Valores, (JsonSerializerOptions?)null)!.SequenceEqual(new[] { "Liderazgo", "Excelencia" })),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 13 ─ reordenamiento → el array JSONB preserva el orden (D-C: índice = orden de visualización)
    [Fact]
    public async Task ActualizarValores_Reordenamiento_PersisteElOrden()
    {
        // Arrange: D-C — drag-and-drop/flechas = reordenar el array; el request llega YA ordenado
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Excelencia", "Liderazgo");
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\",\"Excelencia\"]");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Excelencia\",\"Liderazgo\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo, postCommit);

        // Act
        var resultado = await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: la re-lectura devuelve el MISMO orden enviado (Excelencia primero)
        Assert.Equal(new List<string> { "Excelencia", "Liderazgo" }, resultado.Valores);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(
                It.Is<FilosofiaValoresUpsertDto>(d =>
                    JsonSerializer.Deserialize<List<string>>(d.Valores, (JsonSerializerOptions?)null)!.SequenceEqual(new[] { "Excelencia", "Liderazgo" })),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 14 ★ ─ auditoría UPDATE con valor_anterior/valor_nuevo = {"valores":[...]} (D-D, ADR-003)
    [Fact]
    public async Task ActualizarValores_Exito_RegistraAuditoriaConSnapshotValores()
    {
        // Arrange: D-D/ADR-003 — el PUT de valores audita SOLO el array valores como {"valores":[...]}
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "Excelencia");
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\"]", updatedBy: Guid.NewGuid(), updatedByNombre: "Gerente previo");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo, postCommit);

        // Act
        await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: auditoría UPDATE con snapshot del array (valor_anterior previo, valor_nuevo nuevo)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Filosofia" &&
                    l.EntidadId == cicloId.ToString() &&
                    l.TenantId == tenantId &&
                    l.UsuarioId == _tenantContext.UserId &&
                    l.ValorAnterior != null &&
                    l.ValorAnterior.Contains("\"valores\"") &&
                    l.ValorAnterior.Contains("Liderazgo") &&
                    !l.ValorAnterior.Contains("Excelencia") &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("\"valores\"") &&
                    l.ValorNuevo.Contains("Excelencia")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 15 ─ sin fila previa → valor_anterior=null (primer guardado = UPDATE, D-D)
    [Fact]
    public async Task ActualizarValores_SinFilaPrevia_AuditoriaValorAnteriorNull()
    {
        // Arrange: D-D — el primer guardado se audita como UPDATE con valor_anterior = null
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo: null, postCommit);

        // Act
        await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: valor_anterior=null + valor_nuevo = {"valores":[...]} (ADR-003)
        _mockRepo.Verify(
            r => r.InsertLogAsync(
                It.Is<LogAuditoriaInsert>(l =>
                    l.Accion == "UPDATE" &&
                    l.Entidad == "Filosofia" &&
                    l.EntidadId == cicloId.ToString() &&
                    l.ValorAnterior == null &&
                    l.ValorNuevo != null &&
                    l.ValorNuevo.Contains("\"valores\"") &&
                    l.ValorNuevo.Contains("Liderazgo")),
                It.IsAny<IDbTransaction?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 16 ★ ─ éxito: commit + re-lectura post-commit → FilosofiaResponse con Valores poblado y
    // UpdatedBy/UpdatedAt actualizados (SEC-06) → 200
    [Fact]
    public async Task ActualizarValores_Exito_ReLecturaPostCommit_Retorna200()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var request = CrearRequestValores("Liderazgo", "Excelencia");
        var mockTx = new Mock<IDbTransaction>();
        var updatedAt = DateTimeOffset.UtcNow;
        var previo = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\"]");
        var postCommit = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General", updatedAt: updatedAt);

        ConfigurarFlujoFelizActualizarValores(cicloId, tenantId, previo, postCommit, mockTx);

        // Act
        var resultado = await _service.ActualizarValoresAsync(cicloId, request);

        // Assert: re-lectura post-commit con Valores poblado y UpdatedBy/UpdatedAt actualizados + commit
        Assert.Equal(new List<string> { "Liderazgo", "Excelencia" }, resultado.Valores);
        Assert.Equal(_tenantContext.UserId, resultado.UpdatedBy);
        Assert.Equal("Gerente General", resultado.UpdatedByNombre);
        Assert.Equal(updatedAt, resultado.UpdatedAt);
        mockTx.Verify(t => t.Commit(), Times.Once);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), mockTx.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── HU-012 · Caso 17-18. ObtenerAsync (extensión del mapeo de Valores) ──

    // Caso 17 ─ DAL-F1 → null → 200 con Valores=[] (D-H, sin escritura)
    [Fact]
    public async Task Obtener_SinFilaFilosofia_RetornaValoresVacios()
    {
        // Arrange: D-H — el GET defensivo extiende los defaults con Valores=[] (sin escritura)
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Borrador"));
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilosofiaEntity?)null);

        // Act
        var resultado = await _service.ObtenerAsync(cicloId);

        // Assert: Valores=[] (lista vacía, no null) y NINGUNA escritura (GET solo lectura)
        Assert.Empty(resultado.Valores);
        Assert.Equal(Guid.Empty, resultado.Id);
        _mockRepo.Verify(
            r => r.UpsertFilosofiaValoresAsync(It.IsAny<FilosofiaValoresUpsertDto>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 18 ─ fila con valores='["Liderazgo","Excelencia"]' → Valores en el mismo orden (parseo JSONB)
    [Fact]
    public async Task Obtener_ConFila_RetornaValoresEnOrden()
    {
        // Arrange: D-C — el parseo JSONB → List<string> preserva el orden del array
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloId = Guid.NewGuid();
        var fila = CrearFilosofia(tenantId: tenantId, cicloId: cicloId,
            vision: "<p>Ser el líder del mercado</p>", mision: "<p>Servir con excelencia</p>",
            valores: "[\"Liderazgo\",\"Excelencia\"]",
            updatedBy: _tenantContext.UserId, updatedByNombre: "Gerente General");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(cicloId, tenantId, "PE 2026", 2026, 1, "Activo"));
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, cicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fila);

        // Act
        var resultado = await _service.ObtenerAsync(cicloId);

        // Assert: Valores en el MISMO orden del JSONB (Liderazgo primero, Excelencia segundo)
        Assert.Equal(new List<string> { "Liderazgo", "Excelencia" }, resultado.Valores);
    }
}
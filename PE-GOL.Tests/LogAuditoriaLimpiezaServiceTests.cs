using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PE_GOL.DAL.Interfaces;
using PE_GOL.Tests.Stubs;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para LogAuditoriaLimpiezaService (retención CA #4) — Spec HU-005
/// § "Tests requeridos" (5 casos, tabla #20..#24).
/// TDD fase red (TEST-01): el stub LogAuditoriaLimpiezaService (PE_GOL.Tests.Stubs — el real
/// vive en PE-GOL.API/HostedServices/, proyecto Web SDK no referenciado por tests, mismo
/// patrón que el stub de TenantMiddleware HU-004) lanza NotImplementedException en TODOS los
/// métodos → los 5 tests fallan deliberadamente EN RUNTIME hasta que @BackendDev implemente
/// la lógica en fase 4 (spec § Lógica BLL paso 3 + § Queries DAL L4 + D5).
/// Mocks: IServiceScopeFactory (scope simulado → ILogAuditoriaRepository Scoped resuelto del
/// scope, patrón Singleton→Scoped del spec), IConfiguration (Auditoria:RetencionDias default 90,
/// Auditoria:HoraLimpieza default "03:00"), ILogger&lt;LogAuditoriaLimpiezaService&gt; (RNF-023).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Nombres EXACTOS de la tabla del spec.
/// </summary>
public class LogAuditoriaLimpiezaServiceTests
{
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILogAuditoriaRepository> _mockRepo;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<LogAuditoriaLimpiezaService>> _mockLogger;
    private readonly LogAuditoriaLimpiezaService _service;

    public LogAuditoriaLimpiezaServiceTests()
    {
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockRepo = new Mock<ILogAuditoriaRepository>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<LogAuditoriaLimpiezaService>>();

        // El HostedService es Singleton; el repositorio es Scoped → scope DI por ejecución
        // (using var scope = _scopeFactory.CreateScope(); GetRequiredService<ILogAuditoriaRepository>()).
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(ILogAuditoriaRepository)))
            .Returns(_mockRepo.Object);
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);

        // Configuración por defecto (appsettings.json): RetencionDias=90 (CA #4), HoraLimpieza="03:00"
        _mockConfig.Setup(c => c["Auditoria:RetencionDias"]).Returns("90");
        _mockConfig.Setup(c => c["Auditoria:HoraLimpieza"]).Returns("03:00");

        _service = new LogAuditoriaLimpiezaService(
            _mockScopeFactory.Object, _mockConfig.Object, _mockLogger.Object);
    }

    // ─── Caso 20-24. Retención (CA #4) ──────────────────────────────────────

    // Caso 20 ─ cutoff = NOW - RetencionDias(90); EliminarAnterioresAAsync invocado con ese
    // cutoff (DAL-L4: DELETE WHERE created_at < @Cutoff) — solo se eliminan los > 90 días
    [Fact]
    public async Task Limpiar_RegistrosMayoresA90Dias_EliminaSoloEsos()
    {
        // Arrange: RetencionDias=90 (default) → cutoff ≈ NOW - 90 días
        _mockRepo
            .Setup(r => r.EliminarAnterioresAAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        await _service.EjecutarLimpiezaAsync();

        // Assert: cutoff ≈ NOW - 90 días; scope creado y repo resuelto del scope (Singleton→Scoped)
        _mockRepo.Verify(r => r.EliminarAnterioresAAsync(
            It.Is<DateTimeOffset>(c => Math.Abs((c - DateTimeOffset.UtcNow.AddDays(-90)).TotalMinutes) < 5),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Once);
    }

    // Caso 21 ─ los registros recientes NO se eliminan: el cutoff excluye created_at >= NOW-90
    // (el SQL filtra created_at < @Cutoff → un registro de hace 89 días queda fuera del DELETE)
    [Fact]
    public async Task Limpiar_RegistrosRecientes_NoSeEliminan()
    {
        // Arrange: cutoff debe ser ≤ NOW-89 días (un registro reciente de 89 días NO cae en el DELETE)
        _mockRepo
            .Setup(r => r.EliminarAnterioresAAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        await _service.EjecutarLimpiezaAsync();

        // Assert: cutoff = NOW - 90 días (estrictamente anterior a los recientes; el SQL
        // created_at < @Cutoff los excluye — DAL-L4)
        _mockRepo.Verify(r => r.EliminarAnterioresAAsync(
            It.Is<DateTimeOffset>(c =>
                c <= DateTimeOffset.UtcNow.AddDays(-89) &&
                c >= DateTimeOffset.UtcNow.AddDays(-91)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 22 ─ Auditoria:RetencionDias=120 (configurable) → cutoff = NOW - 120 días
    [Fact]
    public async Task Limpiar_RetencionConfigurable_UsaValorDeAppsettings()
    {
        // Arrange: retención configurable (CA #4 — "retención mínima de 90 días", ampliable)
        _mockConfig.Setup(c => c["Auditoria:RetencionDias"]).Returns("120");
        _mockRepo
            .Setup(r => r.EliminarAnterioresAAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        // Act
        await _service.EjecutarLimpiezaAsync();

        // Assert: cutoff = NOW - 120 días (valor de appsettings, no el default)
        _mockRepo.Verify(r => r.EliminarAnterioresAAsync(
            It.Is<DateTimeOffset>(c => Math.Abs((c - DateTimeOffset.UtcNow.AddDays(-120)).TotalMinutes) < 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 23 ─ error en BD → Log.Error con la excepción y el loop CONTINÚA (RNF-014, graceful:
    // la retención es política de almacenamiento, no bloquea la operación principal)
    [Fact]
    public async Task Limpiar_ErrorEnBD_LoggeaErrorYContinua()
    {
        // Arrange: la BD falla (p. ej. conexión perdida)
        _mockRepo
            .Setup(r => r.EliminarAnterioresAAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection lost"));

        // Act: NO debe propagar la excepción (el siguiente ciclo reintenta)
        await _service.EjecutarLimpiezaAsync();

        // Assert: Log.Error con la excepción y el flujo continúa (RNF-014)
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception>(e => e is InvalidOperationException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Caso 24 ─ tras una ejecución, el siguiente disparo se programa a la HoraLimpieza del día
    // siguiente (verificación del loop diario: hoy a las 03:00 si aún no pasó; mañana si ya pasó)
    [Fact]
    public void Limpiar_EjecutaUnaVezAlDia_ProgramaProximoDisparo()
    {
        // Arrange: HoraLimpieza = "03:00" (default de appsettings)
        const string horaLimpieza = "03:00";
        var antesDeLaHora = new DateTimeOffset(2026, 9, 17, 2, 0, 0, TimeSpan.Zero);
        var despuesDeLaHora = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

        // Act
        var proximoAntes = _service.CalcularProximoDisparo(antesDeLaHora, horaLimpieza);
        var proximoDespues = _service.CalcularProximoDisparo(despuesDeLaHora, horaLimpieza);

        // Assert: hoy 03:00 si aún no pasó; mañana 03:00 si ya pasó (una ejecución por día)
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 3, 0, 0, TimeSpan.Zero), proximoAntes);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 3, 0, 0, TimeSpan.Zero), proximoDespues);
    }
}
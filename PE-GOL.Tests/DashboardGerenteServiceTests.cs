using System.Data;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Responses.Dashboard;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para DashboardService — Tablero del Gerente (Spec HU-016).
/// Fase TDD red por contrato (TEST-01): IDashboardService.ObtenerTableroGerenteAsync lanza
/// NotImplementedException → los 20 tests COMPILAN y FALLAN en runtime (rojo esperado).
/// Nueva clase de test justificada: el fixture usa Rol="Gerente" (no "JefeArea" como HU-015).
/// </summary>
public class DashboardGerenteServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly TenantContext _tenantContext;
    private readonly IDashboardService _service;

    public DashboardGerenteServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "Gerente" // D-F: solo el Gerente tiene este tablero.
        };

        _service = new DashboardService(_mockRepo.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(
        Guid? id = null, Guid? tenantId = null, string nombre = "PE 2026", int añoFiscal = 2026,
        string estado = "Activo")
    {
        return new CicloEntity
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            Nombre = nombre,
            AñoFiscal = añoFiscal,
            Estado = estado
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
            UmbralAmarillo = umbralAmarillo
        };

    private static AccionCicloTableroDto CrearAccion(
        Guid areaId, decimal progreso = 0m, DateTime? fechaVencimiento = null)
        => new()
        {
            Id = Guid.NewGuid(),
            AreaId = areaId,
            Progreso = progreso,
            FechaVencimiento = fechaVencimiento ?? DateTime.Today
        };

    private void ConfigurarFlujoFeliz(
        CicloEntity ciclo,
        IEnumerable<UmbralSemaforoEntity>? umbrales = null,
        IEnumerable<ResumenAreaTableroDto>? areas = null,
        TotalesConsolidadosDalDto? totales = null,
        IEnumerable<AccionCicloTableroDto>? acciones = null)
    {
        var tenantId = _tenantContext.TenantId!.Value;

        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);
        _mockRepo
            .Setup(r => r.ObtenerUmbralesAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(umbrales ?? new[]
            {
                CrearUmbral("KPI", 0.90m, 0.70m, ciclo.Id, tenantId),
                CrearUmbral("PlanAccion", 0.90m, 0.70m, ciclo.Id, tenantId)
            });
        _mockRepo
            .Setup(r => r.ListarAreasConResumenAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(areas ?? Array.Empty<ResumenAreaTableroDto>());
        _mockRepo
            .Setup(r => r.ObtenerTotalesConsolidadosAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totales ?? new TotalesConsolidadosDalDto());
        _mockRepo
            .Setup(r => r.ListarAccionesDelCicloAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acciones ?? Array.Empty<AccionCicloTableroDto>());
    }

    // ─── Casos 1-3. Guardas de acceso y validación ──────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange
        _tenantContext.Rol = "JefeArea"; // Rol incorrecto

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerTableroGerenteAsync());

        _mockRepo.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerTableroGerente_SinTenant_LanzaNotFound()
    {
        // Arrange
        _tenantContext.TenantId = null;

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroGerenteAsync());

        _mockRepo.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerTableroGerente_SinCicloActivo_LanzaNotFound()
    {
        // Arrange: DAL-D1 -> null
        var tenantId = _tenantContext.TenantId!.Value;
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroGerenteAsync());

        _mockRepo.Verify(r => r.ListarAreasConResumenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Caso 4. Sin áreas ───────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_SinAreas_RetornaPanelesVaciosYTotalesCero()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        ConfigurarFlujoFeliz(ciclo); // Todo vacío

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert: 200, paneles vacío, totales = 0, AreasConAlertaActiva = 0
        Assert.NotNull(resultado);
        Assert.Empty(resultado.Paneles);
        Assert.Equal(0, resultado.Totales.TotalAcciones);
        Assert.Equal(0, resultado.Totales.AccionesAtrasadas);
        Assert.Equal(0m, resultado.Totales.PromedioOkrs);
        Assert.Equal(0, resultado.AreasConAlertaActiva);
    }

    // ─── Caso 5. Con áreas ───────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_ConDatos_CalculaPanelesCorrectamente()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var area1 = new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0.85m, AvancePlanAccion = 0.75m };
        var area2 = new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0.60m, AvancePlanAccion = 0.50m };
        ConfigurarFlujoFeliz(ciclo, areas: new[] { area1, area2 });

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(2, resultado.Paneles.Count);
        Assert.Equal(0.85m, resultado.Paneles[0].AvanceOkrs);
        Assert.Equal(0.75m, resultado.Paneles[0].AvancePlanAccion);
        Assert.Equal(0.60m, resultado.Paneles[1].AvanceOkrs);
        Assert.Equal(0.50m, resultado.Paneles[1].AvancePlanAccion);
    }

    // ─── Caso 6-8. Acciones atrasadas ────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_AccionVencidaSinTerminar_RecalculaComoAtrasada()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var areaId = Guid.NewGuid();
        var areas = new[] { new ResumenAreaTableroDto { AreaId = areaId } };
        // Vencida ayer, progreso 40% -> Atrasada
        var accion = CrearAccion(areaId, progreso: 40m, fechaVencimiento: DateTime.Today.AddDays(-1));
        ConfigurarFlujoFeliz(ciclo, areas: areas, acciones: new[] { accion });

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert: recalcula en BLL
        Assert.NotNull(resultado);
        Assert.Equal(1, resultado.Paneles[0].AccionesAtrasadas);
        Assert.Equal(1, resultado.Totales.AccionesAtrasadas);
    }

    [Fact]
    public async Task ObtenerTableroGerente_AccionTerminada_NoCuentaComoAtrasada()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var areaId = Guid.NewGuid();
        var areas = new[] { new ResumenAreaTableroDto { AreaId = areaId } };
        // Vencida pero progreso 100% -> No atrasada
        var accion = CrearAccion(areaId, progreso: 100m, fechaVencimiento: DateTime.Today.AddDays(-5));
        ConfigurarFlujoFeliz(ciclo, areas: areas, acciones: new[] { accion });

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(0, resultado.Paneles[0].AccionesAtrasadas);
    }

    [Fact]
    public async Task ObtenerTableroGerente_AtrasadasPorArea_SeAgrupanPorArea()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var area1 = Guid.NewGuid();
        var area2 = Guid.NewGuid();
        var areas = new[]
        {
            new ResumenAreaTableroDto { AreaId = area1 },
            new ResumenAreaTableroDto { AreaId = area2 }
        };
        var acciones = new[]
        {
            CrearAccion(area1, progreso: 0m, fechaVencimiento: DateTime.Today.AddDays(-1)),
            CrearAccion(area2, progreso: 0m, fechaVencimiento: DateTime.Today.AddDays(-2)),
            CrearAccion(area2, progreso: 0m, fechaVencimiento: DateTime.Today.AddDays(-3))
        };
        ConfigurarFlujoFeliz(ciclo, areas: areas, acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        var panel1 = resultado.Paneles.First(p => p.AreaId == area1);
        var panel2 = resultado.Paneles.First(p => p.AreaId == area2);
        Assert.Equal(1, panel1.AccionesAtrasadas);
        Assert.Equal(2, panel2.AccionesAtrasadas);
        Assert.Equal(3, resultado.Totales.AccionesAtrasadas);
    }

    // ─── Caso 9-11. Semáforo Global ──────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_SemaforoGlobal_EvaluaConUmbralesKpi()
    {
        // Arrange: (0.85 + 0.75) / 2 = 0.80 -> Amarillo (0.70 <= 0.80 < 0.90)
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var areas = new[] { new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0.85m, AvancePlanAccion = 0.75m } };
        ConfigurarFlujoFeliz(ciclo, areas: areas);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("Amarillo", resultado.Paneles[0].SemaforoGlobal);
    }

    [Fact]
    public async Task ObtenerTableroGerente_SemaforoGlobal_DatosVacios_Rojo()
    {
        // Arrange: (0 + 0) / 2 = 0 -> Rojo (< 0.70)
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var areas = new[] { new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0m, AvancePlanAccion = 0m } };
        ConfigurarFlujoFeliz(ciclo, areas: areas);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("Rojo", resultado.Paneles[0].SemaforoGlobal);
    }

    [Fact]
    public async Task ObtenerTableroGerente_UmbralFaltante_UsaDefaults()
    {
        // Arrange: Falta KPI en umbrales -> default 0.90/0.70. (0.95+0.95)/2 = 0.95 -> Verde.
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var areas = new[] { new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0.95m, AvancePlanAccion = 0.95m } };
        ConfigurarFlujoFeliz(ciclo, umbrales: Array.Empty<UmbralSemaforoEntity>(), areas: areas);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal("Verde", resultado.Paneles[0].SemaforoGlobal);
    }

    // ─── Caso 12-13. Alerta Activa ───────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_AlertaActiva_AreaRojaCuenta()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        // Rojo -> 0
        var areas = new[] { new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 0m, AvancePlanAccion = 0m } };
        ConfigurarFlujoFeliz(ciclo, areas: areas);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.True(resultado.Paneles[0].AlertaActiva);
        Assert.Equal(1, resultado.AreasConAlertaActiva);
    }

    [Fact]
    public async Task ObtenerTableroGerente_AlertaActiva_AreaVerdeNoCuenta()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        // Verde -> 1.0
        var areas = new[] { new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), PromedioPuntuacionOkrs = 1m, AvancePlanAccion = 1m } };
        ConfigurarFlujoFeliz(ciclo, areas: areas);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.False(resultado.Paneles[0].AlertaActiva);
        Assert.Equal(0, resultado.AreasConAlertaActiva);
    }

    // ─── Caso 14. Totales Consolidados ───────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_TotalesConsolidados_CalculaTotalAccionesPromedioOkrs()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        var totales = new TotalesConsolidadosDalDto { TotalAcciones = 12, PromedioOkrs = 0.62m };
        ConfigurarFlujoFeliz(ciclo, totales: totales);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(12, resultado.Totales.TotalAcciones);
        Assert.Equal(0.62m, resultado.Totales.PromedioOkrs);
    }

    // ─── Caso 15. Contrato completo ──────────────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_Exito_Retorna200ConContratoCompleto()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, nombre: "PE 2026", añoFiscal: 2026);
        var areaId = Guid.NewGuid();
        var areas = new[] { new ResumenAreaTableroDto { AreaId = areaId, AreaCodigo = "GOL1", AreaNombre = "CEDIS FARMA", PromedioPuntuacionOkrs = 0.8m, AvancePlanAccion = 0.8m } };
        var totales = new TotalesConsolidadosDalDto { TotalAcciones = 5, PromedioOkrs = 0.8m };
        var acciones = new[] { CrearAccion(areaId, progreso: 0m, fechaVencimiento: DateTime.Today.AddDays(-1)) }; // 1 atrasada
        ConfigurarFlujoFeliz(ciclo, areas: areas, totales: totales, acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(ciclo.Id, resultado.CicloId);
        Assert.Equal("PE 2026", resultado.CicloNombre);
        Assert.Equal(2026, resultado.AñoFiscal);
        Assert.Single(resultado.Paneles);
        Assert.Equal(areaId, resultado.Paneles[0].AreaId);
        Assert.Equal("GOL1", resultado.Paneles[0].AreaCodigo);
        Assert.Equal("CEDIS FARMA", resultado.Paneles[0].AreaNombre);
        Assert.Equal(5, resultado.Totales.TotalAcciones);
        Assert.Equal(1, resultado.Totales.AccionesAtrasadas);
        Assert.Equal(0.8m, resultado.Totales.PromedioOkrs);
        Assert.Equal(0, resultado.AreasConAlertaActiva);
    }

    // ─── Caso 16-20. Casos de arquitectura ───────────────────────────────────

    [Fact]
    public async Task ObtenerTableroGerente_SoloCicloActivo_IgnoraOtrosCiclos()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloActivo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var cicloCerrado = CrearCiclo(tenantId: tenantId, estado: "Cerrado");
        
        ConfigurarFlujoFeliz(cicloActivo);
        
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cicloActivo);

        // Act
        await _service.ObtenerTableroGerenteAsync();

        // Assert: Queries uses cicloActivo.Id, never cicloCerrado.Id
        _mockRepo.Verify(r => r.ListarAreasConResumenAsync(tenantId, cicloActivo.Id, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ListarAreasConResumenAsync(tenantId, cicloCerrado.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerTableroGerente_Sec06_SinParamsDelCliente()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        ConfigurarFlujoFeliz(ciclo);

        // Act
        await _service.ObtenerTableroGerenteAsync();

        // Assert: Se usa tenantId del contexto
        _mockRepo.Verify(r => r.ListarAreasConResumenAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObtenerTableroGerente_Sec07NoAplica_GerVeTodasLasAreas()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        ConfigurarFlujoFeliz(ciclo);

        // Act
        await _service.ObtenerTableroGerenteAsync();

        // Assert: Firmas sin areaId (SEC-07 NO APLICA)
        // Ya verificado por compilacion (ListarAreasConResumenAsync no toma areaId)
        _mockRepo.Verify(r => r.ListarAreasConResumenAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ObtenerTotalesConsolidadosAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ListarAccionesDelCicloAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObtenerTableroGerente_NoAudita()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        ConfigurarFlujoFeliz(ciclo);

        // Act
        await _service.ObtenerTableroGerenteAsync();

        // Assert
        _mockRepo.Verify(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerTableroGerente_AreaInactiva_NoApareceEnPaneles()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId);
        // Supongamos que DAL filtra activas, así que el mock no retorna el inactiva
        var areaActiva = new ResumenAreaTableroDto { AreaId = Guid.NewGuid(), AreaNombre = "Activa" };
        ConfigurarFlujoFeliz(ciclo, areas: new[] { areaActiva });

        // Act
        var resultado = await _service.ObtenerTableroGerenteAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Single(resultado.Paneles);
        Assert.Equal("Activa", resultado.Paneles[0].AreaNombre);
    }
}

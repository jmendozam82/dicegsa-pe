using System.Data;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Responses.Dashboard;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para DashboardService — Spec HU-015 § "Tests requeridos" (20 casos de la
/// tabla, L169-192). TDD fase red por contrato (TEST-01): IDashboardService/DashboardService/
/// SemaforoHelper y las extensiones DAL-D1/D3/D4/D5 de ICicloRepository son STUBS que lanzan
/// NotImplementedException (@QA declaró el contrato; @BackendDev implementa en fase 4) → los 20
/// tests COMPILAN y FALLAN en runtime (rojo esperado, TEST-01).
/// Moq sobre ICicloRepository + TenantContext real (D17: TenantId nullable; D12: re-validación
/// de rol en BLL — solo 'JefeArea', D-F). Ctor de DashboardService: (ICicloRepository,
/// TenantContext) + overload con ILogger (D13) — se usa el ctor de 2 args.
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// Contrato DAL (firmas exactas que @BackendDev debe implementar en ICicloRepository):
///   · DAL-D1 Task&lt;CicloEntity?&gt; ObtenerCicloActivoAsync(Guid tenantId, CancellationToken ct = default)
///   · DAL-D3 Task&lt;ResumenOkrsAreaDto&gt; ObtenerResumenOkrsAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
///   · DAL-D4 Task&lt;ResumenPlanAccionAreaDto&gt; ObtenerResumenPlanAccionAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
///   · DAL-D5 Task&lt;IEnumerable&lt;AccionTableroDto&gt;&gt; ListarAccionesAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
///   · Reuso DAL-A2 ObtenerAreaPorIdAsync (HU-009) y DAL-C10 ObtenerUmbralesAsync (HU-008).
/// Lógica BLL bajo test (spec §3): D12 rol → tenant → DAL-D1 ciclo activo (CA #4) → AreaId
/// (SEC-07) → DAL-A2 área → DAL-C10 umbrales (defaults 0.90/0.70 si falta tipo, H5) → DAL-D3/D4/D5
/// → atrasadas recalculadas (RN-017 regla 4, D-D: DateTime.Today &gt; FechaVencimiento.Date && Progreso &lt; 100)
/// → días al vencimiento (min sobre pendientes; null si no hay; negativo si vencida) → semáforos
/// con SemaforoHelper (D-E) → 200 ApiResponse. Sin auditoría (D-H) y sin transacción.
/// </summary>
public class DashboardServiceTests
{
    private readonly Mock<ICicloRepository> _mockRepo;
    private readonly TenantContext _tenantContext;
    private readonly IDashboardService _service;

    public DashboardServiceTests()
    {
        _mockRepo = new Mock<ICicloRepository>();
        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "JefeArea", // D-F: solo el JEF tiene tablero en esta HU (HU-016 es del GER)
            AreaId = Guid.NewGuid()
        };

        _service = new DashboardService(_mockRepo.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(
        Guid? id = null, Guid? tenantId = null, string nombre = "PE 2026", int añoFiscal = 2026,
        int mesInicio = 1, string estado = "Activo", Guid? createdBy = null,
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

    /// <summary>Acción del tablero (DAL-D5). Fechas relativas a DateTime.Today real (D-D: el
    /// tablero recalcula atrasadas/días con la fecha actual, no con status persistido).</summary>
    private static AccionTableroDto CrearAccion(
        Guid? id = null, decimal progreso = 0m, DateTime? fechaInicio = null, DateTime? fechaVencimiento = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Progreso = progreso,
            FechaInicio = fechaInicio ?? DateTime.Today,
            FechaVencimiento = fechaVencimiento ?? DateTime.Today
        };

    /// <summary>Configura el flujo feliz de ObtenerTableroJefeAreaAsync (spec §3 pasos 3-9):
    /// ciclo activo (DAL-D1), área del JEF (DAL-A2), umbrales KPI+PlanAccion 0.90/0.70 (DAL-C10),
    /// resumen OKRs (DAL-D3), resumen plan (DAL-D4) y acciones (DAL-D5). Los tests sobreescriben
    /// los valores por defecto según el escenario.</summary>
    private void ConfigurarFlujoFeliz(
        CicloEntity ciclo, AreaEntity area,
        IEnumerable<UmbralSemaforoEntity>? umbrales = null,
        ResumenOkrsAreaDto? resumenOkrs = null,
        ResumenPlanAccionAreaDto? resumenPlan = null,
        IEnumerable<AccionTableroDto>? acciones = null)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;

        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(area);
        _mockRepo
            .Setup(r => r.ObtenerUmbralesAsync(tenantId, ciclo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(umbrales ?? new[]
            {
                CrearUmbral("KPI", 0.90m, 0.70m, ciclo.Id, tenantId),
                CrearUmbral("PlanAccion", 0.90m, 0.70m, ciclo.Id, tenantId)
            });
        _mockRepo
            .Setup(r => r.ObtenerResumenOkrsAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumenOkrs ?? new ResumenOkrsAreaDto());
        _mockRepo
            .Setup(r => r.ObtenerResumenPlanAccionAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumenPlan ?? new ResumenPlanAccionAreaDto());
        _mockRepo
            .Setup(r => r.ListarAccionesAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acciones ?? Array.Empty<AccionTableroDto>());
    }

    // ─── Caso 1-5. Guardas de acceso y validación ───────────────────────────

    // Caso 1 ─ rol ≠ JefeArea → AccesoDenegadoException 403 (D12/D-F); ninguna DAL de lectura se invoca
    [Fact]
    public async Task ObtenerTablero_RolNoJefeArea_LanzaAccesoDenegado()
    {
        // Arrange: D-F — solo el JEF tiene tablero en esta HU (el tablero del GER es HU-016)
        _tenantContext.Rol = "Gerente";

        // Act & Assert: 403 y ninguna DAL de lectura se invoca
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerTableroJefeAreaAsync());

        _mockRepo.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerAreaPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerUmbralesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerResumenPlanAccionAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ListarAccionesAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 2 ─ TenantContext.TenantId=null → NotFoundException 404 (D17); el repo nunca se consulta
    [Fact]
    public async Task ObtenerTablero_SinTenant_LanzaNotFound()
    {
        // Arrange: D17 — TenantId null (SuperAdmin sin tenant)
        _tenantContext.TenantId = null;

        // Act & Assert: 404 y ninguna query del repo se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroJefeAreaAsync());

        _mockRepo.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerAreaPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerUmbralesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 3 ─ sin ciclo activo (DAL-D1 → null) → NotFoundException 404 (CA #4)
    [Fact]
    public async Task ObtenerTablero_SinCicloActivo_LanzaNotFound()
    {
        // Arrange: CA #4 — no hay ciclo 'Activo' para el tenant (RC-01: máx 1)
        var tenantId = _tenantContext.TenantId!.Value;
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert: 404 y ninguna query de área/tablero se ejecuta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroJefeAreaAsync());

        _mockRepo.Verify(r => r.ObtenerAreaPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerUmbralesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 4 ─ JefeArea sin área asignada (AreaId=null) → NotFoundException 404 (SEC-07)
    [Fact]
    public async Task ObtenerTablero_JefeSinAreaAsignada_LanzaNotFound()
    {
        // Arrange: SEC-07 — el JEF sin area_id no puede tener tablero (no hay área que filtrar)
        var tenantId = _tenantContext.TenantId!.Value;
        _tenantContext.AreaId = null;
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearCiclo(tenantId: tenantId, estado: "Activo"));

        // Act & Assert: 404 y DAL-A2 nunca se consulta
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroJefeAreaAsync());

        _mockRepo.Verify(r => r.ObtenerAreaPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerUmbralesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 5 ─ área inexistente (DAL-A2 → null) → NotFoundException 404 (sin fuga)
    [Fact]
    public async Task ObtenerTablero_AreaInexistente_LanzaNotFound()
    {
        // Arrange: DAL-A2 devuelve null (área inexistente o de otro tenant — sin fuga SEC-06)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AreaEntity?)null);

        // Act & Assert: 404 y las queries del tablero nunca se ejecutan
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerTableroJefeAreaAsync());

        _mockRepo.Verify(r => r.ObtenerUmbralesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ObtenerResumenPlanAccionAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.ListarAccionesAreaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Caso 6-12. Tarjetas (CA #1) ────────────────────────────────────────

    // Caso 6 ─ tablas vacías → 200 con ceros y semáforos "Rojo" (D-B/D-N: fórmula pura)
    [Fact]
    public async Task ObtenerTablero_SinDatos_RetornaCerosYEstadoVacio()
    {
        // Arrange: D-B — tablas okr/objetivo_cg/accion_plan vacías (hasta HU-017+/HU-024+)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            resumenOkrs: new ResumenOkrsAreaDto { TotalOkrs = 0, OkrsAlcanzados = 0, PromedioPuntuacionOkrs = 0m },
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0m },
            acciones: Array.Empty<AccionTableroDto>());

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: 200 con ceros y estado vacío; semáforos "Rojo" (0 < umbral amarillo, D-N)
        Assert.True(resultado.Success);
        Assert.NotNull(resultado.Data);
        Assert.Equal(0, resultado.Data.Tarjetas.TotalOkrs);
        Assert.Equal(0, resultado.Data.Tarjetas.OkrsAlcanzados);
        Assert.Equal(0m, resultado.Data.Tarjetas.AvancePlanAccion);
        Assert.Equal(0, resultado.Data.Tarjetas.AccionesAtrasadas);
        Assert.Null(resultado.Data.Tarjetas.DiasAlVencimientoMasCercano);
        Assert.Equal("Rojo", resultado.Data.Semaforo.Okrs);
        Assert.Equal("Rojo", resultado.Data.Semaforo.PlanAccion);
        Assert.Equal("Rojo", resultado.Data.Semaforo.Global);
        Assert.Equal(0m, resultado.Data.Semaforo.PromedioPuntuacionOkrs);
    }

    // Caso 7 ─ con datos → tarjetas calculadas desde los DTOs DAL (D-C)
    [Fact]
    public async Task ObtenerTablero_ConDatos_CalculaTarjetasCorrectamente()
    {
        // Arrange: 5 OKRs (3 Verde) → TotalOkrs=5, OkrsAlcanzados=3; 2 objetivos CG (0.8 y 0.6)
        // → AVG=0.7 (el AVG lo calcula el DAL, D-C; la BLL solo mapea)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            resumenOkrs: new ResumenOkrsAreaDto { TotalOkrs = 5, OkrsAlcanzados = 3, PromedioPuntuacionOkrs = 0.85m },
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.7m });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: tarjetas CA #1 pobladas desde los DTOs DAL
        Assert.NotNull(resultado.Data);
        Assert.Equal(5, resultado.Data.Tarjetas.TotalOkrs);
        Assert.Equal(3, resultado.Data.Tarjetas.OkrsAlcanzados);
        Assert.Equal(0.7m, resultado.Data.Tarjetas.AvancePlanAccion);
    }

    // Caso 8 ─ acción vencida sin terminar → atrasada (RN-017 regla 4, D-D: NO usa status persistido)
    [Fact]
    public async Task ObtenerTablero_AccionVencidaSinTerminar_RecalculaComoAtrasada()
    {
        // Arrange: D-D — vencida (fecha_vencimiento < hoy) + progreso=40 → atrasada. El status
        // persistido ('EnProgreso') NO viaja en el DTO ni se lee: el tablero recalcula con fecha actual.
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var accion = CrearAccion(progreso: 40m, fechaVencimiento: DateTime.Today.AddDays(-1));
        ConfigurarFlujoFeliz(ciclo, area, acciones: new[] { accion });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: 1 atrasada (DateTime.Today > FechaVencimiento.Date && Progreso < 100)
        Assert.NotNull(resultado.Data);
        Assert.Equal(1, resultado.Data.Tarjetas.AccionesAtrasadas);
    }

    // Caso 9 ─ acción terminada (progreso=100) aunque vencida → NO atrasada (RN-017 regla 4)
    [Fact]
    public async Task ObtenerTablero_AccionTerminada_NoCuentaComoAtrasada()
    {
        // Arrange: progreso=100 → terminada; la condición Progreso < 100 excluye (aunque vencida)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var accion = CrearAccion(progreso: 100m, fechaVencimiento: DateTime.Today.AddDays(-5));
        ConfigurarFlujoFeliz(ciclo, area, acciones: new[] { accion });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: 0 atrasadas
        Assert.NotNull(resultado.Data);
        Assert.Equal(0, resultado.Data.Tarjetas.AccionesAtrasadas);
    }

    // Caso 10 ─ días al vencimiento: min sobre PENDIENTES (la terminada se excluye)
    [Fact]
    public async Task ObtenerTablero_DiasAlVencimiento_CalculaMinimoSobrePendientes()
    {
        // Arrange: pendientes hoy+5 y hoy+2; terminada hoy+1 (excluida) → min = 2
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var acciones = new[]
        {
            CrearAccion(progreso: 50m, fechaVencimiento: DateTime.Today.AddDays(5)),
            CrearAccion(progreso: 80m, fechaVencimiento: DateTime.Today.AddDays(2)),
            CrearAccion(progreso: 100m, fechaVencimiento: DateTime.Today.AddDays(1)) // terminada → excluida
        };
        ConfigurarFlujoFeliz(ciclo, area, acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: min sobre pendientes (Progreso < 100) de (FechaVencimiento.Date - Today).Days
        Assert.NotNull(resultado.Data);
        Assert.Equal(2, resultado.Data.Tarjetas.DiasAlVencimientoMasCercano);
    }

    // Caso 11 ─ sin acciones pendientes → DiasAlVencimientoMasCercano = null
    [Fact]
    public async Task ObtenerTablero_DiasAlVencimiento_SinAccionesPendientes_RetornaNull()
    {
        // Arrange: todas terminadas (progreso=100)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var acciones = new[]
        {
            CrearAccion(progreso: 100m, fechaVencimiento: DateTime.Today.AddDays(1)),
            CrearAccion(progreso: 100m, fechaVencimiento: DateTime.Today.AddDays(-3))
        };
        ConfigurarFlujoFeliz(ciclo, area, acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: null (no hay pendientes → sin vencimiento más cercano)
        Assert.NotNull(resultado.Data);
        Assert.Null(resultado.Data.Tarjetas.DiasAlVencimientoMasCercano);
    }

    // Caso 12 ─ acción pendiente vencida → días negativo (vencida hace N días)
    [Fact]
    public async Task ObtenerTablero_DiasAlVencimiento_AccionVencida_RetornaNegativo()
    {
        // Arrange: pendiente vencida hace 3 días → (hoy-3) - hoy = -3
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var acciones = new[]
        {
            CrearAccion(progreso: 60m, fechaVencimiento: DateTime.Today.AddDays(-3))
        };
        ConfigurarFlujoFeliz(ciclo, area, acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: -3 (negativo = vencida hace N días)
        Assert.NotNull(resultado.Data);
        Assert.Equal(-3, resultado.Data.Tarjetas.DiasAlVencimientoMasCercano);
    }

    // ─── Caso 13-16. Semáforos (CA #2, D-E) ─────────────────────────────────

    // Caso 13 ─ semáforo OKRs con umbrales KPI: 0.92 ≥ 0.90 → "Verde"
    [Fact]
    public async Task ObtenerTablero_SemaforoOkrs_EvaluaConUmbralesKpi()
    {
        // Arrange: promedio 0.92 contra umbral KPI 0.90/0.70 → "Verde"
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            resumenOkrs: new ResumenOkrsAreaDto { TotalOkrs = 5, OkrsAlcanzados = 4, PromedioPuntuacionOkrs = 0.92m });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: SemaforoHelper.Evaluar(0.92, 0.90, 0.70) = "Verde"
        Assert.NotNull(resultado.Data);
        Assert.Equal("Verde", resultado.Data.Semaforo.Okrs);
        Assert.Equal(0.92m, resultado.Data.Semaforo.PromedioPuntuacionOkrs);
    }

    // Caso 14 ─ semáforo plan de acción con umbrales PlanAccion: 0.75 → "Amarillo"
    [Fact]
    public async Task ObtenerTablero_SemaforoPlan_EvaluaConUmbralesPlanAccion()
    {
        // Arrange: avance 0.75 contra umbral PlanAccion 0.90/0.70 → "Amarillo" (0.70 ≤ 0.75 < 0.90)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.75m });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: SemaforoHelper.Evaluar(0.75, 0.90, 0.70) = "Amarillo"
        Assert.NotNull(resultado.Data);
        Assert.Equal("Amarillo", resultado.Data.Semaforo.PlanAccion);
    }

    // Caso 15 ─ semáforo global = promedio numérico (OKRs + plan) / 2 contra umbrales KPI (D-E)
    [Fact]
    public async Task ObtenerTablero_SemaforoGlobal_PromediaOkrsYPlan()
    {
        // Arrange: D-E — global = (0.85 + 0.75) / 2 = 0.80 → entre 0.70 y 0.90 → "Amarillo"
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            resumenOkrs: new ResumenOkrsAreaDto { TotalOkrs = 5, OkrsAlcanzados = 3, PromedioPuntuacionOkrs = 0.85m },
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.75m });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: (0.85 + 0.75) / 2 = 0.80 → "Amarillo" contra umbrales KPI (0.90/0.70)
        Assert.NotNull(resultado.Data);
        Assert.Equal("Amarillo", resultado.Data.Semaforo.Global);
        Assert.Equal(0.85m, resultado.Data.Semaforo.PromedioPuntuacionOkrs);
    }

    // Caso 16 ─ umbral faltante (sin fila PlanAccion) → defaults 0.90/0.70 (H5, patrón HU-008 D5)
    [Fact]
    public async Task ObtenerTablero_UmbralFaltante_UsaDefaults()
    {
        // Arrange: H5 — umbral_semaforo solo tiene la fila KPI; la PlanAccion usa defaults 0.90/0.70
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area,
            umbrales: new[] { CrearUmbral("KPI", 0.90m, 0.70m, ciclo.Id, tenantId) }, // sin PlanAccion
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.95m });

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: 0.95 ≥ 0.90 (default verde) → "Verde" (sin fila PlanAccion en umbral_semaforo)
        Assert.NotNull(resultado.Data);
        Assert.Equal("Verde", resultado.Data.Semaforo.PlanAccion);
    }

    // ─── Caso 17-20. Contrato completo, CA #4, SEC-07 y auditoría ───────────

    // Caso 17 ★ ─ éxito: 200 con contrato completo (CA #1/#2) — ciclo, área, tarjetas y semáforos
    [Fact]
    public async Task ObtenerTablero_Exito_Retorna200ConContratoCompleto()
    {
        // Arrange: datos completos (ciclo activo, área GOL1, 5 OKRs/3 Verde, plan 0.75,
        // 1 atrasada, vencimiento más cercano hoy+3)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, nombre: "PE 2026", añoFiscal: 2026, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        var acciones = new[]
        {
            CrearAccion(progreso: 40m, fechaVencimiento: DateTime.Today.AddDays(-1)), // atrasada
            CrearAccion(progreso: 80m, fechaVencimiento: DateTime.Today.AddDays(3))
        };
        ConfigurarFlujoFeliz(ciclo, area,
            resumenOkrs: new ResumenOkrsAreaDto { TotalOkrs = 5, OkrsAlcanzados = 3, PromedioPuntuacionOkrs = 0.85m },
            resumenPlan: new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.75m },
            acciones: acciones);

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: 200 con el contrato completo poblado (ARCH-07: ApiResponse<T>)
        Assert.True(resultado.Success);
        Assert.NotNull(resultado.Data);
        Assert.Equal(ciclo.Id, resultado.Data.CicloId);
        Assert.Equal("PE 2026", resultado.Data.CicloNombre);
        Assert.Equal(2026, resultado.Data.AñoFiscal);
        Assert.Equal(areaId, resultado.Data.AreaId);
        Assert.Equal("GOL1", resultado.Data.AreaCodigo);
        Assert.Equal("CEDIS FARMA", resultado.Data.AreaNombre);
        Assert.Equal(5, resultado.Data.Tarjetas.TotalOkrs);
        Assert.Equal(3, resultado.Data.Tarjetas.OkrsAlcanzados);
        Assert.Equal(0.75m, resultado.Data.Tarjetas.AvancePlanAccion);
        Assert.Equal(1, resultado.Data.Tarjetas.AccionesAtrasadas);
        Assert.Equal(3, resultado.Data.Tarjetas.DiasAlVencimientoMasCercano);
        Assert.Equal("Amarillo", resultado.Data.Semaforo.Okrs);       // 0.85 → Amarillo
        Assert.Equal("Amarillo", resultado.Data.Semaforo.PlanAccion); // 0.75 → Amarillo
        Assert.Equal("Amarillo", resultado.Data.Semaforo.Global);     // (0.85+0.75)/2 = 0.80 → Amarillo
    }

    // Caso 18 ─ CA #4: solo el ciclo Activo; las queries filtran por ciclo.Id (nunca el Cerrado)
    [Fact]
    public async Task ObtenerTablero_SoloCicloActivo_IgnoraOtrosCiclos()
    {
        // Arrange: existe un ciclo Cerrado con datos del área, pero DAL-D1 devuelve solo el Activo
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var cicloActivo = CrearCiclo(tenantId: tenantId, nombre: "PE 2026", añoFiscal: 2026, estado: "Activo");
        var cicloCerrado = CrearCiclo(tenantId: tenantId, nombre: "PE 2025", añoFiscal: 2025, estado: "Cerrado");
        var area = CrearArea(areaId, tenantId, cicloActivo.Id, "GOL1", "CEDIS FARMA");
        _mockRepo
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cicloActivo); // RC-01: máx 1 Activo por tenant → solo este
        _mockRepo
            .Setup(r => r.ObtenerAreaPorIdAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(area);
        _mockRepo
            .Setup(r => r.ObtenerUmbralesAsync(tenantId, cicloActivo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CrearUmbral("KPI", 0.90m, 0.70m, cicloActivo.Id, tenantId),
                CrearUmbral("PlanAccion", 0.90m, 0.70m, cicloActivo.Id, tenantId)
            });
        _mockRepo
            .Setup(r => r.ObtenerResumenOkrsAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenOkrsAreaDto { TotalOkrs = 5, OkrsAlcanzados = 3, PromedioPuntuacionOkrs = 0.85m });
        _mockRepo
            .Setup(r => r.ObtenerResumenPlanAccionAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenPlanAccionAreaDto { AvancePlanAccion = 0.75m });
        _mockRepo
            .Setup(r => r.ListarAccionesAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccionTableroDto>());

        // Act
        var resultado = await _service.ObtenerTableroJefeAreaAsync();

        // Assert: el tablero es del ciclo Activo; DAL-D3/D4/D5 se invocan con ciclo.Id (CA #4)
        Assert.NotNull(resultado.Data);
        Assert.Equal(cicloActivo.Id, resultado.Data.CicloId);
        Assert.Equal("PE 2026", resultado.Data.CicloNombre);
        Assert.Equal(2026, resultado.Data.AñoFiscal);
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ObtenerResumenPlanAccionAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ListarAccionesAreaAsync(tenantId, cicloActivo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(tenantId, cicloCerrado.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 19 ─ SEC-07: DAL-D3/D4/D5 invocadas con areaId = TenantContext.AreaId
    [Fact]
    public async Task ObtenerTablero_Sec07_TodasLasQueriesFiltranPorAreaDelJef()
    {
        // Arrange: flujo feliz (el areaId del contexto es el del JEF)
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area);

        // Act
        await _service.ObtenerTableroJefeAreaAsync();

        // Assert: las 3 queries del tablero se invocan con areaId = TenantContext.AreaId (SEC-07)
        _mockRepo.Verify(r => r.ObtenerResumenOkrsAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ObtenerResumenPlanAccionAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.ListarAccionesAreaAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 20 ─ D-H: solo lectura → InsertLogAsync NUNCA se invoca (patrón HU-005 CA #3)
    [Fact]
    public async Task ObtenerTablero_NoAudita()
    {
        // Arrange: flujo feliz
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(tenantId: tenantId, estado: "Activo");
        var area = CrearArea(areaId, tenantId, ciclo.Id, "GOL1", "CEDIS FARMA");
        ConfigurarFlujoFeliz(ciclo, area);

        // Act
        await _service.ObtenerTableroJefeAreaAsync();

        // Assert: sin auditoría (endpoint de solo lectura, D-H)
        _mockRepo.Verify(
            r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
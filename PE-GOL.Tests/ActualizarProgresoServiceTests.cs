using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests for HU-020 — Actualización de Progreso de Acciones.
/// Cubre RN-017 (4 reglas de status), RN-018 (recálculo CG), historial y comportamiento idempotente.
/// Escritos por @QA antes de la implementación (TDD — TEST-01).
/// </summary>
public class ActualizarProgresoServiceTests
{
    private readonly Mock<IAccionPlanRepository> _repoMock;
    private readonly Mock<IHistorialProgresoRepository> _historialRepoMock;
    private readonly Mock<IObjetivoCgRepository> _objRepoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly TenantContext _tenantContext;
    private readonly IAccionPlanService _service;

    public ActualizarProgresoServiceTests()
    {
        _repoMock         = new Mock<IAccionPlanRepository>();
        _historialRepoMock = new Mock<IHistorialProgresoRepository>();
        _objRepoMock      = new Mock<IObjetivoCgRepository>();
        _cicloRepoMock    = new Mock<ICicloRepository>();

        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId   = Guid.NewGuid(),
            Rol      = "JefeArea",
            AreaId   = Guid.NewGuid()
        };

        _service = new AccionPlanService(
            _repoMock.Object,
            _cicloRepoMock.Object,
            _objRepoMock.Object,
            _tenantContext,
            _historialRepoMock.Object);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private AccionPlanEntity BuildAccion(
        decimal progreso,
        DateTime? fechaInicio    = null,
        DateTime? fechaVencimiento = null)
    {
        return new AccionPlanEntity
        {
            Id             = Guid.NewGuid(),
            TenantId       = _tenantContext.TenantId!.Value,
            AreaId         = _tenantContext.AreaId!.Value,
            ObjetivoCgId   = Guid.NewGuid(),
            Peso           = 0.4m,
            Progreso       = progreso,
            Status         = "NoIniciado",
            FechaInicio    = fechaInicio    ?? DateTime.UtcNow.AddDays(-10),
            FechaVencimiento = fechaVencimiento ?? DateTime.UtcNow.AddDays(30)
        };
    }

    private void SetupAccion(AccionPlanEntity accion)
    {
        _repoMock.Setup(r => r.ObtenerPorIdAsync(accion.Id, _tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(accion);
        _objRepoMock.Setup(r => r.RecalcularProgresoAsync(
            accion.ObjetivoCgId, It.IsAny<decimal>(), It.IsAny<string>(),
            _tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _historialRepoMock.Setup(r => r.InsertAsync(It.IsAny<HistorialProgresoEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ─── RN-017 Status Rules ─────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarProgreso_Progreso100_StatusTerminado()
    {
        // Arrange — RN-017 regla 1: si progreso = 100 → Terminado (independiente de fechas)
        var accion = BuildAccion(50, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(10));
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 100m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Terminado", result.Status);
        Assert.Equal(100m, result.Progreso);
    }

    [Fact]
    public async Task ActualizarProgreso_Progreso50_FechaFutura_StatusEnProgreso()
    {
        // Arrange — RN-017 regla 2: progreso > 0 y fecha actual <= vencimiento → EnProgreso
        var accion = BuildAccion(0,
            fechaInicio:       DateTime.UtcNow.AddDays(-5),
            fechaVencimiento:  DateTime.UtcNow.AddDays(20));
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 50m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.Equal("EnProgreso", result.Status);
    }

    [Fact]
    public async Task ActualizarProgreso_Progreso0_FechaInicioFutura_StatusNoIniciado()
    {
        // Arrange — RN-017 regla 3: progreso = 0 y fecha actual <= fechaInicio → NoIniciado
        var accion = BuildAccion(0,
            fechaInicio:       DateTime.UtcNow.AddDays(5),   // inicio en el futuro
            fechaVencimiento:  DateTime.UtcNow.AddDays(20));
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 0m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.Equal("NoIniciado", result.Status);
    }

    [Fact]
    public async Task ActualizarProgreso_Progreso50_FechaVencida_StatusAtrasado()
    {
        // Arrange — RN-017 regla 4: fecha actual > vencimiento y progreso < 100 → Atrasado
        var accion = BuildAccion(30,
            fechaInicio:       DateTime.UtcNow.AddDays(-20),
            fechaVencimiento:  DateTime.UtcNow.AddDays(-1)); // vencida ayer
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 50m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.Equal("Atrasado", result.Status);
    }

    [Fact]
    public async Task ActualizarProgreso_Progreso0_FechaVencida_StatusAtrasado()
    {
        // Arrange — RN-017 regla 4: progreso = 0 pero fecha ya venció → Atrasado (no NoIniciado)
        // La acción parte de progreso=30 para que F3 (idempotente) no cortocircuite al enviar 0.
        var accion = BuildAccion(30m,
            fechaInicio:      DateTime.UtcNow.AddDays(-10),
            fechaVencimiento: DateTime.UtcNow.AddDays(-2)); // vencida
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 0m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.Equal("Atrasado", result.Status);
    }

    // ─── Puntuación Ponderada ────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarProgreso_CalculaPuntuacionPonderada_Correctamente()
    {
        // Arrange — puntuacion = peso * progreso / 100 = 0.4 * 75 / 100 = 0.3
        var accion = BuildAccion(0);
        accion.Peso = 0.4m;
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 75m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.Equal(0.3m, result.PuntuacionPonderada);
    }

    // ─── Historial ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarProgreso_SinCambioDeProgreso_NoInsertaHistorial()
    {
        // Arrange — F3: mismo valor de progreso → idempotente, sin historial
        var accion = BuildAccion(50m); // ya tiene 50%
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 50m };

        // Act
        var result = await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        Assert.NotNull(result);
        _historialRepoMock.Verify(r => r.InsertAsync(
            It.IsAny<HistorialProgresoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActualizarProgreso_ConCambioDeProgreso_InsertaHistorialConUsuarioCorreto()
    {
        // Arrange — valor distinto → debe insertar historial con RegistradoPor = UserId
        var accion = BuildAccion(30m);
        SetupAccion(accion);
        var request = new ActualizarProgresoRequest { Progreso = 60m };

        HistorialProgresoEntity? capturedHistorial = null;
        _historialRepoMock.Setup(r => r.InsertAsync(It.IsAny<HistorialProgresoEntity>(), It.IsAny<CancellationToken>()))
            .Callback<HistorialProgresoEntity, CancellationToken>((h, _) => capturedHistorial = h)
            .Returns(Task.CompletedTask);

        // Act
        await _service.ActualizarProgresoAsync(accion.Id, request);

        // Assert
        _historialRepoMock.Verify(r => r.InsertAsync(
            It.IsAny<HistorialProgresoEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedHistorial);
        Assert.Equal(30m, capturedHistorial!.ProgresoAnterior);
        Assert.Equal(60m, capturedHistorial.ProgresoNuevo);
        Assert.Equal(_tenantContext.UserId, capturedHistorial.RegistradoPor);
    }

    // ─── Seguridad / Autorización ─────────────────────────────────────────────

    [Fact]
    public async Task ActualizarProgreso_RolGerente_LanzaAccesoDenegado()
    {
        // Arrange — solo JefeArea puede actualizar progreso
        var tcGerente = new TenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), Rol = "Gerente" };
        var serviceGerente = new AccionPlanService(
            _repoMock.Object, _cicloRepoMock.Object, _objRepoMock.Object, tcGerente, _historialRepoMock.Object);

        var accion = new AccionPlanEntity { Id = Guid.NewGuid(), AreaId = Guid.NewGuid() };
        _repoMock.Setup(r => r.ObtenerPorIdAsync(accion.Id, tcGerente.TenantId!.Value, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(accion);

        var request = new ActualizarProgresoRequest { Progreso = 50m };

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => serviceGerente.ActualizarProgresoAsync(accion.Id, request));
    }

    [Fact]
    public async Task ActualizarProgreso_AreaDiferente_LanzaAccesoDenegado()
    {
        // Arrange — SEC-07: el área de la acción ≠ área del contexto
        var accion = BuildAccion(0m);
        accion.AreaId = Guid.NewGuid(); // distinta al _tenantContext.AreaId
        _repoMock.Setup(r => r.ObtenerPorIdAsync(accion.Id, _tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(accion);

        var request = new ActualizarProgresoRequest { Progreso = 50m };

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ActualizarProgresoAsync(accion.Id, request));
    }
}

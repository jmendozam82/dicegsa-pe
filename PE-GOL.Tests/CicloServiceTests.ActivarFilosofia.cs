using System.Data;
using Moq;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Limites;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Tests;

/// <summary>
/// Extensión de CicloServiceTests — HU-011 CA #2 (Spec HU-011 § "Tests requeridos" #16-#18).
/// TDD fase red (TEST-01): ObtenerFilosofiaAsync (DAL-F1) AÚN NO existe en ICicloRepository →
/// estos 3 tests NO compilan hasta que @BackendDev implemente la extensión (fase 4).
/// La validación de filosofía se inserta en ActivarAsync DESPUÉS de RC-01 (DAL-C7) y ANTES del
/// límite del plan (spec §3 paso 3; Flag #1 resuelto por Jorge: validación dura 422).
/// Los tests #16/#17 verifican que el flujo corta en la nueva validación (ni UPDATE de estado ni
/// consulta del plan); el #18 verifica el flujo normal de activación con filosofía completa
/// (RC-01, límite plan, UPDATE + auditoría ACTIVATE) → 200.
/// </summary>
public partial class CicloServiceTests
{
    // Caso 16 ─ DAL-F1 → null → 422 "Debe registrar la Visión y Misión del ciclo antes de activarlo"
    [Fact]
    public async Task Activar_SinFilosofia_LanzaValidacion()
    {
        // Arrange: RC-01 pasa (0 activos) y la nueva validación de filosofía (CA #2) corta ANTES
        // del límite del plan (spec §3 paso 3: después de RC-01, antes del paso 7)
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador);
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilosofiaEntity?)null);

        // Act & Assert: 422 con el mensaje del CA #2; ni UPDATE de estado ni límite del plan
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("Visión y Misión", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.ObtenerPlanIdDelTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 17 ─ Vision="<p></p>" (vacío tras sanitizar) → 422 (CA #2)
    [Fact]
    public async Task Activar_FilosofiaConVisionVacia_LanzaValidacion()
    {
        // Arrange: la fila existe pero la Visión queda sin texto visible tras quitar etiquetas (CA #2)
        var tenantId = _tenantContext.TenantId!.Value;
        var id = Guid.NewGuid();
        var borrador = CrearCiclo(id, tenantId, "PE 2026", 2026, 1, "Borrador");
        var filosofia = new FilosofiaEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CicloId = id,
            Vision = "<p></p>",
            Mision = "<p>Servir con excelencia</p>",
            Valores = "[]",
            UpdatedBy = _tenantContext.UserId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockRepo
            .Setup(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador);
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filosofia);

        // Act & Assert: 422 (CA #2) y el UPDATE de estado nunca se ejecuta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _service.ActivarAsync(id));

        Assert.Contains("Visión y Misión", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.UpdateEstadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 18 ★ ─ Vision/Mision con texto visible → flujo normal de activación (RC-01, límite plan,
    // UPDATE + auditoría ACTIVATE) → 200
    [Fact]
    public async Task Activar_FilosofiaCompleta_ActivaCiclo()
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
        var filosofia = new FilosofiaEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CicloId = id,
            Vision = "<p>Ser el líder del mercado</p>",
            Mision = "<p>Servir con excelencia</p>",
            Valores = "[]",
            UpdatedBy = _tenantContext.UserId,
            UpdatedByNombre = "Gerente General",
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockRepo
            .SetupSequence(r => r.ObtenerPorIdAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrador)   // 1ª llamada: validación
            .ReturnsAsync(activo);    // 2ª llamada: construcción de la respuesta
        _mockRepo
            .Setup(r => r.ContarCiclosActivosAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockRepo
            .Setup(r => r.ObtenerFilosofiaAsync(tenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filosofia);
        _mockRepo
            .Setup(r => r.ObtenerPlanIdDelTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planId);
        _mockPlanService
            .Setup(s => s.ValidarLimitesParaTenantAsync(tenantId, planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoValidacionLimites.Ok());
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

        // Assert: flujo normal de activación (RC-01, límite plan, UPDATE + auditoría ACTIVATE)
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
}
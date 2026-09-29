using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-023 — Exportación Excel del Plan Consolidado.
/// Spec HU-023 § "Tests requeridos" (casos 26-32).
/// Escritos ANTES de la implementación (TEST-01): los miembros de producción
/// (PlanConsolidadoService, IPlanConsolidadoRepository, DTOs) NO existen todavía
/// → el rojo legítimo es el FALLO DE COMPILACIÓN del proyecto de tests.
///
/// Contrato que @BackendDev debe implementar:
///   BLL  · PE-GOL.BLL/Services/PlanConsolidadoService.cs
///         Ctor: (IPlanConsolidadoRepository, ICicloRepository, TenantContext)
///         ExportarConsolidadoAsync(FiltrosConsolidadoRequest, CancellationToken) → Task<byte[]>
///   DAL  · PE-GOL.DAL/Interfaces/IPlanConsolidadoRepository.cs
///         ListarConsolidadoAsync(Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest, bool skipPaginacion, CancellationToken)
///   DTOs · PE-GOL.DTO/Requests/PlanOperativo/FiltrosConsolidadoRequest.cs
///         PE-GOL.DTO/Responses/PlanOperativo/ConsolidadoItemResponse.cs
/// </summary>
public class PlanConsolidadoExportarTests
{
    private readonly Mock<IPlanConsolidadoRepository> _repoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly TenantContext _tenantContext;
    private readonly IPlanConsolidadoService _service;

    public PlanConsolidadoExportarTests()
    {
        _repoMock = new Mock<IPlanConsolidadoRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();

        _tenantContext = new TenantContext
        {
            TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            UserId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Rol = "Gerente"
        };

        _service = new PlanConsolidadoService(_repoMock.Object, _cicloRepoMock.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(Guid? id = null, string nombre = "PE 2026", int añoFiscal = 2026)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Nombre = nombre,
            AñoFiscal = añoFiscal,
            MesInicio = 1,
            Estado = "Activo"
        };

    private static ConsolidadoItemResponse CrearItem(string areaCodigo, string cgCodigo, DateTime fechaInicio, string status = "EnProgreso")
        => new()
        {
            AccionId = Guid.NewGuid(),
            AccionCodigo = $"{areaCodigo}.{cgCodigo}.A1",
            AccionDescripcion = "Acción de prueba",
            FechaInicio = fechaInicio,
            FechaVencimiento = fechaInicio.AddMonths(3),
            Clasificacion = "Proyecto",
            TipoPresupuesto = "OPEX",
            Progreso = 50m,
            Status = status,
            Peso = 0.5m,
            ResponsableNombre = "Juan Pérez",
            ObjetivoCgId = Guid.NewGuid(),
            ObjetivoCodigo = cgCodigo,
            ObjetivoDescripcion = "Objetivo de prueba",
            ObjetivoProgreso = 50m,
            ObjetivoSemaforo = "Amarillo",
            PilarCodigo = "PEC-1",
            PilarNombre = "Crecimiento",
            AreaId = Guid.NewGuid(),
            AreaCodigo = areaCodigo,
            AreaNombre = "Área de prueba",
            CicloId = Guid.NewGuid(),
            CicloNombre = "PE 2026",
            CicloAnioFiscal = 2026
        };

    private void ConfigurarCicloActivo(CicloEntity ciclo)
    {
        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);
    }

    // ══════════════════════════════ EXPORTACIÓN EXCEL (7 casos) ══════════════════════════════

    // ─── 26. ExportarConsolidado_ConAcciones_RetornaByteArrayNoVacio ─────────

    [Fact]
    public async Task ExportarConsolidado_ConAcciones_RetornaByteArrayNoVacio()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse>
        {
            CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)),
            CrearItem("GOL1", "CG2", new DateTime(2026, 2, 15)),
            CrearItem("GOL2", "CG1", new DateTime(2026, 3, 15))
        };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // Magic bytes de XLSX (ZIP): PK
        Assert.Equal((byte)'P', result[0]);
        Assert.Equal((byte)'K', result[1]);
    }

    // ─── 27. ExportarConsolidado_SinAcciones_RetornaExcelConEncabezados ───────

    [Fact]
    public async Task ExportarConsolidado_SinAcciones_RetornaExcelConEncabezados()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el archivo es un XLSX válido (magic bytes PK)
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal((byte)'P', result[0]);
        Assert.Equal((byte)'K', result[1]);
    }

    // ─── 28. ExportarConsolidado_SinTenant_LanzaNotFound ─────────────────────

    [Fact]
    public async Task ExportarConsolidado_SinTenant_LanzaNotFound()
    {
        // Arrange
        _tenantContext.TenantId = null;

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest()));
    }

    // ─── 29. ExportarConsolidado_RolNoGerente_LanzaAccesoDenegado ─────────────

    [Fact]
    public async Task ExportarConsolidado_RolNoGerente_LanzaAccesoDenegado()
    {
        // Arrange
        _tenantContext.Rol = "JefeArea";

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest()));
    }

    // ─── 30. ExportarConsolidado_SinCicloActivo_LanzaNotFound ────────────────

    [Fact]
    public async Task ExportarConsolidado_SinCicloActivo_LanzaNotFound()
    {
        // Arrange: no hay ciclo activo
        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest()));
    }

    // ─── 31. ExportarConsolidado_FiltrosAplicados_ExcelContieneSoloFiltradas ─

    [Fact]
    public async Task ExportarConsolidado_FiltrosAplicados_ExcelContieneSoloFiltradas()
    {
        // Arrange: 5 acciones, 2 atrasadas → filtro Status = "Atrasado" → 2 filas
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Status = "Atrasado" };
        var itemsFiltradas = new List<ConsolidadoItemResponse>
        {
            CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15), "Atrasado"),
            CrearItem("GOL2", "CG1", new DateTime(2026, 2, 15), "Atrasado")
        };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(itemsFiltradas);

        // Act
        var result = await _service.ExportarConsolidadoAsync(filtros);

        // Assert: el archivo es un XLSX válido
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.Equal((byte)'P', result[0]);
        Assert.Equal((byte)'K', result[1]);
    }

    // ─── 32. ExportarConsolidado_SoloLectura_NoInvocaNingunaEscritura ─────────

    [Fact]
    public async Task ExportarConsolidado_SoloLectura_NoInvocaNingunaEscritura()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());

        // Act
        await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el repositorio no tiene métodos de escritura, pero verificamos que
        // solo se llamó a ListarConsolidadoAsync (lectura) y no a ningún otro método
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.CountConsolidadoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.ObtenerResumenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

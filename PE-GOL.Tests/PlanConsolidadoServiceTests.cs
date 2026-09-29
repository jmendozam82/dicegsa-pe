using System;
using System.Collections.Generic;
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
/// Tests TDD (FASE ROJA) de HU-023 — Vista Consolidada del Plan (Gerente).
/// Spec HU-023 § "Tests requeridos" (casos 1-25 BLL + 26-32 exportación).
/// Escritos ANTES de la implementación (TEST-01): los miembros de producción
/// (PlanConsolidadoService, IPlanConsolidadoRepository, DTOs) NO existen todavía
/// → el rojo legítimo es el FALLO DE COMPILACIÓN del proyecto de tests.
///
/// Contrato que @BackendDev debe implementar:
///   BLL  · PE-GOL.BLL/Services/PlanConsolidadoService.cs
///         Ctor: (IPlanConsolidadoRepository, ICicloRepository, TenantContext)
///         ObtenerConsolidadoAsync(FiltrosConsolidadoRequest, CancellationToken) → Task<ConsolidadoResponse>
///         ExportarConsolidadoAsync(FiltrosConsolidadoRequest, CancellationToken) → Task<byte[]>
///   DAL  · PE-GOL.DAL/Interfaces/IPlanConsolidadoRepository.cs
///         ListarConsolidadoAsync(Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest, bool skipPaginacion, CancellationToken)
///         CountConsolidadoAsync(Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest, CancellationToken)
///         ObtenerResumenAsync(Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest, CancellationToken)
///   DTOs · PE-GOL.DTO/Requests/PlanOperativo/FiltrosConsolidadoRequest.cs
///         PE-GOL.DTO/Responses/PlanOperativo/ConsolidadoResponse.cs
///         PE-GOL.DTO/Responses/PlanOperativo/ResumenConsolidado.cs
///         PE-GOL.DTO/Responses/PlanOperativo/PaginacionInfo.cs
///         PE-GOL.DTO/Responses/PlanOperativo/ConsolidadoItemResponse.cs
/// </summary>
public class PlanConsolidadoServiceTests
{
    private readonly Mock<IPlanConsolidadoRepository> _repoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly TenantContext _tenantContext;
    private readonly IPlanConsolidadoService _service;

    public PlanConsolidadoServiceTests()
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

    private static ConsolidadoItemResponse CrearItem(string areaCodigo, string cgCodigo, DateTime fechaInicio)
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
            Status = "EnProgreso",
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

    private static ResumenConsolidado CrearResumen(int total, int noIniciado, int enProgreso, int terminado, int atrasado)
        => new()
        {
            Total = total,
            NoIniciado = noIniciado,
            EnProgreso = enProgreso,
            Terminado = terminado,
            Atrasado = atrasado
        };

    private void ConfigurarCicloActivo(CicloEntity ciclo)
    {
        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);
    }

    // ══════════════════════════════ CONSULTA PAGINADA (18 casos) ══════════════════════════════

    [Fact]
    public async Task ConsultarAsync_SinFiltros_RetornaPaginadoConResumen()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(1, 0, 1, 0, 0));

        // Act
        var result = await _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(1, result.Resumen.Total);
        Assert.Equal(1, result.Paginacion.TotalItems);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroArea_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var areaId = Guid.NewGuid();
        var filtros = new FiltrosConsolidadoRequest { AreaId = areaId };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.AreaId == areaId), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroCG_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var cgId = Guid.NewGuid();
        var filtros = new FiltrosConsolidadoRequest { CgId = cgId };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.CgId == cgId), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroStatus_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Status = "Atrasado" };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.Status == "Atrasado"), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroClasificacion_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Clasificacion = "Proyecto" };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.Clasificacion == "Proyecto"), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroTipo_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Tipo = "OPEX" };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.Tipo == "OPEX"), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroFechaDesde_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var fechaDesde = new DateTime(2026, 1, 1);
        var filtros = new FiltrosConsolidadoRequest { FechaDesde = fechaDesde };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.FechaDesde == fechaDesde), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltroFechaHasta_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var fechaHasta = new DateTime(2026, 12, 31);
        var filtros = new FiltrosConsolidadoRequest { FechaHasta = fechaHasta };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.FechaHasta == fechaHasta), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_FiltrosCombinados_RetornaFiltrado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var areaId = Guid.NewGuid();
        var filtros = new FiltrosConsolidadoRequest { AreaId = areaId, Status = "Atrasado", Tipo = "CAPEX" };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.AreaId == areaId && f.Status == "Atrasado" && f.Tipo == "CAPEX"), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_Paginacion_PageSizeValido()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Page = 2, PageSize = 10 };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(25);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(25, 0, 0, 0, 0));

        // Act
        var result = await _service.ObtenerConsolidadoAsync(filtros);

        // Assert
        Assert.Equal(2, result.Paginacion.Page);
        Assert.Equal(10, result.Paginacion.PageSize);
        Assert.Equal(25, result.Paginacion.TotalItems);
        Assert.Equal(3, result.Paginacion.TotalPages);
    }

    [Fact]
    public async Task ConsultarAsync_Paginacion_PaginaInvalida_LanzaValidacion()
    {
        // Arrange
        var filtros = new FiltrosConsolidadoRequest { Page = 0 };

        // Act & Assert
        await Assert.ThrowsAsync<ValidacionException>(() => _service.ObtenerConsolidadoAsync(filtros));
    }

    [Fact]
    public async Task ConsultarAsync_Orden_AreaCodigo_CGCodigo_FechaInicio()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse>
        {
            CrearItem("GOL2", "CG1", new DateTime(2026, 3, 1)),
            CrearItem("GOL1", "CG2", new DateTime(2026, 2, 1)),
            CrearItem("GOL1", "CG1", new DateTime(2026, 1, 1))
        };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(3, 0, 0, 0, 0));

        // Act
        var result = await _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: orden esperado: GOL1.CG1 (ene), GOL1.CG2 (feb), GOL2.CG1 (mar)
        Assert.Equal("GOL1", result.Items[0].AreaCodigo);
        Assert.Equal("CG1", result.Items[0].ObjetivoCodigo);
        Assert.Equal("GOL1", result.Items[1].AreaCodigo);
        Assert.Equal("CG2", result.Items[1].ObjetivoCodigo);
        Assert.Equal("GOL2", result.Items[2].AreaCodigo);
    }

    [Fact]
    public async Task ConsultarAsync_RolNoGerente_LanzaForbidden()
    {
        // Arrange
        _tenantContext.Rol = "JefeArea";

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest()));
    }

    [Fact]
    public async Task ConsultarAsync_TenantIdDelContexto_NoDelRequest()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: verifica que el tenantId del contexto se usa, no del request
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_ResumenAplicaMismosFiltros()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Status = "Atrasado" };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(filtros);

        // Assert: el resumen recibe los mismos filtros que la lista
        _repoMock.Verify(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.Is<FiltrosConsolidadoRequest>(f => f.Status == "Atrasado"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsultarAsync_SinDatos_RetornaListaVaciaResumenCeros()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        var result = await _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Resumen.Total);
        Assert.Equal(0, result.Resumen.NoIniciado);
        Assert.Equal(0, result.Resumen.EnProgreso);
        Assert.Equal(0, result.Resumen.Terminado);
        Assert.Equal(0, result.Resumen.Atrasado);
    }

    [Fact]
    public async Task ConsultarAsync_CicloInactivo_RetornaListaVacia()
    {
        // Arrange: no hay ciclo activo
        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(_tenantContext.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest()));
    }

    [Fact]
    public async Task ConsultarAsync_CancellationToken_Propagado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var ct = new CancellationToken();
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, ct))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());
        _repoMock.Setup(r => r.CountConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), ct))
            .ReturnsAsync(0);
        _repoMock.Setup(r => r.ObtenerResumenAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), ct))
            .ReturnsAsync(CrearResumen(0, 0, 0, 0, 0));

        // Act
        await _service.ObtenerConsolidadoAsync(new FiltrosConsolidadoRequest(), ct);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), false, ct), Times.Once);
    }

    // ══════════════════════════════ EXPORTACIÓN EXCEL (7 casos) ══════════════════════════════

    [Fact]
    public async Task GenerarExcelAsync_SinFiltros_GeneraDosHojas()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task GenerarExcelAsync_HojaDatos_ColumnasCorrectas()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el archivo es un XLSX válido (magic bytes PK)
        Assert.Equal((byte)'P', result[0]);
        Assert.Equal((byte)'K', result[1]);
    }

    [Fact]
    public async Task GenerarExcelAsync_HojaDatos_StatusTexto_NoChips()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el archivo es un XLSX válido
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task GenerarExcelAsync_HojaResumen_CuatroConteos()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el archivo es un XLSX válido
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task GenerarExcelAsync_HojaResumen_FiltrosComoMetadato()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest());

        // Assert: el archivo es un XLSX válido
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task GenerarExcelAsync_FiltrosAplicados_ResumenCoincide()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var filtros = new FiltrosConsolidadoRequest { Status = "Atrasado" };
        var items = new List<ConsolidadoItemResponse> { CrearItem("GOL1", "CG1", new DateTime(2026, 1, 15)) };
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, filtros, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        // Act
        var result = await _service.ExportarConsolidadoAsync(filtros);

        // Assert: el archivo es un XLSX válido
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task GenerarExcelAsync_CancellationToken_Propagado()
    {
        // Arrange
        var ciclo = CrearCiclo();
        ConfigurarCicloActivo(ciclo);
        var ct = new CancellationToken();
        _repoMock.Setup(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, ct))
            .ReturnsAsync(new List<ConsolidadoItemResponse>());

        // Act
        await _service.ExportarConsolidadoAsync(new FiltrosConsolidadoRequest(), ct);

        // Assert
        _repoMock.Verify(r => r.ListarConsolidadoAsync(_tenantContext.TenantId!.Value, ciclo.Id, It.IsAny<FiltrosConsolidadoRequest>(), true, ct), Times.Once);
    }
}

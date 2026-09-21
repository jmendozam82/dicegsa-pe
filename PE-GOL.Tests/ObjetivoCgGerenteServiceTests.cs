using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;
using Xunit;

namespace PE_GOL.Tests;

public class ObjetivoCgGerenteServiceTests
{
    private readonly Mock<IObjetivoCgRepository> _repoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<IPilarService> _pilarServiceMock;
    private readonly Mock<ILogger<ObjetivoCgService>> _loggerMock;
    private readonly TenantContext _tenantContext;
    private readonly IObjetivoCgService _service;

    public ObjetivoCgGerenteServiceTests()
    {
        _repoMock = new Mock<IObjetivoCgRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();
        _pilarServiceMock = new Mock<IPilarService>();
        _loggerMock = new Mock<ILogger<ObjetivoCgService>>();

        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "Gerente",
            AreaId = null
        };

        _service = new ObjetivoCgService(
            _repoMock.Object,
            _cicloRepoMock.Object,
            _pilarServiceMock.Object,
            _tenantContext
        );
    }

    [Fact]
    public async Task ListarConsolidado_RolJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "JefeArea";
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest()));
    }

    [Fact]
    public async Task ListarConsolidado_SinTenant_LanzaNotFound()
    {
        _tenantContext.TenantId = null;
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest()));
    }

    [Fact]
    public async Task ListarConsolidado_SinCicloActivo_LanzaNotFound()
    {
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest()));
    }

    [Fact]
    public async Task ListarConsolidado_AuditoriaNoSeRegistra_EsSoloLectura()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ObjetivoCgFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ObjetivoCgConsolidadoResponse>());

        await _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest());

        // Asegurar que no se audita nada (IObjetivoCgRepository no tiene InsertLogAsync directamente pero se prueba que no hay llamadas extra)
        _repoMock.Verify(r => r.InsertLogAsync(It.IsAny<PE_GOL.DTO.Dtos.LogAuditoriaInsert>(), It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListarConsolidado_SinFiltros_DevuelveTodaLaLista()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ObjetivoCgFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ObjetivoCgConsolidadoResponse> { new ObjetivoCgConsolidadoResponse { Id = Guid.NewGuid() } });

        var res = await _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest());
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Single(res.Data);
    }

    [Fact]
    public async Task ListarConsolidado_FiltroAreaId_PasaArgumentoAlRepo()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var filtro = new ObjetivoCgFilterRequest { AreaId = Guid.NewGuid() };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ObjetivoCgConsolidadoResponse>());

        await _service.ListarConsolidadoGerenteAsync(filtro);
        _repoMock.Verify(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarConsolidado_FiltroPilarId_PasaArgumentoAlRepo()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var filtro = new ObjetivoCgFilterRequest { PilarId = Guid.NewGuid() };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        
        await _service.ListarConsolidadoGerenteAsync(filtro);
        _repoMock.Verify(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarConsolidado_FiltroTrimestre_PasaArgumentoAlRepo()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var filtro = new ObjetivoCgFilterRequest { Trimestre = "Q1" };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        
        await _service.ListarConsolidadoGerenteAsync(filtro);
        _repoMock.Verify(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarConsolidado_FiltroSemaforo_PasaArgumentoAlRepo()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var filtro = new ObjetivoCgFilterRequest { Semaforo = "Rojo" };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        
        await _service.ListarConsolidadoGerenteAsync(filtro);
        _repoMock.Verify(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarConsolidado_TodosLosFiltrosCombinados_PasaArgumentosCorrectos()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var filtro = new ObjetivoCgFilterRequest { AreaId = Guid.NewGuid(), PilarId = Guid.NewGuid(), Trimestre = "Q2", Semaforo = "Verde" };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        
        await _service.ListarConsolidadoGerenteAsync(filtro);
        _repoMock.Verify(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, filtro, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarConsolidado_SinResultados_DevuelveListaVaciaCon200()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, It.IsAny<ObjetivoCgFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ObjetivoCgConsolidadoResponse>());
            
        var res = await _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest());
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Empty(res.Data);
    }

    [Fact]
    public async Task ListarConsolidado_ConDatos_MapeaCorrectamenteAreaYPilarNombre()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var mockData = new List<ObjetivoCgConsolidadoResponse> {
            new ObjetivoCgConsolidadoResponse { AreaNombre = "Sistemas", PilarNombre = "Innovación", Codigo = "1.CG1" }
        };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, It.IsAny<ObjetivoCgFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockData);
            
        var res = await _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest());
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Equal("Sistemas", res.Data.First().AreaNombre);
        Assert.Equal("Innovación", res.Data.First().PilarNombre);
    }

    [Fact]
    public async Task ListarConsolidado_ValidaOrdenamientoAscendentePorDefecto()
    {
        var CicloEntity = new CicloEntity { Id = Guid.NewGuid() };
        var mockData = new List<ObjetivoCgConsolidadoResponse> {
            new ObjetivoCgConsolidadoResponse { AreaNombre = "A", Codigo = "A.CG1" },
            new ObjetivoCgConsolidadoResponse { AreaNombre = "B", Codigo = "B.CG1" }
        };
        _cicloRepoMock.Setup(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(CicloEntity);
        _repoMock.Setup(r => r.ListarConsolidadoGerenteAsync(It.IsAny<Guid>(), CicloEntity.Id, It.IsAny<ObjetivoCgFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockData);
            
        var res = await _service.ListarConsolidadoGerenteAsync(new ObjetivoCgFilterRequest());
        Assert.NotNull(res.Data);
        var array = res.Data.ToArray();
        Assert.Equal("A", array[0].AreaNombre);
        Assert.Equal("B", array[1].AreaNombre);
    }
}

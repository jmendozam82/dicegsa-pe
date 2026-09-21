using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

public class AccionPlanServiceTests
{
    private readonly Mock<IAccionPlanRepository> _repoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<IObjetivoCgRepository> _objRepoMock;
    private readonly TenantContext _tenantContext;
    private readonly IAccionPlanService _service;

    public AccionPlanServiceTests()
    {
        _repoMock = new Mock<IAccionPlanRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();
        _objRepoMock = new Mock<IObjetivoCgRepository>();

        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "JefeArea",
            AreaId = Guid.NewGuid()
        };

        _service = new AccionPlanService(_repoMock.Object, _cicloRepoMock.Object, _objRepoMock.Object, _tenantContext);
    }

    [Fact]
    public async Task Crear_ConRolJefeDiferenteArea_LanzaAccesoDenegado()
    {
        // Arrange
        var request = new AccionPlanCreateRequest { Peso = 0.5m };
        var objId = Guid.NewGuid();
        var objEntity = new ObjetivoCgResponse { AreaId = Guid.NewGuid() }; // Area distinta al contexto
        
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objId))
            .ReturnsAsync(objEntity);

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.CrearAsync(objId, request));
    }

    [Fact]
    public async Task Crear_FechasFueraDelCiclo_LanzaValidacionException()
    {
        // Arrange
        var objId = Guid.NewGuid();
        var objEntity = new ObjetivoCgResponse { AreaId = _tenantContext.AreaId.Value, CicloId = Guid.NewGuid() };
        
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objId))
            .ReturnsAsync(objEntity);

        _cicloRepoMock.Setup(r => r.ObtenerPorIdAsync(objEntity.CicloId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { AñoFiscal = 2026, MesInicio = 6 }); // Rango: 2026-06-01 a 2027-05-31

        var request = new AccionPlanCreateRequest 
        { 
            FechaInicio = new DateTime(2026, 5, 15), // Fuera de rango
            FechaVencimiento = new DateTime(2026, 7, 1)
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(objId, request));
    }

    [Fact]
    public async Task Crear_SumaPesosExcedeUno_LanzaValidacionException()
    {
        // Arrange
        var objId = Guid.NewGuid();
        var objEntity = new ObjetivoCgResponse { AreaId = _tenantContext.AreaId.Value, CicloId = Guid.NewGuid() };
        
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objId))
            .ReturnsAsync(objEntity);

        _cicloRepoMock.Setup(r => r.ObtenerPorIdAsync(objEntity.CicloId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { AñoFiscal = 2026, MesInicio = 1 }); 

        _repoMock.Setup(r => r.ObtenerSumaPesosAsync(objId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.6m); // Peso actual es 0.6

        var request = new AccionPlanCreateRequest 
        { 
            FechaInicio = new DateTime(2026, 2, 1), 
            FechaVencimiento = new DateTime(2026, 3, 1),
            Peso = 0.5m // 0.6 + 0.5 = 1.1 > 1.0
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidacionException>(() => _service.CrearAsync(objId, request));
    }

    [Fact]
    public async Task Crear_DatosValidos_CalculaCodigoYOrdenYRetornaResponse()
    {
        // Arrange
        var objId = Guid.NewGuid();
        var objEntity = new ObjetivoCgResponse { AreaId = _tenantContext.AreaId.Value, CicloId = Guid.NewGuid(), Codigo = "TI.01" };
        
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objId))
            .ReturnsAsync(objEntity);

        _cicloRepoMock.Setup(r => r.ObtenerPorIdAsync(objEntity.CicloId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { AñoFiscal = 2026, MesInicio = 1 }); 

        _repoMock.Setup(r => r.ObtenerSumaPesosAsync(objId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.6m);

        _repoMock.Setup(r => r.ObtenerMaximoOrdenAsync(objId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2); // Máximo orden actual es 2

        _repoMock.Setup(r => r.InsertAsync(It.IsAny<AccionPlanEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccionPlanEntity entity, CancellationToken ct) => 
            {
                entity.Id = Guid.NewGuid();
                return entity;
            });

        var request = new AccionPlanCreateRequest 
        { 
            FechaInicio = new DateTime(2026, 2, 1), 
            FechaVencimiento = new DateTime(2026, 3, 1),
            Peso = 0.4m // 0.6 + 0.4 = 1.0
        };

        // Act
        var result = await _service.CrearAsync(objId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TI.01.03", result.Codigo); // Orden 3
        Assert.Equal(3, result.Orden);
        Assert.Equal(0, result.Progreso);
        Assert.Equal("NoIniciado", result.Status);
    }

    [Fact]
    public async Task Actualizar_SumaPesosExcedeUno_LanzaValidacionException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var accionEntity = new AccionPlanEntity { Id = id, ObjetivoCgId = Guid.NewGuid(), Peso = 0.3m, AreaId = _tenantContext.AreaId.Value };
        
        _repoMock.Setup(r => r.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accionEntity);
            
        var objEntity = new ObjetivoCgResponse { Id = accionEntity.ObjetivoCgId, CicloId = Guid.NewGuid(), AreaId = _tenantContext.AreaId.Value };
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, accionEntity.ObjetivoCgId))
            .ReturnsAsync(objEntity);

        _cicloRepoMock.Setup(r => r.ObtenerPorIdAsync(objEntity.CicloId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { AñoFiscal = 2026, MesInicio = 1 });

        _repoMock.Setup(r => r.ObtenerSumaPesosAsync(accionEntity.ObjetivoCgId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.8m); // Suma actual 0.8 (incluyendo el 0.3 de esta accion)

        var request = new AccionPlanUpdateRequest 
        { 
            FechaInicio = new DateTime(2026, 2, 1), 
            FechaVencimiento = new DateTime(2026, 3, 1),
            Peso = 0.6m // (0.8 - 0.3) + 0.6 = 1.1 > 1.0
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidacionException>(() => _service.ActualizarAsync(id, request));
    }

    [Fact]
    public async Task Actualizar_DatosValidos_ActualizaEntidad()
    {
        // Arrange
        var id = Guid.NewGuid();
        var accionEntity = new AccionPlanEntity 
        { 
            Id = id, 
            ObjetivoCgId = Guid.NewGuid(), 
            Peso = 0.3m, 
            AreaId = _tenantContext.AreaId.Value,
            Progreso = 50,
            FechaVencimiento = new DateTime(2026, 5, 1) // Fecha anterior
        };
        
        _repoMock.Setup(r => r.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accionEntity);
            
        var objEntity = new ObjetivoCgResponse { Id = accionEntity.ObjetivoCgId, CicloId = Guid.NewGuid(), AreaId = _tenantContext.AreaId.Value };
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, accionEntity.ObjetivoCgId))
            .ReturnsAsync(objEntity);

        _cicloRepoMock.Setup(r => r.ObtenerPorIdAsync(objEntity.CicloId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { AñoFiscal = 2026, MesInicio = 1 });

        _repoMock.Setup(r => r.ObtenerSumaPesosAsync(accionEntity.ObjetivoCgId, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.8m);

        var request = new AccionPlanUpdateRequest 
        { 
            FechaInicio = new DateTime(2026, 2, 1), 
            FechaVencimiento = DateTime.Now.AddDays(10), // Fecha en el futuro
            Peso = 0.5m // (0.8 - 0.3) + 0.5 = 1.0
        };

        // Act
        var result = await _service.ActualizarAsync(id, request);

        // Assert
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<AccionPlanEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(result);
        Assert.Equal("EnProgreso", result.Status); // Porque 50% y la fecha de vencimiento es futura
    }

    [Fact]
    public async Task Eliminar_RolGerente_LanzaAccesoDenegado()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tcGerente = new TenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), Rol = "Gerente" };
        var serviceGerente = new AccionPlanService(_repoMock.Object, _cicloRepoMock.Object, _objRepoMock.Object, tcGerente);

        var accionEntity = new AccionPlanEntity { Id = id, ObjetivoCgId = Guid.NewGuid() };
        _repoMock.Setup(r => r.ObtenerPorIdAsync(id, tcGerente.TenantId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(accionEntity);
        var objEntity = new ObjetivoCgResponse { Id = accionEntity.ObjetivoCgId, AreaId = Guid.NewGuid() };
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(tcGerente.TenantId.Value, accionEntity.ObjetivoCgId)).ReturnsAsync(objEntity);

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => serviceGerente.EliminarAsync(id));
    }

    [Fact]
    public async Task Eliminar_DatosValidos_CascadaSilenciosaEnDB()
    {
        // Arrange
        var id = Guid.NewGuid();
        var accionEntity = new AccionPlanEntity { Id = id, AreaId = _tenantContext.AreaId.Value, ObjetivoCgId = Guid.NewGuid() };
        
        _repoMock.Setup(r => r.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accionEntity);

        var objEntity = new ObjetivoCgResponse { Id = accionEntity.ObjetivoCgId, AreaId = _tenantContext.AreaId.Value };
        _objRepoMock.Setup(r => r.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, accionEntity.ObjetivoCgId)).ReturnsAsync(objEntity);

        // Act
        await _service.EliminarAsync(id);

        // Assert
        _repoMock.Verify(r => r.DeleteAsync(id, _tenantContext.TenantId.Value, It.IsAny<CancellationToken>()), Times.Once);
    }
}

using System.Data;
using System.Reflection;
using Moq;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-024 — CRUD de OKRs (Spec § "Tests requeridos" casos 1-38, 38 métodos).
/// Escritos ANTES de la implementación (TEST-01): el rojo legítimo es el FALLO DE COMPILACIÓN del
/// proyecto de tests porque <c>IOkrRepository</c>, <c>IOkrService</c>, <c>OkrService</c>,
/// <c>OkrCreateRequest</c>, <c>OkrUpdateRequest</c>, <c>OkrResponse</c> y
/// <c>SiguienteSecuenciaOkrDto</c> AÚN NO existen — sigue el mismo patrón que
/// <c>ObjetivoCgServiceTests</c> (HU-017).
///
/// <para>Reglas de cobertura (TEST-04..TEST-06):</para>
/// <list type="bullet">
///   <item>xUnit + Moq (TEST-03).</item>
///   <item>Patrón Arrange / Act / Assert (TEST-05).</item>
///   <item>Nombres <c>[Metodo]_[Escenario]_[ResultadoEsperado]</c> (TEST-04).</item>
/// </list>
///
/// <para><b>Cobertura por caso del spec</b> (38 tests = 7 + 5 + 12 + 6 + 5 + 3):</para>
/// <list type="bullet">
///   <item>Autorización y contexto: #1, #2, #3, #4, #5, #6, #7.</item>
///   <item>Listar / Obtener: #8, #9, #10, #11, #12.</item>
///   <item>Crear: #13, #14, #15, #16, #17, #18, #19, #20, #21, #22, #23, #24.</item>
///   <item>Actualizar: #25, #26, #27, #28, #29, #30.</item>
///   <item>Eliminar: #31, #32, #33, #34, #35.</item>
///   <item>Contrato / SEC: #36, #37, #38.</item>
/// </list>
///
/// <para><b>Contrato que @BackendDev debe implementar</b> (firmas exactas, sin adivinar):</para>
/// <list type="bullet">
///   <item><c>PE-GOL.BLL/Interfaces/IOkrService.cs</c> → namespace <c>PE_GOL.BLL.Interfaces</c>,
///     con <c>Task&lt;IEnumerable&lt;OkrResponse&gt;&gt; ListarAsync(ct)</c>,
///     <c>Task&lt;OkrResponse&gt; ObtenerPorIdAsync(Guid id, ct)</c>,
///     <c>Task&lt;OkrResponse&gt; CrearAsync(OkrCreateRequest req, ct)</c>,
///     <c>Task&lt;OkrResponse&gt; ActualizarAsync(Guid id, OkrUpdateRequest req, ct)</c>,
///     <c>Task EliminarAsync(Guid id, ct)</c>.</item>
///   <item><c>PE-GOL.BLL/Services/OkrService.cs</c> → ctor
///     <c>(IOkrRepository, ICicloRepository, IPilarService, TenantContext)</c> (4 args).</item>
///   <item><c>PE-GOL.DAL/Interfaces/IOkrRepository.cs</c> → namespace <c>PE_GOL.DAL.Interfaces</c>.</item>
///   <item><c>PE-GOL.DTO/Requests/Okr/OkrCreateRequest.cs</c> + <c>OkrUpdateRequest.cs</c> →
///     namespace <c>PE_GOL.DTO.Requests.Okr</c>, SOLO <c>PilarId</c> (Guid) + <c>Descripcion</c> (string).</item>
///   <item><c>PE-GOL.DTO/Responses/Okr/OkrResponse.cs</c> → namespace <c>PE_GOL.DTO.Responses.Okr</c>,
///     sin <c>TenantId</c> (SEC-06).</item>
///   <item><c>PE-GOL.DTO/Dtos/SiguienteSecuenciaOkrDto.cs</c> → namespace <c>PE_GOL.DTO.Dtos</c>,
///     <c>SiguienteN</c> + <c>SiguienteOrden</c>.</item>
/// </list>
///
/// <para><b>SEC-06 + SEC-07:</b> el actor es JefeArea → tenant_id/area_id/ciclo_id se resuelven SIEMPRE
/// desde <c>TenantContext</c> + <c>ObtenerCicloActivoAsync</c>. Ningún parámetro del request lleva
/// <c>tenant_id</c>/<c>area_id</c>/<c>ciclo_id</c>.</para>
/// </summary>
public class OkrServiceTests
{
    private readonly Mock<IOkrRepository> _okrRepoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<IPilarService> _pilarSvcMock;
    private readonly TenantContext _tenantContext;
    private readonly OkrService _sut;

    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId   = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AreaId    = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid PilarId   = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid OtroPilarId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid UserId    = Guid.Parse("00000000-0000-0000-0000-000000000005");

    public OkrServiceTests()
    {
        _okrRepoMock = new Mock<IOkrRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();
        _pilarSvcMock = new Mock<IPilarService>();
        _tenantContext = new TenantContext
        {
            TenantId = TenantId,
            AreaId = AreaId,
            Rol = "JefeArea",
            UserId = UserId
        };
        _sut = new OkrService(
            _okrRepoMock.Object,
            _cicloRepoMock.Object,
            _pilarSvcMock.Object,
            _tenantContext);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Mock del ciclo activo por defecto (estado "En Curso" = modificable).</summary>
    private void SetupCicloActivo(string estado = "En Curso") =>
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity { Id = CicloId, Estado = estado });

    /// <summary>Pilar válido para los tests donde no se valida su existencia.</summary>
    private void SetupPilarValido() =>
        _pilarSvcMock
            .Setup(x => x.ObtenerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PE_GOL.DTO.Responses.PilarResponse { Id = PilarId, Codigo = "PEC-1", Nombre = "Crecimiento" });

    /// <summary>DTO create/update con datos válidos por defecto.</summary>
    private static OkrCreateRequest CrearOkrInsertDtoValido() => new()
    {
        PilarId = PilarId,
        Descripcion = "Incrementar ventas en 15%"
    };

    private static OkrResponse CrearOkrResponseExistente(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CicloId = CicloId,
        AreaId = AreaId,
        PilarId = PilarId,
        PilarNombre = "Crecimiento",
        Codigo = "OKR.1",
        Descripcion = "Incrementar ventas en 15%",
        PuntuacionFinal = 0m,
        Semaforo = "Rojo",
        CreatedAt = DateTime.UtcNow.AddDays(-5),
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Helper para clonar con cambios (siguiente: with no compila con clases).</summary>
    private static OkrResponse Clonar(OkrResponse src, string? codigo = null, string? descripcion = null,
        Guid? pilarId = null, string? pilarNombre = null, decimal? puntuacionFinal = null, string? semaforo = null,
        Guid? id = null)
    {
        var r = new OkrResponse
        {
            Id = id ?? src.Id,
            CicloId = src.CicloId,
            AreaId = src.AreaId,
            PilarId = pilarId ?? src.PilarId,
            PilarNombre = pilarNombre ?? src.PilarNombre,
            Codigo = codigo ?? src.Codigo,
            Descripcion = descripcion ?? src.Descripcion,
            PuntuacionFinal = puntuacionFinal ?? src.PuntuacionFinal,
            Semaforo = semaforo ?? src.Semaforo,
            CreatedAt = src.CreatedAt,
            UpdatedAt = src.UpdatedAt
        };
        return r;
    }

    // ══════════════════════════════ AUTORIZACIÓN Y CONTEXTO (7) ══════════════════════════════

    /// <summary>#1 · Rol no JefeArea en cualquier escritura → AccesoDenegadoException.</summary>
    [Fact]
    public async Task CrearAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "Gerente";
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
        _okrRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _okrRepoMock.Verify(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#2 · AdminTenant no tiene permisos en ActualizarAsync.</summary>
    [Fact]
    public async Task ActualizarAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "AdminTenant";
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            _sut.ActualizarAsync(Guid.NewGuid(), new OkrUpdateRequest()));
    }

    /// <summary>#3 · SuperAdmin no tiene permisos en EliminarAsync.</summary>
    [Fact]
    public async Task EliminarAsync_RolNoJefeArea_LanzaAccesoDenegado()
    {
        _tenantContext.Rol = "SuperAdmin";
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _sut.EliminarAsync(Guid.NewGuid()));
    }

    /// <summary>#4 · TenantId nulo (SuperAdmin sin tenant) en cualquier operación → NotFoundException.</summary>
    [Fact]
    public async Task ListarAsync_SinTenant_LanzaNotFound()
    {
        _tenantContext.TenantId = null;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync());
        _okrRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#5 · AreaId nula en el contexto → NotFoundException.</summary>
    [Fact]
    public async Task ListarAsync_SinArea_LanzaNotFound()
    {
        SetupCicloActivo();
        _tenantContext.AreaId = null;
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListarAsync());
        _okrRepoMock.Verify(x => x.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#6 · Sin ciclo activo (RC-01) → NotFoundException.</summary>
    [Fact]
    public async Task CrearAsync_SinCicloActivo_LanzaNotFound()
    {
        _cicloRepoMock
            .Setup(x => x.ObtenerCicloActivoAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
    }

    /// <summary>#7 · Ciclo Cerrado (RC-12) en escritura → ValidacionException.</summary>
    [Fact]
    public async Task CrearAsync_CicloCerrado_LanzaValidacionException()
    {
        SetupCicloActivo("Cerrado");
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
    }

    // ══════════════════════════════ LISTAR / OBTENER (5) ══════════════════════════════

    /// <summary>#8 · Listar retorna 3 OKRs con sus campos CA #4 (PuntuacionFinal, Semaforo, PilarNombre).</summary>
    [Fact]
    public async Task ListarAsync_Exito_RetornaOkrsConPuntuacionFinalYSemaforo()
    {
        SetupCicloActivo();
        var okrs = new List<OkrResponse>
        {
            Clonar(CrearOkrResponseExistente(), codigo: "OKR.1", puntuacionFinal: 0.250m, semaforo: "Rojo",    pilarNombre: "Crecimiento"),
            Clonar(CrearOkrResponseExistente(), codigo: "OKR.2", puntuacionFinal: 0.500m, semaforo: "Amarillo", pilarNombre: "Eficiencia"),
            Clonar(CrearOkrResponseExistente(), codigo: "OKR.3", puntuacionFinal: 0.900m, semaforo: "Verde",    pilarNombre: "Calidad")
        };
        _okrRepoMock
            .Setup(x => x.ListarAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(okrs);

        var result = await _sut.ListarAsync();

        Assert.Equal(3, result.Count());
        Assert.Contains(result, o => o.Codigo == "OKR.1" && o.PuntuacionFinal == 0.250m && o.Semaforo == "Rojo" && o.PilarNombre == "Crecimiento");
        Assert.Contains(result, o => o.Codigo == "OKR.2" && o.PuntuacionFinal == 0.500m && o.Semaforo == "Amarillo" && o.PilarNombre == "Eficiencia");
        Assert.Contains(result, o => o.Codigo == "OKR.3" && o.PuntuacionFinal == 0.900m && o.Semaforo == "Verde" && o.PilarNombre == "Calidad");
    }

    /// <summary>#9 · Sin OKRs del área → lista vacía (la UI muestra .empty-state).</summary>
    [Fact]
    public async Task ListarAsync_SinOkrs_RetornaListaVacia()
    {
        SetupCicloActivo();
        _okrRepoMock
            .Setup(x => x.ListarAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OkrResponse>());

        var result = await _sut.ListarAsync();
        Assert.Empty(result);
    }

    /// <summary>#10 · La DAL recibe (tenantId, cicloId, areaId) del contexto — SEC-06/07.</summary>
    [Fact]
    public async Task ListarAsync_DalRecibeTenantCicloYAreaDelContexto()
    {
        SetupCicloActivo();

        await _sut.ListarAsync();

        _okrRepoMock.Verify(x => x.ListarAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#11 · ObtenerPorIdAsync → DTO completo con PilarNombre (INNER JOIN).</summary>
    [Fact]
    public async Task ObtenerPorIdAsync_Exito_RetornaDetalleConPilarNombre()
    {
        var id = Guid.NewGuid();
        var okr = Clonar(CrearOkrResponseExistente(id), codigo: "OKR.2", descripcion: "Detalle OKR", pilarNombre: "Crecimiento");
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(okr);

        var result = await _sut.ObtenerPorIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal(id, result!.Id);
        Assert.Equal("OKR.2", result.Codigo);
        Assert.Equal("Detalle OKR", result.Descripcion);
        Assert.Equal("Crecimiento", result.PilarNombre);
    }

    /// <summary>#12 · OKR inexistente o de otra área → NotFoundException (sin fuga, RN-008).</summary>
    [Fact]
    public async Task ObtenerPorIdAsync_NoExisteODeOtraArea_LanzaNotFound()
    {
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ObtenerPorIdAsync(Guid.NewGuid()));
    }

    // ══════════════════════════════ CREAR (12) ══════════════════════════════

    /// <summary>#13 · Área sin OKRs → codigo = "OKR.1", orden = 1 (CA #1).</summary>
    [Fact]
    public async Task CrearAsync_Exito_GeneraCodigoOKR1_CuandoAreaNoTieneOkrs()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.1", 1, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Clonar(CrearOkrResponseExistente(nuevoId), codigo: "OKR.1", puntuacionFinal: 0m, semaforo: "Rojo"));

        var result = await _sut.CrearAsync(CrearOkrInsertDtoValido());

        Assert.Equal("OKR.1", result.Codigo);
        Assert.Equal(0m, result.PuntuacionFinal);
        Assert.Equal("Rojo", result.Semaforo);
        _okrRepoMock.Verify(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.1", 1, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#14 · MAX(existente es 2) → codigo = "OKR.3" (F4, MAX+1).</summary>
    [Fact]
    public async Task CrearAsync_Exito_GeneraCodigoOKR3_CuandoMaxNExistenteEs2()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 3, SiguienteOrden = 3 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.3", 3, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Clonar(CrearOkrResponseExistente(nuevoId), codigo: "OKR.3"));

        var result = await _sut.CrearAsync(CrearOkrInsertDtoValido());

        Assert.Equal("OKR.3", result.Codigo);
        _okrRepoMock.Verify(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.3", 3, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#15 · F4 regresión crítica: existen OKR.1 y OKR.3 (se eliminó OKR.2) → siguiente = OKR.4.</summary>
    [Fact]
    public async Task CrearAsync_Exito_NoReutilizaCodigoTrasEliminarIntermedio()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2); // COUNT=2 (OKR.1 + OKR.3)
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 4, SiguienteOrden = 4 }); // MAX(3)+1

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.4", 4, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Clonar(CrearOkrResponseExistente(nuevoId), codigo: "OKR.4"));

        var result = await _sut.CrearAsync(CrearOkrInsertDtoValido());

        // NUNCA debe ser "OKR.3" (eso implicaría COUNT+1 → bucle infinito tras eliminar intermedios)
        Assert.Equal("OKR.4", result.Codigo);
        _okrRepoMock.Verify(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.4", 4, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#16 · F3: el INSERT lleva puntuacion_final=0 y semaforo='Rojo' (cubierto en repo test #44).</summary>
    [Fact]
    public async Task CrearAsync_Exito_PersistePuntuacionFinalCeroYSemaforoRojo()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Clonar(CrearOkrResponseExistente(nuevoId), puntuacionFinal: 0m, semaforo: "Rojo"));

        var result = await _sut.CrearAsync(CrearOkrInsertDtoValido());

        // El DTO de salida refleja los defaults F3 (la verificación del SQL se hace en repo test #44)
        Assert.Equal(0m, result.PuntuacionFinal);
        Assert.Equal("Rojo", result.Semaforo);
    }

    /// <summary>#17 · Descripción vacía o solo espacios → ValidacionException (fuente de verdad BLL).</summary>
    [Fact]
    public async Task CrearAsync_DescripcionVacia_LanzaValidacionException()
    {
        SetupCicloActivo();
        SetupPilarValido();
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.CrearAsync(new OkrCreateRequest { PilarId = PilarId, Descripcion = "   " }));
    }

    /// <summary>#18 · Descripción > 500 chars → ValidacionException (límite de negocio BLL).</summary>
    [Fact]
    public async Task CrearAsync_DescripcionMayorA500_LanzaValidacionException()
    {
        SetupCicloActivo();
        SetupPilarValido();
        var descripcionLarga = new string('x', 501);
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.CrearAsync(new OkrCreateRequest { PilarId = PilarId, Descripcion = descripcionLarga }));
    }

    /// <summary>#19 · Pilar inexistente en el ciclo → NotFoundException con mensaje específico.</summary>
    [Fact]
    public async Task CrearAsync_PilarNoExisteEnCiclo_LanzaNotFound()
    {
        SetupCicloActivo();
        _pilarSvcMock
            .Setup(x => x.ObtenerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("El pilar estratégico especificado no existe en este ciclo."));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
        Assert.Equal("El pilar estratégico especificado no existe en este ciclo.", ex.Message);
    }

    /// <summary>#20 · CA #5: ya hay 9 OKRs en el área → ValidacionException.</summary>
    [Fact]
    public async Task CrearAsync_Maximo9OkrsPorArea_LanzaValidacionException()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
        Assert.Contains("9 OKRs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>#21 · F4 capa 2: PostgresException 23505 (UNIQUE (area_id, codigo)) → ValidacionException 422.</summary>
    [Fact]
    public async Task CrearAsync_Choque23505_LanzaValidacionException()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PostgresException("duplicate key", "ERROR", "ERROR", "23505"));

        await Assert.ThrowsAsync<ValidacionException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
        txMock.Verify(x => x.Rollback(), Times.Once);
    }

    /// <summary>#22 · Auditoría CREATE: Entidad="Okr", Accion="CREATE", ValorAnterior=null, ValorNuevo=JSON (ADR-003).</summary>
    [Fact]
    public async Task CrearAsync_Exito_AuditaCreateConEntidadOkr()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(nuevoId));

        await _sut.CrearAsync(CrearOkrInsertDtoValido());

        _okrRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l =>
                l.Accion == "CREATE"
                && l.Entidad == "Okr"
                && l.EntidadId == nuevoId.ToString()
                && l.ValorAnterior == null
                && l.ValorNuevo != null
                && l.ValorNuevo.Contains("OKR.1")),
            txMock.Object,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#23 · La auditoría comparte transacción con la escritura (atomicidad CREATE+LOG).</summary>
    [Fact]
    public async Task CrearAsync_Exito_AuditoriaEnLaMismaTransaccion()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(nuevoId));

        await _sut.CrearAsync(CrearOkrInsertDtoValido());

        // Captura la tx que recibió InsertLogAsync y comprueba que es la misma
        IDbTransaction? capturedTx = null;
        foreach (var inv in _okrRepoMock.Invocations.Where(i => i.Method.Name == nameof(IOkrRepository.InsertLogAsync)))
        {
            capturedTx = inv.Arguments[1] as IDbTransaction;
        }
        Assert.NotNull(capturedTx);
        Assert.Same(txMock.Object, capturedTx);

        txMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>#24 · Error genérico en INSERT → rollback + NO auditoría + propagación.</summary>
    [Fact]
    public async Task CrearAsync_ErrorGenerico_HaceRollbackYSinAuditoria()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Error de BD"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CrearAsync(CrearOkrInsertDtoValido()));
        txMock.Verify(x => x.Rollback(), Times.Once);
        _okrRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════ ACTUALIZAR (6) ══════════════════════════════

    /// <summary>#25 · Actualización OK: descripción y pilar cambian; auditoría UPDATE con anterior+nuevo.</summary>
    [Fact]
    public async Task ActualizarAsync_Exito_ActualizaDescripcionYPilar_AuditaUpdate()
    {
        SetupCicloActivo();
        SetupPilarValido();

        var id = Guid.NewGuid();
        var actual = Clonar(CrearOkrResponseExistente(id), descripcion: "Desc ANTIGUA");
        var actualizado = Clonar(CrearOkrResponseExistente(id), descripcion: "Desc NUEVA", pilarId: OtroPilarId);
        _okrRepoMock
            .SetupSequence(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actual)
            .ReturnsAsync(actualizado);

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);

        var request = new OkrUpdateRequest { PilarId = OtroPilarId, Descripcion = "Desc NUEVA" };

        var result = await _sut.ActualizarAsync(id, request);

        Assert.Equal("Desc NUEVA", result.Descripcion);
        _okrRepoMock.Verify(x => x.ActualizarAsync(TenantId, AreaId, id, request, txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        _okrRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "UPDATE" && l.Entidad == "Okr" && l.ValorAnterior != null && l.ValorNuevo != null),
            txMock.Object,
            It.IsAny<CancellationToken>()), Times.Once);
        txMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>#26 · Descripción vacía o nula → ValidacionException.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ActualizarAsync_DescripcionInvalida_LanzaValidacionException(string descripcion)
    {
        SetupCicloActivo();
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarAsync(Guid.NewGuid(), new OkrUpdateRequest { PilarId = PilarId, Descripcion = descripcion }));
    }

    /// <summary>#27 · OKR inexistente o de otra área → NotFoundException.</summary>
    [Fact]
    public async Task ActualizarAsync_NoExisteODeOtraArea_LanzaNotFound()
    {
        SetupCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ActualizarAsync(Guid.NewGuid(), new OkrUpdateRequest { PilarId = PilarId, Descripcion = "x" }));
    }

    /// <summary>#28 · Cambio a un pilar inexistente en el ciclo → NotFoundException.</summary>
    [Fact]
    public async Task ActualizarAsync_NuevoPilarNoExiste_LanzaNotFound()
    {
        SetupCicloActivo();
        var id = Guid.NewGuid();
        // PilarId del actual != request.PilarId → se valida el nuevo
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(id));
        _pilarSvcMock
            .Setup(x => x.ObtenerAsync(CicloId, OtroPilarId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("El pilar estratégico especificado no existe en este ciclo."));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ActualizarAsync(id, new OkrUpdateRequest { PilarId = OtroPilarId, Descripcion = "nueva desc" }));
    }

    /// <summary>#29 · Ciclo Cerrado en Actualizar → ValidacionException (RC-12).</summary>
    [Fact]
    public async Task ActualizarAsync_CicloCerrado_LanzaValidacionException()
    {
        SetupCicloActivo("Cerrado");
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _sut.ActualizarAsync(Guid.NewGuid(), new OkrUpdateRequest { PilarId = PilarId, Descripcion = "x" }));
    }

    /// <summary>#30 · Optimización: si el PilarId NO cambia, no se re-valida el pilar.</summary>
    [Fact]
    public async Task ActualizarAsync_PilarSinCambio_NoValidaPilar()
    {
        SetupCicloActivo();
        var id = Guid.NewGuid();
        // actual.PilarId == request.PilarId == PilarId
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(id));

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);

        var request = new OkrUpdateRequest { PilarId = PilarId, Descripcion = "nueva" };
        await _sut.ActualizarAsync(id, request);

        // PilarService NO se invoca cuando el pilar no cambia
        _pilarSvcMock.Verify(x => x.ObtenerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════ ELIMINAR (5) ══════════════════════════════

    /// <summary>#31 · F2: sin valores reales → DELETE físico + auditoría DELETE.</summary>
    [Fact]
    public async Task EliminarAsync_Exito_RealizaDeleteFisico_AuditaDelete()
    {
        SetupCicloActivo();
        var id = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(id));
        _okrRepoMock
            .Setup(x => x.VerificarKrsConValoresAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);

        await _sut.EliminarAsync(id);

        _okrRepoMock.Verify(x => x.EliminarAsync(TenantId, AreaId, id, txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        _okrRepoMock.Verify(x => x.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "DELETE" && l.Entidad == "Okr" && l.ValorNuevo == null),
            txMock.Object,
            It.IsAny<CancellationToken>()), Times.Once);
        txMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>#32 · OKR inexistente o de otra área → NotFoundException.</summary>
    [Fact]
    public async Task EliminarAsync_NoExisteODeOtraArea_LanzaNotFound()
    {
        SetupCicloActivo();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OkrResponse?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.EliminarAsync(Guid.NewGuid()));
    }

    /// <summary>#33 · F2: con KRs que tienen valores reales → ValidacionException + DELETE nunca se ejecuta.</summary>
    [Fact]
    public async Task EliminarAsync_ConKrsConValoresReales_LanzaValidacionException()
    {
        SetupCicloActivo();
        var id = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(id));
        _okrRepoMock
            .Setup(x => x.VerificarKrsConValoresAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _sut.EliminarAsync(id));
        Assert.Contains("valores reales", ex.Message, StringComparison.OrdinalIgnoreCase);

        // El DELETE nunca se ejecuta y nunca se inicia transacción
        _okrRepoMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _okrRepoMock.Verify(x => x.EliminarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
        _okrRepoMock.Verify(x => x.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>#34 · F2: con KRs pero sin valores reales → DELETE físico (KRs caen por ON DELETE CASCADE).</summary>
    [Fact]
    public async Task EliminarAsync_ConKrsSinValores_RealizaDelete()
    {
        SetupCicloActivo();
        var id = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(id));
        _okrRepoMock
            .Setup(x => x.VerificarKrsConValoresAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);

        await _sut.EliminarAsync(id);

        // DELETE ejecutado; los KRs se eliminan por ON DELETE CASCADE del DDL L253 (F2)
        _okrRepoMock.Verify(x => x.EliminarAsync(TenantId, AreaId, id, txMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        txMock.Verify(x => x.Commit(), Times.Once);
    }

    /// <summary>#35 · Ciclo Cerrado en Eliminar → ValidacionException (RC-12).</summary>
    [Fact]
    public async Task EliminarAsync_CicloCerrado_LanzaValidacionException()
    {
        SetupCicloActivo("Cerrado");
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.EliminarAsync(Guid.NewGuid()));
    }

    // ══════════════════════════════ CONTRATO / SEC (3) ══════════════════════════════

    /// <summary>#36 · SEC-06: ningún DTO expone TenantId.</summary>
    [Fact]
    public void Contrato_DtosSinTenantId_Sec06()
    {
        // Los DTOs son los 3 que viajan en request/response de los 5 endpoints de HU-024
        var tipos = new[]
        {
            typeof(OkrCreateRequest),
            typeof(OkrUpdateRequest),
            typeof(OkrResponse)
        };

        Assert.All(tipos, tipo =>
        {
            var prop = tipo.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
            Assert.Null(prop);
        });
    }

    /// <summary>#37 · SEC-07: la DAL SIEMPRE recibe areaId del contexto, nunca del request.</summary>
    [Fact]
    public async Task CrearAsync_Sec07_DalRecibeAreaIdDelContexto()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(nuevoId));

        await _sut.CrearAsync(CrearOkrInsertDtoValido());

        // Toda invocación DAL lleva AreaId del contexto (no del request, que no la expone)
        _okrRepoMock.Verify(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()), Times.Once);
        _okrRepoMock.Verify(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()), Times.Once);
        _okrRepoMock.Verify(x => x.CrearAsync(TenantId, CicloId, AreaId, "OKR.1", 1, It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>#38 · El servicio retorna el DTO plano (el wrapper ApiResponse&lt;T&gt; es del controller, ARCH-07).</summary>
    [Fact]
    public async Task CrearAsync_Exito_RetornaDtoPlanoSinApiResponse()
    {
        SetupCicloActivo();
        SetupPilarValido();
        _okrRepoMock
            .Setup(x => x.ContarOkrsPorAreaAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _okrRepoMock
            .Setup(x => x.ObtenerSiguienteSecuenciaOkrAsync(TenantId, CicloId, AreaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiguienteSecuenciaOkrDto { SiguienteN = 1, SiguienteOrden = 1 });

        var txMock = new Mock<IDbTransaction>();
        _okrRepoMock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        var nuevoId = Guid.NewGuid();
        _okrRepoMock
            .Setup(x => x.CrearAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<OkrCreateRequest>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nuevoId);
        _okrRepoMock
            .Setup(x => x.ObtenerPorIdAsync(TenantId, AreaId, nuevoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrearOkrResponseExistente(nuevoId));

        var result = await _sut.CrearAsync(CrearOkrInsertDtoValido());

        // El retorno es OkrResponse directo, no ApiResponse<OkrResponse> (lo arma el controller)
        Assert.IsType<OkrResponse>(result);
        Assert.Equal("OKR.1", result.Codigo);
        Assert.Equal(0m, result.PuntuacionFinal);
        Assert.Equal("Rojo", result.Semaforo);
    }
}
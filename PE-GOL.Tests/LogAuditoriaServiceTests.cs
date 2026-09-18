using System.Reflection;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para LogAuditoriaService — Spec HU-005 § "Tests requeridos" (19 casos, tabla #1..#19).
/// TDD fase red (TEST-01): el stub LogAuditoriaService lanza NotImplementedException en TODOS los
/// métodos → los 17 tests de comportamiento fallan deliberadamente EN RUNTIME hasta que @BackendDev
/// implemente la lógica en fase 4 (spec § Lógica BLL pasos 1-2 + § Queries DAL L1-L3 + decisiones D1..D12).
/// Los tests #18/#19 (inmutabilidad CA #3) verifican el CONTRATO por reflexión sobre las interfaces
/// ILogAuditoriaService/ILogAuditoriaRepository (ya creadas por @QA como contrato — deben pasar
/// incluso en fase roja; si @BackendDev añade un método de escritura, fallan).
/// Moq sobre ILogAuditoriaRepository + TenantContext real (D17: TenantId nullable — el SA no tiene
/// tenant; D12: re-validación de rol en BLL). Patrón Arrange/Act/Assert + nombre
/// [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05). Nombres EXACTOS de la tabla del spec.
/// Ctor de LogAuditoriaService: (ILogAuditoriaRepository, TenantContext) + overload con ILogger (D11).
/// </summary>
public class LogAuditoriaServiceTests
{
    private readonly Mock<ILogAuditoriaRepository> _mockRepo;
    private readonly TenantContext _tenantContext;
    private readonly ILogAuditoriaService _service;

    public LogAuditoriaServiceTests()
    {
        _mockRepo = new Mock<ILogAuditoriaRepository>();
        _tenantContext = new TenantContext
        {
            TenantId = null,          // D17: SuperAdmin sin tenant (tabla global fuera de RLS)
            UserId = Guid.NewGuid(),
            Rol = "SuperAdmin"
        };

        _service = new LogAuditoriaService(_mockRepo.Object, _tenantContext);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static LogAuditoriaEntity CrearEntidad(
        Guid? id = null, Guid? tenantId = null, string? tenantNombre = null,
        Guid? usuarioId = null, string? usuarioNombre = null,
        string accion = "CREATE", string entidad = "Tenant", string? entidadId = null,
        string? valorAnterior = null, string? valorNuevo = null,
        DateTimeOffset? createdAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId,
            TenantNombre = tenantNombre,
            UsuarioId = usuarioId,
            UsuarioNombre = usuarioNombre,
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            ValorAnterior = valorAnterior,
            ValorNuevo = valorNuevo,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1)
        };

    private static LogAuditoriaFiltrosRequest CrearFiltros(
        int? page = null, int? pageSize = null, Guid? tenantId = null, Guid? usuarioId = null,
        string? accion = null, DateTimeOffset? desde = null, DateTimeOffset? hasta = null)
        => new()
        {
            Page = page,
            PageSize = pageSize,
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Accion = accion,
            Desde = desde,
            Hasta = hasta
        };

    /// <summary>Configura el flujo feliz del listado: CountAsync + GetPagedAsync responden.</summary>
    private void ConfigurarListado(int total, IEnumerable<LogAuditoriaEntity> items)
    {
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(total);
        _mockRepo
            .Setup(r => r.GetPagedAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
    }

    // ─── Caso 1-14. ListarAsync (filtros + paginación) ──────────────────────

    // Caso 1 ─ sin filtros → page/pageSize saneados (defaults 1/10), total y totalPages
    // calculados, items mapeados a LogAuditoriaResponse SIN JSONB (D3)
    [Fact]
    public async Task Listar_SinFiltros_RetornaPaginado()
    {
        // Arrange: 25 entradas → page 1 / pageSize 10 → totalPages = ceil(25/10) = 3
        var e1 = CrearEntidad(accion: "CREATE", entidad: "Tenant");
        var e2 = CrearEntidad(accion: "LOGIN", entidad: "Auth");
        ConfigurarListado(25, new[] { e1, e2 });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros());

        // Assert: saneo de defaults (1/10), total y totalPages calculados, mapeo sin JSONB
        Assert.Equal(1, resultado.Page);
        Assert.Equal(10, resultado.PageSize);
        Assert.Equal(25, resultado.Total);
        Assert.Equal(3, resultado.TotalPages);
        Assert.Equal(2, resultado.Items.Count);
        Assert.Equal(e1.Id, resultado.Items[0].Id);
        Assert.Equal("CREATE", resultado.Items[0].Accion);
        Assert.Equal("Tenant", resultado.Items[0].Entidad);
        Assert.Equal(e2.Id, resultado.Items[1].Id);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d =>
                d.Page == 1 && d.PageSize == 10 &&
                d.TenantId == null && d.UsuarioId == null && d.Accion == null &&
                d.Desde == null && d.Hasta == null),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Page == 1 && d.PageSize == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 2 ─ filtro tenantId → propagado al DAL (DAL-L1/L2), solo entradas de ese tenant
    [Fact]
    public async Task Listar_FiltroPorTenant_RetornaSoloDelTenant()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entidad = CrearEntidad(tenantId: tenantId, tenantNombre: "Acme SA");
        ConfigurarListado(1, new[] { entidad });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(tenantId: tenantId));

        // Assert: tenantId propagado al COUNT y al SELECT (DAL-L1/L2)
        Assert.Single(resultado.Items);
        Assert.Equal(tenantId, resultado.Items[0].TenantId);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.TenantId == tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.TenantId == tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 3 ─ filtro usuarioId → propagado al DAL
    [Fact]
    public async Task Listar_FiltroPorUsuario_RetornaSoloDelUsuario()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var entidad = CrearEntidad(usuarioId: usuarioId, usuarioNombre: "Ana Pérez");
        ConfigurarListado(1, new[] { entidad });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(usuarioId: usuarioId));

        // Assert: usuarioId propagado al COUNT y al SELECT
        Assert.Single(resultado.Items);
        Assert.Equal(usuarioId, resultado.Items[0].UsuarioId);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.UsuarioId == usuarioId),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.UsuarioId == usuarioId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 4 ─ filtro accion="LOGIN" → propagado y casteado al enum (DAL-L1: @Accion::accion_auditoria)
    [Fact]
    public async Task Listar_FiltroPorAccion_RetornaSoloEsaAccion()
    {
        // Arrange
        var entidad = CrearEntidad(accion: "LOGIN", entidad: "Auth");
        ConfigurarListado(1, new[] { entidad });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(accion: "LOGIN"));

        // Assert: accion propagada al COUNT y al SELECT
        Assert.Single(resultado.Items);
        Assert.Equal("LOGIN", resultado.Items[0].Accion);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Accion == "LOGIN"),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Accion == "LOGIN"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 5 ─ filtro rango de fechas → desde/hasta propagados (inclusive en ambos extremos)
    [Fact]
    public async Task Listar_FiltroPorRangoFechas_RetornaSoloDelRango()
    {
        // Arrange
        var desde = DateTimeOffset.UtcNow.AddDays(-30);
        var hasta = DateTimeOffset.UtcNow.AddDays(-1);
        var entidad = CrearEntidad(createdAt: desde.AddHours(1));
        ConfigurarListado(1, new[] { entidad });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(desde: desde, hasta: hasta));

        // Assert: desde/hasta propagados (created_at >= @Desde AND created_at <= @Hasta)
        Assert.Single(resultado.Items);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Desde == desde && d.Hasta == hasta),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Desde == desde && d.Hasta == hasta),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 6 ─ filtros combinados (tenant + usuario + accion + rango) → intersección propagada
    [Fact]
    public async Task Listar_FiltrosCombinados_RetornaInterseccion()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var desde = DateTimeOffset.UtcNow.AddDays(-30);
        var hasta = DateTimeOffset.UtcNow.AddDays(-1);
        var entidad = CrearEntidad(tenantId: tenantId, usuarioId: usuarioId, accion: "UPDATE");
        ConfigurarListado(1, new[] { entidad });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(
            tenantId: tenantId, usuarioId: usuarioId, accion: "UPDATE", desde: desde, hasta: hasta));

        // Assert: TODOS los filtros propagados al COUNT y al SELECT (intersección AND)
        Assert.Single(resultado.Items);
        _mockRepo.Verify(r => r.CountAsync(
            It.Is<LogAuditoriaFiltrosDto>(d =>
                d.TenantId == tenantId && d.UsuarioId == usuarioId &&
                d.Accion == "UPDATE" && d.Desde == desde && d.Hasta == hasta),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d =>
                d.TenantId == tenantId && d.UsuarioId == usuarioId &&
                d.Accion == "UPDATE" && d.Desde == desde && d.Hasta == hasta),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 7 ─ pageSize=500 → truncado a 100 (patrón HU-001)
    [Fact]
    public async Task Listar_PageSizeMayor100_TruncaA100()
    {
        // Arrange: 150 entradas → pageSize 100 → totalPages = ceil(150/100) = 2
        ConfigurarListado(150, Array.Empty<LogAuditoriaEntity>());

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(pageSize: 500));

        // Assert: pageSize truncado a 100 y propagado al DAL
        Assert.Equal(100, resultado.PageSize);
        Assert.Equal(2, resultado.TotalPages);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.PageSize == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 8 ─ page=0 → saneado a 1
    [Fact]
    public async Task Listar_PageMenor1_UsaPagina1()
    {
        // Arrange
        ConfigurarListado(5, Array.Empty<LogAuditoriaEntity>());

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros(page: 0));

        // Assert: page saneado a 1 y propagado al DAL
        Assert.Equal(1, resultado.Page);
        _mockRepo.Verify(r => r.GetPagedAsync(
            It.Is<LogAuditoriaFiltrosDto>(d => d.Page == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Caso 9 ─ CountAsync=0 → PagedResult vacío; GetPagedAsync NUNCA se invoca (patrón HU-001 paso 2)
    [Fact]
    public async Task Listar_TotalCero_RetornaPaginaVacia()
    {
        // Arrange: total = 0 (sin entradas que cumplan los filtros)
        _mockRepo
            .Setup(r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros());

        // Assert: página vacía sin ejecutar el SELECT de datos
        Assert.Empty(resultado.Items);
        Assert.Equal(0, resultado.Total);
        Assert.Equal(0, resultado.TotalPages);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 10 ─ desde > hasta → ValidacionException 422; el DAL nunca se consulta
    [Fact]
    public async Task Listar_DesdeMayorQueHasta_LanzaValidacion()
    {
        // Arrange: desde (ayer) > hasta (hace 30 días) → rango invertido
        var desde = DateTimeOffset.UtcNow.AddDays(-1);
        var hasta = DateTimeOffset.UtcNow.AddDays(-30);

        // Act & Assert: 422 y el DAL nunca se consulta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.ListarAsync(CrearFiltros(desde: desde, hasta: hasta)));

        Assert.Contains("fecha", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 11 ─ hasta en el futuro → 422 (el log no puede tener entradas futuras)
    [Fact]
    public async Task Listar_HastaEnFuturo_LanzaValidacion()
    {
        // Arrange: hasta = NOW + 1h (futuro)
        var hasta = DateTimeOffset.UtcNow.AddHours(1);

        // Act & Assert: 422 y el DAL nunca se consulta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.ListarAsync(CrearFiltros(hasta: hasta)));

        Assert.Contains("futuro", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 12 ─ accion="BORRAR" (fuera del enum accion_auditoria) → 422
    [Fact]
    public async Task Listar_AccionInvalida_LanzaValidacion()
    {
        // Arrange: "BORRAR" no pertenece al enum CREATE|UPDATE|DELETE|LOGIN|LOGOUT|ACTIVATE|DEACTIVATE
        // Act & Assert: 422 y el DAL nunca se consulta
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _service.ListarAsync(CrearFiltros(accion: "BORRAR")));

        Assert.Contains("BORRAR", ex.Message, StringComparison.OrdinalIgnoreCase);
        _mockRepo.Verify(
            r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 13 ─ rol AdminTenant → AccesoDenegadoException 403 (D12); el DAL nunca se consulta
    [Fact]
    public async Task Listar_RolNoSuperAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D12 — la BLL re-valida el rol (el [Authorize] del controller es la 1ª capa)
        _tenantContext.Rol = "AdminTenant";

        // Act & Assert: 403 y el DAL nunca se consulta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            _service.ListarAsync(CrearFiltros()));

        _mockRepo.Verify(
            r => r.CountAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepo.Verify(
            r => r.GetPagedAsync(It.IsAny<LogAuditoriaFiltrosDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Caso 14 ─ LEFT JOIN mapeado: tenantNombre/usuarioNombre poblados; null cuando los ids son null
    // (acciones SaaS-level del SA / LOGIN fallido con correo inexistente)
    [Fact]
    public async Task Listar_RetornaNombresDeTenantYUsuario()
    {
        // Arrange: una entrada con tenant+usuario (nombres del LEFT JOIN) y otra SaaS-level (ids null)
        var tenantId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var conNombres = CrearEntidad(
            tenantId: tenantId, tenantNombre: "Acme SA",
            usuarioId: usuarioId, usuarioNombre: "Ana Pérez",
            accion: "UPDATE", entidad: "Ciclo", entidadId: "c1");
        var sinNombres = CrearEntidad(
            tenantId: null, tenantNombre: null, usuarioId: null, usuarioNombre: null,
            accion: "LOGIN", entidad: "Auth");
        ConfigurarListado(2, new[] { conNombres, sinNombres });

        // Act
        var resultado = await _service.ListarAsync(CrearFiltros());

        // Assert: nombres del LEFT JOIN poblados; null cuando los ids son null (D6)
        Assert.Equal(2, resultado.Items.Count);
        Assert.Equal("Acme SA", resultado.Items[0].TenantNombre);
        Assert.Equal("Ana Pérez", resultado.Items[0].UsuarioNombre);
        Assert.Equal("c1", resultado.Items[0].EntidadId);
        Assert.Null(resultado.Items[1].TenantNombre);
        Assert.Null(resultado.Items[1].UsuarioNombre);
        Assert.Null(resultado.Items[1].TenantId);
        Assert.Null(resultado.Items[1].UsuarioId);
    }

    // ─── Caso 15-17. ObtenerPorIdAsync ──────────────────────────────────────

    // Caso 15 ─ GetByIdAsync null → NotFoundException 404
    [Fact]
    public async Task ObtenerPorId_RegistroInexistente_LanzaNoEncontrado()
    {
        // Arrange: DAL-L3 devuelve null (entrada inexistente)
        var id = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogAuditoriaEntity?)null);

        // Act & Assert: 404
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerPorIdAsync(id));
    }

    // Caso 16 ─ registro existente → detalle con valorAnterior/valorNuevo como string
    // (JSON crudo legible, ADR-003)
    [Fact]
    public async Task ObtenerPorId_RegistroExistente_RetornaDetalleConJson()
    {
        // Arrange: entrada con JSONB (snapshot previo/nuevo serializado con UnsafeRelaxedJsonEscaping)
        var id = Guid.NewGuid();
        var entidad = CrearEntidad(
            id: id, tenantId: Guid.NewGuid(), tenantNombre: "Acme SA",
            usuarioId: Guid.NewGuid(), usuarioNombre: "Ana Pérez",
            accion: "UPDATE", entidad: "Ciclo", entidadId: "c1",
            valorAnterior: "{\"estado\":\"Borrador\"}",
            valorNuevo: "{\"estado\":\"Activo\"}");
        _mockRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entidad);

        // Act
        var resultado = await _service.ObtenerPorIdAsync(id);

        // Assert: mapeo completo con los JSON crudos como string (D3/ADR-003)
        Assert.Equal(id, resultado.Id);
        Assert.Equal("Acme SA", resultado.TenantNombre);
        Assert.Equal("Ana Pérez", resultado.UsuarioNombre);
        Assert.Equal("UPDATE", resultado.Accion);
        Assert.Equal("Ciclo", resultado.Entidad);
        Assert.Equal("c1", resultado.EntidadId);
        Assert.Equal("{\"estado\":\"Borrador\"}", resultado.ValorAnterior);
        Assert.Equal("{\"estado\":\"Activo\"}", resultado.ValorNuevo);
        Assert.Equal(entidad.CreatedAt, resultado.CreatedAt);
    }

    // Caso 17 ─ rol Gerente → AccesoDenegadoException 403 (D12); el DAL nunca se consulta
    [Fact]
    public async Task ObtenerPorId_RolNoSuperAdmin_LanzaAccesoDenegado()
    {
        // Arrange: D12 — solo el SA consulta el log (D7)
        _tenantContext.Rol = "Gerente";
        var id = Guid.NewGuid();

        // Act & Assert: 403 y el DAL nunca se consulta
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerPorIdAsync(id));

        _mockRepo.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Caso 18-19. Inmutabilidad (contrato CA #3, por reflexión) ──────────

    // Caso 18 ─ ILogAuditoriaService: solo métodos de lectura (ListarAsync, ObtenerPorIdAsync);
    // sin Create/Update/Delete (CA #3)
    [Fact]
    public void InterfazServicio_NoExponeMetodosDeEscritura()
    {
        // Arrange
        var tipo = typeof(ILogAuditoriaService);

        // Act
        var nombres = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        // Assert: exactamente los 2 métodos de lectura del spec; cero escritura (CA #3/D1)
        Assert.Equal(2, nombres.Length);
        Assert.Contains("ListarAsync", nombres);
        Assert.Contains("ObtenerPorIdAsync", nombres);
        Assert.DoesNotContain(nombres, n => n.StartsWith("Crear", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Actualizar", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Update", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Delete", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Eliminar", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Insert", StringComparison.Ordinal));
    }

    // Caso 19 ─ ILogAuditoriaRepository: sin Update/Delete/Insert públicos; el único borrado
    // es EliminarAnterioresAAsync (batch de retención, no invocable por API — CA #3/D12)
    [Fact]
    public void InterfazRepositorio_NoExponeUpdateNiDeletePublico()
    {
        // Arrange
        var tipo = typeof(ILogAuditoriaRepository);

        // Act
        var nombres = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        // Assert: sin Update*/Delete*/Insert*; solo EliminarAnterioresAAsync como borrado (DAL-L4)
        Assert.DoesNotContain(nombres, n => n.StartsWith("Update", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Delete", StringComparison.Ordinal));
        Assert.DoesNotContain(nombres, n => n.StartsWith("Insert", StringComparison.Ordinal));
        Assert.Contains("EliminarAnterioresAAsync", nombres);
        Assert.Single(nombres, n => n.StartsWith("Eliminar", StringComparison.Ordinal));
    }
}
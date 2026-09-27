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
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-021 — Vista Gantt del Plan de Acción (Spec HU-021 § "Tests
/// requeridos", casos 1-23, L613-637). Escritos ANTES de la implementación (TEST-01): los
/// miembros de producción de HU-021 (DTOs, método BLL, método DAL, acción MVC) NO existen
/// todavía → el rojo legítimo en esta fase es el FALLO DE COMPILACIÓN del proyecto de tests
/// (CS0246 / CS0117 / CS1061) y así lo declara @Orquestador. NO se crean réplicas de los DTOs
/// ni stubs en el proyecto de tests: el patrón de PE-GOL.Tests/Stubs/ solo aplica a clases que
/// viven en proyectos Web SDK no referenciados (HU-004/HU-005); aquí PE-GOL.BLL, PE-GOL.DTO y
/// PE-GOL.Aplicacion SÍ están referenciados, así que un stub invalidaría la especificación
/// ejecutable (obligaría a borrarlo después).
///
/// Patrón: xUnit + Moq (TEST-03), Arrange/Act/Assert, nombres [Metodo]_[Escenario]_[Resultado]
/// (TEST-05), clase propia por servicio (TEST-04).
///
/// ── CONTRATO QUE @BackendDev DEBE IMPLEMENTAR (firma exacta, sin adivinar) ─────────────
/// BLL  · PE-GOL.BLL/Interfaces/IAccionPlanService.cs (extensión aditiva; las 7 firmas de
///        HU-019/HU-020 quedan intactas):
///        Task&lt;GanttPlanResponse&gt; ObtenerGanttAsync(CancellationToken ct = default);
///        → devuelve el DTO PLANO; nunca ApiResponse&lt;T&gt; (ADR-010 Decisión 1, caso 21).
/// DAL  · PE-GOL.DAL/Interfaces/IAccionPlanRepository.cs (extensión aditiva):
///        Task&lt;IEnumerable&lt;GanttAccionFilaDto&gt;&gt; ListarParaGanttAsync(
///            Guid tenantId, Guid cicloId, Guid? areaIdFiltro, CancellationToken ct = default);
/// DTOs · PE-GOL.DTO/Responses/PlanOperativo/ (namespace PE_GOL.DTO.Responses.PlanOperativo):
///        GanttPlanResponse, GanttMesResponse, GanttGrupoResponse, GanttAccionResponse y
///        GanttAccionFilaDto (fila plana de DAL-G1; solo DAL, nunca sale por la API).
///        Vocabulario = Enum::text de PostgreSQL tal cual lo expone el código real:
///        Status "NoIniciado|EnProgreso|Terminado|Atrasado" · Clasificacion
///        "Proyecto|Iniciativa|Operativa" · TipoPresupuesto "OPEX|CAPEX".
///
/// Lógica BLL bajo test (spec § "Lógica BLL", pasos 1-11):
///   1 tenant (TenantId null → NotFoundException 404) → 2 rol ∉ {JefeArea,Gerente} →
///   AccesoDenegadoException 403 → 3 areaIdFiltro = (Rol=="JefeArea") ? AreaId : null, y
///   JefeArea sin AreaId → AccesoDenegadoException (NUNCA "todas las áreas", SEC-07) →
///   4 ciclo activo por ICicloRepository.ObtenerCicloActivoAsync (RC-01; null →
///   NotFoundException "No hay un ciclo activo para el tenant") → 5 UNA sola query DAL-G1 →
///   6 sin acciones NO es error: 200 con Grupos/Acciones vacíos, Escala de 12 meses y los 4
///   conteos en 0 (D-C) → 7 Grupos = CGs distintos PRESERVANDO el orden entregado por DAL-G1
///   (objetivo_orden, objetivo_codigo) con TotalAcciones contado en BLL (DB-04, D-K) →
///   8 ConteoPorStatus con las 4 claves SIEMPRE presentes (0 si no hay) → 9 Escala de 12 meses
///   desde (año_fiscal, mes_inicio), con ABREV[ENE..DIC] + año (cruza de año si mes_inicio≠1) →
///   10 FechaInicioCiclo/FechaFinCiclo = CicloFechaHelper.ObtenerRango(año, mes) convertido con
///   TimeOnly.MinValue (el helper NO se modifica) → 11 mapear tal cual: status y progreso se
///   LEEN, no se recalculan (D-M / F5). Sin auditoría, sin transacción, sin escrituras.
///
/// Lógica de fixture: dos roles sobre la MISMA clase (criterio del spec L611) porque
/// AccionPlanServiceTests fija Rol="JefeArea" y su regresión HU-019/HU-020 debe quedar intacta
/// (no se toca ese archivo). El TenantContext es mutable y xUnit crea una instancia de la
/// clase por test → ConfigurarRol() es seguro. Se usa el constructor EXTENDIDO de HU-020
/// (con IHistorialProgresoRepository) para reproduces el ctor real de DI y demostrar que
/// ObtenerGanttAsync no depende del historial.
/// </summary>
public class AccionPlanGanttServiceTests
{
    private readonly Mock<IAccionPlanRepository> _repoMock;
    private readonly Mock<ICicloRepository> _cicloRepoMock;
    private readonly Mock<IObjetivoCgRepository> _objRepoMock;
    private readonly Mock<IHistorialProgresoRepository> _historialRepoMock;
    private readonly TenantContext _tenantContext;
    private readonly IAccionPlanService _service;

    public AccionPlanGanttServiceTests()
    {
        _repoMock = new Mock<IAccionPlanRepository>();
        _cicloRepoMock = new Mock<ICicloRepository>();
        _objRepoMock = new Mock<IObjetivoCgRepository>();
        _historialRepoMock = new Mock<IHistorialProgresoRepository>();

        _tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rol = "JefeArea",      // caso por defecto; los casos de Gerente reconfiguran con ConfigurarRol
            AreaId = Guid.NewGuid()
        };

        // Ctor EXTENDIDO de HU-020 (5 args) — el mismo que registra DI.
        _service = new AccionPlanService(
            _repoMock.Object,
            _cicloRepoMock.Object,
            _objRepoMock.Object,
            _tenantContext,
            _historialRepoMock.Object);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CicloEntity CrearCiclo(
        Guid? id = null, int añoFiscal = 2026, int mesInicio = 1, string nombre = "PE 2026",
        string estado = "Activo")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Nombre = nombre,
            AñoFiscal = añoFiscal,
            MesInicio = mesInicio,
            Estado = estado
        };

    /// <summary>Fila plana de DAL-G1 (acción + metadatos del CG y del área por JOIN).</summary>
    private static GanttAccionFilaDto CrearFila(
        Guid? objetivoCgId = null,
        Guid? areaId = null,
        int objetivoOrden = 1,
        string objetivoCodigo = "GOL1.CG1",
        string objetivoDescripcion = "Incrementar ventas 15%",
        decimal objetivoProgreso = 0.5m,
        string objetivoSemaforo = "Amarillo",
        string areaCodigo = "GOL1",
        string areaNombre = "CEDIS FARMA",
        string codigo = "GOL1.CG1.01",
        string descripcion = "Lanzar campaña de marketing",
        DateTime? fechaInicio = null,
        DateTime? fechaVencimiento = null,
        string clasificacion = "Proyecto",
        string tipoPresupuesto = "OPEX",
        decimal peso = 0.5m,
        decimal progreso = 0m,
        string status = "NoIniciado",
        int orden = 1,
        string? responsableNombre = "Juan Pérez")
        => new()
        {
            Id = Guid.NewGuid(),
            ObjetivoCgId = objetivoCgId ?? Guid.NewGuid(),
            AreaId = areaId ?? Guid.NewGuid(),
            Codigo = codigo,
            Descripcion = descripcion,
            FechaInicio = fechaInicio ?? new DateTime(2026, 6, 1),
            FechaVencimiento = fechaVencimiento ?? new DateTime(2026, 8, 31),
            Clasificacion = clasificacion,
            TipoPresupuesto = tipoPresupuesto,
            Peso = peso,
            Progreso = progreso,
            Status = status,
            Orden = orden,
            ResponsableNombre = responsableNombre,
            ObjetivoOrden = objetivoOrden,
            ObjetivoCodigo = objetivoCodigo,
            ObjetivoDescripcion = objetivoDescripcion,
            ObjetivoProgreso = objetivoProgreso,
            ObjetivoSemaforo = objetivoSemaforo,
            AreaCodigo = areaCodigo,
            AreaNombre = areaNombre
        };

    /// <summary>Configura el ciclo activo (DAL-D1, RC-01) y la única query de datos (DAL-G1).
    /// El filtro de área se captura con It.IsAny&lt;Guid?&gt;() para que el mismo setup sirva a
    /// los dos roles; el valor EXACTO que viaja a la DAL lo verifican los casos 10 y 12.</summary>
    private void ConfigurarFlujoFeliz(CicloEntity ciclo, IEnumerable<GanttAccionFilaDto>? filas = null)
    {
        var tenantId = _tenantContext.TenantId!.Value;

        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ciclo);

        _repoMock
            .Setup(r => r.ListarParaGanttAsync(tenantId, ciclo.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(filas ?? Array.Empty<GanttAccionFilaDto>());
    }

    /// <summary>Fixtures de dos roles (JefeArea por defecto / Gerente / AdminTenant / sin área).
    /// El TenantContext es la misma instancia inyectada en el servicio (patrón DashboardServiceTests).</summary>
    private void ConfigurarRol(string rol, Guid? areaId)
    {
        _tenantContext.Rol = rol;
        _tenantContext.AreaId = areaId;
    }

    private void VerificarNingunaEscritura()
    {
        _repoMock.Verify(r => r.InsertAsync(It.IsAny<AccionPlanEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<AccionPlanEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Casos 1-4. Guardas de tenant, rol y área (SEC-06 / SEC-07) ──────────

    // Caso 1 ─ TenantId=null → NotFoundException (404, helper existente ObtenerTenantIdOThrow)
    [Fact]
    public async Task ObtenerGantt_SinTenant_LanzaNotFound()
    {
        // Arrange: D17 — SuperAdmin sin tenant_id
        _tenantContext.TenantId = null;

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerGanttAsync());

        // Ninguna DAL se invoca: el tenant se resuelve antes de cualquier query
        _cicloRepoMock.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.ListarParaGanttAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 2 ─ Rol ∉ {JefeArea, Gerente} → AccesoDenegadoException (403) y la DAL-G1 nunca se invoca
    [Fact]
    public async Task ObtenerGantt_RolNoAutorizado_LanzaAccesoDenegado()
    {
        // Arrange: AdminTenant es un rol válido del sistema pero NO está en el alcance de HU-021 (F7)
        ConfigurarRol("AdminTenant", areaId: null);

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerGanttAsync());

        // La re-validación de rol en BLL (paso 2) precede a la resolución del ciclo (paso 4)
        _cicloRepoMock.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.ListarParaGanttAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 3 ─ SEC-07: JefeArea sin area_id en el JWT → 403 y NUNCA "todas las áreas"
    [Fact]
    public async Task ObtenerGantt_RolJefeAreaSinAreaId_LanzaAccesoDenegado()
    {
        // Arrange: JWT sin claim area_id (Paso 3 del spec: se trata como acceso denegado)
        ConfigurarRol("JefeArea", areaId: null);

        // Act & Assert
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _service.ObtenerGanttAsync());

        // La degradación a "todas las áreas" (areaIdFiltro = null) sería una fuga SEC-07:
        // se verifica explícitamente que NUNCA se llama a DAL-G1 con el filtro en null.
        // OJO: dentro de un expression tree (Moq) no vale el patrón `is null` (CS8122).
        _repoMock.Verify(
            r => r.ListarParaGanttAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Guid?>(a => !a.HasValue), It.IsAny<CancellationToken>()),
            Times.Never);
        _cicloRepoMock.Verify(r => r.ObtenerCicloActivoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 4 ─ sin ciclo Activo (DAL-D1 → null) → NotFoundException con el mensaje de HU-015/HU-016
    [Fact]
    public async Task ObtenerGantt_SinCicloActivo_LanzaNotFound()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        _cicloRepoMock
            .Setup(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CicloEntity?)null);

        // Act
        var excepcion = await Assert.ThrowsAsync<NotFoundException>(() => _service.ObtenerGanttAsync());

        // Assert: mensaje idéntico al de HU-015/HU-016 (D-G) y la query de datos no se ejecuta
        Assert.Equal("No hay un ciclo activo para el tenant", excepcion.Message);
        _repoMock.Verify(r => r.ListarParaGanttAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Casos 5-6. Payload completo ────────────────────────────────────────

    // Caso 5 ─ 0 acciones → 200 con payload vacío + Escala de 12 meses + los 4 conteos en 0 (D-C, NO 404)
    [Fact]
    public async Task ObtenerGantt_SinAcciones_RetornaPayloadVacioYConteosEnCero()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: Array.Empty<GanttAccionFilaDto>());

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.NotNull(resultado);
        Assert.Equal(ciclo.Id, resultado.CicloId);
        Assert.Empty(resultado.Grupos);
        Assert.Empty(resultado.Acciones);
        Assert.Equal(12, resultado.Escala.Count);   // el ciclo sí existe → la escala viaja igual
        Assert.Equal(4, resultado.ConteoPorStatus.Count);
        Assert.All(resultado.ConteoPorStatus, kv => Assert.Equal(0, kv.Value));
        Assert.True(resultado.ConteoPorStatus.ContainsKey("NoIniciado"));
        Assert.True(resultado.ConteoPorStatus.ContainsKey("EnProgreso"));
        Assert.True(resultado.ConteoPorStatus.ContainsKey("Terminado"));
        Assert.True(resultado.ConteoPorStatus.ContainsKey("Atrasado"));
        VerificarNingunaEscritura();
    }

    // Caso 6 ─ 1 CG con 2 acciones → 1 grupo + 2 acciones con TODOS los campos mapeados
    [Fact]
    public async Task ObtenerGantt_ConAcciones_RetornaGruposYAcciones()
    {
        // Arrange
        var areaId = _tenantContext.AreaId!.Value;
        var cgId = Guid.NewGuid();
        var inicio = new DateTime(2026, 6, 1);
        var vencimiento = new DateTime(2026, 8, 31);
        var filas = new[]
        {
            CrearFila(objetivoCgId: cgId, areaId: areaId, codigo: "GOL1.CG1.01", orden: 1,
                descripcion: "Lanzar campaña de marketing", fechaInicio: inicio, fechaVencimiento: vencimiento,
                clasificacion: "Proyecto", tipoPresupuesto: "OPEX", peso: 0.6m, progreso: 40m,
                status: "EnProgreso", responsableNombre: "Juan Pérez"),
            CrearFila(objetivoCgId: cgId, areaId: areaId, codigo: "GOL1.CG1.02", orden: 2,
                descripcion: "Abrir 3 tiendas nuevas", fechaInicio: new DateTime(2026, 9, 1),
                fechaVencimiento: new DateTime(2026, 11, 30), clasificacion: "Iniciativa",
                tipoPresupuesto: "CAPEX", peso: 0.4m, progreso: 0m, status: "NoIniciado",
                responsableNombre: null)
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert — grupos
        var grupo = Assert.Single(resultado.Grupos);
        Assert.Equal(cgId, grupo.ObjetivoCgId);
        Assert.Equal("GOL1.CG1", grupo.Codigo);
        Assert.Equal("Incrementar ventas 15%", grupo.Descripcion);
        Assert.Equal(areaId, grupo.AreaId);
        Assert.Equal("GOL1", grupo.AreaCodigo);
        Assert.Equal("CEDIS FARMA", grupo.AreaNombre);
        Assert.Equal(0.5m, grupo.Progreso);        // LÍDO de objetivo_cg.progreso (0..1) — DB-04
        Assert.Equal("Amarillo", grupo.Semaforo);  // LÍDO — NO se recalcula
        Assert.Equal(2, grupo.TotalAcciones);      // contado en BLL, no COUNT en SQL (D-K)

        // Assert — acciones (mapeo 1:1 de la fila de DAL-G1)
        Assert.Equal(2, resultado.Acciones.Count);
        Assert.All(resultado.Acciones, a => Assert.Equal(cgId, a.ObjetivoCgId));
        Assert.All(resultado.Acciones, a => Assert.Equal(areaId, a.AreaId));

        var primera = resultado.Acciones[0];
        Assert.Equal(filas[0].Id, primera.Id);
        Assert.Equal("GOL1.CG1.01", primera.Codigo);
        Assert.Equal("Lanzar campaña de marketing", primera.Descripcion);
        Assert.Equal(inicio, primera.FechaInicio);
        Assert.Equal(vencimiento, primera.FechaVencimiento);
        Assert.Equal(0.6m, primera.Peso);
        Assert.Equal(40m, primera.Progreso);                 // LÍDO (0..100) — DB-04/D-M
        Assert.Equal("EnProgreso", primera.Status);          // LÍDO — D-M
        Assert.Equal("Proyecto", primera.Clasificacion);
        Assert.Equal("OPEX", primera.TipoPresupuesto);
        Assert.Equal("Juan Pérez", primera.ResponsableNombre);
        Assert.Equal(1, primera.Orden);

        Assert.Null(resultado.Acciones[1].ResponsableNombre); // tolerado a null (LEFT JOIN)
    }

    // ─── Casos 7-9. Escala de 12 meses y rango del ciclo (CA #1) ─────────────

    // Caso 7 ─ mes_inicio = 1 → ENE..DIC del año fiscal, con el año en la etiqueta
    [Fact]
    public async Task ObtenerGantt_EscalaConMesInicioEnero_Devuelve12MesesDelAnioFiscal()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila() });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.Equal(12, resultado.Escala.Count);
        Assert.Equal(2026, resultado.Escala[0].Anio);
        Assert.Equal(1, resultado.Escala[0].Mes);
        Assert.Equal("ENE 2026", resultado.Escala[0].Etiqueta);
        Assert.Equal(12, resultado.Escala[11].Mes);
        Assert.Equal(2026, resultado.Escala[11].Anio);
        Assert.Equal("DIC 2026", resultado.Escala[11].Etiqueta);
    }

    // Caso 8 ─ mes_inicio = 6 → la escala arranca en JUN y CRUZA de año (ENE 2027)
    [Fact]
    public async Task ObtenerGantt_EscalaConMesInicioNoEnero_ArrancaEnElMesInicio()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 6);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila() });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: JUN 2026 … MAY 2027 (12 meses exactos, la etiqueta siempre lleva el año)
        Assert.Equal(12, resultado.Escala.Count);
        Assert.Equal(2026, resultado.Escala[0].Anio);
        Assert.Equal(6, resultado.Escala[0].Mes);
        Assert.Equal("JUN 2026", resultado.Escala[0].Etiqueta);
        Assert.Equal(2026, resultado.Escala[6].Anio);
        Assert.Equal(12, resultado.Escala[6].Mes);
        Assert.Equal("DIC 2026", resultado.Escala[6].Etiqueta);
        Assert.Equal(2027, resultado.Escala[7].Anio);
        Assert.Equal(1, resultado.Escala[7].Mes);
        Assert.Equal("ENE 2027", resultado.Escala[7].Etiqueta);
        Assert.Equal("MAY 2027", resultado.Escala[11].Etiqueta);
        Assert.Equal(2027, resultado.Escala[11].Anio);
    }

    // Caso 9 ─ FechaInicioCiclo/FechaFinCiclo = CicloFechaHelper.ObtenerRango (TimeOnly.MinValue)
    [Fact]
    public async Task ObtenerGantt_EscalaRangoCiclo_CubreLos12Meses()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 6);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila() });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: valores explícitos…
        Assert.Equal(new DateTime(2026, 6, 1), resultado.FechaInicioCiclo);
        Assert.Equal(new DateTime(2027, 5, 31), resultado.FechaFinCiclo);

        // …y coincidencia con el helper EXISTENTE, que esta HU NO modifica (spec paso 10)
        var (inicio, fin) = CicloFechaHelper.ObtenerRango(ciclo.AñoFiscal, ciclo.MesInicio);
        Assert.Equal(inicio.ToDateTime(TimeOnly.MinValue), resultado.FechaInicioCiclo);
        Assert.Equal(fin.ToDateTime(TimeOnly.MinValue), resultado.FechaFinCiclo);

        // TimeOnly.MinValue (medianoche): FechaFinCiclo viaja a gantt.config.max_date como FECHA,
        // no como límite de comparación (a diferencia de TimeOnly.MaxValue en ValidarFechasCicloAsync).
        Assert.Equal(TimeSpan.Zero, resultado.FechaFinCiclo.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, resultado.FechaInicioCiclo.TimeOfDay);
    }

    // ─── Casos 10-12. SEC-07: el filtro de área viaja a la DAL (y SOLO a la DAL — F11) ──

    // Caso 10 ★ ─ SEC-07: Verify con el AreaId EXACTO del TenantContext (la barrera real)
    [Fact]
    public async Task ObtenerGantt_RolJefeArea_InyectaSuAreaIdComoFiltro()
    {
        // Arrange
        var areaId = Guid.NewGuid();
        ConfigurarRol("JefeArea", areaId);
        var tenantId = _tenantContext.TenantId!.Value;
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila(areaId: areaId) });

        // Act
        await _service.ObtenerGanttAsync();

        // Assert: DAL-G1 recibe (tenantId, cicloId, areaIdFiltro = AreaId del JWT, ct)
        _repoMock.Verify(
            r => r.ListarParaGanttAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 11 ─ Test de CARACTERIZACIÓN (F11, Jorge 2026-09-27): la BLL NO re-filtra en memoria.
    //
    // ⚠️ POR QUÉ NO EXISTE UN CASO "otra área → resultado vacío":
    // el aislamiento de área de esta HU vive en UNA sola capa, la query DAL-G1
    // (`AND (@AreaIdFiltro IS NULL OR a.area_id = @AreaIdFiltro)`), y así está resuelto por
    // Jorge en F11 — no es un olvido. El endpoint NO acepta area_id del cliente (D-F), así que
    // el escenario "un Jefe de Área pide datos de otra área" no es representable en el
    // contrato. Lo que sí se blinda aquí es la decisión de diseño: la BLL devuelve EXACTAMENTE
    // lo que entrega la DAL y no añade un Where(...) en memoria que enmascararía un fallo del
    // DAL (mismo criterio que AreaService.ListarAreasAsync y ResponsableService.ListarAsync).
    // La garantía real la dan el caso 3 (403 sin area_id) y el caso 10 (Verify del valor exacto).
    // Si algún día se revierte F11 (defensa en profundidad), este test es el que debe cambiar.
    [Fact]
    public async Task ObtenerGantt_RolJefeArea_NoAplicaSegundoFiltroEnBLL()
    {
        // Arrange: la DAL devuelve una fila del área A (legítima) y una de B (una fila que la
        // query REAL no puede devolver con areaIdFiltro = A).
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        ConfigurarRol("JefeArea", areaA);
        var filas = new[]
        {
            CrearFila(areaId: areaA, codigo: "GOL1.CG1.01", areaCodigo: "GOL1", areaNombre: "CEDIS FARMA"),
            CrearFila(areaId: areaB, codigo: "GOL2.CG1.01", areaCodigo: "GOL2", areaNombre: "VENTAS")
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: las 2 acciones vuelven; la BLL no descarta nada (F11)
        Assert.Equal(2, resultado.Acciones.Count);
        Assert.Contains(resultado.Acciones, a => a.AreaId == areaA);
        Assert.Contains(resultado.Acciones, a => a.AreaId == areaB);
        Assert.Equal(2, resultado.Grupos.Count);

        // Y el filtro sí se envió a la DAL (el aislamiento no desaparece, solo no se duplica)
        _repoMock.Verify(
            r => r.ListarParaGanttAsync(_tenantContext.TenantId!.Value, ciclo.Id, areaA, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Caso 12 ─ Gerente (RN-006): SEC-07 NO aplica → areaIdFiltro = null y ve todas las áreas
    [Fact]
    public async Task ObtenerGantt_RolGerente_InyectaAreaIdNullYVeTodasLasAreas()
    {
        // Arrange
        ConfigurarRol("Gerente", areaId: null);
        var tenantId = _tenantContext.TenantId!.Value;
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var filas = new[]
        {
            CrearFila(areaId: areaA, areaCodigo: "GOL1", areaNombre: "CEDIS FARMA"),
            CrearFila(areaId: areaB, areaCodigo: "GOL2", areaNombre: "VENTAS")
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: filtro null explícito (no Guid.Empty) y acciones de más de un área
        _repoMock.Verify(
            r => r.ListarParaGanttAsync(tenantId, ciclo.Id, null, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(2, resultado.Acciones.Count);
        Assert.Contains(resultado.Acciones, a => a.AreaId == areaA);
        Assert.Contains(resultado.Acciones, a => a.AreaId == areaB);
    }

    // ─── Casos 13-14. Conteo por status (RF-031 parcial, DB-04) ──────────────

    // Caso 13 ─ 2 Terminado + 3 EnProgreso + 1 Atrasado + 1 NoIniciado (7 acciones)
    [Fact]
    public async Task ObtenerGantt_ConteoPorStatus_CuentaLasCuatroCategorias()
    {
        // Arrange
        var filas = Enumerable.Range(1, 7)
            .Select(i => CrearFila(
                codigo: $"GOL1.CG1.{i:D2}",
                orden: i,
                status: i switch
                {
                    <= 2 => "Terminado",
                    <= 5 => "EnProgreso",
                    6 => "Atrasado",
                    _ => "NoIniciado"
                }))
            .ToArray();
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.Equal(4, resultado.ConteoPorStatus.Count);
        Assert.Equal(2, resultado.ConteoPorStatus["Terminado"]);
        Assert.Equal(3, resultado.ConteoPorStatus["EnProgreso"]);
        Assert.Equal(1, resultado.ConteoPorStatus["Atrasado"]);
        Assert.Equal(1, resultado.ConteoPorStatus["NoIniciado"]);
    }

    // Caso 14 ─ 5 NoIniciado → las 4 claves existen siempre; las otras 3 en 0 (el front nunca adivina)
    [Fact]
    public async Task ObtenerGantt_ConteoPorStatus_TodasLasClavesPresentesEnCero()
    {
        // Arrange
        var filas = Enumerable.Range(1, 5)
            .Select(i => CrearFila(codigo: $"GOL1.CG1.{i:D2}", orden: i, status: "NoIniciado"))
            .ToArray();
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.Equal(4, resultado.ConteoPorStatus.Count);
        Assert.Equal(5, resultado.ConteoPorStatus["NoIniciado"]);
        Assert.Equal(0, resultado.ConteoPorStatus["Terminado"]);
        Assert.Equal(0, resultado.ConteoPorStatus["EnProgreso"]);
        Assert.Equal(0, resultado.ConteoPorStatus["Atrasado"]);
    }

    // ─── Casos 15-16. Agrupación por Objetivo CG (CA #3) ────────────────────

    // Caso 15 ─ la BLL PRESERVA el orden entregado por DAL-G1 (objetivo_orden, objetivo_codigo);
    // no reordena en memoria (el ORDER BY es de la query, paso 7 del spec).
    [Fact]
    public async Task ObtenerGantt_Grupos_OrdenadosPorObjetivoOrdenYCodigo()
    {
        // Arrange: DAL-G1 entrega los CGs YA ordenados por (orden, codigo). Aquí el orden
        // recibido es 2, 1, 1 → la salida debe ser exactamente ese orden.
        var cg1 = Guid.NewGuid();
        var cg2 = Guid.NewGuid();
        var cg3 = Guid.NewGuid();
        var filas = new[]
        {
            CrearFila(objetivoCgId: cg3, objetivoOrden: 2, objetivoCodigo: "GOL1.CG9"),
            CrearFila(objetivoCgId: cg1, objetivoOrden: 1, objetivoCodigo: "GOL1.CG1"),
            CrearFila(objetivoCgId: cg2, objetivoOrden: 1, objetivoCodigo: "GOL1.CG2")
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.Equal(3, resultado.Grupos.Count);
        Assert.Equal(cg3, resultado.Grupos[0].ObjetivoCgId);
        Assert.Equal(cg1, resultado.Grupos[1].ObjetivoCgId);
        Assert.Equal(cg2, resultado.Grupos[2].ObjetivoCgId);
        Assert.Equal("GOL1.CG9", resultado.Grupos[0].Codigo);
        Assert.Equal("GOL1.CG1", resultado.Grupos[1].Codigo);
        Assert.Equal("GOL1.CG2", resultado.Grupos[2].Codigo);
    }

    // Caso 16 ─ consolidación por CG y TotalAcciones contado en BLL (D-K, DB-04: no hay COUNT en SQL)
    [Fact]
    public async Task ObtenerGantt_Grupos_ConsolidaAccionesDelMismoObjetivo()
    {
        // Arrange
        var cgA = Guid.NewGuid();
        var cgB = Guid.NewGuid();
        var filas = new[]
        {
            CrearFila(objetivoCgId: cgA, objetivoCodigo: "GOL1.CG1", codigo: "GOL1.CG1.01", orden: 1),
            CrearFila(objetivoCgId: cgA, objetivoCodigo: "GOL1.CG1", codigo: "GOL1.CG1.02", orden: 2),
            CrearFila(objetivoCgId: cgB, objetivoCodigo: "GOL1.CG2", codigo: "GOL1.CG2.01", orden: 1)
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        Assert.Equal(2, resultado.Grupos.Count);
        Assert.Equal(3, resultado.Acciones.Count);
        Assert.Equal(2, resultado.Grupos[0].TotalAcciones);
        Assert.Equal(1, resultado.Grupos[1].TotalAcciones);

        // El conteo se hace sobre las filas ya cargadas: no se agrega una query por grupo (D-K / RNF-001)
        _repoMock.Verify(r => r.ListarPorObjetivoCgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _objRepoMock.Verify(r => r.ListarAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _repoMock.Verify(r => r.ListarParaGanttAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Casos 17-18. D-M (F5): los campos calculados se LEEN, no se recalculan ─

    // Caso 17 ★ ─ fecha_vencimiento ya pasada + progreso 40 + status persistido "EnProgreso"
    // → devuelve "EnProgreso". NO "Atrasado": la BLL no reimplementa RN-017 (D-M, F5). El desfase
    // por vencimiento es responsabilidad del batch RN-040 de Sprint 8 (hallazgo 7/8 del spec).
    [Fact]
    public async Task ObtenerGantt_StatusPersistido_NoSeRecalcula()
    {
        // Arrange
        var filas = new[]
        {
            CrearFila(
                fechaInicio: new DateTime(2026, 1, 1),
                fechaVencimiento: DateTime.Today.AddDays(-1),   // vencida HOY
                progreso: 40m,
                status: "EnProgreso")                          // status PERSISTIDO en la BD
        };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        var accion = Assert.Single(resultado.Acciones);
        Assert.Equal("EnProgreso", accion.Status);
        Assert.NotEqual("Atrasado", accion.Status);
        Assert.Equal(40m, accion.Progreso);   // progreso LÍDO tal cual (0..100), no normalizado
    }

    // Caso 18 ─ responsable_id NULL (LEFT JOIN usuario) → sin excepción y ResponsableNombre = null
    [Fact]
    public async Task ObtenerGantt_ResponsableNulo_NoLanzaYDevuelveNombreNull()
    {
        // Arrange
        var filas = new[] { CrearFila(responsableNombre: null) };
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas);

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert
        var accion = Assert.Single(resultado.Acciones);
        Assert.Null(accion.ResponsableNombre);
    }

    // ─── Casos 19-23. Solo lectura, SEC-06 y arquitectura ────────────────────

    // Caso 19 ─ sin escrituras en BD: el Gantt es de solo lectura (D-N)
    [Fact]
    public async Task ObtenerGantt_SoloLectura_NoInvocaNingunaEscritura()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila(), CrearFila() });

        // Act
        await _service.ObtenerGanttAsync();

        // Assert
        VerificarNingunaEscritura();
        _historialRepoMock.Verify(r => r.InsertAsync(It.IsAny<HistorialProgresoEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Caso 20 ─ SEC-06: tenantId del TenantContext y cicloId resuelto en servidor.
    // El método NO recibe argumentos de negocio (reflexión): su única entrada admitida es el
    // CancellationToken opcional. Ningún tenant_id / ciclo_id / area_id entra por el request.
    [Fact]
    public async Task ObtenerGantt_Sec06_UsaTenantYCicloResueltosEnServidor()
    {
        // Arrange
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila(areaId: areaId) });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: DAL-D1 con el tenantId del contexto…
        _cicloRepoMock.Verify(r => r.ObtenerCicloActivoAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
        // …y DAL-G1 con ese mismo tenantId y con el cicloId resuelto internamente
        _repoMock.Verify(r => r.ListarParaGanttAsync(tenantId, ciclo.Id, areaId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ciclo.Id, resultado.CicloId);

        // El método no recibe entradas del cliente (SEC-06)
        var metodo = typeof(IAccionPlanService).GetMethod(nameof(IAccionPlanService.ObtenerGanttAsync));
        Assert.NotNull(metodo);
        Assert.All(metodo!.GetParameters(), p => Assert.Equal(typeof(CancellationToken), p.ParameterType));
    }

    // Caso 21 ─ ADR-010 Decisión 1: la BLL nunca expone ApiResponse<T>; devuelve el DTO plano.
    [Fact]
    public async Task ObtenerGantt_Exito_RetornaDtoPlanoSinApiResponse()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila() });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: el retorno en runtime es el DTO de dominio
        Assert.IsType<GanttPlanResponse>(resultado);

        // Y el CONTRATO lo fija la reflexión: firma pública sin ApiResponse<…> en ninguna parte
        var metodo = typeof(IAccionPlanService).GetMethod(nameof(IAccionPlanService.ObtenerGanttAsync));
        Assert.NotNull(metodo);
        Assert.Equal(typeof(Task<GanttPlanResponse>), metodo!.ReturnType);
        Assert.False(UsaApiResponse(typeof(IAccionPlanService)));
        Assert.False(UsaApiResponse(typeof(AccionPlanService)));
    }

    // Caso 22 ─ "no audita" es ESTRUCTURAL: AccionPlanService no tiene dependencia de auditoría
    // (ninguno de sus 2 constructores la declara) e IAccionPlanRepository no expone InsertLogAsync
    // → no hay forma de auditar desde el Gantt. Mismo criterio que DashboardServiceTests caso 20.
    [Fact]
    public void ObtenerGantt_NoAuditaPorReflexion()
    {
        // Arrange & Act
        var tiposAuditoria = new[] { typeof(ILogAuditoriaRepository), typeof(LogAuditoriaInsert) };
        var constructores = typeof(AccionPlanService).GetConstructors();

        // Assert: al menos un ctor (HU-019 + HU-020) y ninguno con parámetro de auditoría
        Assert.NotEmpty(constructores);
        foreach (var ctor in constructores)
            Assert.DoesNotContain(ctor.GetParameters(), p => tiposAuditoria.Contains(p.ParameterType));

        // El repositorio de acciones no expone el método de auditoría
        Assert.Null(typeof(IAccionPlanRepository).GetMethod("InsertLogAsync"));
        Assert.DoesNotContain(
            typeof(IAccionPlanRepository).GetMethods(),
            m => m.Name.Contains("Log", StringComparison.OrdinalIgnoreCase));
    }

    // Caso 23 ─ SEC-06: ningún DTO de respuesta expone TenantId; CicloId SÍ existe (es salida)
    [Fact]
    public async Task ObtenerGantt_Contrato_SinTenantIdEnElDto()
    {
        // Arrange
        var ciclo = CrearCiclo(añoFiscal: 2026, mesInicio: 1);
        ConfigurarFlujoFeliz(ciclo, filas: new[] { CrearFila() });

        // Act
        var resultado = await _service.ObtenerGanttAsync();

        // Assert: TenantId NO existe en ninguno de los DTOs del contrato del Gantt
        Assert.Null(typeof(GanttPlanResponse).GetProperty("TenantId"));
        Assert.Null(typeof(GanttGrupoResponse).GetProperty("TenantId"));
        Assert.Null(typeof(GanttAccionResponse).GetProperty("TenantId"));
        Assert.Null(typeof(GanttMesResponse).GetProperty("TenantId"));

        // CicloId sí existe: es dato de SALIDA (el ciclo activo ya fue resuelto en BLL), y vale el
        // id que devolvió DAL-D1 — nunca un valor elegido por el cliente.
        var propiedadCicloId = typeof(GanttPlanResponse).GetProperty("CicloId");
        Assert.NotNull(propiedadCicloId);
        Assert.Equal(typeof(Guid), propiedadCicloId!.PropertyType);
        Assert.Equal(ciclo.Id, resultado.CicloId);
    }

    // ─── Utilidades de reflexión (casos 21-23) ──────────────────────────────

    /// <summary>true si algún método público de <paramref name="tipo"/> declara ApiResponse&lt;&gt;
    /// en el tipo de retorno o en algún parámetro (incluido anidado en Task&lt;&gt;).
    /// ADR-010 Decisión 1: la BLL nunca expone el wrapper HTTP; solo el controller envuelve.</summary>
    private static bool UsaApiResponse(Type tipo)
    {
        var metodos = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return metodos.Any(m => ContieneApiResponse(m.ReturnType) ||
                                m.GetParameters().Any(p => ContieneApiResponse(p.ParameterType)));
    }

    private static bool ContieneApiResponse(Type tipo)
    {
        if (tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(ApiResponse<>))
            return true;

        return tipo.IsGenericType && tipo.GetGenericArguments().Any(ContieneApiResponse);
    }
}

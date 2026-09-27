using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

public class AccionPlanService : IAccionPlanService
{
    /// <summary>
    /// Abreviaturas de mes en español (01_AS-IS_PE_GOL.md L125) para la escala de 12 meses del
    /// Gantt (HU-021, CA #1). El índice del array es mes - 1.
    /// </summary>
    private static readonly string[] AbreviaturasMeses =
        ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    private readonly IAccionPlanRepository _repository;
    private readonly ICicloRepository _cicloRepository;
    private readonly IObjetivoCgRepository _objetivoCgRepository;
    private readonly TenantContext _tenantContext;
    private readonly IHistorialProgresoRepository? _historialRepository;

    // Constructor principal (HU-019)
    public AccionPlanService(
        IAccionPlanRepository repository,
        ICicloRepository cicloRepository,
        IObjetivoCgRepository objetivoCgRepository,
        TenantContext tenantContext)
    {
        _repository           = repository;
        _cicloRepository      = cicloRepository;
        _objetivoCgRepository = objetivoCgRepository;
        _tenantContext        = tenantContext;
    }

    // Constructor extendido (HU-020): añade IHistorialProgresoRepository
    public AccionPlanService(
        IAccionPlanRepository repository,
        ICicloRepository cicloRepository,
        IObjetivoCgRepository objetivoCgRepository,
        TenantContext tenantContext,
        IHistorialProgresoRepository historialRepository)
        : this(repository, cicloRepository, objetivoCgRepository, tenantContext)
    {
        _historialRepository = historialRepository;
    }

    private void ValidarRolJefeArea(Guid areaIdObjetivo)
    {
        if (_tenantContext.Rol != "JefeArea")
            throw new AccesoDenegadoException("Solo un Jefe de Área puede gestionar las acciones del plan.");

        if (areaIdObjetivo != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No puede gestionar acciones de un objetivo que pertenece a otra área.");
    }

    private async Task ValidarFechasCicloAsync(Guid cicloId, DateTime fechaInicio, DateTime fechaVencimiento, CancellationToken ct)
    {
        var ciclo = await _cicloRepository.ObtenerPorIdAsync(ObtenerTenantIdOThrow(), cicloId, ct)
            ?? throw new NotFoundException("Ciclo no encontrado.");

        var (inicioCiclo, finCiclo) = CicloFechaHelper.ObtenerRango(ciclo.AñoFiscal, ciclo.MesInicio);
        var inicioCicloDateTime = inicioCiclo.ToDateTime(TimeOnly.MinValue);
        var finCicloDateTime = finCiclo.ToDateTime(TimeOnly.MaxValue);

        if (fechaInicio < inicioCicloDateTime || fechaVencimiento > finCicloDateTime)
            throw new ValidacionException($"Las fechas de la acción deben estar dentro del rango del ciclo: {inicioCiclo:yyyy-MM-dd} al {finCiclo:yyyy-MM-dd}.");

        if (fechaInicio > fechaVencimiento)
            throw new ValidacionException("La fecha de inicio no puede ser mayor a la fecha de vencimiento.");
    }

    /// <summary>Aplica las 4 reglas de RN-017 en orden de precedencia para determinar el status de la acción.</summary>
    private static string CalcularStatus(decimal progreso, DateTime fechaInicio, DateTime fechaVencimiento)
    {
        var hoy = DateTime.UtcNow.Date;
        if (progreso == 100m)                                return "Terminado";
        if (progreso > 0m  && hoy <= fechaVencimiento.Date) return "EnProgreso";
        if (progreso == 0m && hoy <= fechaInicio.Date)      return "NoIniciado";
        return "Atrasado";
    }

    private AccionPlanResponse MapToResponse(AccionPlanEntity entity)
    {
        return new AccionPlanResponse
        {
            Id                    = entity.Id,
            ObjetivoCgId          = entity.ObjetivoCgId,
            Codigo                = entity.Codigo,
            Descripcion           = entity.Descripcion,
            DescripcionEntregable = entity.DescripcionEntregable,
            ResponsableId         = entity.ResponsableId,
            ResponsableNombre     = entity.ResponsableNombre,
            FechaInicio           = entity.FechaInicio,
            FechaVencimiento      = entity.FechaVencimiento,
            Clasificacion         = entity.Clasificacion,
            TipoPresupuesto       = entity.TipoPresupuesto,
            Peso                  = entity.Peso,
            Aclaraciones          = entity.Aclaraciones,
            Progreso              = entity.Progreso,
            PuntuacionPonderada   = entity.PuntuacionPonderada,
            Status                = entity.Status,
            AlertaEnviada         = entity.AlertaEnviada,
            Orden                 = entity.Orden,
            CreatedAt             = entity.CreatedAt,
            UpdatedAt             = entity.UpdatedAt
        };
    }

    public async Task<AccionPlanResponse> CrearAsync(Guid objetivoCgId, AccionPlanCreateRequest request, CancellationToken ct = default)
    {
        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(ObtenerTenantIdOThrow(), objetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await ValidarFechasCicloAsync(objetivo.CicloId, request.FechaInicio, request.FechaVencimiento, ct);

        var sumaPesos = await _repository.ObtenerSumaPesosAsync(objetivoCgId, ObtenerTenantIdOThrow(), ct);
        if (sumaPesos + request.Peso > 1.0m)
            throw new ValidacionException($"La suma de los pesos de las acciones excede el 100% (1.0). Peso acumulado actual: {sumaPesos}.");

        var maxOrden  = await _repository.ObtenerMaximoOrdenAsync(objetivoCgId, ObtenerTenantIdOThrow(), ct);
        var nuevoOrden = maxOrden + 1;
        var nuevoCodigo = $"{objetivo.Codigo}.{nuevoOrden:D2}";

        var entity = new AccionPlanEntity
        {
            TenantId              = ObtenerTenantIdOThrow(),
            CicloId               = objetivo.CicloId,
            AreaId                = objetivo.AreaId,
            ObjetivoCgId          = objetivoCgId,
            Codigo                = nuevoCodigo,
            Descripcion           = request.Descripcion,
            DescripcionEntregable = request.DescripcionEntregable,
            ResponsableId         = request.ResponsableId,
            FechaInicio           = request.FechaInicio,
            FechaVencimiento      = request.FechaVencimiento,
            Clasificacion         = request.Clasificacion,
            TipoPresupuesto       = request.TipoPresupuesto,
            Peso                  = request.Peso,
            Aclaraciones          = request.Aclaraciones,
            Progreso              = 0m,
            PuntuacionPonderada   = 0m,
            Status                = "NoIniciado",
            Orden                 = nuevoOrden
        };

        var inserted = await _repository.InsertAsync(entity, ct);
        return MapToResponse(inserted);
    }

    public async Task<AccionPlanResponse> ActualizarAsync(Guid id, AccionPlanUpdateRequest request, CancellationToken ct = default)
    {
        var entity = await _repository.ObtenerPorIdAsync(id, ObtenerTenantIdOThrow(), ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(ObtenerTenantIdOThrow(), entity.ObjetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await ValidarFechasCicloAsync(objetivo.CicloId, request.FechaInicio, request.FechaVencimiento, ct);

        var sumaPesosActual = await _repository.ObtenerSumaPesosAsync(entity.ObjetivoCgId, ObtenerTenantIdOThrow(), ct);
        var nuevaSuma = sumaPesosActual - entity.Peso + request.Peso;
        if (nuevaSuma > 1.0m)
            throw new ValidacionException($"La suma de los pesos de las acciones excede el 100% (1.0). Peso actual acumulado (sin esta acción): {sumaPesosActual - entity.Peso}.");

        entity.Descripcion           = request.Descripcion;
        entity.DescripcionEntregable = request.DescripcionEntregable;
        entity.ResponsableId         = request.ResponsableId;
        entity.FechaInicio           = request.FechaInicio;
        entity.FechaVencimiento      = request.FechaVencimiento;
        entity.Clasificacion         = request.Clasificacion;
        entity.TipoPresupuesto       = request.TipoPresupuesto;
        entity.Peso                  = request.Peso;
        entity.Aclaraciones          = request.Aclaraciones;
        entity.Status                = CalcularStatus(entity.Progreso, entity.FechaInicio, entity.FechaVencimiento);

        await _repository.UpdateAsync(entity, ct);
        return MapToResponse(entity);
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.ObtenerPorIdAsync(id, ObtenerTenantIdOThrow(), ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(ObtenerTenantIdOThrow(), entity.ObjetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await _repository.DeleteAsync(id, ObtenerTenantIdOThrow(), ct);
    }

    public async Task<AccionPlanResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.ObtenerPorIdAsync(id, ObtenerTenantIdOThrow(), ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        if (_tenantContext.Rol == "JefeArea" && entity.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No tiene permisos para ver esta acción.");

        return MapToResponse(entity);
    }

    public async Task<IEnumerable<AccionPlanResponse>> ListarPorObjetivoCgAsync(Guid objetivoCgId, CancellationToken ct = default)
    {
        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(ObtenerTenantIdOThrow(), objetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        if (_tenantContext.Rol == "JefeArea" && objetivo.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No tiene permisos para ver las acciones de este objetivo.");

        var entities = await _repository.ListarPorObjetivoCgAsync(objetivoCgId, ObtenerTenantIdOThrow(), ct);
        return entities.Select(MapToResponse);
    }

    // ─── HU-020 — Actualización de Progreso ──────────────────────────────────

    /// <summary>
    /// Actualiza el % de progreso, recalcula status (RN-017), puntuación ponderada,
    /// inserta historial (solo si cambió) y recalcula progreso + semáforo del CG (RN-018, F2).
    /// F3: idempotente — mismo progreso → 200 OK sin efectos secundarios.
    /// </summary>
    public async Task<AccionPlanResponse> ActualizarProgresoAsync(
        Guid accionId, ActualizarProgresoRequest request, CancellationToken ct = default)
    {
        // Validar rol (solo JefeArea)
        if (_tenantContext.Rol != "JefeArea")
            throw new AccesoDenegadoException("Solo un Jefe de Área puede actualizar el progreso de una acción.");

        var entity = await _repository.ObtenerPorIdAsync(accionId, _tenantContext.TenantId!.Value, ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        // SEC-07: verificar área
        if (entity.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No puede actualizar el progreso de una acción de otra área.");

        // F3: comportamiento idempotente
        if (entity.Progreso == request.Progreso)
            return MapToResponse(entity);

        var progresoAnterior = entity.Progreso;
        var statusAnterior   = entity.Status;

        // RN-017: calcular nuevo status
        var nuevoStatus     = CalcularStatus(request.Progreso, entity.FechaInicio, entity.FechaVencimiento);
        var nuevaPuntuacion = entity.Peso * (request.Progreso / 100m);

        entity.Progreso            = request.Progreso;
        entity.Status              = nuevoStatus;
        entity.PuntuacionPonderada = nuevaPuntuacion;
        entity.UpdatedAt           = DateTime.UtcNow;

        // 8a: persistir accion_plan
        await _repository.UpdateAsync(entity, ct);

        // 8b: insertar historial
        if (_historialRepository != null)
        {
            await _historialRepository.InsertAsync(new HistorialProgresoEntity
            {
                TenantId         = _tenantContext.TenantId!.Value,
                AccionId         = accionId,
                ProgresoAnterior = progresoAnterior,
                ProgresoNuevo    = request.Progreso,
                StatusAnterior   = statusAnterior,
                StatusNuevo      = nuevoStatus,
                RegistradoPor    = _tenantContext.UserId ?? Guid.Empty
            }, ct);
        }

        // 8c-8d: obtener umbrales del ciclo para recalcular semáforo del CG (F2)
        decimal umbralVerde    = 0.90m;
        decimal umbralAmarillo = 0.70m;
        var ciclo = await _cicloRepository.ObtenerPorIdAsync(_tenantContext.TenantId!.Value, entity.CicloId, ct);
        if (ciclo != null)
        {
            var umbrales   = (await _cicloRepository.ObtenerUmbralesAsync(_tenantContext.TenantId!.Value, ciclo.Id, ct)).ToList();
            var umbralPlan = umbrales.FirstOrDefault(u => u.Tipo == "PlanAccion");
            if (umbralPlan != null)
            {
                umbralVerde    = umbralPlan.UmbralVerde;
                umbralAmarillo = umbralPlan.UmbralAmarillo;
            }
        }

        // 8c-8d (spec HU-020): RN-018 recalcula el progreso del CG como la suma ponderada
        // de TODAS las acciones del objetivo (SUM(peso * progreso / 100.0)).
        var porcentajeCg = await _repository.ObtenerSumaPonderadaProgresoAsync(
            entity.ObjetivoCgId, _tenantContext.TenantId!.Value, ct);
        var semaforo     = SemaforoHelper.Evaluar(porcentajeCg / 100m, umbralVerde, umbralAmarillo);

        // 8e: UPDATE objetivo_cg progreso + semaforo
        await _objetivoCgRepository.RecalcularProgresoAsync(
            entity.ObjetivoCgId, porcentajeCg, semaforo, _tenantContext.TenantId!.Value, ct);

        return MapToResponse(entity);
    }

    /// <summary>Lista el historial de cambios de progreso de una acción (HU-020, F1: acceso JefeArea + Gerente).</summary>
    public async Task<IEnumerable<HistorialProgresoResponse>> ListarHistorialAsync(
        Guid accionId, CancellationToken ct = default)
    {
        var entity = await _repository.ObtenerPorIdAsync(accionId, _tenantContext.TenantId!.Value, ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        // SEC-07: solo JefeArea verifica área (Gerente puede leer historial de cualquier área)
        if (_tenantContext.Rol == "JefeArea" && entity.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No tiene permisos para ver el historial de esta acción.");

        if (_historialRepository == null)
            return Enumerable.Empty<HistorialProgresoResponse>();

        return await _historialRepository.ListarPorAccionAsync(accionId, _tenantContext.TenantId!.Value, ct);
    }

    // ─── HU-021 — Vista Gantt del Plan de Acción ─────────────────────────────

    /// <summary>
    /// Spec HU-021 § Lógica BLL, pasos 1-11. Endpoint de SOLO LECTURA: sin escrituras, sin
    /// auditoría (estructural — este servicio no depende de ILogAuditoriaRepository) y sin
    /// transacción. Devuelve el DTO plano; el wrapper ApiResponse&lt;T&gt; lo arma el controller
    /// de la API (ADR-010 Decisión 1).
    /// </summary>
    public async Task<GanttPlanResponse> ObtenerGanttAsync(CancellationToken ct = default)
    {
        // 1 · Tenant (SEC-06): del TenantContext (claims del JWT), nunca del request.
        var tenantId = ObtenerTenantIdOThrow();

        // 2 · Rol (RN-007/RN-006, doble capa): el atributo [Authorize(Roles="JefeArea,Gerente")]
        // ya lo bloquea en la API; la re-validación protege la invocación desde otra capa (D12 de HU-015).
        if (_tenantContext.Rol is not ("JefeArea" or "Gerente"))
            throw new AccesoDenegadoException("Solo el Jefe de Área y el Gerente pueden ver el Gantt del plan de acción.");

        // 3 · Filtro de área (SEC-07): JefeArea → su área; Gerente → null (RN-006, todas).
        // Un JefeArea sin area_id en el JWT es ACCESO DENEGADO, nunca "todas las áreas".
        Guid? areaIdFiltro = null;
        if (_tenantContext.Rol == "JefeArea")
        {
            if (_tenantContext.AreaId is null)
                throw new AccesoDenegadoException("No tiene permisos para ver el Gantt del plan de acción: el token no tiene área asignada.");

            areaIdFiltro = _tenantContext.AreaId.Value;
        }

        // 4 · Ciclo activo (RC-01): DAL-D1 de HU-015, reutilizado sin cambios.
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct)
            ?? throw new NotFoundException("No hay un ciclo activo para el tenant");

        // 5 · Datos: UNA sola query (DAL-G1). Sin paginación y sin filtros del cliente.
        // F11: el aislamiento por área vive SOLO en la DAL — la BLL devuelve exactamente lo
        // que recibe, sin un Where(...) en memoria que enmascararía un fallo del DAL.
        var filas = (await _repository.ListarParaGanttAsync(tenantId, ciclo.Id, areaIdFiltro, ct)).ToList();

        // 10 · Rango del ciclo (CicloFechaHelper.ObtenerRango devuelve DateOnly; conversión
        // explícita con TimeOnly.MinValue porque las fechas viajan a gantt.config.min/max_date).
        var (inicio, fin) = CicloFechaHelper.ObtenerRango(ciclo.AñoFiscal, ciclo.MesInicio);

        // 9 · Escala de exactamente 12 meses desde (año_fiscal, mes_inicio), cruzando de año
        // cuando mes_inicio ≠ 1 (por eso la etiqueta siempre lleva el año).
        var baseEscala = new DateTime(ciclo.AñoFiscal, ciclo.MesInicio, 1);
        var escala = new List<GanttMesResponse>(12);
        for (var i = 0; i < 12; i++)
        {
            var mes = baseEscala.AddMonths(i);
            escala.Add(new GanttMesResponse
            {
                Anio = mes.Year,
                Mes = mes.Month,
                Etiqueta = $"{AbreviaturasMeses[mes.Month - 1]} {mes.Year}"
            });
        }

        // 6-8 · Sin acciones NO es un error (D-C): 200 con Grupos/Acciones vacíos, la escala de
        // 12 meses y los 4 conteos en 0 — la vista decide el .empty-state (UX-05).
        var grupos = new List<GanttGrupoResponse>();
        var totalPorCg = new Dictionary<Guid, int>();
        var conteoPorStatus = new Dictionary<string, int>
        {
            ["NoIniciado"] = 0,
            ["EnProgreso"] = 0,
            ["Terminado"] = 0,
            ["Atrasado"] = 0
        };

        foreach (var fila in filas)
        {
            // 7 · Agrupación por Objetivo CG (CA #3), en el ORDEN que entrega DAL-G1
            // (oc.orden, oc.codigo): el primero que aparece de cada CG abre su grupo.
            if (!totalPorCg.TryGetValue(fila.ObjetivoCgId, out var accionesDelCg))
            {
                grupos.Add(new GanttGrupoResponse
                {
                    ObjetivoCgId = fila.ObjetivoCgId,
                    Codigo = fila.ObjetivoCodigo,
                    Descripcion = fila.ObjetivoDescripcion,
                    AreaId = fila.AreaId,
                    AreaCodigo = fila.AreaCodigo,
                    AreaNombre = fila.AreaNombre,
                    // Progreso y Semáforo del CG se LEEN tal cual (DB-04): no se recalculan.
                    Progreso = fila.ObjetivoProgreso,
                    Semaforo = fila.ObjetivoSemaforo,
                    TotalAcciones = 0
                });
                accionesDelCg = 0;
            }

            accionesDelCg++;
            totalPorCg[fila.ObjetivoCgId] = accionesDelCg;

            // 8 · Conteo por status (RF-031 parcial) sobre las filas ya cargadas (DB-04).
            conteoPorStatus[fila.Status] = conteoPorStatus.TryGetValue(fila.Status, out var previo)
                ? previo + 1
                : 1;
        }

        // D-K · TotalAcciones se cuenta en BLL (no hay COUNT en SQL: una sola query).
        foreach (var grupo in grupos)
            grupo.TotalAcciones = totalPorCg[grupo.ObjetivoCgId];

        // 11 · Mapeo 1:1 de la fila de DAL-G1. Status, Progreso y Peso se copian TAL CUAL
        // (D-M/F5: el Gantt NO reimplementa RN-017; el color es el mismo del listado de HU-019).
        var acciones = filas.Select(fila => new GanttAccionResponse
        {
            Id = fila.Id,
            ObjetivoCgId = fila.ObjetivoCgId,
            AreaId = fila.AreaId,
            Codigo = fila.Codigo,
            Descripcion = fila.Descripcion,
            FechaInicio = fila.FechaInicio,
            FechaVencimiento = fila.FechaVencimiento,
            Progreso = fila.Progreso,
            Status = fila.Status,
            Clasificacion = fila.Clasificacion,
            TipoPresupuesto = fila.TipoPresupuesto,
            Peso = fila.Peso,
            ResponsableNombre = fila.ResponsableNombre,
            Orden = fila.Orden
        }).ToList();

        return new GanttPlanResponse
        {
            CicloId = ciclo.Id,
            CicloNombre = ciclo.Nombre,
            AñoFiscal = ciclo.AñoFiscal,
            MesInicio = ciclo.MesInicio,
            FechaInicioCiclo = inicio.ToDateTime(TimeOnly.MinValue),
            FechaFinCiclo = fin.ToDateTime(TimeOnly.MinValue),
            Escala = escala,
            Grupos = grupos,
            Acciones = acciones,
            ConteoPorStatus = conteoPorStatus
        };
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>D17: TenantId null (SuperAdmin sin tenant) → 404 defensivo (los roles autorizados
    /// siempre tienen tenant_id; el SuperAdmin queda fuera por [Authorize(Roles=...)]).</summary>
    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("No se pudo determinar el tenant del usuario autenticado");
        return _tenantContext.TenantId.Value;
    }
}
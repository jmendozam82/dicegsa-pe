using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

public class AccionPlanService : IAccionPlanService
{
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
        var ciclo = await _cicloRepository.ObtenerPorIdAsync(cicloId, _tenantContext.TenantId.Value, ct)
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
        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await ValidarFechasCicloAsync(objetivo.CicloId, request.FechaInicio, request.FechaVencimiento, ct);

        var sumaPesos = await _repository.ObtenerSumaPesosAsync(objetivoCgId, _tenantContext.TenantId.Value, ct);
        if (sumaPesos + request.Peso > 1.0m)
            throw new ValidacionException($"La suma de los pesos de las acciones excede el 100% (1.0). Peso acumulado actual: {sumaPesos}.");

        var maxOrden  = await _repository.ObtenerMaximoOrdenAsync(objetivoCgId, _tenantContext.TenantId.Value, ct);
        var nuevoOrden = maxOrden + 1;
        var nuevoCodigo = $"{objetivo.Codigo}.{nuevoOrden:D2}";

        var entity = new AccionPlanEntity
        {
            TenantId              = _tenantContext.TenantId.Value,
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
        var entity = await _repository.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, entity.ObjetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await ValidarFechasCicloAsync(objetivo.CicloId, request.FechaInicio, request.FechaVencimiento, ct);

        var sumaPesosActual = await _repository.ObtenerSumaPesosAsync(entity.ObjetivoCgId, _tenantContext.TenantId.Value, ct);
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
        var entity = await _repository.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, entity.ObjetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        ValidarRolJefeArea(objetivo.AreaId);
        await _repository.DeleteAsync(id, _tenantContext.TenantId.Value, ct);
    }

    public async Task<AccionPlanResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.ObtenerPorIdAsync(id, _tenantContext.TenantId.Value, ct)
            ?? throw new NotFoundException("Acción no encontrada.");

        if (_tenantContext.Rol == "JefeArea" && entity.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No tiene permisos para ver esta acción.");

        return MapToResponse(entity);
    }

    public async Task<IEnumerable<AccionPlanResponse>> ListarPorObjetivoCgAsync(Guid objetivoCgId, CancellationToken ct = default)
    {
        var objetivo = await _objetivoCgRepository.ObtenerPorIdSinAreaAsync(_tenantContext.TenantId.Value, objetivoCgId)
            ?? throw new NotFoundException("Objetivo CG no encontrado.");

        if (_tenantContext.Rol == "JefeArea" && objetivo.AreaId != _tenantContext.AreaId)
            throw new AccesoDenegadoException("No tiene permisos para ver las acciones de este objetivo.");

        var entities = await _repository.ListarPorObjetivoCgAsync(objetivoCgId, _tenantContext.TenantId.Value, ct);
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
        var ciclo = await _cicloRepository.ObtenerPorIdAsync(entity.CicloId, _tenantContext.TenantId!.Value, ct);
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

        // El semáforo del CG se evalúa sobre el porcentaje ponderado de esta acción como indicativo.
        // El RecalcularProgresoAsync en la DAL hace el UPDATE con los parámetros ya calculados.
        // @BackendDev: verificar RLS (service_role) para que el subquery en BD sume todas las áreas (spec HU-020 Nota RLS).
        var semaforo    = SemaforoHelper.Evaluar(nuevaPuntuacion, umbralVerde, umbralAmarillo);
        var porcentajeCg = nuevaPuntuacion * 100m;

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
}
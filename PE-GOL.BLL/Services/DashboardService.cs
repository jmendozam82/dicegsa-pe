using Microsoft.Extensions.Logging;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Responses.Dashboard;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio del Tablero de Inicio del Jefe de Área — Spec HU-015 § Lógica BLL (pasos 1-11).
/// Implementación real (fase 4 del Loop); los 20 tests de @QA (DashboardServiceTests) son la
/// especificación ejecutable.
/// Flujo: D12 rol 'JefeArea' (403) → tenant (404) → DAL-D1 ciclo activo (CA #4, 404) → AreaId
/// del contexto (SEC-07, 404) → DAL-A2 área (404) → DAL-C10 umbrales (defaults 0.90/0.70 si
/// falta tipo, H5) → DAL-D3 OKRs → DAL-D4 plan → DAL-D5 acciones (recalculo atrasadas RN-017
/// regla 4 + días al vencimiento, D-D) → semáforos con SemaforoHelper (D-E) → 200 ApiResponse.
/// Sin auditoría (D-H: solo lectura) y sin transacción (solo lecturas).
/// Ctor aprobado por spec: (ICicloRepository, TenantContext) + overload con ILogger (D13,
/// patrón PilarService HU-013).
/// </summary>
public class DashboardService : IDashboardService
{
    private const string RolJefeArea = "JefeArea";

    /// <summary>Defaults del DDL L150-151 (HU-008 CA #4) — replicados de CicloService (H5):
    /// si el ciclo no tiene fila de un tipo en umbral_semaforo, se usan 0.90/0.70 en memoria.</summary>
    private const decimal UmbralVerdeDefault = 0.90m;
    private const decimal UmbralAmarilloDefault = 0.70m;

    private readonly ICicloRepository _repository;
    private readonly TenantContext _tenantContext; // D17: TenantId nullable; D12: re-validación de rol
    private readonly ILogger<DashboardService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public DashboardService(ICicloRepository repository, TenantContext tenantContext)
        : this(repository, tenantContext, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public DashboardService(ICicloRepository repository, TenantContext tenantContext, ILogger<DashboardService>? logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Spec § Lógica BLL pasos 1-11 · ObtenerTableroJefeAreaAsync: 200 con el tablero del
    /// área del JEF para el ciclo activo del tenant. 403 rol ≠ JefeArea (D12/D-F) · 404 (sin
    /// tenant / sin ciclo activo CA #4 / JEF sin área / área inexistente). SEC-07: DAL-D3/D4/D5
    /// filtran por TenantContext.AreaId. Sin auditoría (D-H) y sin transacción (solo lecturas).</summary>
    public async Task<ApiResponse<TableroJefeAreaResponse>> ObtenerTableroJefeAreaAsync(CancellationToken ct = default)
    {
        // Paso 1 · D12: re-validación defensiva de rol (D-F: solo el JEF tiene tablero en esta HU;
        // el [Authorize(Roles)] del controller es la primera capa; la BLL es la fuente de verdad).
        if (!string.Equals(_tenantContext.Rol, RolJefeArea, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el Jefe de Área tiene tablero de inicio");

        // Paso 2 · D17: TenantId null (SuperAdmin sin tenant) → 404 defensivo.
        var tenantId = ObtenerTenantIdOThrow();

        // Paso 3 · CA #4: ciclo activo resuelto en BLL vía DAL-D1 (D-G). RC-01 garantiza máx 1 fila.
        var ciclo = await _repository.ObtenerCicloActivoAsync(tenantId, ct)
            ?? throw new NotFoundException("No hay un ciclo activo para el tenant");

        // Paso 4 · SEC-07: el JEF sin área asignada no puede tener tablero (no hay área que filtrar).
        var areaId = _tenantContext.AreaId
            ?? throw new NotFoundException("El usuario no tiene un área asignada");

        // Paso 5 · DAL-A2 reutilizada: 404 si el área no existe o es de otro tenant (sin fuga SEC-06).
        var area = await _repository.ObtenerAreaPorIdAsync(tenantId, ciclo.Id, areaId, ct)
            ?? throw new NotFoundException($"El área '{areaId}' no existe");

        // Paso 6 · DAL-C10 reutilizada: umbrales por tipo (KPI / PlanAccion). Defensivo H5: si un
        // tipo no tiene fila, se usan los defaults 0.90/0.70 EN MEMORIA (patrón HU-008 D5).
        var umbrales = (await _repository.ObtenerUmbralesAsync(tenantId, ciclo.Id, ct)).ToList();
        var porTipo = umbrales.ToDictionary(u => u.Tipo, u => u);
        var umbralKpi = porTipo.GetValueOrDefault("KPI");
        var umbralPlanAccion = porTipo.GetValueOrDefault("PlanAccion");
        var umbralKpiVerde = umbralKpi?.UmbralVerde ?? UmbralVerdeDefault;
        var umbralKpiAmarillo = umbralKpi?.UmbralAmarillo ?? UmbralAmarilloDefault;
        var umbralPlanVerde = umbralPlanAccion?.UmbralVerde ?? UmbralVerdeDefault;
        var umbralPlanAmarillo = umbralPlanAccion?.UmbralAmarillo ?? UmbralAmarilloDefault;

        // Pasos 7-8 · DAL-D3/D4: leen campos calculados PERSISTIDOS (D-C — mantenidos por las BLL
        // de HU-017+/HU-024+, DB-04; el tablero no duplica lógica de puntuación de módulos futuros).
        var resumenOkrs = await _repository.ObtenerResumenOkrsAreaAsync(tenantId, ciclo.Id, areaId, ct);
        var resumenPlan = await _repository.ObtenerResumenPlanAccionAreaAsync(tenantId, ciclo.Id, areaId, ct);

        // Paso 9 · DAL-D5 + recalculo en BLL (D-D): el status persistido puede estar desactualizado
        // (depende del batch nocturno RN-040) → atrasadas y días al vencimiento se calculan con la
        // fecha actual (RN-017 regla 4: vencida sin terminar = DateTime.Today > FechaVencimiento.Date
        // && Progreso < 100).
        // Días al vencimiento más cercano (CA #1): min sobre pendientes (Progreso < 100) con
        // vencimiento FUTURO (>= hoy); si todas las pendientes están vencidas, la vencida más
        // reciente (menos negativa = más cercana a hoy); null si no hay pendientes. Negativo =
        // vencida hace N días. (Los tests 10/11/12/17 de @QA fijan esta semántica: la atrasada se
        // excluye del min cuando existen pendientes futuras — caso 17 espera 3 con [-1, +3].)
        var acciones = (await _repository.ListarAccionesAreaAsync(tenantId, ciclo.Id, areaId, ct)).ToList();
        var accionesAtrasadas = acciones.Count(a => DateTime.Today > a.FechaVencimiento.Date && a.Progreso < 100);
        var pendientes = acciones.Where(a => a.Progreso < 100).ToList();
        int? diasAlVencimiento = null;
        if (pendientes.Count > 0)
        {
            var pendientesFuturas = pendientes
                .Where(a => (a.FechaVencimiento.Date - DateTime.Today).Days >= 0)
                .ToList();
            diasAlVencimiento = pendientesFuturas.Count > 0
                ? pendientesFuturas.Min(a => (a.FechaVencimiento.Date - DateTime.Today).Days)
                : pendientes.Max(a => (a.FechaVencimiento.Date - DateTime.Today).Days);
        }

        // Paso 10 · Semáforos (BLL, DB-04; D-E): OKRs contra umbrales KPI, plan contra umbrales
        // PlanAccion, global = promedio numérico (OKRs + plan) / 2 contra umbrales KPI (F2 de Jorge).
        var semaforoOkrs = SemaforoHelper.Evaluar(resumenOkrs.PromedioPuntuacionOkrs, umbralKpiVerde, umbralKpiAmarillo);
        var semaforoPlan = SemaforoHelper.Evaluar(resumenPlan.AvancePlanAccion, umbralPlanVerde, umbralPlanAmarillo);
        var promedioGlobal = (resumenOkrs.PromedioPuntuacionOkrs + resumenPlan.AvancePlanAccion) / 2;
        var semaforoGlobal = SemaforoHelper.Evaluar(promedioGlobal, umbralKpiVerde, umbralKpiAmarillo);

        // Paso 11 · Mapear y responder (ARCH-07: 200 con ApiResponse<TableroJefeAreaResponse>).
        var tablero = new TableroJefeAreaResponse
        {
            CicloId = ciclo.Id,
            CicloNombre = ciclo.Nombre,
            AñoFiscal = ciclo.AñoFiscal,
            AreaId = area.Id,
            AreaCodigo = area.Codigo,
            AreaNombre = area.Nombre,
            Tarjetas = new TarjetasResumenResponse
            {
                TotalOkrs = resumenOkrs.TotalOkrs,
                OkrsAlcanzados = resumenOkrs.OkrsAlcanzados,
                AvancePlanAccion = resumenPlan.AvancePlanAccion,
                AccionesAtrasadas = accionesAtrasadas,
                DiasAlVencimientoMasCercano = diasAlVencimiento
            },
            Semaforo = new SemaforoAreaResponse
            {
                Okrs = semaforoOkrs,
                PlanAccion = semaforoPlan,
                Global = semaforoGlobal,
                PromedioPuntuacionOkrs = resumenOkrs.PromedioPuntuacionOkrs
            }
        };

        _logger?.LogInformation(
            "Tablero del Jefe de Área {AreaId} consultado (ciclo {CicloId}). Modulo=Dashboard, Accion=READ",
            areaId, ciclo.Id);

        return new ApiResponse<TableroJefeAreaResponse> { Success = true, Data = tablero };
    }

    /// <summary>D17: TenantId null (SuperAdmin sin tenant) → 404 defensivo (el SuperAdmin queda
    /// fuera por [Authorize(Roles = "JefeArea")]; los roles autorizados siempre tienen tenant_id).</summary>
    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("No se pudo determinar el tenant del usuario autenticado");
        return _tenantContext.TenantId.Value;
    }
}
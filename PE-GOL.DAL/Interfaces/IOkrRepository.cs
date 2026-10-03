using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para OKRs (Spec HU-024 § Queries DAL).
/// Toda query incluye WHERE tenant_id = @TenantId (SEC-06) y, al ser entidad de área,
/// AND area_id = @AreaId (SEC-07) — incluidas UPDATE y DELETE (refuerzo deliberado).
/// </summary>
public interface IOkrRepository
{
    /// <summary>Query 1 · Listar OKRs del área en el ciclo activo con JOIN pilar (CA #4).</summary>
    Task<IEnumerable<OkrResponse>> ListarAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>Query 2 · Obtener OKR por id (del área del JEF) con JOIN pilar.</summary>
    Task<OkrResponse?> ObtenerPorIdAsync(Guid tenantId, Guid areaId, Guid id, CancellationToken ct = default);

    /// <summary>Query 3 · Contar OKRs del área en el ciclo (CA #5).</summary>
    Task<int> ContarOkrsPorAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>Query 4 · Siguiente código OKR.N y orden por área (CA #1, F4).</summary>
    Task<SiguienteSecuenciaOkrDto> ObtenerSiguienteSecuenciaOkrAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>Query 5 · Verificar si algún KR del OKR tiene valores reales registrados (CA #3 / RN-028).</summary>
    Task<bool> VerificarKrsConValoresAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>Query 6 · INSERT OKR con puntuacion_final=0.000 y semaforo='Rojo' (F3).</summary>
    Task<Guid> CrearAsync(Guid tenantId, Guid cicloId, Guid areaId, string codigo, int orden, OkrCreateRequest request, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 7 · UPDATE solo pilar_id/descripcion/updated_at (CA #1, DB-04).</summary>
    Task ActualizarAsync(Guid tenantId, Guid areaId, Guid id, OkrUpdateRequest request, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 8 · DELETE físico del OKR (los KRs caen por ON DELETE CASCADE, F2).</summary>
    Task EliminarAsync(Guid tenantId, Guid areaId, Guid id, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 9 · INSERT log_auditoria (ADR-003).</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction.</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    // ─── Extensión para registro mensual de valores (HU-026) ────────────────────────────────────
    /// <summary>DAL-5 (HU-026) · UPDATE okr.puntuacion_final y okr.semaforo (RF-038 / RN-027).
    /// El gate area_id va también en el UPDATE (SEC-07: entidad de área).</summary>
    Task<int> ActualizarPuntuacionOkrAsync(Guid tenantId, Guid areaId, Guid okrId, decimal puntuacionFinal, string semaforo, IDbTransaction? tx = null, CancellationToken ct = default);

    // ─── Extensiones para consolidado (HU-027) ──────────────────────────────────────────────────
    /// <summary>Listado consolidado de OKRs del área (precedente para futura vista GER).</summary>
    Task<IEnumerable<OkrResponse>> ListarConsolidadoAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>Conteo consolidado de OKRs del área (precedente para futura vista GER).</summary>
    Task<int> CountAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);
}

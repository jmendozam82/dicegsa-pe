using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Ciclo;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para ciclo (Spec HU-007 § Queries DAL: DAL-C1 a DAL-C11).
/// Toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06); el
/// @TenantId proviene del TenantContext (JWT), nunca del body/query. NO aplica AND area_id
/// (SEC-07): ciclo y umbral_semaforo no son entidades de área (RN-007: JefeArea solo lectura).
/// Las operaciones de escritura (INSERT/UPDATE/estado/umbrales + auditoría) se ejecutan en
/// UNA sola transacción IDbTransaction gestionada por la BLL (patrón HU-001/HU-003).
/// </summary>
public interface ICicloRepository
{
    /// <summary>DAL-C1 · INSERT ciclo (estado 'Borrador') ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertAsync(CicloInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-C2 · SELECT por id aislado por tenant. Retorna null si no existe o pertenece a otro tenant (sin fuga).</summary>
    Task<CicloEntity?> ObtenerPorIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>DAL-C3 · SELECT listado del tenant, ORDER BY año_fiscal DESC (sin paginación, D3).</summary>
    Task<IEnumerable<CicloEntity>> ListarAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-C4 · UPDATE datos generales (nombre/año_fiscal/mes_inicio). Retorna filas afectadas.</summary>
    Task<int> UpdateAsync(CicloUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-C5 · UPDATE estado (activar/cerrar) con activated_at/closed_at. Retorna filas afectadas.</summary>
    Task<int> UpdateEstadoAsync(Guid tenantId, Guid id, string estado, DateTimeOffset? activatedAt, DateTimeOffset? closedAt, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-C6 · unicidad de año fiscal (CA #2). excludeId excluye el propio ciclo en UPDATE.</summary>
    Task<bool> ExisteAñoFiscalAsync(Guid tenantId, int añoFiscal, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>DAL-C7 · COUNT ciclos activos del tenant (RC-01), excluyendo el ciclo que se activa.</summary>
    Task<int> ContarCiclosActivosAsync(Guid tenantId, Guid excludeId, CancellationToken ct = default);

    /// <summary>DAL-C8 · plan_id del tenant (para IPlanService.ValidarLimitesParaTenantAsync, D-C). Retorna null si el tenant no existe.</summary>
    Task<Guid?> ObtenerPlanIdDelTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-C9 · INSERT umbral_semaforo (defaults al crear / copiados al clonar). Retorna filas afectadas.</summary>
    Task<int> InsertarUmbralAsync(UmbralSemaforoDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-C10 · SELECT umbrales del ciclo origen (para clonar).</summary>
    Task<IEnumerable<UmbralSemaforoEntity>> ObtenerUmbralesAsync(Guid tenantId, Guid origenId, CancellationToken ct = default);

    /// <summary>DAL-C11 · INSERT log_auditoria (entidad 'Ciclo'). El JSON se serializa con UnsafeRelaxedJsonEscaping (ADR-003). Retorna filas afectadas.</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-U2 (HU-008) · UPSERT umbral_semaforo: INSERT ... ON CONFLICT (ciclo_id, tipo) DO UPDATE
    /// (target = UNIQUE del DDL L154; espíritu DB-06, D4). Actualiza los defaults de HU-007 sin duplicar
    /// (idempotente, sin carrera TOCTOU ni 23505). Retorna filas afectadas.</summary>
    Task<int> UpsertUmbralAsync(UmbralSemaforoDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction (mismo patrón que ITenantRepository).</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para ciclo (Spec HU-007 § Queries DAL: DAL-C1 a DAL-C11;
/// Spec HU-009 § Queries DAL: DAL-A1 a DAL-A11 — áreas, hijos del agregado Ciclo, D-I).
/// Toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06); el
/// @TenantId proviene del TenantContext (JWT), nunca del body/query. NO aplica AND area_id
/// (SEC-07) para ciclo/umbral_semaforo (no son entidades de área); SÍ aplica para area
/// (DAL-A2/A3 con areaIdFiltro desde TenantContext.AreaId cuando el rol es JefeArea).
/// Las operaciones de escritura (INSERT/UPDATE/estado/umbrales/áreas + auditoría) se ejecutan
/// en UNA sola transacción IDbTransaction gestionada por la BLL (patrón HU-001/HU-003).
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

    // ─── HU-009 · Áreas estratégicas (Spec HU-009 § Queries DAL: DAL-A1 a DAL-A11) ───
    // Las áreas son hijos del agregado Ciclo (D-I): se extiende ICicloRepository, no se crea
    // repositorio nuevo (mismo criterio que UmbralSemaforo en HU-008). SEC-07: area SÍ es
    // entidad de área → AND id = @AreaId cuando el rol es JefeArea (areaIdFiltro).

    /// <summary>DAL-A1 · INSERT area ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertarAreaAsync(AreaInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A2 · SELECT area por id (aislada por tenant y ciclo). Retorna null si no existe o es de otro tenant (sin fuga).</summary>
    Task<AreaEntity?> ObtenerAreaPorIdAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>DAL-A3 · SELECT áreas del ciclo, ORDER BY orden ASC. areaIdFiltro (SEC-07): si no es null,
    /// AND id = @AreaIdFiltro (JefeArea solo su área). Sin filtro (null) → todas (ADM/GER y clonación).</summary>
    Task<IEnumerable<AreaEntity>> ListarAreasAsync(Guid tenantId, Guid cicloId, Guid? areaIdFiltro = null, CancellationToken ct = default);

    /// <summary>DAL-A4 · UPDATE area (nombre/comentarios/responsable_id, updated_at = NOW()). Retorna filas afectadas.</summary>
    Task<int> ActualizarAreaAsync(AreaUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A5 · UPDATE area SET activa = FALSE, updated_at = NOW() (sin DELETE — CA #5, D-E). Retorna filas afectadas.</summary>
    Task<int> DesactivarAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-A6 · COUNT áreas ACTIVAS del ciclo (RN-010, chequeo preciso por ciclo — D-D).</summary>
    Task<int> ContarAreasActivasEnCicloAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default);

    /// <summary>DAL-A7 · MAX(orden)+1 sobre TODAS las áreas del ciclo (incl. inactivas → sin reutilizar
    /// códigos tras desactivar). Retorna el siguiente orden/código GOL (CA #1, DB-04).</summary>
    Task<int> ObtenerSiguienteOrdenAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default);

    /// <summary>DAL-A8 · COUNT áreas ACTIVAS del ciclo con ese responsable (RN-012). excludeAreaId excluye
    /// self en UPDATE (DAL-A8 con excludeAreaId = areaId).</summary>
    Task<int> ContarAreasConResponsableAsync(Guid tenantId, Guid cicloId, Guid responsableId, Guid? excludeAreaId, CancellationToken ct = default);

    /// <summary>DAL-A9 · SELECT usuario por id del tenant (id, nombre, correo, rol, estado). La BLL valida
    /// rol = 'JefeArea' y estado = 'Activo' (RN-011, CA #2). Retorna null si no existe o es de otro tenant.</summary>
    Task<UsuarioEntity?> ObtenerResponsableAsync(Guid tenantId, Guid responsableId, CancellationToken ct = default);

    /// <summary>DAL-A10 · SELECT candidatos a responsable: usuarios del tenant con rol JefeArea y estado
    /// Activo, con ya_asignado = EXISTS(área ACTIVA del ciclo con ese responsable) (RN-012). ORDER BY nombre.</summary>
    Task<IEnumerable<ResponsableCandidatoResponse>> ListarResponsablesCandidatosAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default);

    /// <summary>DAL-A11 · UPDATE usuario SET area_id = @AreaId, updated_at = NOW() (sync SEC-07, D-J).
    /// @AreaId null libera al responsable anterior (evita fuga de acceso SEC-07). Retorna filas afectadas.</summary>
    Task<int> ActualizarAreaIdUsuarioAsync(Guid usuarioId, Guid? areaId, IDbTransaction? tx = null, CancellationToken ct = default);

    // ─── HU-010 · Responsables (Spec HU-010 § Queries DAL: DAL-R1 a DAL-R7) ───
    // Los responsables son hijos del agregado Ciclo (D-I): se extiende ICicloRepository, no se
    // crea repositorio nuevo (mismo criterio que Area/UmbralSemaforo). Responsable = usuario del
    // tenant con rol='JefeArea' (D-B). SEC-07: responsable SÍ es entidad de área (vinculada a
    // area.responsable_id) → AND a.id = @AreaIdFiltro cuando el rol es JefeArea (DAL-R3).
    // Observación de Jorge (DAL-R2/R3): LEFT JOIN area con la condición del ciclo en el JOIN
    // (ON a.id = u.area_id AND a.ciclo_id = @CicloId), NO en el WHERE — un usuario cuyo area_id
    // apunta a un área de otro ciclo se devuelve con area_codigo/area_nombre null (no se pierde).

    /// <summary>DAL-R1 · INSERT usuario (rol JefeArea, estado Activo, requiere_cambio_pwd TRUE) ...
    /// RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    Task<Guid?> InsertarUsuarioAsync(ResponsableInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-R2 · SELECT responsable por id (aislado por tenant; rol JefeArea). LEFT JOIN
    /// area con la condición del ciclo en el JOIN (observación de Jorge). Retorna null si no existe
    /// o es de otro tenant (sin fuga).</summary>
    Task<ResponsableEntity?> ObtenerResponsablePorIdAsync(Guid tenantId, Guid cicloId, Guid responsableId, CancellationToken ct = default);

    /// <summary>DAL-R3 · SELECT responsables del ciclo (rol JefeArea), ORDER BY nombre. areaIdFiltro
    /// (SEC-07): si no es null, AND a.id = @AreaIdFiltro (JefeArea solo su responsable). Sin filtro
    /// (null) → todos (ADM/GER). LEFT JOIN area con la condición del ciclo en el JOIN.</summary>
    Task<List<ResponsableEntity>> ListarResponsablesAsync(Guid tenantId, Guid cicloId, Guid? areaIdFiltro = null, CancellationToken ct = default);

    /// <summary>DAL-R4 · ¿Existe un usuario con ese correo en el tenant? (case-insensitive, LOWER).</summary>
    Task<bool> ExisteCorreoEnTenantAsync(Guid tenantId, string correo, CancellationToken ct = default);

    /// <summary>DAL-R5 · COUNT usuarios ACTIVOS del tenant (RN-010, chequeo preciso por tenant — D-D).</summary>
    Task<int> ContarUsuariosActivosEnTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>DAL-R6 · UPDATE area SET responsable_id = @ResponsableId, updated_at = NOW()
    /// (aislada por tenant y ciclo). @ResponsableId null libera el área (queda sin responsable).</summary>
    Task AsignarResponsableAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, Guid? responsableId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-R7 · UPDATE usuario SET estado = 'Inactivo', updated_at = NOW() (desactivar responsable).</summary>
    Task DesactivarUsuarioAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction (mismo patrón que ITenantRepository).</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
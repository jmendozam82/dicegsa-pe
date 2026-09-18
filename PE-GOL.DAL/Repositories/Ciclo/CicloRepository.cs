using System.Data;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.Estrategia;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Repositories.Ciclo;

/// <summary>
/// Repositorio de ciclos anuales con Dapper (Spec HU-007 § Queries DAL: DAL-C1 a DAL-C11).
/// Toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06); el
/// @TenantId proviene del TenantContext (JWT), nunca del body/query. NO aplica AND area_id
/// (SEC-07): ciclo y umbral_semaforo no son entidades de área. RLS habilitado en ambas tablas
/// (06 L464-465) — doble capa con el filtro del DAL.
/// Todas las queries usan parámetros nombrados (SEC-05); prohibido concatenar SQL.
/// El CancellationToken se propaga vía CommandDefinition (forma canónica de Dapper).
/// </summary>
public sealed class CicloRepository : ICicloRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public CicloRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Abre una conexión gestionada por el repositorio e inicia la transacción IDbTransaction
    /// (spec HU-007 § Lógica BLL: INSERT/UPDATE/umbrales + auditoría en una sola transacción).
    /// La conexión queda asociada a la transacción y se libera con commit/rollback/dispose de la
    /// transacción o al dispose del repositorio (registrado Scoped en IOC, fin del request HTTP).
    /// </summary>
    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Libera cualquier conexión previa que hubiera quedado asociada sin cerrar.
        _connectionActiva?.Dispose();

        var conn = _factory.CreateConnection();
        try
        {
            conn.Open();
            var tx = conn.BeginTransaction();
            _connectionActiva = conn;
            return Task.FromResult(tx);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>Libera la conexión activa al finalizar el scope DI (repositorio Scoped).</summary>
    public void Dispose()
    {
        _connectionActiva?.Dispose();
        _connectionActiva = null;
    }

    /// <summary>DAL-C1 · INSERT ciclo (estado Borrador) ... RETURNING id, created_at, updated_at.</summary>
    public async Task<Guid?> InsertAsync(CicloInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO ciclo (tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by)
            VALUES (@TenantId, @Nombre, @AñoFiscal, @MesInicio, 'Borrador', @CreatedBy)
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteScalarAsync<Guid>(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid>(cmd);
    }

    /// <summary>DAL-C2 · SELECT por id (aislado por tenant). Retorna null si no existe o es de otro tenant.</summary>
    public async Task<CicloEntity?> ObtenerPorIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by,
                   activated_at, closed_at, created_at, updated_at
            FROM ciclo
            WHERE tenant_id = @TenantId
              AND id = @Id;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<CicloEntity>(cmd);
    }

    /// <summary>DAL-C3 · SELECT listado del tenant (orden por año fiscal DESC, sin paginación — D3).</summary>
    public async Task<IEnumerable<CicloEntity>> ListarAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, nombre, año_fiscal, mes_inicio, estado, created_by,
                   activated_at, closed_at, created_at, updated_at
            FROM ciclo
            WHERE tenant_id = @TenantId
            ORDER BY año_fiscal DESC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.QueryAsync<CicloEntity>(cmd);
    }

    /// <summary>DAL-C4 · UPDATE datos generales (solo Borrador — la BLL valida el estado antes). Retorna filas afectadas.</summary>
    public async Task<int> UpdateAsync(CicloUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE ciclo
            SET nombre      = @Nombre,
                año_fiscal  = @AñoFiscal,
                mes_inicio  = @MesInicio,
                updated_at  = NOW()
            WHERE tenant_id = @TenantId
              AND id = @Id
            RETURNING id, created_at, updated_at;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C5 · UPDATE estado (activar / cerrar). Retorna filas afectadas.</summary>
    public async Task<int> UpdateEstadoAsync(Guid tenantId, Guid id, string estado, DateTimeOffset? activatedAt, DateTimeOffset? closedAt, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE ciclo
            SET estado       = @Estado::estado_ciclo,
                activated_at = @ActivatedAt,
                closed_at    = @ClosedAt,
                updated_at   = NOW()
            WHERE tenant_id = @TenantId
              AND id = @Id
            RETURNING id, created_at, updated_at;";

        var parametros = new
        {
            TenantId = tenantId,
            Id = id,
            Estado = estado,
            ActivatedAt = activatedAt,
            ClosedAt = closedAt
        };

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, parametros, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C6 · Validación de unicidad de año fiscal (CA #2). excludeId excluye self en UPDATE.</summary>
    public async Task<bool> ExisteAñoFiscalAsync(Guid tenantId, int añoFiscal, Guid? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM ciclo
            WHERE tenant_id = @TenantId
              AND año_fiscal = @AñoFiscal
              AND (@ExcludeId IS NULL OR id <> @ExcludeId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, AñoFiscal = añoFiscal, ExcludeId = excludeId },
            cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-C7 · COUNT ciclos activos (RC-01 — solo un Activo por tenant). Excluye el ciclo que se activa.</summary>
    public async Task<int> ContarCiclosActivosAsync(Guid tenantId, Guid excludeId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM ciclo
            WHERE tenant_id = @TenantId
              AND estado = 'Activo'::estado_ciclo
              AND id <> @ExcludeId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, ExcludeId = excludeId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-C8 · plan_id del tenant (para ValidarLimitesParaTenantAsync, D-C). Retorna null si el tenant no existe.</summary>
    public async Task<Guid?> ObtenerPlanIdDelTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT plan_id FROM tenant WHERE id = @TenantId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid?>(cmd);
    }

    /// <summary>DAL-C9 · INSERT umbral_semaforo (defaults al crear / copiados al clonar). Retorna filas afectadas.</summary>
    public async Task<int> InsertarUmbralAsync(UmbralSemaforoDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO umbral_semaforo (ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo)
            VALUES (@CicloId, @TenantId, @Tipo::tipo_umbral, @UmbralVerde, @UmbralAmarillo);";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-C10 · SELECT umbrales del ciclo origen (para clonar).</summary>
    public async Task<IEnumerable<UmbralSemaforoEntity>> ObtenerUmbralesAsync(Guid tenantId, Guid origenId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo, updated_at
            FROM umbral_semaforo
            WHERE tenant_id = @TenantId
              AND ciclo_id = @OrigenId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, OrigenId = origenId }, cancellationToken: ct);
        return await conn.QueryAsync<UmbralSemaforoEntity>(cmd);
    }

    /// <summary>DAL-C11 · INSERT log_auditoria (entidad 'Ciclo'). Retorna filas afectadas.</summary>
    public async Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO log_auditoria (tenant_id, usuario_id, accion, entidad, entidad_id,
                                       valor_anterior, valor_nuevo)
            VALUES (@TenantId, @UsuarioId, @Accion, @Entidad, @EntidadId,
                    @ValorAnterior::jsonb, @ValorNuevo::jsonb);";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-U2 (HU-008) · UPSERT umbral_semaforo: INSERT ... ON CONFLICT (ciclo_id, tipo)
    /// DO UPDATE (target = UNIQUE del DDL L154; espíritu DB-06, D4). Idempotente: actualiza los
    /// defaults de HU-007 sin duplicar ni colisionar 23505; elimina la carrera TOCTOU del patrón
    /// SELECT+UPDATE. @Tipo::tipo_umbral cast al enum del DDL (L36). Retorna filas afectadas.</summary>
    public async Task<int> UpsertUmbralAsync(UmbralSemaforoDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO umbral_semaforo (ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo, updated_at)
            VALUES (@CicloId, @TenantId, @Tipo::tipo_umbral, @UmbralVerde, @UmbralAmarillo, NOW())
            ON CONFLICT (ciclo_id, tipo) DO UPDATE
            SET umbral_verde    = EXCLUDED.umbral_verde,
                umbral_amarillo = EXCLUDED.umbral_amarillo,
                updated_at      = NOW();";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    // ─── HU-009 · Áreas estratégicas (Spec HU-009 § Queries DAL: DAL-A1 a DAL-A11) ───
    // Implementación real (fase IMPLEMENT). Las áreas son hijos del agregado Ciclo (D-I):
    // toda query incluye WHERE tenant_id = @TenantId (SEC-06) y, cuando el rol es JefeArea,
    // AND id = @AreaIdFiltro (SEC-07 — area SÍ es entidad de área). Parámetros nombrados
    // (SEC-05) y CancellationToken vía CommandDefinition (forma canónica de Dapper).

    /// <summary>DAL-A1 · INSERT area ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    public async Task<Guid?> InsertarAreaAsync(AreaInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO area (tenant_id, ciclo_id, codigo, nombre, comentarios, responsable_id, orden, activa)
            VALUES (@TenantId, @CicloId, @Codigo, @Nombre, @Comentarios, @ResponsableId, @Orden, @Activa)
            RETURNING id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteScalarAsync<Guid>(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<Guid>(cmd);
    }

    /// <summary>DAL-A2 · SELECT area por id (aislada por tenant y ciclo). LEFT JOIN usuario para
    /// responsable_nombre/correo. Retorna null si no existe o es de otro tenant (sin fuga).</summary>
    public async Task<AreaEntity?> ObtenerAreaPorIdAsync(Guid tenantId, Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT a.id, a.tenant_id, a.ciclo_id, a.codigo, a.nombre, a.comentarios, a.responsable_id,
                   u.nombre AS responsable_nombre, u.correo AS responsable_correo,
                   a.orden, a.activa, a.created_at, a.updated_at
            FROM area a
            LEFT JOIN usuario u ON u.id = a.responsable_id
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id = @CicloId
              AND a.id = @AreaId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId },
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<AreaEntity>(cmd);
    }

    /// <summary>DAL-A3 · SELECT áreas del ciclo, ORDER BY orden ASC. areaIdFiltro (SEC-07): si no es
    /// null, AND id = @AreaIdFiltro (JefeArea solo su área). Sin filtro (null) → todas (ADM/GER y clonación).</summary>
    public async Task<IEnumerable<AreaEntity>> ListarAreasAsync(Guid tenantId, Guid cicloId, Guid? areaIdFiltro = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT a.id, a.tenant_id, a.ciclo_id, a.codigo, a.nombre, a.comentarios, a.responsable_id,
                   u.nombre AS responsable_nombre, u.correo AS responsable_correo,
                   a.orden, a.activa, a.created_at, a.updated_at
            FROM area a
            LEFT JOIN usuario u ON u.id = a.responsable_id
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id = @CicloId
              AND (@AreaIdFiltro IS NULL OR a.id = @AreaIdFiltro)
            ORDER BY a.orden ASC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaIdFiltro = areaIdFiltro },
            cancellationToken: ct);
        return await conn.QueryAsync<AreaEntity>(cmd);
    }

    /// <summary>DAL-A4 · UPDATE area (nombre/comentarios/responsable_id, updated_at = NOW()). Retorna filas afectadas.</summary>
    public async Task<int> ActualizarAreaAsync(AreaUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE area
            SET nombre         = @Nombre,
                comentarios    = @Comentarios,
                responsable_id = @ResponsableId,
                updated_at     = NOW()
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @Id;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, dto, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, dto, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-A5 · UPDATE area SET activa = FALSE, updated_at = NOW() (sin DELETE — CA #5, D-E). Retorna filas afectadas.</summary>
    public async Task<int> DesactivarAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE area
            SET activa     = FALSE,
                updated_at = NOW()
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @AreaId;";

        var parametros = new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId };

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, parametros, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-A6 · COUNT áreas ACTIVAS del ciclo (RN-010, chequeo preciso por ciclo — D-D).</summary>
    public async Task<int> ContarAreasActivasEnCicloAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM area
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND activa = TRUE;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-A7 · MAX(orden)+1 sobre TODAS las áreas del ciclo (incl. inactivas → sin reutilizar
    /// códigos tras desactivar). Retorna el siguiente orden/código GOL (CA #1, DB-04).</summary>
    public async Task<int> ObtenerSiguienteOrdenAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(MAX(orden), 0) + 1
            FROM area
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-A8 · COUNT áreas ACTIVAS del ciclo con ese responsable (RN-012). excludeAreaId
    /// excluye self en UPDATE (DAL-A8 con excludeAreaId = areaId).</summary>
    public async Task<int> ContarAreasConResponsableAsync(Guid tenantId, Guid cicloId, Guid responsableId, Guid? excludeAreaId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM area
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND responsable_id = @ResponsableId
              AND activa = TRUE
              AND (@ExcludeAreaId IS NULL OR id <> @ExcludeAreaId);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, ResponsableId = responsableId, ExcludeAreaId = excludeAreaId },
            cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-A9 · SELECT usuario por id del tenant (id, nombre, correo, rol, estado). La BLL
    /// valida rol = 'JefeArea' y estado = 'Activo' (RN-011, CA #2). Retorna null si no existe o es de otro tenant.</summary>
    public async Task<UsuarioEntity?> ObtenerResponsableAsync(Guid tenantId, Guid responsableId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, nombre, correo, rol, estado
            FROM usuario
            WHERE tenant_id = @TenantId
              AND id = @ResponsableId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, ResponsableId = responsableId },
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioEntity>(cmd);
    }

    /// <summary>DAL-A10 · SELECT candidatos a responsable: usuarios del tenant con rol JefeArea y
    /// estado Activo, con ya_asignado = EXISTS(área ACTIVA del ciclo con ese responsable) (RN-012).
    /// ORDER BY nombre.</summary>
    public async Task<IEnumerable<ResponsableCandidatoResponse>> ListarResponsablesCandidatosAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT u.id, u.nombre, u.correo,
                   EXISTS (SELECT 1 FROM area a
                           WHERE a.ciclo_id = @CicloId
                             AND a.responsable_id = u.id
                             AND a.activa = TRUE) AS ya_asignado
            FROM usuario u
            WHERE u.tenant_id = @TenantId
              AND u.rol = 'JefeArea'::rol_usuario
              AND u.estado = 'Activo'::estado_usuario
            ORDER BY u.nombre;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.QueryAsync<ResponsableCandidatoResponse>(cmd);
    }

    /// <summary>DAL-A11 · UPDATE usuario SET area_id = @AreaId, updated_at = NOW() (sync SEC-07, D-J).
    /// @AreaId null libera al responsable anterior (evita fuga de acceso SEC-07). Retorna filas afectadas.</summary>
    public async Task<int> ActualizarAreaIdUsuarioAsync(Guid usuarioId, Guid? areaId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET area_id    = @AreaId,
                updated_at = NOW()
            WHERE id = @UsuarioId;";

        var parametros = new { UsuarioId = usuarioId, AreaId = areaId };

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);
            return await tx.Connection!.ExecuteAsync(cmdTx);
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, parametros, cancellationToken: ct);
        return await conn.ExecuteAsync(cmd);
    }
}
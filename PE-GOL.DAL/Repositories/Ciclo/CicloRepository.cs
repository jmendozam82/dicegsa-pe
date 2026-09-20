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

    // ─── HU-010 · Responsables (Spec HU-010 § Queries DAL: DAL-R1 a DAL-R7) ───
    // Implementación real (fase IMPLEMENT). Los responsables son hijos del agregado Ciclo (D-I):
    // toda query incluye WHERE tenant_id = @TenantId (SEC-06) y, cuando el rol es JefeArea,
    // AND a.id = @AreaIdFiltro (SEC-07 — responsable SÍ es entidad de área). Parámetros nombrados
    // (SEC-05) y CancellationToken vía CommandDefinition (forma canónica de Dapper).
    // Observación de Jorge (DAL-R2/R3): LEFT JOIN area con la condición del ciclo en el JOIN
    // (ON a.id = u.area_id AND a.ciclo_id = @CicloId), NO en el WHERE — un usuario cuyo area_id
    // apunta a un área de otro ciclo (clonado/reasignado) se devuelve con area_codigo/area_nombre
    // null en lugar de perderse la fila.

    /// <summary>DAL-R1 · INSERT usuario (rol JefeArea, estado Activo, requiere_cambio_pwd TRUE) ...
    /// RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    public async Task<Guid?> InsertarUsuarioAsync(ResponsableInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO usuario (tenant_id, nombre, correo, password_hash, rol, area_id, estado, requiere_cambio_pwd)
            VALUES (@TenantId, @Nombre, @Correo, @PasswordHash, @Rol::rol_usuario, @AreaId, @Estado::estado_usuario, @RequiereCambioPwd)
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

    /// <summary>DAL-R2 · SELECT responsable por id (aislado por tenant; rol JefeArea). LEFT JOIN
    /// area con la condición del ciclo en el JOIN (observación de Jorge). Retorna null si no existe
    /// o es de otro tenant (sin fuga).</summary>
    public async Task<ResponsableEntity?> ObtenerResponsablePorIdAsync(Guid tenantId, Guid cicloId, Guid responsableId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT u.id, u.tenant_id, u.nombre, u.correo, u.rol, u.estado, u.area_id,
                   u.requiere_cambio_pwd, u.ultimo_login, u.created_at, u.updated_at,
                   a.codigo AS area_codigo, a.nombre AS area_nombre
            FROM usuario u
            LEFT JOIN area a ON a.id = u.area_id AND a.ciclo_id = @CicloId
            WHERE u.tenant_id = @TenantId
              AND u.id = @ResponsableId
              AND u.rol = 'JefeArea'::rol_usuario;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, ResponsableId = responsableId },
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<ResponsableEntity>(cmd);
    }

    /// <summary>DAL-R3 · SELECT responsables del ciclo (rol JefeArea), ORDER BY nombre. areaIdFiltro
    /// (SEC-07): si no es null, AND a.id = @AreaIdFiltro (JefeArea solo su responsable). Sin filtro
    /// (null) → todos (ADM/GER). LEFT JOIN area con la condición del ciclo en el JOIN.</summary>
    public async Task<List<ResponsableEntity>> ListarResponsablesAsync(Guid tenantId, Guid cicloId, Guid? areaIdFiltro = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT u.id, u.tenant_id, u.nombre, u.correo, u.rol, u.estado, u.area_id,
                   u.requiere_cambio_pwd, u.ultimo_login, u.created_at, u.updated_at,
                   a.codigo AS area_codigo, a.nombre AS area_nombre
            FROM usuario u
            LEFT JOIN area a ON a.id = u.area_id AND a.ciclo_id = @CicloId
            WHERE u.tenant_id = @TenantId
              AND u.rol = 'JefeArea'::rol_usuario
              AND (@AreaIdFiltro IS NULL OR a.id = @AreaIdFiltro)
            ORDER BY u.nombre;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaIdFiltro = areaIdFiltro },
            cancellationToken: ct);
        var result = await conn.QueryAsync<ResponsableEntity>(cmd);
        return result.ToList();
    }

    /// <summary>DAL-R4 · ¿Existe un usuario con ese correo en el tenant? (case-insensitive, LOWER).</summary>
    public async Task<bool> ExisteCorreoEnTenantAsync(Guid tenantId, string correo, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM usuario
            WHERE tenant_id = @TenantId
              AND LOWER(correo) = LOWER(@Correo);";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, Correo = correo }, cancellationToken: ct);
        var count = await conn.ExecuteScalarAsync<int>(cmd);
        return count > 0;
    }

    /// <summary>DAL-R5 · COUNT usuarios ACTIVOS del tenant (RN-010, chequeo preciso por tenant — D-D).</summary>
    public async Task<int> ContarUsuariosActivosEnTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM usuario
            WHERE tenant_id = @TenantId
              AND estado = 'Activo'::estado_usuario;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-R6 · UPDATE area SET responsable_id = @ResponsableId, updated_at = NOW()
    /// (aislada por tenant y ciclo). @ResponsableId null libera el área (queda sin responsable).</summary>
    public async Task AsignarResponsableAreaAsync(Guid tenantId, Guid cicloId, Guid areaId, Guid? responsableId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE area
            SET responsable_id = @ResponsableId,
                updated_at     = NOW()
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @AreaId;";

        var parametros = new { TenantId = tenantId, CicloId = cicloId, AreaId = areaId, ResponsableId = responsableId };

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);
            await tx.Connection!.ExecuteAsync(cmdTx);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, parametros, cancellationToken: ct);
        await conn.ExecuteAsync(cmd);
    }

    /// <summary>DAL-R7 · UPDATE usuario SET estado = 'Inactivo', updated_at = NOW() (desactivar responsable).</summary>
    public async Task DesactivarUsuarioAsync(Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE usuario
            SET estado     = 'Inactivo'::estado_usuario,
                updated_at = NOW()
            WHERE id = @UsuarioId;";

        if (tx is not null)
        {
            var cmdTx = new CommandDefinition(sql, new { UsuarioId = usuarioId }, tx, cancellationToken: ct);
            await tx.Connection!.ExecuteAsync(cmdTx);
            return;
        }

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { UsuarioId = usuarioId }, cancellationToken: ct);
        await conn.ExecuteAsync(cmd);
    }

    // ─── HU-011 · Filosofía (Spec HU-011 § Queries DAL: DAL-F1/F2) ───
    // Implementación real (fase IMPLEMENT). La filosofía es hija del agregado Ciclo (D-I):
    // toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06). SEC-07 NO
    // APLICA (D-E): filosofia es corporativa (sin area_id; RLS solo por tenant — 06 L561) → sin
    // AND area_id para ningún rol. Parámetros nombrados (SEC-05) y CancellationToken vía
    // CommandDefinition (forma canónica de Dapper).

    /// <summary>DAL-F1 · SELECT filosofia del ciclo (LEFT JOIN usuario para updated_by_nombre).
    /// Retorna null si no existe fila (D-H — el GET defensivo cubre la ausencia con defaults).</summary>
    public async Task<FilosofiaEntity?> ObtenerFilosofiaAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT f.id, f.tenant_id, f.ciclo_id, f.vision, f.mision, f.valores, f.updated_by,
                   u.nombre AS updated_by_nombre, f.updated_at
            FROM filosofia f
            LEFT JOIN usuario u ON u.id = f.updated_by
            WHERE f.tenant_id = @TenantId
              AND f.ciclo_id = @CicloId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<FilosofiaEntity>(cmd);
    }

    /// <summary>DAL-F2 · UPSERT filosofia: INSERT ... ON CONFLICT (tenant_id, ciclo_id) DO UPDATE
    /// (target = UNIQUE del DDL L173; DB-06, D-B — crea la fila en el primer guardado, sin fila
    /// default en CrearAsync). Retorna filas afectadas.</summary>
    public async Task<int> UpsertFilosofiaAsync(FilosofiaUpsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO filosofia (tenant_id, ciclo_id, vision, mision, updated_by, updated_at)
            VALUES (@TenantId, @CicloId, @Vision, @Mision, @UpdatedBy, NOW())
            ON CONFLICT (tenant_id, ciclo_id) DO UPDATE
            SET vision     = EXCLUDED.vision,
                mision     = EXCLUDED.mision,
                updated_by = EXCLUDED.updated_by,
                updated_at = NOW()
            RETURNING id, updated_at;";

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

    /// <summary>DAL-F3 (HU-012) · UPSERT valores corporativos: INSERT ... ON CONFLICT
    /// (tenant_id, ciclo_id) DO UPDATE (target = UNIQUE del DDL L173; DB-06, D-B). El INSERT
    /// inicial crea la fila con vision=''/mision='' (el GER puede registrar valores sin haber
    /// escrito visión/misión aún); el branch DO UPDATE SOLO toca valores/updated_by/updated_at —
    /// NO pisa vision/mision (D-B). @Valores es un string JSON serializado por la BLL con cast
    /// ::jsonb (SEC-05: parametrizado, sin concatenación). SEC-07 NO APLICA (D-E): filosofia es
    /// corporativa (sin area_id; RLS solo por tenant — 06 L561) → sin AND area_id para ningún rol.
    /// Retorna filas afectadas.</summary>
    public async Task<int> UpsertFilosofiaValoresAsync(FilosofiaValoresUpsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO filosofia (tenant_id, ciclo_id, vision, mision, valores, updated_by, updated_at)
            VALUES (@TenantId, @CicloId, '', '', @Valores::jsonb, @UpdatedBy, NOW())
            ON CONFLICT (tenant_id, ciclo_id) DO UPDATE
            SET valores     = EXCLUDED.valores,
                updated_by  = EXCLUDED.updated_by,
                updated_at  = NOW()
            RETURNING id, updated_at;";

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

    // ─── HU-013 · Pilares Estratégicos (Spec HU-013 § Queries DAL: DAL-P1 a DAL-P9) ───
    // Implementación real (fase IMPLEMENT). Los pilares son hijos del agregado Ciclo (D-I):
    // toda query incluye WHERE tenant_id = @TenantId como primera condición (SEC-06). SEC-07 NO
    // APLICA (D-E): pilar es corporativa (sin area_id; RLS pilar_policy solo por tenant — 06 L562)
    // → sin AND area_id para ningún rol (el JEF lee TODOS los pilares del ciclo, RN-007).
    // Parámetros nombrados (SEC-05) y CancellationToken vía CommandDefinition (forma canónica de
    // Dapper). Los conteos de DAL-P1/P2 son datos derivados de lectura que NO se persisten (D-D).

    /// <summary>DAL-P1 · SELECT pilares del ciclo con conteos de CG y OKRs (CA #5, D-D — LEFT JOIN
    /// + COUNT(DISTINCT) evita duplicar filas cuando un pilar tiene CGs y OKRs simultáneamente),
    /// ORDER BY orden ASC, codigo ASC. HU-014 (aditivo): el SELECT incluye objetivo_q1..q4.</summary>
    public async Task<IEnumerable<PilarConteosDto>> ListarPilaresConConteosAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT p.id, p.ciclo_id, p.codigo, p.nombre, p.estrategia_victoria,
                   p.objetivo_q1, p.objetivo_q2, p.objetivo_q3, p.objetivo_q4,
                   p.orden, p.created_at, p.updated_at,
                   COUNT(DISTINCT oc.id) AS total_objetivos_cg,
                   COUNT(DISTINCT ok.id) AS total_okrs
            FROM pilar p
            LEFT JOIN objetivo_cg oc ON oc.pilar_id = p.id
            LEFT JOIN okr ok ON ok.pilar_id = p.id
            WHERE p.tenant_id = @TenantId
              AND p.ciclo_id = @CicloId
            GROUP BY p.id
            ORDER BY p.orden ASC, p.codigo ASC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.QueryAsync<PilarConteosDto>(cmd);
    }

    /// <summary>DAL-P2 · SELECT pilar por id con conteos (misma query que DAL-P1 + AND p.id = @PilarId).
    /// Retorna null si no existe o es de otro tenant (sin fuga). HU-014 (aditivo): incluye objetivo_q1..q4.</summary>
    public async Task<PilarConteosDto?> ObtenerPilarConConteosAsync(Guid tenantId, Guid cicloId, Guid pilarId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT p.id, p.ciclo_id, p.codigo, p.nombre, p.estrategia_victoria,
                   p.objetivo_q1, p.objetivo_q2, p.objetivo_q3, p.objetivo_q4,
                   p.orden, p.created_at, p.updated_at,
                   COUNT(DISTINCT oc.id) AS total_objetivos_cg,
                   COUNT(DISTINCT ok.id) AS total_okrs
            FROM pilar p
            LEFT JOIN objetivo_cg oc ON oc.pilar_id = p.id
            LEFT JOIN okr ok ON ok.pilar_id = p.id
            WHERE p.tenant_id = @TenantId
              AND p.ciclo_id = @CicloId
              AND p.id = @PilarId
            GROUP BY p.id
            ORDER BY p.orden ASC, p.codigo ASC;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, PilarId = pilarId },
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<PilarConteosDto>(cmd);
    }

    /// <summary>DAL-P3 · SELECT pilar por id SIN conteos (para validaciones/update/delete).
    /// Retorna null si no existe o es de otro tenant (sin fuga). HU-014 (aditivo): incluye objetivo_q1..q4.</summary>
    public async Task<PilarEntity?> ObtenerPilarAsync(Guid tenantId, Guid cicloId, Guid pilarId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, tenant_id, ciclo_id, codigo, nombre, estrategia_victoria,
                   objetivo_q1, objetivo_q2, objetivo_q3, objetivo_q4,
                   orden, created_at, updated_at
            FROM pilar
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @PilarId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql,
            new { TenantId = tenantId, CicloId = cicloId, PilarId = pilarId },
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<PilarEntity>(cmd);
    }

    /// <summary>DAL-P4 · COUNT pilares del ciclo (CA #2 — máximo 8, D-C).</summary>
    public async Task<int> ContarPilaresDelCicloAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM pilar
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int>(cmd);
    }

    /// <summary>DAL-P5 · Siguiente secuencia del ciclo: MAX(regexp_match(codigo,'^PEC-(\d+)$'))+1
    /// (CA #1, D-B — extrae el N del código PEC-N de forma defensiva, solo filas con formato exacto
    /// 'PEC-<dígitos>') y MAX(orden)+1 (D-H) — una sola query.</summary>
    public async Task<SiguienteSecuenciaPilarDto> ObtenerSiguienteSecuenciaPilarAsync(Guid tenantId, Guid cicloId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COALESCE(MAX((regexp_match(codigo, '^PEC-(\d+)$'))[1]::INT), 0) + 1 AS siguiente_n,
                   COALESCE(MAX(orden), 0) + 1 AS siguiente_orden
            FROM pilar
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, CicloId = cicloId }, cancellationToken: ct);
        return await conn.QuerySingleAsync<SiguienteSecuenciaPilarDto>(cmd);
    }

    /// <summary>DAL-P6 · INSERT pilar ... RETURNING id. Retorna el nuevo id (null si no insertó).</summary>
    public async Task<Guid?> InsertarPilarAsync(PilarInsertDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO pilar (tenant_id, ciclo_id, codigo, nombre, estrategia_victoria, orden)
            VALUES (@TenantId, @CicloId, @Codigo, @Nombre, @EstrategiaVictoria, @Orden)
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

    /// <summary>DAL-P7 · UPDATE pilar (nombre/estrategia_victoria/orden, updated_at = NOW()).
    /// NO toca codigo (auto-generado, CA #1). Retorna filas afectadas.</summary>
    public async Task<int> ActualizarPilarAsync(PilarUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE pilar
            SET nombre              = @Nombre,
                estrategia_victoria = @EstrategiaVictoria,
                orden               = @Orden,
                updated_at          = NOW()
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @PilarId
            RETURNING updated_at;";

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

    /// <summary>DAL-P8 · DELETE físico de pilar (D-A). Retorna filas afectadas.</summary>
    public async Task<int> EliminarPilarAsync(Guid tenantId, Guid cicloId, Guid pilarId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE FROM pilar
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @PilarId;";

        var parametros = new { TenantId = tenantId, CicloId = cicloId, PilarId = pilarId };

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

    /// <summary>DAL-P9 · COUNT dependencias del pilar: objetivo_cg y okr por pilar_id (CA #3).
    /// Subqueries COUNT — la BLL valida > 0 → 422 antes del DELETE (capa 1).</summary>
    public async Task<PilarDependenciasDto> ContarDependenciasPilarAsync(Guid tenantId, Guid pilarId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT (SELECT COUNT(1) FROM objetivo_cg WHERE tenant_id = @TenantId AND pilar_id = @PilarId) AS total_objetivos_cg,
                   (SELECT COUNT(1) FROM okr WHERE tenant_id = @TenantId AND pilar_id = @PilarId) AS total_okrs;";

        using var conn = _factory.CreateConnection();
        conn.Open();
        var cmd = new CommandDefinition(sql, new { TenantId = tenantId, PilarId = pilarId }, cancellationToken: ct);
        return await conn.QuerySingleAsync<PilarDependenciasDto>(cmd);
    }

    /// <summary>DAL-P10 (HU-014) · UPDATE pilar SET objetivo_q1..q4 + updated_at = NOW().
    /// NO toca codigo/nombre/estrategia_victoria/orden (D-A — contrato HU-013 intacto).
    /// SEC-06: tenant_id solo del TenantContext (JWT). SEC-07 NO APLICA (D-E): pilar es
    /// corporativa (sin area_id; RLS pilar_policy solo por tenant) → sin AND area_id.
    /// Retorna filas afectadas.</summary>
    public async Task<int> ActualizarObjetivosTrimestralesAsync(PilarObjetivosTrimestralesUpdateDto dto, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE pilar
            SET objetivo_q1 = @ObjetivoQ1,
                objetivo_q2 = @ObjetivoQ2,
                objetivo_q3 = @ObjetivoQ3,
                objetivo_q4 = @ObjetivoQ4,
                updated_at  = NOW()
            WHERE tenant_id = @TenantId
              AND ciclo_id = @CicloId
              AND id = @PilarId
            RETURNING updated_at;";

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
}
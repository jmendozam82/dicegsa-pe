using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Repositories.Objetivos;

/// <summary>
/// Spec HU-022 (DAL) · <c>entregable_adjunto</c>. Dapper + parámetros nombrados, sin concatenar
/// SQL (STACK-03, SEC-05). El <c>CancellationToken</c> se propaga con
/// <see cref="CommandDefinition"/> (forma canónica de Dapper en este repositorio).
/// <para>
/// <b>Las 6 queries filtran por <c>tenant_id = @TenantId</c></b> con parámetro nombrado, incluso
/// las que ya filtran por <c>accion_id</c>: es defensa en profundidad, porque la RLS es la capa 2
/// del aislamiento, no la única (ARCH-04). En los dos <c>INSERT</c> el aislamiento va en la
/// propia columna de escritura (<c>tenant_id</c> se inserta desde el <c>TenantContext</c>), y el
/// invariante queda anotado al pie del enunciado para que quede visible en el log de SQL.
/// </para>
/// <para>
/// <b>SEC-07 no se aplica:</b> <c>entregable_adjunto</c> no tiene <c>area_id</c>; el filtro por
/// área lo aplica la BLL contra <c>accion_plan.area_id</c> antes de llegar aquí.
/// </para>
/// <para>
/// <b>Sin <c>ILogger</c></b> (los 9 repositorios del repo reciben solo <c>IDbConnectionFactory</c>):
/// el logging vive en BLL/Services (STACK-11) y un fallo de Dapper se propaga.
/// </para>
/// </summary>
public sealed class EntregableAdjuntoRepository : IEntregableAdjuntoRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public EntregableAdjuntoRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────── Consultas ──────────────────────────────────────────────────

    /// <summary>
    /// CA #3 · Listado de la acción con el nombre de quien subió cada archivo.
    /// El <c>LEFT JOIN usuario</c> (no <c>JOIN</c>) con <c>u.tenant_id = e.tenant_id</c> evita que
    /// un usuario borrado desaparezca la fila del adjunto; <c>SubidoPorNombre</c> llega nulo y la
    /// vista muestra «—».
    /// </summary>
    private const string SqlListar = @"
        SELECT e.id              AS Id,
               e.tenant_id       AS TenantId,
               e.accion_id       AS AccionId,
               e.nombre_archivo  AS NombreArchivo,
               e.file_path       AS FilePath,
               e.file_size_bytes AS FileSizeBytes,
               e.tipo_mime       AS TipoMime,
               e.subido_por      AS SubidoPor,
               e.created_at      AS CreatedAt,
               u.nombre          AS SubidoPorNombre
        FROM entregable_adjunto e
        LEFT JOIN usuario u ON u.id = e.subido_por AND u.tenant_id = e.tenant_id
        WHERE e.tenant_id = @TenantId
          AND e.accion_id = @AccionId
        ORDER BY e.created_at ASC, e.nombre_archivo ASC;";

    /// <summary>CA #1 · Conteo de adjuntos de la acción (el límite de 5 se valida en la BLL, DB-04).</summary>
    private const string SqlContar = @"
        SELECT COUNT(*)
        FROM entregable_adjunto
        WHERE tenant_id = @TenantId
          AND accion_id = @AccionId;";

    /// <summary>
    /// Adjunto por PK exigiendo LAS TRES columnas: tenant + acción + id. El
    /// <c>AND accion_id = @AccionId</c> no es decorativo — impide firmar o borrar un anexo
    /// válido de otra acción falseando el <c>accionId</c> de la ruta (→ 404, no 403).
    /// </summary>
    private const string SqlObtenerPorId = @"
        SELECT e.id              AS Id,
               e.tenant_id       AS TenantId,
               e.accion_id       AS AccionId,
               e.nombre_archivo  AS NombreArchivo,
               e.file_path       AS FilePath,
               e.file_size_bytes AS FileSizeBytes,
               e.tipo_mime       AS TipoMime,
               e.subido_por      AS SubidoPor,
               e.created_at      AS CreatedAt,
               u.nombre          AS SubidoPorNombre
        FROM entregable_adjunto e
        LEFT JOIN usuario u ON u.id = e.subido_por AND u.tenant_id = e.tenant_id
        WHERE e.tenant_id = @TenantId
          AND e.accion_id = @AccionId
          AND e.id = @EntregableId;";

    /// <summary>
    /// Alta. Son 8 columnas y 8 parámetros: la marca temporal la fija el <c>DEFAULT now()</c> del
    /// DDL (no la envía la BLL) y <c>SubidoPorNombre</c> es columna del <c>LEFT JOIN</c>, no de la
    /// tabla. Sin <c>ON CONFLICT</c>: el <c>id</c> es un UUID nuevo (DB-06 no aplica).
    /// </summary>
    private const string SqlInsertar = @"
        INSERT INTO entregable_adjunto
            (id, tenant_id, accion_id, nombre_archivo, file_path, file_size_bytes, tipo_mime, subido_por)
        VALUES
            (@Id, @TenantId, @AccionId, @NombreArchivo, @FilePath, @FileSizeBytes, @TipoMime, @SubidoPor);
        -- tenant_id = @TenantId · SEC-06/DB-03: siempre del TenantContext (JWT), nunca del cliente.";

    /// <summary>
    /// Borrado físico acotado por tenant + acción + id. Devuelve las filas afectadas para que la
    /// BLL distinga «borrada» (1) de «carrera» (0 → 404 + Rollback, sin auditar un borrado que
    /// no ocurrió). El <c>ON DELETE CASCADE</c> de <c>accion_id</c> en el DDL cubre el borrado de
    /// la acción (HU-019); esta DAL no lo reproduce ni lo altera.
    /// </summary>
    private const string SqlEliminar = @"
        DELETE FROM entregable_adjunto
        WHERE tenant_id = @TenantId
          AND accion_id = @AccionId
          AND id = @EntregableId;";

    /// <summary>
    /// Auditoría de dominio (ADR-003): los 7 campos de <see cref="LogAuditoriaInsert"/> y nada
    /// más (la marca temporal la pone el <c>DEFAULT now()</c>; la tabla no tiene columnas de
    /// estado ni de mensaje). <c>accion</c> es el enum <c>accion_auditoria</c> del DDL y los
    /// snapshots son <c>JSONB</c>, de ahí los dos casts explícitos.
    /// </summary>
    private const string SqlInsertarLog = @"
        INSERT INTO log_auditoria
            (tenant_id, usuario_id, accion, entidad, entidad_id, valor_anterior, valor_nuevo)
        VALUES
            (@TenantId, @UsuarioId, @Accion::accion_auditoria, @Entidad, @EntidadId, @ValorAnterior::jsonb, @ValorNuevo::jsonb);
        -- tenant_id = @TenantId · auditoría en la MISMA transacción que la fila de entregable_adjunto (ADR-003).";

    // ─────────────────────────── Lecturas ───────────────────────────────────────────────────

    public async Task<IEnumerable<EntregableAdjuntoEntity>> ListarPorAccionAsync(
        Guid accionId, Guid tenantId, CancellationToken ct = default)
    {
        var cmd = new CommandDefinition(
            SqlListar, new { AccionId = accionId, TenantId = tenantId }, cancellationToken: ct);

        return await GetConnection().QueryAsync<EntregableAdjuntoEntity>(cmd);
    }

    public async Task<int> ContarPorAccionAsync(
        Guid accionId, Guid tenantId, CancellationToken ct = default)
    {
        var cmd = new CommandDefinition(
            SqlContar, new { AccionId = accionId, TenantId = tenantId }, cancellationToken: ct);

        return await GetConnection().ExecuteScalarAsync<int>(cmd);
    }

    public async Task<EntregableAdjuntoEntity?> ObtenerPorIdAsync(
        Guid entregableId, Guid accionId, Guid tenantId, CancellationToken ct = default)
    {
        var cmd = new CommandDefinition(
            SqlObtenerPorId,
            new { EntregableId = entregableId, AccionId = accionId, TenantId = tenantId },
            cancellationToken: ct);

        return await GetConnection().QuerySingleOrDefaultAsync<EntregableAdjuntoEntity>(cmd);
    }

    // ─────────────────────────── Transacción ────────────────────────────────────────────────

    /// <summary>
    /// Abre la transacción del lote sobre una conexión nueva, que queda cacheada para que los
    /// <c>INSERT</c>/<c>DELETE</c> con <c>tx</c> vayan por la MISMA conexión. La cierra la BLL.
    /// </summary>
    public Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Libera cualquier conexión previa que hubiera quedado asociada sin cerrar.
        _connectionActiva?.Dispose();
        _connectionActiva = null;

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

    // ─────────────────────────── Escrituras ──────────────────────────────────────────────────

    public async Task InsertarAsync(
        EntregableAdjuntoEntity entity, IDbTransaction? tx, CancellationToken ct = default)
    {
        // Solo las 8 columnas escribibles: la marca temporal la fija el DDL y el nombre del
        // usuario viene del LEFT JOIN de las lecturas.
        var p = new
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            AccionId = entity.AccionId,
            NombreArchivo = entity.NombreArchivo,
            FilePath = entity.FilePath,
            FileSizeBytes = entity.FileSizeBytes,
            TipoMime = entity.TipoMime,
            SubidoPor = entity.SubidoPor
        };

        await ExecuteWriteAsync(SqlInsertar, p, tx, ct);
    }

    public async Task<int> EliminarAsync(
        Guid entregableId, Guid accionId, Guid tenantId, IDbTransaction? tx, CancellationToken ct = default)
    {
        var p = new { EntregableId = entregableId, AccionId = accionId, TenantId = tenantId };

        return await ExecuteWriteAsync(SqlEliminar, p, tx, ct);
    }

    public async Task InsertLogAsync(
        LogAuditoriaInsert dto, IDbTransaction? tx, CancellationToken ct = default)
    {
        await ExecuteWriteAsync(SqlInsertarLog, dto, tx, ct);
    }

    // ─────────────────────────── Infraestructura interna ──────────────────────────────────

    /// <summary>
    /// Ejecuta un enunciado de escritura sobre la conexión de la transacción si la hay (para que el
    /// <c>INSERT</c>/<c>DELETE</c> y su auditoría sean atómicos) y, si no, sobre la conexión
    /// gestionada del repositorio. Devuelve las filas afectadas.
    /// </summary>
    private async Task<int> ExecuteWriteAsync(
        string sql, object parametros, IDbTransaction? tx, CancellationToken ct)
    {
        var cmd = new CommandDefinition(sql, parametros, tx, cancellationToken: ct);

        // Con transacción: su conexión es la que tiene abierta la unidad de trabajo.
        if (tx is not null)
            return await tx.Connection!.ExecuteAsync(cmd);

        return await GetConnection().ExecuteAsync(cmd);
    }

    /// <summary>Conexión gestionada del repositorio (Scoped en IOC): se cachea y se abre si hace falta.</summary>
    private IDbConnection GetConnection()
    {
        _connectionActiva ??= _factory.CreateConnection();

        if (_connectionActiva.State != ConnectionState.Open)
            _connectionActiva.Open();

        return _connectionActiva;
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
        _connectionActiva = null;
    }
}

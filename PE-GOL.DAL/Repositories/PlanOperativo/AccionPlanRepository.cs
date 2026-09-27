using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Repositories.PlanOperativo;

public class AccionPlanRepository : IAccionPlanRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public AccionPlanRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private IDbConnection GetConnection()
    {
        if (_connectionActiva == null || _connectionActiva.State != ConnectionState.Open)
        {
            _connectionActiva = _factory.CreateConnection();
            if (_connectionActiva.State != ConnectionState.Open)
                _connectionActiva.Open();
        }
        return _connectionActiva;
    }

    public async Task<AccionPlanEntity> InsertAsync(AccionPlanEntity entity, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO accion_plan (
                tenant_id, ciclo_id, area_id, objetivo_cg_id, codigo, descripcion,
                descripcion_entregable, responsable_id, fecha_inicio, fecha_vencimiento,
                clasificacion, tipo_presupuesto, peso, aclaraciones, progreso,
                puntuacion_ponderada, status, alerta_enviada, orden, created_at, updated_at
            ) VALUES (
                @TenantId, @CicloId, @AreaId, @ObjetivoCgId, @Codigo, @Descripcion,
                @DescripcionEntregable, @ResponsableId, @FechaInicio, @FechaVencimiento,
                @Clasificacion::clasificacion_accion, @TipoPresupuesto::tipo_presupuesto, @Peso, @Aclaraciones, @Progreso,
                @PuntuacionPonderada, @Status::status_accion, @AlertaEnviada, @Orden, @CreatedAt, @UpdatedAt
            ) RETURNING id;";

        var id = await GetConnection().ExecuteScalarAsync<Guid>(sql, entity);
        entity.Id = id;
        return entity;
    }

    public async Task UpdateAsync(AccionPlanEntity entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        var sql = @"
            UPDATE accion_plan SET
                descripcion = @Descripcion,
                descripcion_entregable = @DescripcionEntregable,
                responsable_id = @ResponsableId,
                fecha_inicio = @FechaInicio,
                fecha_vencimiento = @FechaVencimiento,
                clasificacion = @Clasificacion::clasificacion_accion,
                tipo_presupuesto = @TipoPresupuesto::tipo_presupuesto,
                peso = @Peso,
                aclaraciones = @Aclaraciones,
                progreso = @Progreso,
                puntuacion_ponderada = @PuntuacionPonderada,
                status = @Status::status_accion,
                alerta_enviada = @AlertaEnviada,
                orden = @Orden,
                updated_at = @UpdatedAt
            WHERE id = @Id AND tenant_id = @TenantId;";

        await GetConnection().ExecuteAsync(sql, entity);
    }

    public async Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var sql = "DELETE FROM accion_plan WHERE id = @Id AND tenant_id = @TenantId;";
        await GetConnection().ExecuteAsync(sql, new { Id = id, TenantId = tenantId });
    }

    public async Task<AccionPlanEntity?> ObtenerPorIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                a.*,
                u.nombre AS ResponsableNombre
            FROM accion_plan a
            LEFT JOIN usuario u ON a.responsable_id = u.id
            WHERE a.id = @Id AND a.tenant_id = @TenantId;";
            
        return await GetConnection().QuerySingleOrDefaultAsync<AccionPlanEntity>(sql, new { Id = id, TenantId = tenantId });
    }

    public async Task<IEnumerable<AccionPlanEntity>> ListarPorObjetivoCgAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                a.*,
                u.nombre AS ResponsableNombre
            FROM accion_plan a
            LEFT JOIN usuario u ON a.responsable_id = u.id
            WHERE a.objetivo_cg_id = @ObjetivoCgId AND a.tenant_id = @TenantId
            ORDER BY a.orden ASC, a.created_at ASC;";
            
        return await GetConnection().QueryAsync<AccionPlanEntity>(sql, new { ObjetivoCgId = objetivoCgId, TenantId = tenantId });
    }

    public async Task<decimal> ObtenerSumaPesosAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = "SELECT COALESCE(SUM(peso), 0) FROM accion_plan WHERE objetivo_cg_id = @ObjetivoCgId AND tenant_id = @TenantId;";
        return await GetConnection().ExecuteScalarAsync<decimal>(sql, new { ObjetivoCgId = objetivoCgId, TenantId = tenantId });
    }

    public async Task<decimal> ObtenerSumaPonderadaProgresoAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = "SELECT COALESCE(SUM(peso * progreso / 100.0), 0) FROM accion_plan WHERE objetivo_cg_id = @ObjetivoCgId AND tenant_id = @TenantId;";
        return await GetConnection().ExecuteScalarAsync<decimal>(sql, new { ObjetivoCgId = objetivoCgId, TenantId = tenantId });
    }

    public async Task<int> ObtenerMaximoOrdenAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = "SELECT COALESCE(MAX(orden), 0) FROM accion_plan WHERE objetivo_cg_id = @ObjetivoCgId AND tenant_id = @TenantId;";
        return await GetConnection().ExecuteScalarAsync<int>(sql, new { ObjetivoCgId = objetivoCgId, TenantId = tenantId });
    }

    // ─── HU-021 · Vista Gantt del Plan de Acción (Spec HU-021 § Queries DAL: DAL-G1) ───
    // Lectura única que alimenta el payload completo del Gantt (RNF-001: 1 query, sin N+1).
    // SEC-05: todos los valores parametrizados por nombre, sin concatenación de SQL.
    // SEC-06: WHERE a.tenant_id = @TenantId (TenantContext/JWT; el ciclo llega ya resuelto en BLL).
    // SEC-07: AND (@AreaIdFiltro IS NULL OR a.area_id = @AreaIdFiltro) — el aislamiento por área
    // vive AQUÍ y en ningún otro lugar (F11: la BLL no re-filtra en memoria).
    // DB-04/D-K: sin COUNT, SUM, FILTER ni CASE de negocio — TotalAcciones, ConteoPorStatus y la
    // Escala se calculan en la BLL. Sin JOIN a ciclo: sus metadatos llegan por DAL-D1.
    // Soporte: índice idx_accion_plan_ciclo_area (ADR-012, migración V005).

    public async Task<IEnumerable<GanttAccionFilaDto>> ListarParaGanttAsync(
        Guid tenantId, Guid cicloId, Guid? areaIdFiltro, CancellationToken ct = default)
    {
        var sql = @"
            SELECT a.id                  AS id,
                   a.objetivo_cg_id      AS objetivo_cg_id,
                   a.area_id             AS area_id,
                   a.codigo              AS codigo,
                   a.descripcion         AS descripcion,
                   a.fecha_inicio        AS fecha_inicio,
                   a.fecha_vencimiento   AS fecha_vencimiento,
                   a.clasificacion::text AS clasificacion,
                   a.tipo_presupuesto::text AS tipo_presupuesto,
                   a.peso                AS peso,
                   a.progreso            AS progreso,
                   a.status::text        AS status,
                   a.orden               AS orden,
                   u.nombre              AS responsable_nombre,
                   oc.orden              AS objetivo_orden,
                   oc.codigo             AS objetivo_codigo,
                   oc.descripcion        AS objetivo_descripcion,
                   oc.progreso           AS objetivo_progreso,
                   oc.semaforo::text     AS objetivo_semaforo,
                   ar.codigo             AS area_codigo,
                   ar.nombre             AS area_nombre
            FROM accion_plan a
            JOIN objetivo_cg oc ON oc.id = a.objetivo_cg_id AND oc.tenant_id = a.tenant_id
            JOIN area ar         ON ar.id = a.area_id         AND ar.tenant_id = a.tenant_id
            LEFT JOIN usuario u  ON u.id = a.responsable_id
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id  = @CicloId
              AND (@AreaIdFiltro IS NULL OR a.area_id = @AreaIdFiltro)
            ORDER BY oc.orden ASC, oc.codigo ASC, a.orden ASC, a.codigo ASC;";

        return await GetConnection().QueryAsync<GanttAccionFilaDto>(sql,
            new { TenantId = tenantId, CicloId = cicloId, AreaIdFiltro = areaIdFiltro });
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}
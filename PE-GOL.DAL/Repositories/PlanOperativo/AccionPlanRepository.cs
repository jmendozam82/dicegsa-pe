using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
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

    public async Task<int> ObtenerMaximoOrdenAsync(Guid objetivoCgId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = "SELECT COALESCE(MAX(orden), 0) FROM accion_plan WHERE objetivo_cg_id = @ObjetivoCgId AND tenant_id = @TenantId;";
        return await GetConnection().ExecuteScalarAsync<int>(sql, new { ObjetivoCgId = objetivoCgId, TenantId = tenantId });
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}
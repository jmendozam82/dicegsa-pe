using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Repositories.PlanOperativo;

public sealed class HistorialProgresoRepository : IHistorialProgresoRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private System.Data.IDbConnection? _connectionActiva;

    public HistorialProgresoRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private System.Data.IDbConnection GetConnection()
        => _connectionActiva ??= _factory.CreateConnection();

    public async Task InsertAsync(HistorialProgresoEntity entity, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO historial_progreso
                (tenant_id, accion_id, progreso_anterior, progreso_nuevo,
                 status_anterior, status_nuevo, registrado_por)
            VALUES
                (@TenantId, @AccionId, @ProgresoAnterior, @ProgresoNuevo,
                 @StatusAnterior::status_accion, @StatusNuevo::status_accion, @RegistradoPor);";

        await GetConnection().ExecuteAsync(sql, new
        {
            entity.TenantId,
            entity.AccionId,
            entity.ProgresoAnterior,
            entity.ProgresoNuevo,
            entity.StatusAnterior,
            entity.StatusNuevo,
            entity.RegistradoPor
        });
    }

    public async Task<IEnumerable<HistorialProgresoResponse>> ListarPorAccionAsync(
        Guid accionId, Guid tenantId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT hp.id                    AS Id,
                   hp.accion_id             AS AccionId,
                   hp.progreso_anterior     AS ProgresoAnterior,
                   hp.progreso_nuevo        AS ProgresoNuevo,
                   hp.status_anterior::text AS StatusAnterior,
                   hp.status_nuevo::text    AS StatusNuevo,
                   hp.registrado_por        AS RegistradoPor,
                   u.nombre                 AS RegistradoPorNombre,
                   hp.created_at            AS CreatedAt
            FROM historial_progreso hp
            LEFT JOIN usuario u ON hp.registrado_por = u.id
            WHERE hp.accion_id = @AccionId
              AND hp.tenant_id = @TenantId
            ORDER BY hp.created_at DESC;";

        return await GetConnection().QueryAsync<HistorialProgresoResponse>(sql,
            new { AccionId = accionId, TenantId = tenantId });
    }

    public void Dispose() => _connectionActiva?.Dispose();
}

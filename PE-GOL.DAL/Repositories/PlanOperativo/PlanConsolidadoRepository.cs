using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.DAL.Repositories.PlanOperativo;

/// <summary>
/// Repositorio de solo lectura para la vista consolidada del Plan (Spec HU-023 § Queries DAL).
/// SEC-05: todas las queries parametrizadas por nombre, sin concatenación de SQL.
/// SEC-06: WHERE tenant_id = @TenantId en todas las queries.
/// SEC-07 NO APLICA: el rol Gerente ve todas las áreas (RN-006).
/// </summary>
public class PlanConsolidadoRepository : IPlanConsolidadoRepository, IDisposable
{
    private readonly IDbConnectionFactory _factory;
    private IDbConnection? _connectionActiva;

    public PlanConsolidadoRepository(IDbConnectionFactory factory)
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

    /// <summary>
    /// Query 1 — SELECT paginado con JOINs. skipPaginacion = true → sin OFFSET/FETCH.
    /// </summary>
    public async Task<List<ConsolidadoItemResponse>> ListarConsolidadoAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        bool skipPaginacion = false, CancellationToken ct = default)
    {
        var sql = @"
            SELECT
                a.id                    AS AccionId,
                a.codigo                AS AccionCodigo,
                a.descripcion           AS AccionDescripcion,
                a.fecha_inicio          AS FechaInicio,
                a.fecha_vencimiento     AS FechaVencimiento,
                a.clasificacion::text   AS Clasificacion,
                a.tipo_presupuesto::text AS TipoPresupuesto,
                a.progreso              AS Progreso,
                a.status::text          AS Status,
                a.peso                  AS Peso,
                u.nombre                AS ResponsableNombre,
                oc.id                   AS ObjetivoCgId,
                oc.codigo               AS ObjetivoCodigo,
                oc.descripcion          AS ObjetivoDescripcion,
                oc.progreso             AS ObjetivoProgreso,
                oc.semaforo::text       AS ObjetivoSemaforo,
                p.codigo                AS PilarCodigo,
                p.nombre                AS PilarNombre,
                ar.id                   AS AreaId,
                ar.codigo               AS AreaCodigo,
                ar.nombre               AS AreaNombre,
                c.id                    AS CicloId,
                c.nombre                AS CicloNombre,
                c.año_fiscal            AS CicloAnioFiscal
            FROM accion_plan a
            JOIN objetivo_cg oc ON oc.id = a.objetivo_cg_id AND oc.tenant_id = a.tenant_id
            JOIN pilar p       ON p.id = oc.pilar_id       AND p.tenant_id = a.tenant_id
            JOIN area ar       ON ar.id = a.area_id         AND ar.tenant_id = a.tenant_id
            JOIN ciclo c       ON c.id = a.ciclo_id        AND c.tenant_id = a.tenant_id
            LEFT JOIN usuario u ON u.id = a.responsable_id
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id  = @CicloId
              AND (@AreaId       IS NULL OR a.area_id = @AreaId)
              AND (@CgId         IS NULL OR a.objetivo_cg_id = @CgId)
              AND (@Status       IS NULL OR a.status::text = @Status)
              AND (@Clasificacion IS NULL OR a.clasificacion::text = @Clasificacion)
              AND (@Tipo        IS NULL OR a.tipo_presupuesto::text = @Tipo)
              AND (@FechaDesde   IS NULL OR a.fecha_inicio >= @FechaDesde)
              AND (@FechaHasta   IS NULL OR a.fecha_inicio <= @FechaHasta)
            ORDER BY ar.codigo ASC, oc.codigo ASC, a.fecha_inicio ASC";

        if (!skipPaginacion)
        {
            sql += " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        }

        // ADR-015: DynamicParameters con DbType explícito. Los parámetros de fecha sobre
        // columnas DATE deben viajar como DbType.Date para evitar PostgresException 42P08
        // («could not determine data type of parameter») cuando son null en el patrón IS NULL OR.
        var parametros = ConstruirParametrosBase(tenantId, cicloId, filtros);
        parametros.Add("Offset", (filtros.Page - 1) * filtros.PageSize, DbType.Int32);
        parametros.Add("PageSize", filtros.PageSize, DbType.Int32);

        var result = await GetConnection().QueryAsync<ConsolidadoItemResponse>(sql, parametros);
        return result.AsList();
    }

    /// <summary>
    /// Query 2 — COUNT total con mismos WHERE.
    /// </summary>
    public async Task<int> CountConsolidadoAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT COUNT(*)
            FROM accion_plan a
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id  = @CicloId
              AND (@AreaId       IS NULL OR a.area_id = @AreaId)
              AND (@CgId         IS NULL OR a.objetivo_cg_id = @CgId)
              AND (@Status       IS NULL OR a.status::text = @Status)
              AND (@Clasificacion IS NULL OR a.clasificacion::text = @Clasificacion)
              AND (@Tipo        IS NULL OR a.tipo_presupuesto::text = @Tipo)
              AND (@FechaDesde   IS NULL OR a.fecha_inicio >= @FechaDesde)
              AND (@FechaHasta   IS NULL OR a.fecha_inicio <= @FechaHasta)";

        // ADR-015: DynamicParameters con DbType explícito para todos los parámetros,
        // especialmente DbType.Date en los filtros de fecha sobre columnas DATE.
        var parametros = ConstruirParametrosBase(tenantId, cicloId, filtros);

        return await GetConnection().ExecuteScalarAsync<int>(sql, parametros);
    }

    /// <summary>
    /// Query 3 — GROUP BY status (COUNT FILTER) para resumen.
    /// </summary>
    public async Task<ResumenConsolidado> ObtenerResumenAsync(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT
                COUNT(*) AS Total,
                COUNT(*) FILTER (WHERE a.status::text = 'NoIniciado')  AS NoIniciado,
                COUNT(*) FILTER (WHERE a.status::text = 'EnProgreso')  AS EnProgreso,
                COUNT(*) FILTER (WHERE a.status::text = 'Terminado')   AS Terminado,
                COUNT(*) FILTER (WHERE a.status::text = 'Atrasado')    AS Atrasado
            FROM accion_plan a
            WHERE a.tenant_id = @TenantId
              AND a.ciclo_id  = @CicloId
              AND (@AreaId       IS NULL OR a.area_id = @AreaId)
              AND (@CgId         IS NULL OR a.objetivo_cg_id = @CgId)
              AND (@Status       IS NULL OR a.status::text = @Status)
              AND (@Clasificacion IS NULL OR a.clasificacion::text = @Clasificacion)
              AND (@Tipo        IS NULL OR a.tipo_presupuesto::text = @Tipo)
              AND (@FechaDesde   IS NULL OR a.fecha_inicio >= @FechaDesde)
              AND (@FechaHasta   IS NULL OR a.fecha_inicio <= @FechaHasta)";

        // ADR-015: DynamicParameters con DbType explícito para todos los parámetros,
        // especialmente DbType.Date en los filtros de fecha sobre columnas DATE.
        var parametros = ConstruirParametrosBase(tenantId, cicloId, filtros);

        var resumen = await GetConnection().QueryFirstOrDefaultAsync<ResumenConsolidado>(sql, parametros);
        return resumen ?? new ResumenConsolidado();
    }

    /// <summary>
    /// Construye los 9 parámetros comunes a las 3 queries (ADR-015).
    /// Query 1 añade Offset/PageSize sobre este conjunto.
    /// </summary>
    private static DynamicParameters ConstruirParametrosBase(
        Guid tenantId, Guid cicloId, FiltrosConsolidadoRequest filtros)
    {
        var parametros = new DynamicParameters();
        parametros.Add("TenantId", tenantId, DbType.Guid);
        parametros.Add("CicloId", cicloId, DbType.Guid);
        parametros.Add("AreaId", filtros.AreaId, DbType.Guid);
        parametros.Add("CgId", filtros.CgId, DbType.Guid);
        parametros.Add("Status", filtros.Status, DbType.String);
        parametros.Add("Clasificacion", filtros.Clasificacion, DbType.String);
        parametros.Add("Tipo", filtros.Tipo, DbType.String);
        parametros.Add("FechaDesde", filtros.FechaDesde, DbType.Date);
        parametros.Add("FechaHasta", filtros.FechaHasta, DbType.Date);
        return parametros;
    }

    public void Dispose()
    {
        _connectionActiva?.Dispose();
    }
}

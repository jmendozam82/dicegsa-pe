using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de negocio para la vista consolidada del Plan (Spec HU-023 § Lógica BLL).
/// Solo lectura: sin escrituras, sin auditoría, sin transacción.
/// El wrapper ApiResponse&lt;T&gt; lo arma el controller (ARCH-07).
/// </summary>
public class PlanConsolidadoService : IPlanConsolidadoService
{
    private readonly IPlanConsolidadoRepository _repository;
    private readonly ICicloRepository _cicloRepository;
    private readonly TenantContext _tenantContext;

    public PlanConsolidadoService(
        IPlanConsolidadoRepository repository,
        ICicloRepository cicloRepository,
        TenantContext tenantContext)
    {
        _repository = repository;
        _cicloRepository = cicloRepository;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<ConsolidadoResponse> ObtenerConsolidadoAsync(
        FiltrosConsolidadoRequest filtros, CancellationToken ct = default)
    {
        // 1 · Tenant (SEC-06)
        var tenantId = ObtenerTenantIdOThrow();

        // 2 · Rol (RN-006): solo Gerente
        if (_tenantContext.Rol != "Gerente")
            throw new AccesoDenegadoException("Solo el Gerente puede ver el plan consolidado.");

        // 3 · Validar filtros
        ValidarFiltros(filtros);

        // 4 · Ciclo activo (RC-01)
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct)
            ?? throw new NotFoundException("No hay un ciclo activo para el tenant");

        // 5 · Query consolidada paginada + ordenamiento (DB-04: orden en BLL)
        var items = (await _repository.ListarConsolidadoAsync(tenantId, ciclo.Id, filtros, false, ct))
            .OrderBy(i => i.AreaCodigo)
            .ThenBy(i => i.ObjetivoCodigo)
            .ThenBy(i => i.FechaInicio)
            .ToList();

        // 6 · Count total
        var total = await _repository.CountConsolidadoAsync(tenantId, ciclo.Id, filtros, ct);

        // 7 · Resumen (DB-04): conteos por status con mismos filtros
        var resumen = await _repository.ObtenerResumenAsync(tenantId, ciclo.Id, filtros, ct);

        // 8 · Construir respuesta
        return new ConsolidadoResponse
        {
            Items = items,
            Resumen = resumen,
            Paginacion = new PaginacionInfo
            {
                Page = filtros.Page,
                PageSize = filtros.PageSize,
                TotalItems = total,
                TotalPages = (int)Math.Ceiling(total / (double)filtros.PageSize)
            }
        };
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportarConsolidadoAsync(
        FiltrosConsolidadoRequest filtros, CancellationToken ct = default)
    {
        // 1 · Tenant (SEC-06)
        var tenantId = ObtenerTenantIdOThrow();

        // 2 · Rol (RN-006): solo Gerente
        if (_tenantContext.Rol != "Gerente")
            throw new AccesoDenegadoException("Solo el Gerente puede exportar el plan consolidado.");

        // 3 · Validar filtros
        ValidarFiltros(filtros);

        // 4 · Ciclo activo (RC-01)
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct)
            ?? throw new NotFoundException("No hay un ciclo activo para el tenant");

        // 5 · Query sin paginación
        var todos = await _repository.ListarConsolidadoAsync(tenantId, ciclo.Id, filtros, true, ct);

        // 6 · Generar Excel con ClosedXML (STACK-08, ADR-04)
        return GenerarExcel(todos, filtros, ciclo);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("No se pudo determinar el tenant del usuario autenticado");
        return _tenantContext.TenantId.Value;
    }

    private static void ValidarFiltros(FiltrosConsolidadoRequest filtros)
    {
        if (filtros.Page < 1)
            throw new ValidacionException("La página debe ser mayor o igual a 1.");

        if (filtros.PageSize < 1 || filtros.PageSize > 100)
            throw new ValidacionException("El tamaño de página debe estar entre 1 y 100.");

        if (filtros.FechaDesde is not null && filtros.FechaHasta is not null && filtros.FechaDesde > filtros.FechaHasta)
            throw new ValidacionException("La fecha desde debe ser anterior o igual a la fecha hasta.");
    }

    private static byte[] GenerarExcel(
        List<ConsolidadoItemResponse> items, FiltrosConsolidadoRequest filtros, CicloEntity ciclo)
    {
        using var workbook = new XLWorkbook();

        // Hoja "Datos" — columnas tabulares
        var hojaDatos = workbook.Worksheets.Add("Datos");
        var encabezados = new[]
        {
            "Área", "Pilar", "CG", "Código Acción", "Descripción",
            "Fecha Inicio", "Fecha Vencimiento", "Status", "Clasificación",
            "Tipo", "Progreso %", "Peso", "Responsable"
        };

        for (var i = 0; i < encabezados.Length; i++)
            hojaDatos.Cell(1, i + 1).Value = encabezados[i];

        var fila = 2;
        foreach (var item in items)
        {
            hojaDatos.Cell(fila, 1).Value = item.AreaNombre;
            hojaDatos.Cell(fila, 2).Value = item.PilarNombre;
            hojaDatos.Cell(fila, 3).Value = item.ObjetivoCodigo;
            hojaDatos.Cell(fila, 4).Value = item.AccionCodigo;
            hojaDatos.Cell(fila, 5).Value = item.AccionDescripcion;
            hojaDatos.Cell(fila, 6).Value = item.FechaInicio.ToString("yyyy-MM-dd");
            hojaDatos.Cell(fila, 7).Value = item.FechaVencimiento.ToString("yyyy-MM-dd");
            hojaDatos.Cell(fila, 8).Value = item.Status;
            hojaDatos.Cell(fila, 9).Value = item.Clasificacion;
            hojaDatos.Cell(fila, 10).Value = item.TipoPresupuesto;
            hojaDatos.Cell(fila, 11).Value = item.Progreso;
            hojaDatos.Cell(fila, 12).Value = item.Peso;
            hojaDatos.Cell(fila, 13).Value = item.ResponsableNombre ?? string.Empty;
            fila++;
        }

        // Auto-ajustar columnas
        hojaDatos.Columns().AdjustToContents();

        // Hoja "Resumen" — 4 conteos + filtros aplicados
        var hojaResumen = workbook.Worksheets.Add("Resumen");
        hojaResumen.Cell(1, 1).Value = "Resumen del Plan Consolidado";
        hojaResumen.Cell(1, 1).Style.Font.Bold = true;

        hojaResumen.Cell(3, 1).Value = "Ciclo:";
        hojaResumen.Cell(3, 2).Value = ciclo.Nombre;
        hojaResumen.Cell(4, 1).Value = "Año Fiscal:";
        hojaResumen.Cell(4, 2).Value = ciclo.AñoFiscal;

        hojaResumen.Cell(6, 1).Value = "Status";
        hojaResumen.Cell(6, 2).Value = "Cantidad";
        hojaResumen.Cell(6, 1).Style.Font.Bold = true;
        hojaResumen.Cell(6, 2).Style.Font.Bold = true;

        // Conteos calculados en BLL sobre los items (DB-04)
        var total = items.Count;
        var noIniciado = items.Count(i => i.Status == "NoIniciado");
        var enProgreso = items.Count(i => i.Status == "EnProgreso");
        var terminado = items.Count(i => i.Status == "Terminado");
        var atrasado = items.Count(i => i.Status == "Atrasado");

        hojaResumen.Cell(7, 1).Value = "NoIniciado";
        hojaResumen.Cell(7, 2).Value = noIniciado;
        hojaResumen.Cell(8, 1).Value = "EnProgreso";
        hojaResumen.Cell(8, 2).Value = enProgreso;
        hojaResumen.Cell(9, 1).Value = "Terminado";
        hojaResumen.Cell(9, 2).Value = terminado;
        hojaResumen.Cell(10, 1).Value = "Atrasado";
        hojaResumen.Cell(10, 2).Value = atrasado;
        hojaResumen.Cell(11, 1).Value = "Total";
        hojaResumen.Cell(11, 2).Value = total;
        hojaResumen.Cell(11, 1).Style.Font.Bold = true;
        hojaResumen.Cell(11, 2).Style.Font.Bold = true;

        // Filtros aplicados como metadato
        hojaResumen.Cell(13, 1).Value = "Filtros aplicados:";
        hojaResumen.Cell(13, 1).Style.Font.Bold = true;
        hojaResumen.Cell(14, 1).Value = "Área:";
        hojaResumen.Cell(14, 2).Value = filtros.AreaId?.ToString() ?? "Todas";
        hojaResumen.Cell(15, 1).Value = "CG:";
        hojaResumen.Cell(15, 2).Value = filtros.CgId?.ToString() ?? "Todos";
        hojaResumen.Cell(16, 1).Value = "Status:";
        hojaResumen.Cell(16, 2).Value = filtros.Status ?? "Todos";
        hojaResumen.Cell(17, 1).Value = "Clasificación:";
        hojaResumen.Cell(17, 2).Value = filtros.Clasificacion ?? "Todas";
        hojaResumen.Cell(18, 1).Value = "Tipo:";
        hojaResumen.Cell(18, 2).Value = filtros.Tipo ?? "Todos";
        hojaResumen.Cell(19, 1).Value = "Fecha Desde:";
        hojaResumen.Cell(19, 2).Value = filtros.FechaDesde?.ToString("yyyy-MM-dd") ?? "—";
        hojaResumen.Cell(20, 1).Value = "Fecha Hasta:";
        hojaResumen.Cell(20, 2).Value = filtros.FechaHasta?.ToString("yyyy-MM-dd") ?? "—";

        hojaResumen.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

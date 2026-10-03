using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de registro mensual de valores reales de KRs (Spec HU-026 § Lógica BLL).
/// Ctor: (IValorMensualKrRepository, IOkrRepository, IKeyResultRepository, ICicloRepository,
/// ITenantRepository, TimeProvider, TenantContext) — patrón KeyResultService.
/// Doble compuerta SEC-07: el OKR padre se valida contra el área del JEF y el ciclo activo.
/// Los campos calculados (puntuaciones, semáforo) se calculan aquí y se persisten (DB-04).
/// </summary>
public class ValorMensualKrService : IValorMensualKrService
{
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "ValorMensualKR";
    private const string TipoUmbralOkr = "KPI";
    private const decimal UmbralVerdeDefault = 0.90m;
    private const decimal UmbralAmarilloDefault = 0.70m;
    private const int EscalaPuntuacion = 3;
    private const int EscalaValor = 1;
    private const int MesesPorTrimestre = 3;
    private static readonly TimeSpan OffsetFallbackPeru = TimeSpan.FromHours(-5);

    private static readonly string[] NombreMeses =
    {
        "ENE", "FEB", "MAR", "ABR", "MAY", "JUN",
        "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"
    };

    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IValorMensualKrRepository _repo;
    private readonly IOkrRepository _okrRepository;
    private readonly IKeyResultRepository _keyResultRepository;
    private readonly ICicloRepository _cicloRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly TimeProvider _timeProvider;
    private readonly TenantContext _tenantContext;

    public ValorMensualKrService(
        IValorMensualKrRepository repo,
        IOkrRepository okrRepository,
        IKeyResultRepository keyResultRepository,
        ICicloRepository cicloRepository,
        ITenantRepository tenantRepository,
        TimeProvider timeProvider,
        TenantContext tenantContext)
    {
        _repo = repo;
        _okrRepository = okrRepository;
        _keyResultRepository = keyResultRepository;
        _cicloRepository = cicloRepository;
        _tenantRepository = tenantRepository;
        _timeProvider = timeProvider;
        _tenantContext = tenantContext;
    }

    // --- Helpers de contexto / autorización ----------------------------------------------------

    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("Tenant no identificado en el contexto actual.");
        return _tenantContext.TenantId.Value;
    }

    private Guid ObtenerAreaIdOThrow()
    {
        if (_tenantContext.AreaId is null || _tenantContext.AreaId == Guid.Empty)
            throw new NotFoundException("El usuario no tiene un área asignada.");
        return _tenantContext.AreaId.Value;
    }

    private void ValidarPermisoEscritura()
    {
        ObtenerTenantIdOThrow();
        if (_tenantContext.Rol != RolJefeArea)
            throw new AccesoDenegadoException($"El rol {_tenantContext.Rol} no tiene permisos para registrar valores mensuales de Key Results.");
        ObtenerAreaIdOThrow();
    }

    private async Task<CicloEntity> ObtenerCicloActivoOThrowAsync(CancellationToken ct)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct);
        if (ciclo == null)
            throw new NotFoundException("No hay ningún ciclo activo para este tenant.");
        return ciclo;
    }

    /// <summary>
    /// Doble compuerta SEC-07: resuelve el OKR padre desde el área del JEF (sin fuga — RN-008)
    /// y verifica que pertenezca al ciclo activo. Se invoca en las 4 operaciones, lecturas incluidas.
    /// </summary>
    private async Task<OkrResponse> ObtenerOkrConAreaOThrowAsync(Guid okrId, CancellationToken ct)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();
        var ciclo = await ObtenerCicloActivoOThrowAsync(ct);

        var okr = await _okrRepository.ObtenerPorIdAsync(tenantId, areaId, okrId, ct);
        if (okr == null)
            throw new NotFoundException("El OKR no existe o no pertenece a su área.");
        if (okr.CicloId != ciclo.Id)
            throw new NotFoundException("El OKR no pertenece al ciclo activo.");

        return okr;
    }

    // --- Ventana de edición (F0 + F0c) ----------------------------------------------------------

    /// <summary>
    /// Resuelve la ventana de meses editables y los umbrales del ciclo (F0 + F0c).
    /// mesMin = ciclo.MesInicio · mesMax = 0 | mes(hoy) | 12 según el año fiscal.
    /// La fecha de negocio sale de TimeProvider + tenant.zona_horaria (determinista en tests).
    /// </summary>
    private async Task<(int MesMin, int MesMax, DateOnly FechaNegocio, decimal UmbralVerde, decimal UmbralAmarillo)>
        ObtenerVentanaEdicionAsync(CicloEntity ciclo, CancellationToken ct)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        var zona = ResolverZonaHoraria(tenant?.ZonaHoraria);
        var fechaNegocio = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), zona).Date;

        var mesMin = ciclo.MesInicio;
        int mesMax;
        if (ciclo.AñoFiscal > fechaNegocio.Year)
            mesMax = 0;        // ciclo del futuro: no hay meses editables
        else if (ciclo.AñoFiscal < fechaNegocio.Year)
            mesMax = 12;       // ciclo de un año anterior aún no cerrado
        else
            mesMax = fechaNegocio.Month;   // ciclo del año en curso: hasta el mes actual

        var umbrales = await _cicloRepository.ObtenerUmbralesAsync(tenantId, ciclo.Id, ct);
        var umbralKpi = umbrales.FirstOrDefault(u => u.Tipo == TipoUmbralOkr);
        var umbralVerde = umbralKpi?.UmbralVerde ?? UmbralVerdeDefault;
        var umbralAmarillo = umbralKpi?.UmbralAmarillo ?? UmbralAmarilloDefault;

        return (mesMin, mesMax, new DateOnly(fechaNegocio.Year, fechaNegocio.Month, fechaNegocio.Day), umbralVerde, umbralAmarillo);
    }

    private static TimeZoneInfo ResolverZonaHoraria(string? zona)
    {
        if (string.IsNullOrWhiteSpace(zona))
            return TimeZoneInfo.CreateCustomTimeZone("PE-GOL/PET", OffsetFallbackPeru, "PE-GOL/PET", "PE-GOL/PET");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zona);
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("PE-GOL/PET", OffsetFallbackPeru, "PE-GOL/PET", "PE-GOL/PET");
        }
    }

    private static bool EsMesEditable(int mes, int min, int max) => mes >= min && mes <= max;

    // --- Operaciones ---------------------------------------------------------------------------

    public async Task<ValorMensualKrGrillaResponse> ObtenerGrillaAsync(Guid okrId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();
        var okr = await ObtenerOkrConAreaOThrowAsync(okrId, ct);
        if (okr.AreaId != areaId)
            throw new NotFoundException("El OKR no existe o no pertenece a su área.");
        var ciclo = await ObtenerCicloActivoOThrowAsync(ct);
        var ventana = await ObtenerVentanaEdicionAsync(ciclo, ct);

        var krs = (await _keyResultRepository.ListarAsync(tenantId, okrId, ct)).ToList();
        var valores = (await _repo.ListarPorOkrAsync(tenantId, okrId, ct)).ToList();
        var valoresPorKr = valores.GroupBy(v => v.KeyResultId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(v => v.Mes, v => v.Valor));
        var okrPuntuacionFinal = Math.Round(krs.Sum(k => k.PuntuacionPonderada), EscalaPuntuacion, MidpointRounding.AwayFromZero);

        var filas = new List<ValorMensualKrFilaResponse>();
        foreach (var kr in krs)
        {
            var krValores = valoresPorKr.TryGetValue(kr.Id, out var dict) ? dict : new Dictionary<int, decimal>();
            filas.Add(ConstruirFila(kr, krValores, ventana.MesMin, ventana.MesMax, ventana.UmbralVerde, ventana.UmbralAmarillo));
        }

        var krsConValor = valores.Select(v => v.KeyResultId).Distinct().Count();

        return new ValorMensualKrGrillaResponse
        {
            OkrId = okr.Id,
            OkrCodigo = okr.Codigo,
            OkrDescripcion = okr.Descripcion,
            PilarNombre = okr.PilarNombre,
            CicloId = ciclo.Id,
            AnioFiscal = ciclo.AñoFiscal,
            MesInicio = ciclo.MesInicio,
            FechaNegocio = ventana.FechaNegocio,
            MesActual = ventana.FechaNegocio.Month,
            MesMinEditable = ventana.MesMin,
            MesMaxEditable = ventana.MesMax,
            MesesEditables = Enumerable.Range(ventana.MesMin, Math.Max(ventana.MesMax - ventana.MesMin + 1, 0)).ToList(),
            UmbralVerde = ventana.UmbralVerde,
            UmbralAmarillo = ventana.UmbralAmarillo,
            KRs = filas,
            CalculoOkr = new ValorMensualKrCalculoOkrResponse
            {
PuntuacionFinal = okrPuntuacionFinal,
                Semaforo = okr.Semaforo,
                KrsConValor = krsConValor
            }
        };
    }

    public async Task<ValorMensualKrGuardarResponse> GuardarValoresAsync(Guid okrId, Guid keyResultId, ValorMensualKrUpdateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();

        var ciclo = await ObtenerCicloActivoOThrowAsync(ct);
        if (ciclo.Estado == "Cerrado")
            throw new ValidacionException("El ciclo activo se encuentra cerrado. No se pueden registrar valores mensuales.");

        var okr = await ObtenerOkrConAreaOThrowAsync(okrId, ct);
        var kr = await _keyResultRepository.ObtenerPorIdAsync(tenantId, okrId, keyResultId, ct);
        if (kr == null)
            throw new NotFoundException("El Key Result no existe o no pertenece a este OKR.");

        ValidarVector(request);
        var ventana = await ObtenerVentanaEdicionAsync(ciclo, ct);
        ValidarVentana(request, ventana.MesMin, ventana.MesMax);

        // Snapshot previo (solo los meses del vector) para la auditoría (F5).
        var previos = (await _repo.ListarPorKrAsync(tenantId, okrId, keyResultId, ct))
            .ToDictionary(v => v.Mes, v => v.Valor);

        using var tx = await _repo.BeginTransactionAsync(ct);
        (decimal PuntuacionFinal, string Semaforo) calculoOkr;
        try
        {
            foreach (var item in request.Valores)
                await _repo.UpsertAsync(tenantId, keyResultId, item.Mes, item.Valor, _tenantContext.UserId ?? Guid.Empty, tx, ct);

            await RecalcularKeyResultAsync(tenantId, okrId, keyResultId, kr.Peso, tx, ct);
            calculoOkr = await RecalcularOkrAsync(tenantId, areaId, okrId, ventana.UmbralVerde, ventana.UmbralAmarillo, tx, ct);

            // Auditoría (ADR-003 · F5): UNA entrada con el delta antes/después.
            var algunMesNuevo = request.Valores.Any(v => !previos.ContainsKey(v.Mes));
            var accion = algunMesNuevo ? "CREATE" : "UPDATE";
            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = accion,
                Entidad = EntidadAuditoria,
                EntidadId = keyResultId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { krId = keyResultId, codigo = kr.Codigo, meses = MesesSnapshot(request, previos) }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { krId = keyResultId, codigo = kr.Codigo, meses = MesesNuevos(request) }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23514")
        {
            tx.Rollback();
            throw new ValidacionException("Valor fuera del rango permitido (0.0–1.0).");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            tx.Rollback();
            throw new ValidacionException("Conflicto al registrar el valor del mes. Intente de nuevo.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return await ConstruirRespuestaAsync(tenantId, okrId, keyResultId, ventana.MesMin, ventana.MesMax, ventana.UmbralVerde, ventana.UmbralAmarillo, calculoOkr, ct);
    }

    public async Task<ValorMensualKrGuardarResponse> EliminarValorAsync(Guid okrId, Guid keyResultId, int mes, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();

        var ciclo = await ObtenerCicloActivoOThrowAsync(ct);
        if (ciclo.Estado == "Cerrado")
            throw new ValidacionException("El ciclo activo se encuentra cerrado. No se pueden registrar valores mensuales.");

        var okr = await ObtenerOkrConAreaOThrowAsync(okrId, ct);
        var kr = await _keyResultRepository.ObtenerPorIdAsync(tenantId, okrId, keyResultId, ct);
        if (kr == null)
            throw new NotFoundException("El Key Result no existe o no pertenece a este OKR.");

        if (mes < 1 || mes > 12)
            throw new ValidacionException("El mes debe estar entre 1 (enero) y 12 (diciembre).");

        var valorExistente = await _repo.ObtenerValorAsync(tenantId, okrId, keyResultId, mes, ct);
        if (valorExistente == null)
            throw new NotFoundException("No hay un valor registrado para ese mes.");

        var ventana = await ObtenerVentanaEdicionAsync(ciclo, ct);

        using var tx = await _repo.BeginTransactionAsync(ct);
        (decimal PuntuacionFinal, string Semaforo) calculoOkr;
        try
        {
            await _repo.EliminarAsync(tenantId, okrId, keyResultId, mes, tx, ct);
            await RecalcularKeyResultAsync(tenantId, okrId, keyResultId, kr.Peso, tx, ct);
            calculoOkr = await RecalcularOkrAsync(tenantId, areaId, okrId, ventana.UmbralVerde, ventana.UmbralAmarillo, tx, ct);

            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DELETE",
                Entidad = EntidadAuditoria,
                EntidadId = keyResultId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { mes, valor = valorExistente.Valor }, JsonOpcionesAuditoria),
                ValorNuevo = null
            }, tx, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return await ConstruirRespuestaAsync(tenantId, okrId, keyResultId, ventana.MesMin, ventana.MesMax, ventana.UmbralVerde, ventana.UmbralAmarillo, calculoOkr, ct);
    }

    public async Task<byte[]> ExportarAsync(Guid okrId, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();
        var okr = await ObtenerOkrConAreaOThrowAsync(okrId, ct);
        if (okr.AreaId != areaId)
            throw new NotFoundException("El OKR no existe o no pertenece a su área.");
        var ciclo = await ObtenerCicloActivoOThrowAsync(ct);
        var ventana = await ObtenerVentanaEdicionAsync(ciclo, ct);

        var krs = (await _keyResultRepository.ListarAsync(tenantId, okrId, ct)).ToList();
        var valores = (await _repo.ListarPorOkrAsync(tenantId, okrId, ct)).ToList();

        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add(okr.Codigo);

            var headers = new[]
            {
                "KR", "Descripción", "Peso",
                "ENE", "FEB", "MAR", "ABR", "MAY", "JUN",
                "JUL", "AGO", "SEP", "OCT", "NOV", "DIC",
                "Q1", "Q2", "Q3", "Q4", "Final", "Ponderada", "Semaforo"
            };
            for (var i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var row = 2;
            foreach (var kr in krs)
            {
                ws.Cell(row, 1).Value = kr.Codigo;
                ws.Cell(row, 2).Value = kr.Descripcion;
                ws.Cell(row, 3).Value = (double)kr.Peso;

                var krValores = valores.Where(v => v.KeyResultId == kr.Id).ToDictionary(v => v.Mes, v => v.Valor);
                for (var m = 1; m <= 12; m++)
                {
                    if (krValores.ContainsKey(m))
                        ws.Cell(row, 3 + m).Value = (double)krValores[m];
                }

                ws.Cell(row, 16).Value = (double)kr.PuntuacionQ1;
                ws.Cell(row, 17).Value = (double)kr.PuntuacionQ2;
                ws.Cell(row, 18).Value = (double)kr.PuntuacionQ3;
                ws.Cell(row, 19).Value = (double)kr.PuntuacionQ4;
                ws.Cell(row, 20).Value = (double)kr.PuntuacionFinal;
                ws.Cell(row, 21).Value = (double)kr.PuntuacionPonderada;
                ws.Cell(row, 22).Value = SemaforoHelper.Evaluar(kr.PuntuacionFinal, ventana.UmbralVerde, ventana.UmbralAmarillo);
                row++;
            }

            wb.SaveAs(ms);
        }

        return ms.ToArray();
    }

    // --- Validaciones -------------------------------------------------------------------------

    private static void ValidarVector(ValorMensualKrUpdateRequest request)
    {
        if (request.Valores is null || request.Valores.Count == 0)
            throw new ValidacionException("Debe enviar al menos un valor mensual.");
        if (request.Valores.Count > 12)
            throw new ValidacionException("Un KR tiene como máximo 12 meses (enero–diciembre).");

        var repetidos = request.Valores.GroupBy(v => v.Mes).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Any())
            throw new ValidacionException($"No se puede enviar el mismo mes más de una vez. Meses repetidos: {string.Join(", ", repetidos)}.");

        foreach (var item in request.Valores)
        {
            if (item.Mes < 1 || item.Mes > 12)
                throw new ValidacionException("El mes debe estar entre 1 (enero) y 12 (diciembre).");
            if (item.Valor < 0.0m || item.Valor > 1.0m)
                throw new ValidacionException("El valor debe estar entre 0.0 y 1.0.");
            if (decimal.Round(item.Valor, EscalaValor, MidpointRounding.ToZero) != item.Valor)
                throw new ValidacionException("El valor admite como máximo 1 decimal (por ejemplo 0.5).");
        }
    }

    private static void ValidarVentana(ValorMensualKrUpdateRequest request, int mesMin, int mesMax)
    {
        if (mesMin > mesMax)
            throw new ValidacionException("El ciclo aún no se ha iniciado; no hay meses editables.");

        foreach (var item in request.Valores)
        {
            if (item.Mes < mesMin)
                throw new ValidacionException($"El mes {item.Mes} ({NombreMeses[item.Mes - 1]}) es anterior al inicio del ciclo (mes {mesMin}).");
            if (item.Mes > mesMax)
                throw new ValidacionException($"El mes {item.Mes} ({NombreMeses[item.Mes - 1]}) aún no ha ocurrido y no puede registrarse.");
        }
    }

    // --- Cálculo (DB-04) ---------------------------------------------------------------------

    /// <summary>
    /// Recalcula las 6 columnas de puntuación del KR a partir de TODOS sus valores (DB-04).
    /// Q = promedio de los meses CON valor del trimestre (RN-025) · Final = promedio de los
    /// trimestres con datos (F1 Opción B) · Ponderada = final × peso (RF-037).
    /// </summary>
    private async Task RecalcularKeyResultAsync(Guid tenantId, Guid okrId, Guid keyResultId, decimal peso, IDbTransaction tx, CancellationToken ct)
    {
        var valores = await _repo.ListarPorKrAsync(tenantId, okrId, keyResultId, ct);
        var porMes = valores.ToDictionary(v => v.Mes, v => v.Valor);

        var puntuaciones = new decimal[4];
        var trimestresConDatos = new List<decimal>();
        for (var q = 1; q <= 4; q++)
        {
            var meses = Enumerable.Range(q * MesesPorTrimestre - 2, MesesPorTrimestre);
            var conValor = meses.Where(m => porMes.ContainsKey(m)).Select(m => porMes[m]).ToList();
            if (conValor.Any())
            {
                var puntuacion = Math.Round(conValor.Sum() / conValor.Count, EscalaPuntuacion, MidpointRounding.AwayFromZero);
                puntuaciones[q - 1] = puntuacion;
                trimestresConDatos.Add(puntuacion);
            }
            else
            {
                puntuaciones[q - 1] = 0.000m;
            }
        }

        var puntuacionFinal = trimestresConDatos.Any()
            ? Math.Round(trimestresConDatos.Average(), EscalaPuntuacion, MidpointRounding.AwayFromZero)
            : 0.000m;
        var puntuacionPonderada = Math.Round(puntuacionFinal * peso, EscalaPuntuacion, MidpointRounding.AwayFromZero);

        await _repo.ActualizarPuntuacionesKrAsync(tenantId, okrId, keyResultId,
            puntuaciones[0], puntuaciones[1], puntuaciones[2], puntuaciones[3],
            puntuacionFinal, puntuacionPonderada, tx, ct);
    }

    /// <summary>
    /// Recalcula la puntuación final del OKR como la suma de las ponderadas persistidas (RN-026)
    /// y evalúa el semáforo con los umbrales del ciclo (RN-027).
    /// </summary>
    private async Task<(decimal PuntuacionFinal, string Semaforo)> RecalcularOkrAsync(
        Guid tenantId, Guid areaId, Guid okrId, decimal umbralVerde, decimal umbralAmarillo, IDbTransaction tx, CancellationToken ct)
    {
        var krs = await _keyResultRepository.ListarAsync(tenantId, okrId, ct);
        var okrPuntuacionFinal = Math.Round(krs.Sum(k => k.PuntuacionPonderada), EscalaPuntuacion, MidpointRounding.AwayFromZero);
        var okrSemaforo = SemaforoHelper.Evaluar(okrPuntuacionFinal, umbralVerde, umbralAmarillo);
        await _okrRepository.ActualizarPuntuacionOkrAsync(tenantId, areaId, okrId, okrPuntuacionFinal, okrSemaforo, tx, ct);
        return (okrPuntuacionFinal, okrSemaforo);
    }

    // --- Construcción de respuestas -------------------------------------------------------------

    private async Task<ValorMensualKrGuardarResponse> ConstruirRespuestaAsync(
        Guid tenantId, Guid okrId, Guid keyResultId,
        int mesMin, int mesMax, decimal umbralVerde, decimal umbralAmarillo,
        (decimal PuntuacionFinal, string Semaforo) calculoOkr, CancellationToken ct)
    {
        var kr = await _keyResultRepository.ObtenerPorIdAsync(tenantId, okrId, keyResultId, ct);
        var valores = await _repo.ListarPorKrAsync(tenantId, okrId, keyResultId, ct);
        var porMes = valores.ToDictionary(v => v.Mes, v => v.Valor);

        var fila = ConstruirFila(kr!, porMes, mesMin, mesMax, umbralVerde, umbralAmarillo);
        var krsConValor = await _repo.ListarPorOkrAsync(tenantId, okrId, ct);

        return new ValorMensualKrGuardarResponse
        {
            KeyResult = fila,
            CalculoOkr = new ValorMensualKrCalculoOkrResponse
            {
                PuntuacionFinal = calculoOkr.PuntuacionFinal,
                Semaforo = calculoOkr.Semaforo,
                KrsConValor = krsConValor.Select(v => v.KeyResultId).Distinct().Count()
            }
        };
    }

    private static ValorMensualKrFilaResponse ConstruirFila(
        KeyResultResponse kr, Dictionary<int, decimal> porMes,
        int mesMin, int mesMax, decimal umbralVerde, decimal umbralAmarillo)
    {
        var celdas = new List<ValorMensualKrCeldaResponse>();
        for (var m = 1; m <= 12; m++)
        {
            var editable = EsMesEditable(m, mesMin, mesMax);
            string? motivo = null;
            if (!editable)
                motivo = m < mesMin ? "Mes anterior al inicio del ciclo" : "Mes aún no ocurrido";

            celdas.Add(new ValorMensualKrCeldaResponse
            {
                Mes = m,
                Nombre = NombreMeses[m - 1],
                Registrado = porMes.ContainsKey(m),
                Valor = porMes.ContainsKey(m) ? porMes[m] : null,
                Editable = editable,
                MotivoBloqueo = motivo
            });
        }

        return new ValorMensualKrFilaResponse
        {
            KeyResultId = kr.Id,
            Codigo = kr.Codigo,
            Descripcion = kr.Descripcion,
            Peso = kr.Peso,
            Meses = celdas,
            MesesConValor = porMes.Count,
            PuntuacionQ1 = kr.PuntuacionQ1,
            PuntuacionQ2 = kr.PuntuacionQ2,
            PuntuacionQ3 = kr.PuntuacionQ3,
            PuntuacionQ4 = kr.PuntuacionQ4,
            PuntuacionFinal = kr.PuntuacionFinal,
            PuntuacionPonderada = kr.PuntuacionPonderada,
            Semaforo = SemaforoHelper.Evaluar(kr.PuntuacionFinal, umbralVerde, umbralAmarillo)
        };
    }

    private static Dictionary<string, decimal?> MesesSnapshot(ValorMensualKrUpdateRequest request, Dictionary<int, decimal> previos)
    {
        var meses = new Dictionary<string, decimal?>();
        foreach (var item in request.Valores)
            meses[item.Mes.ToString()] = previos.ContainsKey(item.Mes) ? previos[item.Mes] : null;
        return meses;
    }

    private static Dictionary<string, decimal?> MesesNuevos(ValorMensualKrUpdateRequest request)
    {
        var meses = new Dictionary<string, decimal?>();
        foreach (var item in request.Valores)
            meses[item.Mes.ToString()] = item.Valor;
        return meses;
    }
}

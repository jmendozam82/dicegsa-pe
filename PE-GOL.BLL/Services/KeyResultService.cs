using System.Text.Encodings.Web;
using System.Text.Json;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de Key Results de un OKR (Spec HU-025 � L�gica BLL).
/// Ctor: (IKeyResultRepository, IOkrRepository, ICicloRepository, IPilarService, TenantContext) � patr�n OkrService.
/// Doble compuerta SEC-07: el OKR padre se valida contra el �rea del JEF y el ciclo activo.
/// El invariante S pesos = 1.000 se aplica SOLO en ActualizarPesosAsync (F0, Opci�n B).
/// </summary>
public class KeyResultService : IKeyResultService
{
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "KeyResult";
    private const int MaxKeyResultsPorOkr = 5;          // CA #2 / RN-022 (F2)
    private const int MaxLongitudDescripcion = 500;
    private const decimal PesoMinimoExclusivo = 0.000m;
    private const decimal PesoMaximo = 1.000m;
    private const int EscalaPeso = 3;
    private const decimal SumaPesosRequerida = 1.000m;  // CA #3 / RC-06

    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IKeyResultRepository _repo;
    private readonly IOkrRepository _okrRepository;
    private readonly ICicloRepository _cicloRepository;
    private readonly TenantContext _tenantContext;

    public KeyResultService(
        IKeyResultRepository repo,
        IOkrRepository okrRepository,
        ICicloRepository cicloRepository,
        TenantContext tenantContext)
    {
        _repo = repo;
        _okrRepository = okrRepository;
        _cicloRepository = cicloRepository;
        _tenantContext = tenantContext;
    }

    // --- Helpers de contexto / autorizaci�n ----------------------------------------------------

    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("Tenant no identificado en el contexto actual.");
        return _tenantContext.TenantId.Value;
    }

    private Guid ObtenerAreaIdOThrow()
    {
        if (_tenantContext.AreaId is null || _tenantContext.AreaId == Guid.Empty)
            throw new NotFoundException("El usuario no tiene un �rea asignada.");
        return _tenantContext.AreaId.Value;
    }

    private void ValidarPermisoEscritura()
    {
        ObtenerTenantIdOThrow();
        if (_tenantContext.Rol != RolJefeArea)
            throw new AccesoDenegadoException($"El rol {_tenantContext.Rol} no tiene permisos para modificar Key Results del �rea.");
        ObtenerAreaIdOThrow();
    }

    private async Task<Guid> ObtenerCicloActivoIdAsync(CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct);
        if (ciclo == null)
            throw new NotFoundException("No hay ning�n ciclo activo para este tenant.");
        return ciclo.Id;
    }

    private async Task<Guid> ObtenerCicloActivoYVerificarEstadoAsync(bool soloLectura, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct);
        if (ciclo == null)
            throw new NotFoundException("No hay ning�n ciclo activo para este tenant.");

        if (!soloLectura && ciclo.Estado == "Cerrado")
            throw new ValidacionException("El ciclo activo se encuentra cerrado. No se pueden modificar los Key Results.");

        return ciclo.Id;
    }

    /// <summary>
    /// Doble compuerta SEC-07: resuelve el OKR padre desde el �rea del JEF (sin fuga � RN-008)
    /// y verifica que pertenezca al ciclo activo. Se invoca en las 6 operaciones, lecturas incluidas.
    /// </summary>
    private async Task<OkrResponse> ObtenerOkrPadreOThrowAsync(Guid okrId, CancellationToken ct)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var areaId = ObtenerAreaIdOThrow();
        var cicloId = await ObtenerCicloActivoIdAsync(ct);

        var okr = await _okrRepository.ObtenerPorIdAsync(tenantId, areaId, okrId, ct);
        if (okr == null)
            throw new NotFoundException("El OKR no existe o no pertenece a su �rea.");
        if (okr.CicloId != cicloId)
            throw new NotFoundException("El OKR no pertenece al ciclo activo.");

        return okr;
    }

    // --- Helpers de validaci�n -----------------------------------------------------------------

    private static void ValidarDescripcionYTexto(string descripcion)
    {
        var trimmed = descripcion?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            throw new ValidacionException("La descripci�n del Key Result es obligatoria.");
        if (trimmed.Length > MaxLongitudDescripcion)
            throw new ValidacionException("La descripci�n no puede exceder los 500 caracteres.");
    }

    /// <summary>�nica validaci�n de peso del POST/PUT singular (F0): rango (0, 1] y escala = 3.</summary>
    private static void ValidarPeso(decimal peso)
    {
        if (peso <= PesoMinimoExclusivo)
            throw new ValidacionException("El peso debe ser mayor que 0.");
        if (peso > PesoMaximo)
            throw new ValidacionException("El peso no puede ser mayor que 1.000 (100%).");
        if (decimal.Round(peso, EscalaPeso) != peso)
            throw new ValidacionException("El peso admite hasta 3 decimales (por ejemplo 0.250).");
    }

    /// <summary>CA #3 (regla dura). Se invoca SOLO desde ActualizarPesosAsync (F0).</summary>
    private static void ValidarSumaPesos(decimal suma, Guid okrId)
    {
        if (suma != SumaPesosRequerida)
            throw new ValidacionException($"La suma de los pesos de los Key Results debe ser exactamente 1.000 (100%); la suma actual es {suma:0.000}.");
    }

    /// <summary>
    /// Valida que el vector recibido cubra exactamente el conjunto de KRs del OKR (mismos ids y
    /// cantidad). Cubre el caso de un id ajeno sin revelar si ese id existe (SEC-07/RN-008).
    /// </summary>
    private static void ValidarCoberturaVector(List<KeyResultPesoRequest> pesos, List<Guid> idsEsperados, Guid okrId)
    {
        var idsRecibidos = pesos.Select(p => p.Id).ToList();
        var hayRepetidos = idsRecibidos.Distinct().Count() != idsRecibidos.Count;
        var cubreExacto = idsRecibidos.Count == idsEsperados.Count
                          && !hayRepetidos
                          && idsRecibidos.All(id => idsEsperados.Contains(id));

        if (!cubreExacto)
            throw new ValidacionException($"La lista de pesos debe contener exactamente los {idsEsperados.Count} Key Results del OKR (ids y cantidad).");
    }

    // --- Operaciones ---------------------------------------------------------------------------

    public async Task<IEnumerable<KeyResultResponse>> ListarAsync(Guid okrId, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        _ = ObtenerAreaIdOThrow();
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        return await _repo.ListarAsync(tenantId, okrId, ct);
    }

    public async Task<KeyResultResponse> ObtenerPorIdAsync(Guid okrId, Guid id, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        _ = ObtenerAreaIdOThrow();
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        var kr = await _repo.ObtenerPorIdAsync(tenantId, okrId, id, ct);
        if (kr == null)
            throw new NotFoundException("El Key Result no existe o no pertenece a este OKR.");

        return kr;
    }

    public async Task<KeyResultResponse> CrearAsync(Guid okrId, KeyResultCreateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        // CA #2: m�ximo 5 KRs por OKR (regla blanda � carrera TOCTOU aceptada, F2).
        var conteo = await _repo.ContarKeyResultsAsync(tenantId, okrId, ct);
        if (conteo.Cantidad >= MaxKeyResultsPorOkr)
            throw new ValidacionException("M�ximo 5 Key Results por OKR.");

        ValidarDescripcionYTexto(request.Descripcion);
        ValidarPeso(request.Peso);

        // CA #3 � el POST singular NO valida la suma de pesos (F0, Opci�n B aprobada).

        // CA #1 (F4): c�digo KR.N secuencial por OKR.
        var sec = await _repo.ObtenerSiguienteSecuenciaKeyResultAsync(tenantId, okrId, ct);
        var codigo = $"KR.{sec.SiguienteN}";
        var orden = sec.SiguienteOrden;

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repo.CrearAsync(tenantId, okrId, codigo, orden, request, tx, ct);
            var creado = await ObtenerPorIdAsync(okrId, nuevoId, ct);

            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = JsonSerializer.Serialize(creado, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();
            return creado;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            tx.Rollback();
            throw new ValidacionException("Hubo un conflicto generando el c�digo correlativo. Por favor intente de nuevo.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<KeyResultResponse> ActualizarAsync(Guid okrId, Guid id, KeyResultUpdateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        var actual = await ObtenerPorIdAsync(okrId, id, ct);

        ValidarDescripcionYTexto(request.Descripcion);
        ValidarPeso(request.Peso);

        // CA #3 � el PUT singular NO valida la suma (F0, Opci�n B aprobada).

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            await _repo.ActualizarAsync(tenantId, okrId, id, request, tx, ct);
            var actualizado = await ObtenerPorIdAsync(okrId, id, ct);

            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(actual, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(actualizado, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();
            return actualizado;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ActualizarPesosAsync(Guid okrId, KeyResultPesosUpdateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        // Fotograf�a previa (fuera de la transacci�n): alimenta el snapshot de auditor�a.
        var actuales = (await _repo.ListarAsync(tenantId, okrId, ct)).ToList();
        if (actuales.Count == 0)
            throw new ValidacionException("No hay Key Results en este OKR a los que asignar pesos.");

        var idsEsperados = actuales.Select(x => x.Id).ToList();
        ValidarCoberturaVector(request.Pesos, idsEsperados, okrId);

        foreach (var item in request.Pesos)
            ValidarPeso(item.Peso);

        // CA #3 (dura, sobre el conjunto completo) � �nico lugar del sistema donde se aplica (F0).
        var suma = Math.Round(request.Pesos.Sum(p => p.Peso), 3, MidpointRounding.AwayFromZero);
        ValidarSumaPesos(suma, okrId);

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            await _repo.ActualizarPesosAsync(tenantId, okrId, request.Pesos, tx, ct);

            // Verificaci�n de integridad ANTES de confirmar (precedente hotfix v2 HU-022, defecto M).
            var sumaPersistida = (await _repo.ObtenerSumaPesosAsync(tenantId, okrId, ct)).SumaPesos;
            if (Math.Round(sumaPersistida, 3, MidpointRounding.AwayFromZero) != SumaPesosRequerida)
                throw new ValidacionException($"La suma de los pesos de los Key Results debe ser exactamente 1.000 (100%); la suma actual es {sumaPersistida:0.000}.");

            // Auditor�a (ADR-003): UNA sola entrada con el reparto completo (granularidad = operaci�n).
            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = okrId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(actuales, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(request.Pesos, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task EliminarAsync(Guid okrId, Guid id, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        var tenantId = ObtenerTenantIdOThrow();
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);
        await ObtenerOkrPadreOThrowAsync(okrId, ct);

        var actual = await ObtenerPorIdAsync(okrId, id, ct);

        // CA #4 (F3): el DELETE bloquea SOLO por valores reales; NO re-valida S.
        var tieneValores = await _repo.VerificarValoresRealesAsync(tenantId, id, ct);
        if (tieneValores)
            throw new ValidacionException("No se puede eliminar el Key Result porque tiene valores reales registrados en el ciclo activo. Primero deben borrarse los valores del per�odo.");

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            await _repo.EliminarAsync(tenantId, okrId, id, tx, ct);

            await _repo.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DELETE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(actual, JsonOpcionesAuditoria),
                ValorNuevo = null
            }, tx, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}

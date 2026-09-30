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
/// Servicio de OKRs del área (Spec HU-024 § Lógica BLL).
/// Ctor: (IOkrRepository, ICicloRepository, IPilarService, TenantContext).
/// </summary>
public class OkrService : IOkrService
{
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "Okr";
    private const int MaxOkrsPorArea = 9;
    private const int MaxLongitudDescripcion = 500;

    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IOkrRepository _repo;
    private readonly ICicloRepository _cicloRepository;
    private readonly IPilarService _pilarService;
    private readonly TenantContext _tenantContext;

    public OkrService(
        IOkrRepository repo,
        ICicloRepository cicloRepository,
        IPilarService pilarService,
        TenantContext tenantContext)
    {
        _repo = repo;
        _cicloRepository = cicloRepository;
        _pilarService = pilarService;
        _tenantContext = tenantContext;
    }

    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("Tenant no identificado en el contexto actual.");
        return _tenantContext.TenantId.Value;
    }

    private void ValidarPermisoEscritura()
    {
        ObtenerTenantIdOThrow();
        if (_tenantContext.Rol != RolJefeArea)
            throw new AccesoDenegadoException($"El rol {_tenantContext.Rol} no tiene permisos para modificar OKRs del área.");

        if (_tenantContext.AreaId is null)
            throw new NotFoundException("El usuario no tiene un área asignada.");
    }

    private async Task<Guid> ObtenerCicloActivoIdAsync(CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct);
        if (ciclo == null)
            throw new NotFoundException("No hay ningún ciclo activo para este tenant.");
        return ciclo.Id;
    }

    private async Task<Guid> ObtenerCicloActivoYVerificarEstadoAsync(bool soloLectura, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();
        var ciclo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId, ct);
        if (ciclo == null)
            throw new NotFoundException("No hay ningún ciclo activo para este tenant.");

        if (!soloLectura && ciclo.Estado == "Cerrado")
            throw new ValidacionException("El ciclo activo se encuentra cerrado. No se pueden modificar los objetivos estratégicos.");

        return ciclo.Id;
    }

    private static void ValidarDescripcion(string descripcion)
    {
        var trimmed = descripcion?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            throw new ValidacionException("La descripción del objetivo es obligatoria.");
        if (trimmed.Length > MaxLongitudDescripcion)
            throw new ValidacionException("La descripción no puede exceder los 500 caracteres.");
    }

    private async Task ValidarPilarAsync(Guid cicloId, Guid pilarId, CancellationToken ct)
    {
        try
        {
            await _pilarService.ObtenerAsync(cicloId, pilarId, ct);
        }
        catch (NotFoundException)
        {
            throw new NotFoundException("El pilar estratégico especificado no existe en este ciclo.");
        }
    }

    public async Task<IEnumerable<OkrResponse>> ListarAsync(CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        if (_tenantContext.AreaId is null)
            throw new NotFoundException("El usuario no tiene un área asignada.");

        var cicloId = await ObtenerCicloActivoIdAsync(ct);
        var areaId = _tenantContext.AreaId.Value;

        return await _repo.ListarAsync(tenantId, cicloId, areaId, ct);
    }

    public async Task<OkrResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        if (_tenantContext.AreaId is null)
            throw new NotFoundException("El usuario no tiene un área asignada.");

        var areaId = _tenantContext.AreaId.Value;
        var okr = await _repo.ObtenerPorIdAsync(tenantId, areaId, id, ct);
        if (okr == null)
            throw new NotFoundException("El OKR no existe o no pertenece a su área.");

        return okr;
    }

    public async Task<OkrResponse> CrearAsync(OkrCreateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        ValidarDescripcion(request.Descripcion);

        var tenantId = ObtenerTenantIdOThrow();
        var areaId = _tenantContext.AreaId!.Value;
        var cicloId = await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);

        await ValidarPilarAsync(cicloId, request.PilarId, ct);

        var count = await _repo.ContarOkrsPorAreaAsync(tenantId, cicloId, areaId, ct);
        if (count >= MaxOkrsPorArea)
            throw new ValidacionException("Máximo 9 OKRs por área por ciclo.");

        var sec = await _repo.ObtenerSiguienteSecuenciaOkrAsync(tenantId, cicloId, areaId, ct);
        var codigo = $"OKR.{sec.SiguienteN}";
        var orden = sec.SiguienteOrden;

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            var nuevoId = await _repo.CrearAsync(tenantId, cicloId, areaId, codigo, orden, request, tx, ct);
            var creado = await ObtenerPorIdAsync(nuevoId, ct);

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
            throw new ValidacionException("Hubo un conflicto generando el código correlativo. Por favor intente de nuevo.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<OkrResponse> ActualizarAsync(Guid id, OkrUpdateRequest request, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();
        ValidarDescripcion(request.Descripcion);

        var tenantId = ObtenerTenantIdOThrow();
        var areaId = _tenantContext.AreaId!.Value;
        var cicloId = await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);

        var actual = await ObtenerPorIdAsync(id, ct);

        if (actual.PilarId != request.PilarId)
            await ValidarPilarAsync(cicloId, request.PilarId, ct);

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            await _repo.ActualizarAsync(tenantId, areaId, id, request, tx, ct);
            var actualizado = await ObtenerPorIdAsync(id, ct);

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

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        ValidarPermisoEscritura();

        var tenantId = ObtenerTenantIdOThrow();
        var areaId = _tenantContext.AreaId!.Value;
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false, ct);

        var actual = await ObtenerPorIdAsync(id, ct);

        var tieneValores = await _repo.VerificarKrsConValoresAsync(tenantId, id, ct);
        if (tieneValores)
            throw new ValidacionException("No se puede eliminar el OKR porque tiene KRs con valores reales registrados. Primero deben borrarse los valores del período.");

        using var tx = await _repo.BeginTransactionAsync(ct);
        try
        {
            await _repo.EliminarAsync(tenantId, areaId, id, tx, ct);

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

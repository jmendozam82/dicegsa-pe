using System.Text.Encodings.Web;
using System.Text.Json;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Security;

namespace PE_GOL.BLL.Services;

public class ObjetivoCgService : IObjetivoCgService
{
    private const string RolJefeArea = "JefeArea";
    private const string EntidadAuditoria = "ObjetivoCg";

    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IObjetivoCgRepository _repo;
    private readonly ICicloRepository _cicloRepository;
    private readonly IPilarService _pilarService;
    private readonly TenantContext _tenantContext;

    public ObjetivoCgService(
        IObjetivoCgRepository repo,
        ICicloRepository cicloRepository,
        IPilarService pilarService,
        TenantContext tenantContext)
    {
        _repo = repo;
        _cicloRepository = cicloRepository;
        _pilarService = pilarService;
        _tenantContext = tenantContext;
    }

    private void ValidarTenantContext()
    {
        if (_tenantContext.TenantId == Guid.Empty)
            throw new NotFoundException("Tenant no identificado en el contexto actual.");
    }

    private void ValidarPermisoEscritura()
    {
        ValidarTenantContext();
        if (_tenantContext.Rol != RolJefeArea)
            throw new AccesoDenegadoException($"El rol {_tenantContext.Rol} no tiene permisos para modificar objetivos corporativos del área.");
        
        if (_tenantContext.AreaId == null || _tenantContext.AreaId == Guid.Empty)
            throw new NotFoundException("El usuario no tiene un área asignada.");
    }

    private async Task<Guid> ObtenerCicloActivoIdAsync()
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloActivo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId);
        if (cicloActivo == null)
            throw new NotFoundException("No hay ningún ciclo activo para este tenant.");
        return cicloActivo.Id;
    }

    private async Task<Guid> ObtenerCicloActivoYVerificarEstadoAsync(bool soloLectura)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var cicloActivo = await _cicloRepository.ObtenerCicloActivoAsync(tenantId);
        if (cicloActivo == null)
            throw new NotFoundException("No hay ningún ciclo activo para este tenant.");
            
        if (!soloLectura && cicloActivo.Estado == "Cerrado")
            throw new ValidacionException("El ciclo activo se encuentra cerrado. No se pueden modificar los objetivos estratégicos.");

        return cicloActivo.Id;
    }

    public async Task<IEnumerable<ObjetivoCgResponse>> ListarAsync()
    {
        ValidarTenantContext();
        var cicloId = await ObtenerCicloActivoIdAsync();
        
        if (_tenantContext.AreaId == null || _tenantContext.AreaId == Guid.Empty)
            throw new NotFoundException("El usuario no tiene un área asignada.");

        return await _repo.ListarAsync(_tenantContext.TenantId!.Value, cicloId, _tenantContext.AreaId!.Value);
    }

    public async Task<ObjetivoCgResponse> ObtenerPorIdAsync(Guid id)
    {
        ValidarTenantContext();
        if (_tenantContext.AreaId == null || _tenantContext.AreaId == Guid.Empty)
            throw new NotFoundException("El usuario no tiene un área asignada.");

        var objetivo = await _repo.ObtenerPorIdAsync(_tenantContext.TenantId!.Value, _tenantContext.AreaId!.Value, id);
        if (objetivo == null)
            throw new NotFoundException("El objetivo corporativo no existe o no pertenece a su área.");

        return objetivo;
    }

    public async Task<ObjetivoCgResponse> CrearAsync(ObjetivoCgCreateRequest request)
    {
        ValidarPermisoEscritura();
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var cicloId = await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false);

        // Validar pilar
        try
        {
            await _pilarService.ObtenerAsync(cicloId, request.PilarId);
        }
        catch (NotFoundException)
        {
            throw new NotFoundException("El pilar estratégico especificado no existe en este ciclo.");
        }

        // Obtener prefijo de área
        var areaCodigo = await _repo.ObtenerCodigoAreaAsync(tenantId, areaId);
        if (string.IsNullOrEmpty(areaCodigo))
            throw new NotFoundException("No se encontró el código del área para generar el correlativo.");

        var count = await _repo.ObtenerConteoPorAreaAsync(tenantId, cicloId, areaId);
        var nuevoCodigo = $"{areaCodigo}.CG{count + 1}";

        using var tx = await _repo.BeginTransactionAsync();
        try
        {
            var nuevoId = await _repo.CrearAsync(tenantId, cicloId, areaId, nuevoCodigo, request, tx);
            
            var creado = await ObtenerPorIdAsync(nuevoId);

            // Auditoría
            await _repo.InsertLogAsync(new PE_GOL.DTO.Dtos.LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "CREATE",
                Entidad = EntidadAuditoria,
                EntidadId = nuevoId.ToString(),
                ValorAnterior = null,
                ValorNuevo = JsonSerializer.Serialize(creado, JsonOpcionesAuditoria)
            }, tx);

            tx.Commit();
            return creado;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            tx.Rollback();
            // Manejo de condición de carrera TOCTOU en la autogeneración de códigos (RC-13)
            throw new ValidacionException("Hubo un conflicto generando el código correlativo. Por favor intente de nuevo.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<ObjetivoCgResponse> ActualizarAsync(Guid id, ObjetivoCgUpdateRequest request)
    {
        ValidarPermisoEscritura();
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        var cicloId = await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false);

        var actual = await ObtenerPorIdAsync(id);

        // Validar nuevo pilar
        if (actual.PilarId != request.PilarId)
        {
            try
            {
                await _pilarService.ObtenerAsync(cicloId, request.PilarId);
            }
            catch (NotFoundException)
            {
                throw new NotFoundException("El nuevo pilar estratégico especificado no existe en este ciclo.");
            }
        }

        using var tx = await _repo.BeginTransactionAsync();
        try
        {
            await _repo.ActualizarAsync(id, request, tx);
            var actualizado = await ObtenerPorIdAsync(id);

            await _repo.InsertLogAsync(new PE_GOL.DTO.Dtos.LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(actual, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(actualizado, JsonOpcionesAuditoria)
            }, tx);

            tx.Commit();
            return actualizado;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task EliminarAsync(Guid id)
    {
        ValidarPermisoEscritura();
        var tenantId = _tenantContext.TenantId!.Value;
        var areaId = _tenantContext.AreaId!.Value;
        await ObtenerCicloActivoYVerificarEstadoAsync(soloLectura: false);

        var actual = await ObtenerPorIdAsync(id);

        var tieneAcciones = await _repo.VerificarAccionesAsociadasAsync(tenantId, id);
        if (tieneAcciones)
            throw new ValidacionException("No se puede eliminar el objetivo estratégico porque tiene acciones asociadas.");

        using var tx = await _repo.BeginTransactionAsync();
        try
        {
            await _repo.EliminarAsync(id, tx);

            await _repo.InsertLogAsync(new PE_GOL.DTO.Dtos.LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId,
                Accion = "DELETE",
                Entidad = EntidadAuditoria,
                EntidadId = id.ToString(),
                ValorAnterior = JsonSerializer.Serialize(actual, JsonOpcionesAuditoria),
                ValorNuevo = null
            }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}

using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Npgsql;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Entity.Saas;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;
using PE_GOL.Utility.Storage;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Servicio de configuración de la empresa/tenant — Spec HU-006 § Lógica BLL (pasos 1-4).
/// Implementación real (fase 4 del Loop); los tests de @QA (EmpresaServiceTests) son la
/// especificación ejecutable. D2: EmpresaService es la AUTOCONFIGURACIÓN del tenant por el ADM
/// (a diferencia de TenantService, gestión SaaS del SuperAdmin). Ambos tocan la tabla tenant
/// con roles y alcances distintos; se extiende ITenantRepository (no hay repositorio nuevo).
/// Reglas: rol AdminTenant (D12) → 403; tenant inexistente → 404; nombre vacío/&gt;150/duplicado
/// (D9/DAL-E5) → 422; zona horaria IANA inválida (D7) → 422; logo: formato PNG/JPG + ≤ 2 MB +
/// firma de bytes (D14) → 422; fallo de storage → InfraestructuraException SIN tocar BD (RNF-014).
/// Auditoría (ADR-003): Entidad="Tenant", JSON legible con UnsafeRelaxedJsonEscaping, NUNCA bytes.
/// Captura SQLSTATE 23505 (uq_tenant_nombre, ADR-001) → ValidacionException con rollback explícito.
/// </summary>
public class EmpresaService : IEmpresaService
{
    private const string RolAdminTenant = "AdminTenant";
    private const string EntidadAuditoria = "Tenant";
    private const long TamanoMaximoLogoBytes = 2 * 1024 * 1024; // CA #1: 2 MB
    private static readonly TimeSpan ExpiracionUrlFirmada = TimeSpan.FromHours(24); // ARCH-06/D5
    private static readonly HashSet<string> ContentTypesPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg"
    };
    private static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg"
    };

    /// <summary>UnsafeRelaxedJsonEscaping: NO escapa caracteres no-ASCII → JSON legible (ADR-003).</summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ITenantRepository _repository;
    private readonly TenantContext _tenantContext;
    private readonly IStorageHelper _storageHelper;
    private readonly ILogger<EmpresaService>? _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public EmpresaService(ITenantRepository repository, TenantContext tenantContext, IStorageHelper storageHelper)
        : this(repository, tenantContext, storageHelper, null) { }

    /// <summary>Ctor con ILogger opcional para RNF-023 (logging estructurado en producción vía IOC).</summary>
    public EmpresaService(ITenantRepository repository, TenantContext tenantContext, IStorageHelper storageHelper, ILogger<EmpresaService>? logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _storageHelper = storageHelper;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/empresa — lectura multi-rol (AdminTenant/Gerente/JefeArea; la restricción de
    /// roles es del [Authorize] del controller, D10 — la BLL NO restringe lectura, SEC-07 no aplica:
    /// tenant es la raíz del multitenancy, no una entidad de área).
    /// Pasos: tenantId del contexto (404 si null, D17) → GetByIdAsync (404 si inexistente) →
    /// respuesta con URL firmada 24 h del logo (null si no hay logo o si el storage falla, RNF-014).
    /// </summary>
    public async Task<EmpresaResponse> ObtenerAsync(CancellationToken ct = default)
    {
        var tenantId = ObtenerTenantIdOThrow();

        var entidad = await _repository.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException($"El tenant '{tenantId}' no existe");

        return await ConstruirRespuestaAsync(entidad, ct);
    }

    /// <summary>
    /// PUT /api/v1/empresa — solo AdminTenant (D12: re-validación defensiva en BLL; el [Authorize]
    /// del controller es la primera capa, la BLL es la fuente de verdad).
    /// Pasos: rol → tenantId (404) → GetByIdAsync original (404) → normalizar nombre (trim + colapso
    /// espacios) → validar forma (vacío/&gt;150 → 422 ANTES de unicidad) → unicidad excluyendo self
    /// (DAL-E5, 422) → zona horaria IANA (D7, 422) → tx: DAL-E2 + auditoría (ADR-003) + commit →
    /// re-lectura post-commit → respuesta. D8: SOLO nombre/eslogan/zonaHoraria; NUNCA DAL-4 (HU-001).
    /// </summary>
    public async Task<EmpresaResponse> ActualizarAsync(EmpresaUpdateRequest request, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol en BLL.
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede actualizar la configuración de la empresa");

        var tenantId = ObtenerTenantIdOThrow();

        var original = await _repository.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException($"El tenant '{tenantId}' no existe");

        var nombre = NormalizarNombre(request.Nombre);

        // Validación de forma ANTES de unicidad (test #15: vacío/151 chars → 422 sin consultar unicidad).
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ValidacionException("El nombre de la empresa es obligatorio");
        if (nombre.Length > 150)
            throw new ValidacionException("El nombre de la empresa no puede superar 150 caracteres");

        // D9/DAL-E5: unicidad de nombre excluyendo el propio tenant (excludeId).
        if (await _repository.ExisteNombreAsync(nombre, tenantId, ct))
            throw new ValidacionException($"Ya existe una empresa con el nombre '{nombre}'");

        // D7: zona horaria IANA válida (ZonaHorariaHelper.EsValida).
        if (!ZonaHorariaHelper.EsValida(request.ZonaHoraria))
            throw new ValidacionException("La zona horaria no es válida; use un identificador IANA (ej: America/Managua)");

        var dto = new TenantConfiguracionUpdateDto
        {
            Id = tenantId,
            Nombre = nombre,
            Eslogan = request.Eslogan,
            ZonaHoraria = request.ZonaHoraria
        };

        // Spec HU-006 § Lógica BLL: UPDATE + auditoría en UNA sola transacción (IDbTransaction).
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.ActualizarConfiguracionAsync(dto, tx, ct);

            _logger?.LogInformation(
                "Empresa {TenantId} actualizada. Modulo=Empresa, Accion=UPDATE, Entidad={Entidad}",
                tenantId, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = tenantId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new
                {
                    nombre = original.Nombre,
                    eslogan = original.Eslogan,
                    zonaHoraria = original.ZonaHoraria
                }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    nombre = dto.Nombre,
                    eslogan = dto.Eslogan,
                    zonaHoraria = dto.ZonaHoraria
                }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver el estado ya persistido.
            var actualizada = await _repository.GetByIdAsync(tenantId, ct)
                ?? throw new NotFoundException($"El tenant '{tenantId}' no existe");

            return await ConstruirRespuestaAsync(actualizada, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // ADR-001 · Capa 2: el índice único uq_tenant_nombre también protege el UPDATE.
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar empresa {TenantId}. Modulo=Empresa", tenantId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Empresa"); }
            throw new ValidacionException($"Ya existe una empresa con el nombre '{nombre}'");
        }
    }

    /// <summary>
    /// POST /api/v1/empresa/logo — solo AdminTenant (D12). Multipart (IFormFile en el controller).
    /// Pasos: rol → tenantId (404) → archivo obligatorio (null/length 0 → 422) → formato permitido
    /// (content-type {image/png,image/jpeg} + extensión {.png,.jpg,.jpeg} → 422) → tamaño ≤ 2 MB
    /// (CA #1 → 422) → firma de bytes PNG/JPEG (D14 → 422) → storage (fallo → InfraestructuraException
    /// SIN tocar BD, RNF-014) → tx: DAL-E3 + auditoría (solo {logoUrl:path}, NUNCA bytes, ADR-003) +
    /// commit → re-lectura post-commit → respuesta con URL firmada fresca.
    /// NOTA: NO se lee la entidad antes del upload (test #22: el fallo de storage debe lanzar 500
    /// sin depender de la existencia previa del tenant en BD).
    /// </summary>
    public async Task<EmpresaResponse> SubirLogoAsync(Stream stream, string fileName, string contentType, long length, CancellationToken ct = default)
    {
        // D12: re-validación defensiva de rol en BLL.
        if (!string.Equals(_tenantContext.Rol, RolAdminTenant, StringComparison.Ordinal))
            throw new AccesoDenegadoException("Solo el administrador del tenant puede actualizar el logo de la empresa");

        var tenantId = ObtenerTenantIdOThrow();

        // D14 paso 3a: archivo obligatorio (null o vacío).
        if (stream is null || length <= 0)
            throw new ValidacionException("Debe seleccionar un archivo de imagen");

        // D14 paso 3b: formato permitido (content-type + extensión).
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ContentTypesPermitidos.Contains(contentType) || !ExtensionesPermitidas.Contains(extension))
            throw new ValidacionException("El logo debe ser PNG o JPG");

        // CA #1: tamaño máximo 2 MB.
        if (length > TamanoMaximoLogoBytes)
            throw new ValidacionException("El logo no puede superar 2 MB");

        // D14 paso 3c: firma de bytes (PNG 89 50 4E 47 | JPEG FF D8 FF).
        if (!TieneFirmaValida(stream))
            throw new ValidacionException("El archivo no es una imagen PNG o JPG válida");

        // RNF-014: el fallo de storage NO toca la BD — el logo anterior sigue vigente.
        string path;
        try
        {
            path = await _storageHelper.SubirLogoAsync(tenantId, stream, extension, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fallo al subir el logo al bucket. Modulo=Empresa, TenantId={TenantId}", tenantId);
            throw new InfraestructuraException("No se pudo subir el logo al almacenamiento; intente nuevamente", ex);
        }

        // Spec HU-006 § Lógica BLL: UPDATE logo_url + auditoría en UNA sola transacción.
        using var tx = await _repository.BeginTransactionAsync(ct);
        try
        {
            await _repository.ActualizarLogoUrlAsync(tenantId, path, tx, ct);

            _logger?.LogInformation(
                "Logo de empresa {TenantId} actualizado. Modulo=Empresa, Accion=UPDATE, Entidad={Entidad}",
                tenantId, EntidadAuditoria);

            await _repository.InsertLogAsync(new LogAuditoriaInsert
            {
                TenantId = tenantId,
                UsuarioId = _tenantContext.UserId, // D6 (HU-004): actor desde el JWT
                Accion = "UPDATE",
                Entidad = EntidadAuditoria,
                EntidadId = tenantId.ToString(),
                ValorAnterior = JsonSerializer.Serialize(new { logoUrl = (string?)null }, JsonOpcionesAuditoria),
                ValorNuevo = JsonSerializer.Serialize(new { logoUrl = path }, JsonOpcionesAuditoria)
            }, tx, ct);

            tx.Commit();

            // Lectura posterior al commit para devolver la URL firmada fresca (D5).
            var actualizada = await _repository.GetByIdAsync(tenantId, ct)
                ?? throw new NotFoundException($"El tenant '{tenantId}' no existe");

            return await ConstruirRespuestaAsync(actualizada, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            _logger?.LogWarning(ex, "Violación de unicidad 23505 al actualizar logo de empresa {TenantId}. Modulo=Empresa", tenantId);
            try { tx.Rollback(); }
            catch (Exception rollbackEx) { _logger?.LogWarning(rollbackEx, "Rollback fallido tras 23505. Modulo=Empresa"); }
            throw new ValidacionException("No se pudo actualizar el logo de la empresa");
        }
    }

    // ─── Helpers privados ───────────────────────────────────────────────────

    /// <summary>TenantId del contexto (SEC-06: nunca del body/query). null ⇔ SuperAdmin → 404 defensivo (D17).</summary>
    private Guid ObtenerTenantIdOThrow()
    {
        if (_tenantContext.TenantId is null)
            throw new NotFoundException("No se encontró el tenant en el contexto de autenticación");
        return _tenantContext.TenantId.Value;
    }

    /// <summary>Mismo helper que TenantService (HU-001): trim + colapso de espacios internos.</summary>
    private static string NormalizarNombre(string nombre)
    {
        var recortado = nombre.Trim();
        return string.Join(" ", recortado.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Firma de bytes D14: PNG (89 50 4E 47) o JPEG (FF D8 FF). Restaura la posición del stream.</summary>
    private static bool TieneFirmaValida(Stream stream)
    {
        if (stream is null || !stream.CanRead)
            return false;

        var posicionOriginal = stream.CanSeek ? stream.Position : 0;
        try
        {
            var firma = new byte[3];
            if (stream.Read(firma, 0, 3) < 3)
                return false;

            return (firma[0] == 0x89 && firma[1] == 0x50 && firma[2] == 0x4E) // PNG
                || (firma[0] == 0xFF && firma[1] == 0xD8 && firma[2] == 0xFF); // JPEG
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = posicionOriginal;
        }
    }

    /// <summary>URL firmada 24 h del logo; null si no hay logo o si el storage falla (RNF-014 graceful).</summary>
    private async Task<string?> ObtenerUrlFirmadaSeguraAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return await _storageHelper.ObtenerUrlFirmadaAsync(path, ExpiracionUrlFirmada, ct);
        }
        catch (Exception ex)
        {
            // RNF-014: el fallo de storage NO debe tumbar la lectura de configuración.
            _logger?.LogWarning(ex, "No se pudo generar la URL firmada del logo. Modulo=Empresa, TenantId={TenantId}", _tenantContext.TenantId);
            return null;
        }
    }

    /// <summary>Mapea TenantEntity → EmpresaResponse (logoUrl = URL firmada fresca, nunca el path crudo — D5).</summary>
    private async Task<EmpresaResponse> ConstruirRespuestaAsync(TenantEntity entidad, CancellationToken ct)
    {
        return new EmpresaResponse
        {
            TenantId = entidad.Id,
            Nombre = entidad.Nombre,
            Eslogan = entidad.Eslogan,
            LogoUrl = await ObtenerUrlFirmadaSeguraAsync(entidad.LogoUrl, ct),
            ZonaHoraria = entidad.ZonaHoraria ?? "America/Managua", // default del DDL tenant.zona_horaria
            Descripcion = entidad.Descripcion,
            UpdatedAt = entidad.UpdatedAt
        };
    }
}
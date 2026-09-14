using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de configuración de la empresa/tenant (Spec HU-006 § Lógica BLL).
/// D2: EmpresaService es la AUTOCONFIGURACIÓN del tenant por el ADM (a diferencia de
/// TenantService, que es la gestión SaaS del SuperAdmin). Ambos tocan la tabla tenant
/// con roles y alcances distintos. No se crea repositorio nuevo: se extiende ITenantRepository.
/// Endpoints: GET /api/v1/empresa (lectura multi-rol) · PUT /api/v1/empresa (ADM) ·
/// POST /api/v1/empresa/logo (ADM, multipart).
/// Lanza AccesoDenegadoException (403), NotFoundException (404), ValidacionException (422)
/// e InfraestructuraException (500) desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ITenantRepository, TenantContext, IStorageHelper) + overload
/// (..., ILogger&lt;EmpresaService&gt;?). NOTA @QA: el spec declara StorageHelper concreto;
/// se define IStorageHelper (interfaz) para mockeabilidad con Moq — StorageHelper la implementa.
/// </summary>
public interface IEmpresaService
{
    /// <summary>GET /api/v1/empresa — 200 con EmpresaResponse (logoUrl firmada 24 h o null) · 404 sin tenant/tenant inexistente.</summary>
    Task<EmpresaResponse> ObtenerAsync(CancellationToken ct = default);

    /// <summary>PUT /api/v1/empresa — 200 con EmpresaResponse · 403 rol ≠ AdminTenant · 404 · 422 (nombre duplicado/zona inválida/forma).</summary>
    Task<EmpresaResponse> ActualizarAsync(EmpresaUpdateRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/empresa/logo — 200 con EmpresaResponse (URL firmada fresca) · 403 · 404 · 422 (formato/tamaño) · 500 (fallo storage, sin tocar BD).</summary>
    Task<EmpresaResponse> SubirLogoAsync(Stream stream, string fileName, string contentType, long length, CancellationToken ct = default);
}
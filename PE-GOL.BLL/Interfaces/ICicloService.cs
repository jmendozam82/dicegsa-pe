using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Ciclos Anuales (Spec HU-007 § Lógica BLL — 7 métodos).
/// D-A: ADM crea/edita/activa/clona (Borrador→Activo); GER cierra (Activo→Cerrado).
/// D12: cada método de escritura re-valida el rol desde TenantContext.Rol (la BLL es la
/// fuente de verdad; el [Authorize(Roles)] del controller es la primera capa).
/// D-C: ActivarAsync consume IPlanService.ValidarLimitesParaTenantAsync (CA #2 HU-002).
/// Lanza AccesoDenegadoException (403), NotFoundException (404) y ValidacionException (422)
/// desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, TenantContext) + overload
/// (..., ILogger&lt;CicloService&gt;?) — patrón UsuarioService/EmpresaService (D13, RNF-023).
/// </summary>
public interface ICicloService
{
    /// <summary>GET /api/v1/ciclos — 200 con List&lt;CicloResponse&gt; (ORDER BY año_fiscal DESC) · 404 sin tenant.</summary>
    Task<List<CicloResponse>> ListarAsync(CancellationToken ct = default);

    /// <summary>GET /api/v1/ciclos/{id} — 200 con CicloResponse · 404 si no existe o es de otro tenant.</summary>
    Task<CicloResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos — 201 con CicloResponse (Borrador + 2 umbrales default) · 403 rol ≠ ADM · 404 · 422 (año fiscal duplicado/mesInicio/nombre).</summary>
    Task<CicloResponse> CrearAsync(CicloCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{id} — 200 con CicloResponse · 403 · 404 · 422 (no Borrador/año fiscal duplicado/forma).</summary>
    Task<CicloResponse> ActualizarAsync(Guid id, CicloUpdateRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{id}/activar — 200 con CicloResponse (Activo) · 403 · 404 · 422 (ya activo/cerrado/RC-01/límite plan).</summary>
    Task<CicloResponse> ActivarAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{id}/cerrar — 200 con CicloResponse (Cerrado) · 403 rol ≠ GER · 404 · 422 (no Activo).</summary>
    Task<CicloResponse> CerrarAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{id}/clonar — 201 con CicloResponse del nuevo (umbrales copiados) · 403 · 404 · 422 (año fiscal duplicado/forma).</summary>
    Task<CicloResponse> ClonarAsync(Guid idOrigen, ClonarCicloRequest request, CancellationToken ct = default);
}
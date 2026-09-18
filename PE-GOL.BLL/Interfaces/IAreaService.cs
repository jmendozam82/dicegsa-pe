using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Áreas Estratégicas (Spec HU-009 § Lógica BLL — 6 métodos).
/// D-A: escritura ADM-only (crear/editar/desactivar); GET multi-rol ADM/GER/JEF (JEF solo su
/// área, SEC-07). D12: cada método de escritura re-valida el rol desde TenantContext.Rol.
/// D-C: CrearAsync consume IPlanService.ObtenerLimitesAsync (chequeo por ciclo RN-010) +
/// IPlanService.ValidarLimitesParaTenantAsync (guarda a nivel tenant, CA #2 HU-002).
/// Lanza AccesoDenegadoException (403), NotFoundException (404) y ValidacionException (422)
/// desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, TenantContext) + overload
/// (..., ILogger&lt;AreaService&gt;?) — patrón D13 (UsuarioService/EmpresaService/CicloService).
/// </summary>
public interface IAreaService
{
    /// <summary>GET /api/v1/ciclos/{cicloId}/areas — 200 con List&lt;AreaResponse&gt; (ORDER BY orden ASC) · 404 (ciclo inexistente/otro tenant/sin tenant). JefeArea: solo su área (SEC-07: areaIdFiltro = TenantContext.AreaId).</summary>
    Task<List<AreaResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default);

    /// <summary>GET /api/v1/ciclos/{cicloId}/areas/{areaId} — 200 con AreaResponse · 404 · 403 si rol JefeArea y areaId != TenantContext.AreaId (D12).</summary>
    Task<AreaResponse> ObtenerPorIdAsync(Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{cicloId}/areas — 201 con AreaResponse (código GOL auto-generado, CA #1) · 403 rol ≠ ADM · 404 · 422 (ciclo Cerrado RC-12, responsable inválido RN-011, ya asignado RN-012, límite RN-010, 23505).</summary>
    Task<AreaResponse> CrearAsync(Guid cicloId, AreaCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{cicloId}/areas/{areaId} — 200 con AreaResponse · 403 · 404 · 422 (ciclo Cerrado, RN-012 excluyendo self, 23505).</summary>
    Task<AreaResponse> ActualizarAsync(Guid cicloId, Guid areaId, AreaUpdateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{cicloId}/areas/{areaId}/desactivar — 200 con AreaResponse (activa=FALSE, sin DELETE — CA #5, D-E) · 403 · 404 · 422 (ya inactiva / ciclo Cerrado). NO limpia usuario.area_id (D-J).</summary>
    Task<AreaResponse> DesactivarAsync(Guid cicloId, Guid areaId, CancellationToken ct = default);

    /// <summary>GET /api/v1/ciclos/{cicloId}/areas/responsables — 200 con List&lt;ResponsableCandidatoResponse&gt; (JefeArea Activos del tenant, yaAsignado por RN-012) · 404. Puente hasta HU-010.</summary>
    Task<List<ResponsableCandidatoResponse>> ListarResponsablesCandidatosAsync(Guid cicloId, CancellationToken ct = default);
}
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Responsables (Spec HU-010 § Lógica BLL) — hijos del agregado Ciclo (D-I).
/// Responsable = usuario del tenant con rol='JefeArea' (D-B). Escrituras ADM-only (D-A); GET
/// multi-rol ADM/GER/JEF (JEF solo su responsable, SEC-07). Lanza AccesoDenegadoException (403),
/// NotFoundException (404) y ValidacionException (422) desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ICicloRepository, IPlanService, IAuthService, IEmailService,
/// TenantContext) + overload con ILogger (D-L, RNF-023).
/// </summary>
public interface IResponsableService
{
    /// <summary>GET /api/v1/ciclos/{cicloId}/responsables — Listado del ciclo. JefeArea: solo su
    /// responsable (SEC-07: areaIdFiltro = TenantContext.AreaId). 404 si el ciclo no existe/otro tenant/sin tenant.</summary>
    Task<List<ResponsableResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default);

    /// <summary>GET /api/v1/ciclos/{cicloId}/responsables/{responsableId} — Detalle. 404 si no
    /// existe/otro tenant · 403 si rol JefeArea y responsable.AreaId != TenantContext.AreaId (D12).</summary>
    Task<ResponsableResponse> ObtenerPorIdAsync(Guid cicloId, Guid responsableId, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{cicloId}/responsables — Crea usuario JefeArea + asigna área +
    /// correo de activación con pwd temporal (D-C). 403 · 404 · 422 (RC-12, RN-011/012, correo
    /// duplicado, límites del plan). Retorna 201.</summary>
    Task<ResponsableResponse> CrearAsync(Guid cicloId, ResponsableCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId} — Reasigna a otra área
    /// (sync usuario.area_id + liberar origen + asignar destino en una tx, D-E). Advertencia si el
    /// área origen queda sin responsable (CONTRATO #25). Misma área → no-op sin auditoría (CONTRATO #23).</summary>
    Task<ResponsableResponse> ReasignarAsync(Guid cicloId, Guid responsableId, ResponsableReassignRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId}/desactivar — estado
    /// 'Inactivo' + liberar area.responsable_id. NO limpia usuario.area_id (D-J, SEC-07).</summary>
    Task<ResponsableResponse> DesactivarAsync(Guid cicloId, Guid responsableId, CancellationToken ct = default);
}
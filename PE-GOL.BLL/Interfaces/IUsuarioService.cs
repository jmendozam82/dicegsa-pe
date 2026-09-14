using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Usuarios Globales — Spec HU-003 § Lógica BLL + § Endpoints (7).
/// Solo SuperAdmin ([Authorize(Roles = "SuperAdmin")] en el controller — fase @BackendDev).
/// Los métodos lanzan ValidacionException (422), NotFoundException (404) e
/// InfraestructuraException (500) desde PE_GOL.Utility.Exceptions.
///
/// Ctor aprobado por spec § Lógica BLL: (IUsuarioRepository, IPlanService) + overload
/// (..., ILogger&lt;UsuarioService&gt;?) — IPlanService se inyecta para el CA #2 de HU-002
/// (ValidarLimitesParaTenantAsync es la ÚNICA fuente de validación de límites; PROHIBIDO
/// duplicar la lógica — ver D3 del spec y el reporte @QA HU-003).
/// </summary>
public interface IUsuarioService
{
    /// <summary>POST /api/v1/usuarios — 201. 422: correo duplicado/tenant inexistente/área
    /// no pertenece/JefeArea sin área/tenant al tope de max_usuarios.</summary>
    Task<UsuarioResponse> CrearAsync(UsuarioCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/usuarios/{id} — 200. 404 si no existe. 422: mismas reglas
    /// que Crear + re-valida límites SOLO si cambia tenant_id (D4/D6, contra el DESTINO).</summary>
    Task<UsuarioResponse> ActualizarAsync(Guid id, UsuarioUpdateRequest request, CancellationToken ct = default);

    /// <summary>POST /api/v1/usuarios/{id}/desactivar — 200. estado='Inactivo' + revoca
    /// refresh tokens (DAL-U9). 404 · 422 si ya está Inactivo. SIN validación de límites (D4).</summary>
    Task<UsuarioResponse> DesactivarAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/usuarios/{id}/activar — 200. estado='Activo'. 404 · 422 si ya
    /// está Activo o si el tenant está al tope (D7 aprobado por Jorge — caso #26).</summary>
    Task<UsuarioResponse> ActivarAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/usuarios/{id}/reset-contrasena — 200. Genera contraseña temporal,
    /// hash BCrypt cost 12, requiere_cambio_pwd=TRUE, intentos_fallidos=0, bloqueado_hasta=NULL
    /// y revoca refresh tokens (DAL-U10 + DAL-U9). La contraseña NO se devuelve por HTTP.</summary>
    Task<UsuarioResponse> ResetearContrasenaAsync(Guid id, CancellationToken ct = default);

    /// <summary>GET /api/v1/usuarios — listado paginado con filtros opcionales
    /// (tenantId/rol/estado). page≥1, pageSize 1..100 (trunca a 100).</summary>
    Task<PagedResult<UsuarioResponse>> ListarAsync(int page, int pageSize, Guid? tenantId, string? rol, string? estado, CancellationToken ct = default);

    /// <summary>GET /api/v1/usuarios/{id} — 404 si no existe.</summary>
    Task<UsuarioResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
}
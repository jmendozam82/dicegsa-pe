using PE_GOL.DTO.Common;
using PE_GOL.DTO.Responses.Dashboard;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio del Tablero de Inicio del Jefe de Área (Spec HU-015 § Lógica BLL — 1 método).
/// D-F: endpoint restringido a rol 'JefeArea' (D12 — la BLL re-valida el rol desde
/// TenantContext.Rol; el [Authorize(Roles)] del controller es la primera capa).
/// D-G: el ciclo activo se resuelve en BLL vía DAL-D1 (CA #4) — TenantContext no tiene CicloId (H1).
/// D-H: solo lectura, SIN auditoría y SIN transacción.
/// Lanza AccesoDenegadoException (403) y NotFoundException (404) desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ICicloRepository, TenantContext) + overload (..., ILogger&lt;DashboardService&gt;?)
/// — patrón D13 (PilarService HU-013).
/// </summary>
public interface IDashboardService
{
    /// <summary>GET /api/v1/dashboard/jefe-area — 200 con ApiResponse&lt;TableroJefeAreaResponse&gt;
    /// · 403 rol ≠ JefeArea (D12/D-F) · 404 (sin tenant / sin ciclo activo CA #4 / JEF sin área /
    /// área inexistente). SEC-07: DAL-D3/D4/D5 filtran por TenantContext.AreaId.</summary>
    Task<ApiResponse<TableroJefeAreaResponse>> ObtenerTableroJefeAreaAsync(CancellationToken ct = default);
}
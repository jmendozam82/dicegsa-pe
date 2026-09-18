using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de consulta del Log de Auditoría — Spec HU-005 § Lógica BLL.
/// SOLO LECTURA (CA #3 inmutabilidad): ListarAsync + ObtenerPorIdAsync. Cero métodos de
/// escritura — la escritura la realizan los servicios de dominio vía InsertLogAsync
/// (INSERT-only, patrón existente HU-001..HU-008); el único borrado es el batch de
/// retención (LogAuditoriaLimpiezaService, no invocable por API).
/// Solo SuperAdmin ([Authorize(Roles = "SuperAdmin")] en el controller + re-validación
/// defensiva D12 en BLL → AccesoDenegadoException 403).
/// Ctor aprobado por spec: (ILogAuditoriaRepository, TenantContext) + overload
/// (..., ILogger&lt;LogAuditoriaService&gt;?) — patrón UsuarioService/AuthService/CicloService (RNF-023).
/// </summary>
public interface ILogAuditoriaService
{
    /// <summary>GET /api/v1/log-auditoria — listado paginado con filtros opcionales
    /// (tenantId/usuarioId/accion/desde/hasta). 422: desde&gt;hasta, hasta en el futuro,
    /// accion inválida. 403: rol distinto a SuperAdmin. Sin JSONB en el listado (D3).</summary>
    Task<PagedResult<LogAuditoriaResponse>> ListarAsync(LogAuditoriaFiltrosRequest filtros, CancellationToken ct = default);

    /// <summary>GET /api/v1/log-auditoria/{id} — detalle con valor_anterior/valor_nuevo
    /// como JSON crudo (string, ADR-003). 404 si no existe. 403: rol distinto a SuperAdmin.</summary>
    Task<LogAuditoriaDetalleResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
}
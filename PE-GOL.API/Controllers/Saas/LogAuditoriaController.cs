using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers.Saas;

/// <summary>
/// Endpoints de consulta del Log de Auditoría (Spec HU-005 § Endpoints) — recurso SaaS
/// GLOBAL (misma zona que TenantController/PlanController/UsuarioController; el log es
/// metadato de plataforma, no de tenant). Protegidos con [Authorize(Roles = "SuperAdmin")]
/// (D7: solo SA en esta HU — el acceso ADM se difiere al Sprint de hardening).
/// SOLO LECTURA (CA #3 inmutabilidad): cero endpoints de escritura; la escritura la
/// realizan los servicios de dominio vía InsertLogAsync (INSERT-only); el único borrado
/// es el batch de retención (LogAuditoriaLimpiezaService, no invocable por API).
/// SEC-06: tenantId/usuarioId NUNCA viajan en el body; son query params de filtro
/// declarados explícitamente por el SA (misma excepción documentada que HU-001/HU-003).
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07).
/// </summary>
[ApiController]
[Route("api/v1/log-auditoria")]
[Authorize(Roles = "SuperAdmin")]
public class LogAuditoriaController : ControllerBase
{
    private readonly ILogAuditoriaService _service;
    private readonly IValidator<LogAuditoriaFiltrosRequest> _validator;

    public LogAuditoriaController(ILogAuditoriaService service, IValidator<LogAuditoriaFiltrosRequest> validator)
    {
        _service = service;
        _validator = validator;
    }

    /// <summary>GET /api/v1/log-auditoria — Listado paginado con filtros opcionales
    /// (page/pageSize/tenantId/usuarioId/accion/desde/hasta). Retorna PagedResult&lt;LogAuditoriaResponse&gt;
    /// SIN valor_anterior/valor_nuevo (D3). 422 si: desde&gt;hasta, hasta en el futuro, accion
    /// inválida o paginación inválida. 400 si el query string está malformado (p. ej. desde no parseable).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LogAuditoriaResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Listar([FromQuery] LogAuditoriaFiltrosRequest filtros, CancellationToken ct = default)
    {
        // FluentValidation cubre la FORMA de los filtros (UX-04); la BLL re-valida como
        // fuente de verdad (spec § Lógica BLL paso 3).
        var validacion = await _validator.ValidateAsync(filtros, ct);
        if (!validacion.IsValid)
        {
            return UnprocessableEntity(new ApiResponse<object>
            {
                Success = false,
                Message = "Filtros de auditoría inválidos",
                Data = null,
                Errors = validacion.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }

        var data = await _service.ListarAsync(filtros, ct);
        return Ok(new ApiResponse<PagedResult<LogAuditoriaResponse>> { Success = true, Data = data });
    }

    /// <summary>GET /api/v1/log-auditoria/{id} — Detalle de una entrada por id (param de ruta
    /// UUID). Retorna LogAuditoriaDetalleResponse con valor_anterior/valor_nuevo como JSON
    /// crudo (string, ADR-003). 404 si no existe.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LogAuditoriaDetalleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPorId(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<LogAuditoriaDetalleResponse> { Success = true, Data = data });
    }
}
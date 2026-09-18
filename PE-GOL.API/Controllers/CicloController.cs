using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers;

/// <summary>
/// Endpoints de Gestión de Ciclos Anuales (Spec HU-007 § Endpoints) — recurso de alcance
/// TENANT (no SaaS) → controller en la raíz de Controllers/ (mismo criterio que EmpresaController
/// de HU-006, D2). El tenant_id se resuelve SIEMPRE del TenantContext (claims del JWT, SEC-06) —
/// nunca del body ni del query string.
/// Roles (D1/D-A): GET multi-rol de lectura (AdminTenant/Gerente/JefeArea — RN-007) ·
/// POST/PUT/activar/clonar solo AdminTenant · cerrar solo Gerente.
/// SEC-07: ciclo no es entidad de área → sin AND area_id (JefeArea solo lectura, RN-007).
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07).
/// </summary>
[ApiController]
[Route("api/v1/ciclos")]
public class CicloController : ControllerBase
{
    private readonly ICicloService _service;
    private readonly IAreaService _areaService; // HU-009: áreas estratégicas (hijos del agregado Ciclo, D-I)

    public CicloController(ICicloService service, IAreaService areaService)
    {
        _service = service;
        _areaService = areaService;
    }

    /// <summary>GET /api/v1/ciclos — Listado del tenant ordenado por año fiscal DESC (sin paginación, D3).</summary>
    [HttpGet]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<List<CicloResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Listar(CancellationToken ct = default)
    {
        var data = await _service.ListarAsync(ct);
        return Ok(new ApiResponse<List<CicloResponse>> { Success = true, Data = data });
    }

    /// <summary>GET /api/v1/ciclos/{id} — Detalle de un ciclo por id (404 si no existe o es de otro tenant).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPorId(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<CicloResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/ciclos — Crea un ciclo en Borrador + 2 umbrales por defecto (solo AdminTenant).</summary>
    [HttpPost]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear([FromBody] CicloCreateRequest request, CancellationToken ct = default)
    {
        var data = await _service.CrearAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CicloResponse> { Success = true, Message = "Ciclo creado", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{id} — Edita nombre/añoFiscal/mesInicio (solo Borrador, RC-12; solo AdminTenant).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] CicloUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<CicloResponse> { Success = true, Message = "Ciclo actualizado", Data = data });
    }

    /// <summary>POST /api/v1/ciclos/{id}/activar — Activa Borrador → Activo (CA #3; solo AdminTenant).
    /// 422 si ya está Activo/Cerrado, ya existe otro Activo (RC-01) o el plan excede max_ciclos_activos.</summary>
    [HttpPost("{id:guid}/activar")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.ActivarAsync(id, ct);
        return Ok(new ApiResponse<CicloResponse> { Success = true, Message = "Ciclo activado", Data = data });
    }

    /// <summary>POST /api/v1/ciclos/{id}/cerrar — Cierra Activo → Cerrado (CA #4; solo Gerente, D-A).</summary>
    [HttpPost("{id:guid}/cerrar")]
    [Authorize(Roles = "Gerente")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cerrar(Guid id, CancellationToken ct = default)
    {
        var data = await _service.CerrarAsync(id, ct);
        return Ok(new ApiResponse<CicloResponse> { Success = true, Message = "Ciclo cerrado", Data = data });
    }

    /// <summary>POST /api/v1/ciclos/{id}/clonar — Clona el ciclo origen: nuevo en Borrador + umbrales
    /// copiados (D-B, CA #5 parcial; solo AdminTenant). Áreas/responsables se difieren a HU-009/HU-010.</summary>
    [HttpPost("{id:guid}/clonar")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<CicloResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Clonar(Guid id, [FromBody] ClonarCicloRequest request, CancellationToken ct = default)
    {
        var data = await _service.ClonarAsync(id, request, ct);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CicloResponse> { Success = true, Message = "Ciclo clonado", Data = data });
    }

    /// <summary>GET /api/v1/ciclos/{cicloId}/umbrales — Umbrales del ciclo (ambos tipos; lectura
    /// multi-rol ADM/GER/JEF — RN-007). 404 si el ciclo no existe/otro tenant/sin tenant. Defensivo
    /// D5: si el ciclo no tiene filas, responde los defaults 0.90/0.70 sin escribir en BD.
    /// SEC-07: umbral_semaforo no es entidad de área → sin AND area_id.</summary>
    [HttpGet("{cicloId:guid}/umbrales")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<UmbralesCicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ObtenerUmbrales(Guid cicloId, CancellationToken ct = default)
    {
        var data = await _service.ObtenerUmbralesAsync(cicloId, ct);
        return Ok(new ApiResponse<UmbralesCicloResponse> { Success = true, Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/umbrales — UPSERT conjunto KPI+PlanAccion + auditoría
    /// UPDATE 'UmbralSemaforo' en UNA transacción (D1/D4; solo AdminTenant, D12). 422 si el ciclo
    /// no está en Borrador (RN-039/RC-12/RN-004), algún valor fuera de 0.00–1.00, o
    /// umbralVerde &lt;= umbralAmarillo (CHECKs L155-157; 23514 capa 2 BD → 422, D7).</summary>
    [HttpPut("{cicloId:guid}/umbrales")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<UmbralesCicloResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ActualizarUmbrales(Guid cicloId, [FromBody] UmbralesUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarUmbralesAsync(cicloId, request, ct);
        return Ok(new ApiResponse<UmbralesCicloResponse> { Success = true, Message = "Umbrales actualizados", Data = data });
    }

    // ─── HU-009 · Áreas estratégicas (Spec HU-009 § Endpoints) ─────────────────
    // Recurso hijo del agregado Ciclo (D-I): rutas anidadas bajo /api/v1/ciclos/{cicloId}/areas.
    // Roles (D-A): GET multi-rol de lectura (AdminTenant/Gerente/JefeArea — RN-007; JefeArea solo
    // su área, SEC-07) · POST/PUT/desactivar solo AdminTenant (D12). El tenant_id se resuelve
    // SIEMPRE del TenantContext (SEC-06) — nunca del body ni del query string.
    // Respuestas SIEMPRE con el wrapper ApiResponse<T> (ARCH-07).

    /// <summary>GET /api/v1/ciclos/{cicloId}/areas — Listado de áreas del ciclo (ORDER BY orden ASC).
    /// JefeArea: solo su área (SEC-07: areaIdFiltro = TenantContext.AreaId). 404 si el ciclo no
    /// existe/otro tenant/sin tenant.</summary>
    [HttpGet("{cicloId:guid}/areas")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<List<AreaResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListarAreas(Guid cicloId, CancellationToken ct = default)
    {
        var data = await _areaService.ListarAsync(cicloId, ct);
        return Ok(new ApiResponse<List<AreaResponse>> { Success = true, Data = data });
    }

    /// <summary>GET /api/v1/ciclos/{cicloId}/areas/{areaId} — Detalle de un área (404 si no existe
    /// o es de otro tenant; 403 si rol JefeArea y areaId != TenantContext.AreaId — D12, SEC-07).</summary>
    [HttpGet("{cicloId:guid}/areas/{areaId:guid}")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AreaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ObtenerArea(Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        var data = await _areaService.ObtenerPorIdAsync(cicloId, areaId, ct);
        return Ok(new ApiResponse<AreaResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/ciclos/{cicloId}/areas — Crea un área (código GOL auto-generado, CA #1;
    /// solo AdminTenant). 422: ciclo Cerrado (RC-12), responsable inválido (RN-011), ya asignado
    /// (RN-012), límite del plan (RN-010) o colisión 23505 (ADR-001/007).</summary>
    [HttpPost("{cicloId:guid}/areas")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<AreaResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CrearArea(Guid cicloId, [FromBody] AreaCreateRequest request, CancellationToken ct = default)
    {
        var data = await _areaService.CrearAsync(cicloId, request, ct);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<AreaResponse> { Success = true, Message = "Área creada", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/areas/{areaId} — Edita nombre/comentarios/responsable
    /// (solo AdminTenant). 422: ciclo Cerrado (RC-12), responsable inválido (RN-011), ya asignado a
    /// otra área (RN-012 excluyendo self) o colisión 23505 (ADR-001/007).</summary>
    [HttpPut("{cicloId:guid}/areas/{areaId:guid}")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<AreaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ActualizarArea(Guid cicloId, Guid areaId, [FromBody] AreaUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _areaService.ActualizarAsync(cicloId, areaId, request, ct);
        return Ok(new ApiResponse<AreaResponse> { Success = true, Message = "Área actualizada", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/areas/{areaId}/desactivar — Desactiva el área
    /// (activa=FALSE, sin DELETE — CA #5, D-E; solo AdminTenant). 422: ya inactiva o ciclo Cerrado.
    /// NO limpia usuario.area_id (D-J: el JefeArea conserva lectura).</summary>
    [HttpPut("{cicloId:guid}/areas/{areaId:guid}/desactivar")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<AreaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesactivarArea(Guid cicloId, Guid areaId, CancellationToken ct = default)
    {
        var data = await _areaService.DesactivarAsync(cicloId, areaId, ct);
        return Ok(new ApiResponse<AreaResponse> { Success = true, Message = "Área desactivada", Data = data });
    }

    /// <summary>GET /api/v1/ciclos/{cicloId}/areas/responsables — Candidatos a Jefe de Área
    /// (usuarios del tenant con rol JefeArea y estado Activo, con yaAsignado por RN-012).
    /// Puente hasta HU-010. 404 si el ciclo no existe/otro tenant/sin tenant.</summary>
    [HttpGet("{cicloId:guid}/areas/responsables")]
    [Authorize(Roles = "AdminTenant,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<List<ResponsableCandidatoResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListarResponsables(Guid cicloId, CancellationToken ct = default)
    {
        var data = await _areaService.ListarResponsablesCandidatosAsync(cicloId, ct);
        return Ok(new ApiResponse<List<ResponsableCandidatoResponse>> { Success = true, Data = data });
    }
}
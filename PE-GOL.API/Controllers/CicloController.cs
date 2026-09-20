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
    private readonly IResponsableService _responsableService; // HU-010: responsables (hijos del agregado Ciclo, D-I)
    private readonly IFilosofiaService _filosofiaService; // HU-011: visión y misión (hija del agregado Ciclo, D-I)

    public CicloController(
        ICicloService service, IAreaService areaService, IResponsableService responsableService,
        IFilosofiaService filosofiaService)
    {
        _service = service;
        _areaService = areaService;
        _responsableService = responsableService;
        _filosofiaService = filosofiaService;
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

    /// <summary>GET /api/v1/ciclos/{cicloId}/filosofia — Visión y Misión del ciclo (HU-011 §1).
    /// Multi-rol de lectura (AdminTenant/Gerente/JefeArea — RN-007). SEC-07 NO APLICA (D-E):
    /// filosofia es corporativa (sin area_id) → el JefeArea lee la filosofía completa.
    /// Defensivo D-H: si no existe fila → 200 con Id=Guid.Empty, Vision/Mision="", UpdatedBy/UpdatedAt=null.</summary>
    [HttpGet("{cicloId:guid}/filosofia")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<FilosofiaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerFilosofia(Guid cicloId, CancellationToken ct = default)
    {
        var data = await _filosofiaService.ObtenerAsync(cicloId, ct);
        return Ok(new ApiResponse<FilosofiaResponse> { Success = true, Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/filosofia — Registra/edita Visión y Misión (HU-011 §2;
    /// solo Gerente, D-A). 422 si el ciclo está Cerrado (RC-12), el texto visible queda vacío (CA #2)
    /// o excede 5000 caracteres. UPSERT (DB-06): crea la fila en el primer guardado (D-B).</summary>
    [HttpPut("{cicloId:guid}/filosofia")]
    [Authorize(Roles = "Gerente")]
    [ProducesResponseType(typeof(ApiResponse<FilosofiaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ActualizarFilosofia(Guid cicloId, [FromBody] FilosofiaUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _filosofiaService.ActualizarAsync(cicloId, request, ct);
        return Ok(new ApiResponse<FilosofiaResponse> { Success = true, Message = "Visión y Misión actualizadas", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/filosofia/valores — Registra/ordena/elimina los
    /// Valores Corporativos del ciclo (HU-012; solo Gerente, RN-006/D12). Body: lista YA ordenada
    /// (D-C — el orden del array es el orden final). 422: ciclo Cerrado (RC-12), 0 valores, >15
    /// valores, valor vacío tras trim, valor >100 chars o duplicados case-insensitive (CA #4).
    /// UPSERT (DB-06, D-B): crea la fila en el primer guardado con vision=''/mision='' y solo
    /// actualiza valores/updated_by/updated_at (no pisa vision/mision). SEC-07 NO APLICA (D-E):
    /// filosofia es corporativa (sin area_id) → sin AND area_id.</summary>
    [HttpPut("{cicloId:guid}/filosofia/valores")]
    [Authorize(Roles = "Gerente")]
    [ProducesResponseType(typeof(ApiResponse<FilosofiaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ActualizarValores(Guid cicloId, [FromBody] ValoresUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _filosofiaService.ActualizarValoresAsync(cicloId, request, ct);
        return Ok(new ApiResponse<FilosofiaResponse> { Success = true, Message = "Valores corporativos actualizados", Data = data });
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

    // ─── HU-010 · Responsables (Spec HU-010 § Endpoints) ─────────────────────
    // Recurso hijo del agregado Ciclo (D-I): rutas anidadas bajo /api/v1/ciclos/{cicloId}/responsables.
    // Roles (D-A): GET multi-rol de lectura (AdminTenant/Gerente/JefeArea — RN-007; JefeArea solo
    // su responsable, SEC-07) · POST/PUT/desactivar solo AdminTenant (D12). El tenant_id se resuelve
    // SIEMPRE del TenantContext (SEC-06) — nunca del body ni del query string.
    // Respuestas SIEMPRE con el wrapper ApiResponse<T> (ARCH-07).
    // Nota de ruteo: 'responsables' (segmento literal) no colisiona con 'areas/{areaId:guid}'.

    /// <summary>GET /api/v1/ciclos/{cicloId}/responsables — Listado de responsables del ciclo
    /// (ORDER BY nombre). JefeArea: solo su responsable (SEC-07: areaIdFiltro = TenantContext.AreaId).
    /// 404 si el ciclo no existe/otro tenant/sin tenant.</summary>
    [HttpGet("{cicloId:guid}/responsables")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<List<ResponsableResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListarResponsablesDelCiclo(Guid cicloId, CancellationToken ct = default)
    {
        var data = await _responsableService.ListarAsync(cicloId, ct);
        return Ok(new ApiResponse<List<ResponsableResponse>> { Success = true, Data = data });
    }

    /// <summary>GET /api/v1/ciclos/{cicloId}/responsables/{responsableId} — Detalle de un responsable
    /// (404 si no existe o es de otro tenant; 403 si rol JefeArea y responsable.AreaId !=
    /// TenantContext.AreaId — D12, SEC-07).</summary>
    [HttpGet("{cicloId:guid}/responsables/{responsableId:guid}")]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<ResponsableResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ObtenerResponsableDelCiclo(Guid cicloId, Guid responsableId, CancellationToken ct = default)
    {
        var data = await _responsableService.ObtenerPorIdAsync(cicloId, responsableId, ct);
        return Ok(new ApiResponse<ResponsableResponse> { Success = true, Data = data });
    }

    /// <summary>POST /api/v1/ciclos/{cicloId}/responsables — Crea un usuario Jefe de Área y lo
    /// asigna como responsable del área indicada (solo AdminTenant). 201 con el responsable creado.
    /// 403 rol ≠ ADM · 404 ciclo inexistente/otro tenant/sin tenant · 422: ciclo Cerrado (RC-12),
    /// área inexistente/no activa/ya con responsable (RN-011), responsable ya asignado a otra área
    /// activa (RN-012), correo ya existe en el tenant, tenant al tope de usuarios del plan (RN-010).</summary>
    [HttpPost("{cicloId:guid}/responsables")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<ResponsableResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CrearResponsable(Guid cicloId, [FromBody] ResponsableCreateRequest request, CancellationToken ct = default)
    {
        var data = await _responsableService.CrearAsync(cicloId, request, ct);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<ResponsableResponse> { Success = true, Message = "Responsable creado", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId} — Reasigna el responsable
    /// a otra área (cambio de area_id; solo AdminTenant). 200 con el responsable reasignado (con
    /// Advertencia si el área origen queda sin responsable — CONTRATO #25). 403 · 404 · 422 con las
    /// mismas reglas que POST (RN-012 excluyendo self; RN-011: área destino sin responsable).
    /// Misma área → no-op sin auditoría (CONTRATO #23).</summary>
    [HttpPut("{cicloId:guid}/responsables/{responsableId:guid}")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<ResponsableResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReasignarResponsable(Guid cicloId, Guid responsableId, [FromBody] ResponsableReassignRequest request, CancellationToken ct = default)
    {
        var data = await _responsableService.ReasignarAsync(cicloId, responsableId, request, ct);
        return Ok(new ApiResponse<ResponsableResponse> { Success = true, Message = "Responsable reasignado", Data = data });
    }

    /// <summary>PUT /api/v1/ciclos/{cicloId}/responsables/{responsableId}/desactivar — Desactiva el
    /// responsable (usuario.estado = 'Inactivo'; solo AdminTenant). Sin body. 200 con el responsable
    /// desactivado. 403 · 404 · 422 si ya está inactivo o el ciclo está Cerrado (RC-12).
    /// NO se limpia usuario.area_id (D-J, SEC-07: el JefeArea conserva lectura de su área).</summary>
    [HttpPut("{cicloId:guid}/responsables/{responsableId:guid}/desactivar")]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<ResponsableResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DesactivarResponsable(Guid cicloId, Guid responsableId, CancellationToken ct = default)
    {
        var data = await _responsableService.DesactivarAsync(cicloId, responsableId, ct);
        return Ok(new ApiResponse<ResponsableResponse> { Success = true, Message = "Responsable desactivado", Data = data });
    }
}
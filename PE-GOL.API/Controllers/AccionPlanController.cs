using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Security;

namespace PE_GOL.API.Controllers;

[ApiController]
[Route("api/v1/acciones")]
[Authorize] // Default es usuario autenticado
public class AccionPlanController : ControllerBase
{
    private readonly IAccionPlanService _service;

    // ── HU-022 · Entregables adjuntos ────────────────────────────────────────────────────────────
    // El servicio de entregables NO recibe el TenantContext inyectado: exige tenantId, userId, rol y
    // areaId POR PARÁMETRO para poder testearse sin ambientación (SEC-06). El controller es quien
    // los extrae del TenantContext scoped que pobló TenantMiddleware desde los claims del JWT, y
    // quien proyecta el multipart a los DTO de la HU-022 (D-D: IFormFile no sale de esta capa).
    private readonly IEntregableService _entregables;
    private readonly IValidator<EntregableAdjuntoUploadRequest> _validadorSubida;
    private readonly TenantContext _tenantContext;

    public AccionPlanController(
        IAccionPlanService service,
        IEntregableService entregables,
        IValidator<EntregableAdjuntoUploadRequest> validadorSubida,
        TenantContext tenantContext)
    {
        _service = service;
        _entregables = entregables;
        _validadorSubida = validadorSubida;
        _tenantContext = tenantContext;
    }

    [HttpGet("~/api/v1/objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<AccionPlanResponse>>), 200)]
    public async Task<IActionResult> Listar(Guid objetivoCgId, CancellationToken ct)
    {
        var acciones = await _service.ListarPorObjetivoCgAsync(objetivoCgId, ct);
        return Ok(new ApiResponse<IEnumerable<AccionPlanResponse>> { Success = true, Data = acciones });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct)
    {
        var accion = await _service.ObtenerPorIdAsync(id, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion });
    }

    [HttpPost("~/api/v1/objetivos-cg/{objetivoCgId:guid}/acciones")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 201)]
    public async Task<IActionResult> Crear(Guid objetivoCgId, [FromBody] AccionPlanCreateRequest request, CancellationToken ct)
    {
        var accion = await _service.CrearAsync(objetivoCgId, request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = accion.Id }, new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción creada exitosamente." });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    public async Task<IActionResult> Actualizar(Guid id, [FromBody] AccionPlanUpdateRequest request, CancellationToken ct)
    {
        var accion = await _service.ActualizarAsync(id, request, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Acción actualizada exitosamente." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await _service.EliminarAsync(id, ct);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "Acción eliminada exitosamente." });
    }

    // ─── HU-020 — Progreso e Historial ───────────────────────────────────────

    /// <summary>
    /// Actualiza el % de progreso de una acción.
    /// Recalcula status (RN-017), puntuación ponderada, historial y semáforo del CG (RN-018, F2).
    /// Idempotente: si el progreso no cambia retorna 200 OK sin efectos secundarios (F3).
    /// </summary>
    [HttpPatch("{id:guid}/progreso")]
    [Authorize(Roles = "JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<AccionPlanResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ActualizarProgreso(
        Guid id, [FromBody] ActualizarProgresoRequest request, CancellationToken ct)
    {
        var accion = await _service.ActualizarProgresoAsync(id, request, ct);
        return Ok(new ApiResponse<AccionPlanResponse> { Success = true, Data = accion, Message = "Progreso actualizado exitosamente." });
    }

    /// <summary>Lista el historial de cambios de progreso de una acción (F1: JefeArea + Gerente).</summary>
    [HttpGet("{id:guid}/historial")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<HistorialProgresoResponse>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> ObtenerHistorial(Guid id, CancellationToken ct)
    {
        var historial = await _service.ListarHistorialAsync(id, ct);
        return Ok(new ApiResponse<IEnumerable<HistorialProgresoResponse>> { Success = true, Data = historial });
    }

    // ─── HU-021 — Vista Gantt del Plan de Acción ─────────────────────────────

    /// <summary>
    /// Datos del Gantt del plan de acción del CICLO ACTIVO del tenant (HU-021, solo lectura).
    /// Sin body y sin query string: el tenant sale del JWT (SEC-06) y el ciclo se resuelve en la
    /// BLL (RC-01). La BLL devuelve el DTO plano; el wrapper ApiResponse&lt;T&gt; se arma aquí
    /// (ARCH-07, ADR-010 Decisión 1).
    /// 200 → GanttPlanResponse con la escala de 12 meses, los grupos (Objetivos CG con acciones),
    ///       las acciones planas y los 4 conteos por status. Un ciclo sin acciones devuelve 200 con
    ///       payload vacío (no 404): la vista muestra el .empty-state (UX-05).
    /// 401 → sin JWT válido · 403 → rol ∉ {JefeArea, Gerente}, o JefeArea sin área en el token.
    /// 404 → sin tenant en el contexto, o no hay ciclo Activo para el tenant.
    /// 500 → error inesperado del servidor.
    /// </summary>
    [HttpGet("gantt")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<GanttPlanResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 500)]
    public async Task<IActionResult> ObtenerGantt(CancellationToken ct)
    {
        var gantt = await _service.ObtenerGanttAsync(ct);
        return Ok(new ApiResponse<GanttPlanResponse> { Success = true, Data = gantt });
    }

    // ─── HU-022 · Acciones · Entregables adjuntos (bucket privado «entregables») ─────────────────
    // Los 4 endpoints cuelgan de la acción (D-B): no hay EntregableController nuevo. Los 6 endpoints
    // de acciones de HU-019/HU-020/HU-021 quedan intactos y NO colisionan con las rutas nuevas:
    // el patrón existente {id:guid} exige UN solo segmento y estos usan dos o tres.
    // El wrapper ApiResponse<T> se arma AQUÍ (ARCH-07, ADR-010 D-1): la BLL devuelve el DTO plano.
    // Ningún endpoint acepta tenant_id, ciclo_id, area_id ni file_path (SEC-06): salen del
    // TenantContext o los resuelve la BLL contra accion_plan.

    /// <summary>
    /// POST /api/v1/acciones/{accionId}/entregables — Sube 1..5 adjuntos a la acción
    /// (multipart/form-data, campo «archivos»; solo JefeArea de su propia área, SEC-07).
    /// Tipos admitidos: PDF, DOCX, XLSX, PNG y JPG (CA #2). Máximo 20 MB por archivo (RN-020) y
    /// máximo 5 adjuntos por acción contando los ya existentes (CA #1); el límite de petición es
    /// 5 × 20 MB + overhead (106 MB, F2).
    /// 201 → adjuntos creados (nombre, fecha de subida, usuario y tamaño; CA #3) ·
    /// 400 → el campo «archivos» vino vacío · 401 → sin JWT válido · 403 → rol no autorizado, sin
    /// área en el token o acción de otra área · 404 → la acción no existe para el tenant ·
    /// 413 → el cuerpo supera el límite de multipart (F2) · 422 → tipo/tamaño/conteo no permitidos,
    /// la firma de bytes no corresponde a la extensión, o el ciclo está Cerrado/Borrador (RC-12) ·
    /// 500 → fallo de Storage; la subida es todo o nada por lote (RNF-014, D-H).
    /// </summary>
    [HttpPost("{accionId:guid}/entregables")]
    [Authorize(Roles = "JefeArea")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EntregableAdjuntoResponse>>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubirEntregables(
        Guid accionId, [FromForm] List<IFormFile> archivos, CancellationToken ct)
    {
        // Guard del controller: sin archivos → 400 (precedente EmpresaController.SubirLogo). El resto
        // de reglas (tamaño, tipo, conteo TOTAL frente a los ya existentes, firma de bytes y estado
        // del ciclo) las aplica la BLL y resuelven como 422: la validación del servidor es la fuente
        // de verdad (UX-04).
        if (archivos is null || archivos.Count == 0)
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Debe adjuntar entre 1 y 5 archivos en el campo «archivos».",
                Errors = ["Debe adjuntar entre 1 y 5 archivos en el campo «archivos»."]
            });

        // (Peticion, Forma): un solo recorrido del multipart produce las dos proyecciones (D-D).
        var (peticion, forma) = ProyectarMultipart(accionId, archivos);

        // Capa 1 de validaciones (STACK-04): FORMA. El conteo total, la firma de bytes y el estado
        // del ciclo los valida después la BLL, que es donde se resuelven (UX-04).
        var validacion = await _validadorSubida.ValidateAsync(forma, ct);
        if (!validacion.IsValid)
            return UnprocessableEntity(new ApiResponse<object>
            {
                Success = false,
                Message = "Los archivos adjuntos no son válidos.",
                Errors = validacion.Errors.Select(e => e.ErrorMessage).ToList()
            });

        var creados = (await _entregables.SubirAsync(peticion, ct)).ToList();

        return CreatedAtAction(nameof(ListarEntregables), new { accionId },
            new ApiResponse<IEnumerable<EntregableAdjuntoResponse>>
            {
                Success = true,
                Data = creados,
                Message = $"Se agregaron {creados.Count} archivo(s) al entregable de la acción."
            });
    }

    /// <summary>
    /// GET /api/v1/acciones/{accionId}/entregables — Lista los adjuntos de la acción (CA #3):
    /// nombre, fecha de subida, usuario que lo subió y tamaño. JefeArea: solo su área (SEC-07);
    /// Gerente: todas las áreas (RN-006).
    /// La lista NO trae URLs firmadas (D-G): la descarga se pide bajo demanda, para no exponer
    /// 5 enlaces temporales en cada listado ni cachear URLs que caducan a las 24 h.
    /// 200 → lista de adjuntos; una acción sin adjuntos devuelve 200 con «[]», nunca 404 (D-C) ·
    /// 401 → sin JWT · 403 → rol no autorizado o acción de otra área · 404 → la acción no existe.
    /// </summary>
    [HttpGet("{accionId:guid}/entregables")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EntregableAdjuntoResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListarEntregables(
        Guid accionId, CancellationToken ct)
    {
        var entregables = await _entregables.ListarAsync(
            accionId, TenantIdRequerido(), UserIdActual, RolActual, _tenantContext.AreaId, ct);

        return Ok(new ApiResponse<IEnumerable<EntregableAdjuntoResponse>>
        {
            Success = true,
            Data = entregables
        });
    }

    /// <summary>
    /// GET /api/v1/acciones/{accionId}/entregables/{entregableId}/descarga — Devuelve la URL firmada
    /// de acceso temporal del adjunto (CA #4, ARCH-06: bucket privado «entregables», expiración
    /// <b>24 horas</b>). El navegador descarga DIRECTO desde Supabase Storage: la API nunca hace
    /// proxy del binario (RNF-001). Es una LECTURA: no audita (ADR-003) y funciona en cualquier
    /// estado del ciclo.
    /// 200 → URL firmada + «ExpiraEn»; si el Storage falla degrada a Url = null con 200 y el
    /// mensaje lo explica, nunca 500 (D-G, RNF-014) · 401 → sin JWT · 403 → rol no autorizado o
    /// acción de otra área · 404 → la acción no existe, el adjunto no existe o no pertenece a esa
    /// acción (no se revela la existencia de adjuntos de otras acciones).
    /// </summary>
    [HttpGet("{accionId:guid}/entregables/{entregableId:guid}/descarga")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<EntregableDescargaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DescargarEntregable(
        Guid accionId, Guid entregableId, CancellationToken ct)
    {
        var descarga = await _entregables.ObtenerDescargaAsync(
            accionId, entregableId, TenantIdRequerido(), RolActual, _tenantContext.AreaId, ct);

        return Ok(new ApiResponse<EntregableDescargaResponse>
        {
            Success = true,
            Data = descarga,
            // D-G: con Url = null el mensaje explica que no se pudo generar el enlace; la fila
            // sigue operativa y la UI solo muestra un aviso (degradación elegante, RNF-014).
            Message = descarga.Url is null
                ? "No se pudo generar el enlace de descarga. Inténtalo de nuevo."
                : $"URL firmada válida por {EntregableAdjuntoReglas.ExpiracionHoras} horas."
        });
    }

    /// <summary>
    /// DELETE /api/v1/acciones/{accionId}/entregables/{entregableId} — Elimina el adjunto (CA #5):
    /// solo quien lo subió o el Gerente. La UI pide antes la confirmación en un modal, pero la regla
    /// vive en la BLL y se expone por el campo «PuedeEliminar» del listado: la UI nunca es la
    /// frontera de seguridad. Borra primero en BD (fila + auditoría, misma transacción) y después
    /// en Storage, en best-effort (D-I): un fallo de Storage no puede impedir un borrado ya confirmado.
    /// 200 → adjunto eliminado · 401 → sin JWT · 403 → rol no autorizado, acción de otra área o el
    /// actor no es el autor y no es Gerente · 404 → la acción o el adjunto no existen, o el adjunto
    /// pertenece a otra acción · 422 → el ciclo está Cerrado/Borrador (RC-12).
    /// </summary>
    [HttpDelete("{accionId:guid}/entregables/{entregableId:guid}")]
    [Authorize(Roles = "JefeArea,Gerente")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EliminarEntregable(
        Guid accionId, Guid entregableId, CancellationToken ct)
    {
        // La BLL devuelve las FILAS AFECTADAS: 0 significa que otra petición borró la fila entre la
        // lectura y el DELETE (carrera) → la BLL ya lanza NotFoundException y esto responde 404.
        var filasAfectadas = await _entregables.EliminarAsync(
            accionId, entregableId, TenantIdRequerido(), UserIdActual, RolActual,
            _tenantContext.AreaId, ct);

        return Ok(new ApiResponse<bool>
        {
            Success = true,
            Data = filasAfectadas > 0,
            Message = "Adjunto eliminado."
        });
    }

    /// <summary>
    /// Proyecta el multipart a los dos DTO de HU-022 con UN solo recorrido (D-D): el de FORMA que
    /// consume el <c>AbstractValidator</c> de la API y el de VERDAD que consume la BLL. El
    /// controller es el único punto del repositorio que conoce <see cref="IFormFile"/>: ni
    /// <c>PE-GOL.DTO</c> ni <c>PE-GOL.BLL</c> referencian ASP.NET Core.
    /// <c>TenantId</c>, <c>UserId</c>, <c>Rol</c> y <c>AreaId</c> se copian del <see cref="TenantContext"/>
    /// (SEC-06): el cliente no envía ninguno de ellos, y el nombre visible del archivo lo sanea la
    /// BLL (D-E) — aquí solo se lee el nombre tal cual llega para poder validarlo (STACK-04).
    /// </summary>
    private (EntregableSubidaRequest Peticion, EntregableAdjuntoUploadRequest Forma) ProyectarMultipart(
        Guid accionId, List<IFormFile> archivos)
    {
        var peticion = new EntregableSubidaRequest
        {
            AccionId = accionId,
            TenantId = TenantIdRequerido(),
            UserId = UserIdActual,
            Rol = RolActual,
            AreaId = _tenantContext.AreaId,
            Archivos = new List<EntregableArchivo>(archivos.Count)
        };

        var forma = new EntregableAdjuntoUploadRequest
        {
            AccionId = accionId,
            Archivos = new List<ArchivoSubidaMetadata>(archivos.Count)
        };

        foreach (var archivo in archivos)
        {
            var nombre = archivo.FileName ?? string.Empty;
            var contentType = archivo.ContentType ?? string.Empty;
            var extension = Path.GetExtension(nombre).TrimStart('.').ToLowerInvariant();

            forma.Archivos.Add(new ArchivoSubidaMetadata
            {
                NombreArchivo = nombre,
                ContentType = contentType,
                TamanoBytes = archivo.Length,
                ExtensionDeNombre = extension
            });

            peticion.Archivos.Add(new EntregableArchivo
            {
                NombreOriginal = nombre,
                ContentType = contentType,
                ExtensionDeNombre = extension,
                TamanoBytes = archivo.Length,
                // El binario se lee UNA sola vez, bajo demanda de la BLL: el mismo array sirve
                // para la firma de bytes y para la subida al bucket (D-H).
                LeerContenidoAsync = async token =>
                {
                    // Capacidad inicial = tamaño declarado: evita reallocaciones al copiar. El tope
                    // de 20 MB (RN-020) lo valida la BLL, así que el cast a int es seguro aquí.
                    await using var buffer = new MemoryStream((int)archivo.Length);
                    await archivo.CopyToAsync(buffer, token);
                    return buffer.ToArray();
                }
            });
        }

        return (peticion, forma);
    }

    /// <summary>
    /// SEC-06/ARCH-04: el tenant sale del <see cref="TenantContext"/> que pobló TenantMiddleware
    /// desde los claims del JWT — nunca del body ni del query string. Sin claim de tenant (o no
    /// parseable) la petición no puede tocar datos de ningún tenant, así que se corta con 404 antes
    /// de llegar a la BLL, igual que un recurso inexistente.
    /// </summary>
    private Guid TenantIdRequerido()
        => _tenantContext.TenantId
           ?? throw new NotFoundException("No hay contexto de tenant para esta sesión.");

    /// <summary>
    /// Actor de la operación (SEC-06). <c>[Authorize]</c> garantiza un JWT válido con claim
    /// <c>user_id</c>; si faltara, <see cref="Guid.Empty"/> no casa con ningún autor, de modo que la
    /// regla «autor o Gerente» del CA #5 resuelve como 403 en vez de abrir la puerta.
    /// </summary>
    private Guid UserIdActual => _tenantContext.UserId ?? Guid.Empty;

    /// <summary>Rol del actor (SEC-06). Un rol vacío o desconocido cae en el 403 de la BLL.</summary>
    private string RolActual => _tenantContext.Rol ?? string.Empty;
}
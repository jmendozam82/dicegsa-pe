using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Responses.PlanOperativo;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI del Plan de Acción del Jefe de Área (Spec HU-019 § UI — Index/Crear/Editar.cshtml) y de la
/// Actualización de Progreso + Historial (Spec HU-020 § UI — Progreso/Historial.cshtml).
/// Clase: JefeArea, Gerente (HU-019 nota de acceso). Escrituras (crear/editar/eliminar/progreso):
/// solo JefeArea (RN-007 — el Gerente es solo lectura). Historial: JefeArea, Gerente (HU-020).
/// SEC-07: la API filtra por AreaId del JWT; la UI nunca envía area_id ni tenant_id (SEC-06).
/// D-3: navegación MVC full page load (form POST, sin AJAX).
/// </summary>
[Authorize(Roles = "JefeArea,Gerente")]
public class AccionPlanController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<AccionPlanController> _logger;
    private readonly SesionService _sesionService;

    public AccionPlanController(IApiClient apiClient, SesionService sesionService, ILogger<AccionPlanController> logger)
    {
        _apiClient = apiClient;
        _sesionService = sesionService;
        _logger = logger;
    }

    // ─── Listado (GET /AccionPlan?objetivoCgId=) — solo JefeArea ─────────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Index(Guid? objetivoCgId)
    {
        try
        {
            var modelo = new AccionPlanIndexViewModel { ObjetivoCgId = objetivoCgId };

            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
            modelo.HayCicloActivo = cicloActivo is not null;
            modelo.CicloNombre = cicloActivo?.Nombre;

            if (cicloActivo is not null)
            {
                // SEC-07: la API filtra por AreaId del JWT — el selector solo ve los CGs del área.
                modelo.ObjetivosCg = await _apiClient.GetAsync<List<ObjetivoCgResponse>>("/api/v1/objetivos-cg");

                if (objetivoCgId.HasValue)
                {
                    var cg = modelo.ObjetivosCg.FirstOrDefault(o => o.Id == objetivoCgId.Value);
                    if (cg is not null)
                    {
                        modelo.ObjetivoCgCodigo = cg.Codigo;
                        modelo.ObjetivoCgDescripcion = cg.Descripcion;
                        modelo.ObjetivoCgProgreso = cg.Progreso;
                        modelo.ObjetivoCgSemaforo = cg.Semaforo;
                        modelo.Items = await _apiClient.GetAsync<List<AccionPlanResponse>>(
                            $"/api/v1/objetivos-cg/{objetivoCgId}/acciones");
                    }
                }
            }

            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new AccionPlanIndexViewModel { ObjetivoCgId = objetivoCgId });
        }
    }

    // ─── Crear (GET/POST /AccionPlan/Crear?objetivoCgId=) — solo JefeArea ────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Crear(Guid objetivoCgId)
    {
        try
        {
            var cg = await ObtenerCgAsync(objetivoCgId);
            if (cg is null)
                return NotFound();

            var responsables = await ObtenerResponsablesAsync();
            return View(new AccionPlanFormViewModel
            {
                ObjetivoCgId = cg.Id,
                ObjetivoCgCodigo = cg.Codigo,
                ObjetivoCgDescripcion = cg.Descripcion,
                Responsables = responsables
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { objetivoCgId });
        }
    }

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Crear(Guid objetivoCgId, AccionPlanCreateRequest request)
    {
        try
        {
            var accion = await _apiClient.PostAsync<AccionPlanCreateRequest, AccionPlanResponse>(
                $"/api/v1/objetivos-cg/{objetivoCgId}/acciones", request);
            TempData["Success"] = $"Acción '{accion.Codigo}' creada correctamente.";
            return RedirectToAction(nameof(Index), new { objetivoCgId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            var cg = await ObtenerCgAsync(objetivoCgId);
            var responsables = await ObtenerResponsablesAsync();
            return View(new AccionPlanFormViewModel
            {
                ObjetivoCgId = objetivoCgId,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                ObjetivoCgDescripcion = cg?.Descripcion ?? string.Empty,
                Descripcion = request.Descripcion,
                DescripcionEntregable = request.DescripcionEntregable,
                ResponsableId = request.ResponsableId,
                FechaInicio = request.FechaInicio,
                FechaVencimiento = request.FechaVencimiento,
                Clasificacion = request.Clasificacion,
                TipoPresupuesto = request.TipoPresupuesto,
                Peso = request.Peso,
                Aclaraciones = request.Aclaraciones,
                Responsables = responsables
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /AccionPlan/Editar/{id}) — solo JefeArea ───────────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Editar(Guid id)
    {
        try
        {
            var accion = await _apiClient.GetAsync<AccionPlanResponse>($"/api/v1/acciones/{id}");
            var cg = await ObtenerCgAsync(accion.ObjetivoCgId);
            var responsables = await ObtenerResponsablesAsync();

            return View(new AccionPlanFormViewModel
            {
                AccionId = accion.Id,
                ObjetivoCgId = accion.ObjetivoCgId,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                ObjetivoCgDescripcion = cg?.Descripcion ?? string.Empty,
                Codigo = accion.Codigo,
                Descripcion = accion.Descripcion,
                DescripcionEntregable = accion.DescripcionEntregable,
                ResponsableId = accion.ResponsableId,
                FechaInicio = accion.FechaInicio,
                FechaVencimiento = accion.FechaVencimiento,
                Clasificacion = accion.Clasificacion,
                TipoPresupuesto = accion.TipoPresupuesto,
                Peso = accion.Peso,
                Aclaraciones = accion.Aclaraciones,
                Responsables = responsables,
                EsEdicion = true
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Editar(Guid id, Guid objetivoCgId, AccionPlanUpdateRequest request)
    {
        try
        {
            var accion = await _apiClient.PutAsync<AccionPlanUpdateRequest, AccionPlanResponse>(
                $"/api/v1/acciones/{id}", request);
            TempData["Success"] = $"Acción '{accion.Codigo}' actualizada correctamente.";
            return RedirectToAction(nameof(Index), new { objetivoCgId });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            var cg = await ObtenerCgAsync(objetivoCgId);
            var responsables = await ObtenerResponsablesAsync();
            return View(new AccionPlanFormViewModel
            {
                AccionId = id,
                ObjetivoCgId = objetivoCgId,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                ObjetivoCgDescripcion = cg?.Descripcion ?? string.Empty,
                Descripcion = request.Descripcion,
                DescripcionEntregable = request.DescripcionEntregable,
                ResponsableId = request.ResponsableId,
                FechaInicio = request.FechaInicio,
                FechaVencimiento = request.FechaVencimiento,
                Clasificacion = request.Clasificacion,
                TipoPresupuesto = request.TipoPresupuesto,
                Peso = request.Peso,
                Aclaraciones = request.Aclaraciones,
                Responsables = responsables,
                EsEdicion = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (POST /AccionPlan/Eliminar/{id}) — solo JefeArea ───────────

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid id, Guid objetivoCgId)
    {
        try
        {
            var accion = await _apiClient.DeleteAsync<AccionPlanResponse>($"/api/v1/acciones/{id}");
            TempData["Success"] = $"Acción '{accion.Codigo}' eliminada correctamente.";
            return RedirectToAction(nameof(Index), new { objetivoCgId });
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { objetivoCgId });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Progreso (GET/POST /AccionPlan/Progreso/{id}) — solo JefeArea ───────

    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> Progreso(Guid id)
    {
        try
        {
            var accion = await _apiClient.GetAsync<AccionPlanResponse>($"/api/v1/acciones/{id}");
            var cg = await ObtenerCgAsync(accion.ObjetivoCgId);

            return View(new AccionPlanProgresoViewModel
            {
                AccionId = accion.Id,
                ObjetivoCgId = accion.ObjetivoCgId,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                Codigo = accion.Codigo,
                Descripcion = accion.Descripcion,
                Progreso = accion.Progreso,
                Status = accion.Status,
                FechaInicio = accion.FechaInicio,
                FechaVencimiento = accion.FechaVencimiento,
                Peso = accion.Peso,
                PuntuacionPonderada = accion.PuntuacionPonderada
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    [Authorize(Roles = "JefeArea")]
    [HttpPost]
    public async Task<IActionResult> Progreso(Guid id, ActualizarProgresoRequest request)
    {
        try
        {
            var accion = await _apiClient.PatchAsync<ActualizarProgresoRequest, AccionPlanResponse>(
                $"/api/v1/acciones/{id}/progreso", request);
            TempData["Success"] = $"Progreso de '{accion.Codigo}' actualizado a {accion.Progreso:0.##}% (status: {accion.Status}).";
            return RedirectToAction(nameof(Index), new { objetivoCgId = accion.ObjetivoCgId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);

            // Re-cargar el estado actual para re-render (el PATCH falló por validación de forma/negocio).
            var accion = await ObtenerAccionAsync(id);
            var cg = accion is null ? null : await ObtenerCgAsync(accion.ObjetivoCgId);
            return View(new AccionPlanProgresoViewModel
            {
                AccionId = id,
                ObjetivoCgId = accion?.ObjetivoCgId ?? Guid.Empty,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                Codigo = accion?.Codigo ?? string.Empty,
                Descripcion = accion?.Descripcion ?? string.Empty,
                Progreso = request.Progreso,
                Status = accion?.Status ?? string.Empty,
                FechaInicio = accion?.FechaInicio ?? DateTime.Today,
                FechaVencimiento = accion?.FechaVencimiento ?? DateTime.Today,
                Peso = accion?.Peso ?? 0m,
                PuntuacionPonderada = accion?.PuntuacionPonderada ?? 0m
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Historial (GET /AccionPlan/Historial/{id}) — JefeArea, Gerente ──────

    [Authorize(Roles = "JefeArea,Gerente")]
    public async Task<IActionResult> Historial(Guid id)
    {
        try
        {
            var accion = await _apiClient.GetAsync<AccionPlanResponse>($"/api/v1/acciones/{id}");
            var cg = await ObtenerCgAsync(accion.ObjetivoCgId);
            var historial = await _apiClient.GetAsync<List<HistorialProgresoResponse>>(
                $"/api/v1/acciones/{id}/historial");

            return View(new AccionPlanHistorialViewModel
            {
                AccionId = accion.Id,
                ObjetivoCgId = accion.ObjetivoCgId,
                ObjetivoCgCodigo = cg?.Codigo ?? string.Empty,
                Codigo = accion.Codigo,
                Descripcion = accion.Descripcion,
                Items = historial
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Gantt (GET /AccionPlan/Gantt) — JefeArea, Gerente (HU-021) ──────────

    /// <summary>
    /// Vista Gantt del plan de acción del ciclo activo (Spec HU-021 § UI). Doble capa de
    /// autorización: la acción declara [Authorize(Roles = "JefeArea,Gerente")] porque la clase la
    /// tiene pero `Index` la estrecha a JefeArea (RN-007 — el Gerente es el principal beneficiario
    /// del Gantt multi-área, F7).
    /// SEC-06: sin query params — ni tenant_id, ni ciclo_id, ni area_id; la API los deduce del JWT.
    /// 404 de la API = "no hay ciclo activo" = estado de UI (no se pinta banner de error);
    /// cualquier otro fallo de la API sí lo muestra y la vista se renderiza igual (no se rompe).
    /// </summary>
    [Authorize(Roles = "JefeArea,Gerente")]
    public async Task<IActionResult> Gantt()
    {
        try
        {
            var gantt = await _apiClient.GetAsync<GanttPlanResponse>("/api/v1/acciones/gantt");

            return View(new AccionPlanGanttViewModel
            {
                HayCicloActivo = true,
                HayAcciones = gantt.Acciones.Count > 0,
                CicloNombre = gantt.CicloNombre,
                Escala = gantt.Escala,
                ConteoPorStatus = gantt.ConteoPorStatus,
                JsonPlan = SerializarGantt(gantt)
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            // Sin ciclo activo: la vista se RENDERIZA con su empty state (UX-05) y sin
            // TempData["Error"] — no es un fallo, es un estado esperado del dominio (RC-01).
            _logger.LogInformation("Vista Gantt sin ciclo activo: {Mensaje}", ex.Message);
            return View(new AccionPlanGanttViewModel { HayCicloActivo = false, HayAcciones = false });
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new AccionPlanGanttViewModel());
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Entregables Adjuntos (HU-022) ─────────────────────────────────────────



    // ─── Entregables Adjuntos (HU-022 + hotfix ADR-016) ──────────────────────

    /// <summary>
    /// Vista de entregables adjuntos de una acción (HU-022).
    /// GET /AccionPlan/Entregables/{id}
    /// Muestra la lista de adjuntos y permite subir nuevos, descargar y eliminar.
    /// </summary>
    [Authorize(Roles = "JefeArea,Gerente")]
    public async Task<IActionResult> Entregables(Guid id)
    {
        try
        {
            var accion = await ObtenerAccionAsync(id);
            if (accion is null)
            {
                return NotFound();
            }

            var adjuntos = await _apiClient.GetAsync<List<EntregableAdjuntoResponse>>(
                $"/api/v1/acciones/{id}/entregables");

            // Verificar si el ciclo está activo (para habilitar/deshabilitar subida)
            // RC-01: solo hay un ciclo activo por tenant
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var cicloActivo = ciclos.Any(c => c.Estado == "Activo");

            return View(new EntregablesViewModel
            {
                AccionId = id,
                AccionCodigo = accion.Codigo,
                AccionNombre = accion.Descripcion,
                Adjuntos = adjuntos ?? new List<EntregableAdjuntoResponse>(),
                CicloActivo = cicloActivo,
                PuedeSubir = User.IsInRole("JefeArea")
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    /// <summary>Proxy del listado de adjuntos (ADR-016). GET /AccionPlan/EntregablesDatos/{id}.
    /// El JWT viaja server-side desde la sesión; el navegador no lo envía.</summary>
    [HttpGet]
    public async Task<IActionResult> EntregablesDatos(Guid id)
    {
        try
        {
            var lista = await _apiClient.GetAsync<List<EntregableAdjuntoResponse>>(
                $"/api/v1/acciones/{id}/entregables");
            return new ObjectResult(lista) { StatusCode = 200 };
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Proxy MVC de entregables: error {StatusCode} de la API interna (acción {AccionId})",
                ex.StatusCode, id);
            return new ObjectResult(new { message = SanitizarMensajeProxy(ex.Message), errors = ex.Errors }) { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    /// <summary>Proxy de la subida (ADR-016). POST /AccionPlan/EntregablesSubir/{id}.
    /// Solo JefeArea: estrecha la clase porque la API es JEF-only (API AccionPlanController.cs:174).</summary>
    [HttpPost]
    [Authorize(Roles = "JefeArea")]
    public async Task<IActionResult> EntregablesSubir(Guid id, [FromForm] List<IFormFile> archivos)
    {
        if (archivos is null || archivos.Count == 0)
        {
            return new ObjectResult(new { message = "Selecciona al menos un archivo para subir." })
            { StatusCode = 400 };
        }

        try
        {
            var creados = await _apiClient.PostMultipartAsync<List<EntregableAdjuntoResponse>>(
                $"/api/v1/acciones/{id}/entregables", archivos, "archivos");
            return new ObjectResult(creados) { StatusCode = 200 };
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Proxy MVC de entregables: error {StatusCode} de la API interna (acción {AccionId})",
                ex.StatusCode, id);
            return new ObjectResult(new { message = SanitizarMensajeProxy(ex.Message), errors = ex.Errors }) { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    /// <summary>Proxy de la descarga (ADR-016). GET /AccionPlan/EntregablesDescarga/{id}?entregableId=…</summary>
    [HttpGet]
    public async Task<IActionResult> EntregablesDescarga(Guid id, [FromQuery] Guid entregableId)
    {
        try
        {
            var payload = await _apiClient.GetAsync<EntregableDescargaResponse>(
                $"/api/v1/acciones/{id}/entregables/{entregableId}/descarga");
            return new ObjectResult(payload) { StatusCode = 200 };
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Proxy MVC de entregables: error {StatusCode} de la API interna (acción {AccionId})",
                ex.StatusCode, id);
            return new ObjectResult(new { message = SanitizarMensajeProxy(ex.Message), errors = ex.Errors }) { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    /// <summary>Proxy del borrado (ADR-016). POST /AccionPlan/EntregablesEliminar/{id}?entregableId=…</summary>
    [HttpPost]
    public async Task<IActionResult> EntregablesEliminar(Guid id, [FromQuery] Guid entregableId)
    {
        try
        {
            var eliminado = await _apiClient.DeleteAsync<bool>(
                $"/api/v1/acciones/{id}/entregables/{entregableId}");
            return new ObjectResult(eliminado) { StatusCode = 200 };
        }
        catch (ApiClientException ex)
        {
            _logger.LogError(ex, "Proxy MVC de entregables: error {StatusCode} de la API interna (acción {AccionId})",
                ex.StatusCode, id);
            return new ObjectResult(new { message = SanitizarMensajeProxy(ex.Message), errors = ex.Errors }) { StatusCode = ex.StatusCode };
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializa el payload del Gantt para embeberlo en
    /// &lt;script type="application/json" id="gantt-plan-data"&gt; (SEC-05: se usa
    /// JavaScriptEncoder.Default, NUNCA UnsafeRelaxedJsonEscaping — con el encoder permisivo una
    /// descripción que contenga "&lt;/script&gt;" inyectaría markup; con el default viaja escapada
    /// y el navegador la lee como texto). camelCase para que el JS del front lo consuma directo.
    /// </summary>
    private static string SerializarGantt(GanttPlanResponse gantt)
        => JsonSerializer.Serialize(gantt, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.Default
        });

    /// <summary>
    /// Detalle de un CG (GET /api/v1/objetivos-cg/{id} — JefeArea). Null si 404/403 (p. ej. rol
    /// Gerente en Historial, donde el detalle del CG no es accesible — el historial sí lo es).
    /// </summary>
    private async Task<ObjetivoCgResponse?> ObtenerCgAsync(Guid id)
    {
        try
        {
            return await _apiClient.GetAsync<ObjetivoCgResponse>($"/api/v1/objetivos-cg/{id}");
        }
        catch (ApiClientException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sanitiza el mensaje de error antes de devolverlo al navegador (ADR-016).
    /// Elimina URLs absolutas, tokens JWT y el prefijo Bearer para evitar filtrar
    /// detalles internos de la API. Los mensajes user-facing sin estos detalles
    /// pasan intactos.
    /// </summary>
    private static string SanitizarMensajeProxy(string? mensaje)
    {
        if (string.IsNullOrEmpty(mensaje))
            return mensaje ?? string.Empty;

        var sanitizado = mensaje;

        // Reemplazar URLs absolutas (http://... o https://...) por marcador.
        sanitizado = Regex.Replace(
            sanitizado,
            @"https?://[^\s""'<>]+",
            "[URL interna]",
            RegexOptions.IgnoreCase);

        // Reemplazar "Bearer" seguido de token JWT por marcador (antes del token suelto).
        sanitizado = Regex.Replace(
            sanitizado,
            @"Bearer\s+eyJ[A-Za-z0-9_\-]*\.[A-Za-z0-9_\-]*\.[A-Za-z0-9_\-]*",
            "[token]",
            RegexOptions.IgnoreCase);

        // Reemplazar tokens JWT sueltos (eyJ... . ... . ...) por marcador.
        sanitizado = Regex.Replace(
            sanitizado,
            @"eyJ[A-Za-z0-9_\-]*\.[A-Za-z0-9_\-]*\.[A-Za-z0-9_\-]*",
            "[token]",
            RegexOptions.IgnoreCase);

        // Reemplazar cualquier ocurrencia remanente de "Bearer" seguido de token-like.
        sanitizado = Regex.Replace(
            sanitizado,
            @"Bearer\s+[A-Za-z0-9_\-\.]+",
            "[token]",
            RegexOptions.IgnoreCase);

        return sanitizado;
    }

    /// <summary>Detalle de una acción (GET /api/v1/acciones/{id}). Null si falla.</summary>
    private async Task<AccionPlanResponse?> ObtenerAccionAsync(Guid id)
    {
        try
        {
            return await _apiClient.GetAsync<AccionPlanResponse>($"/api/v1/acciones/{id}");
        }
        catch (ApiClientException)
        {
            return null;
        }
    }

    /// <summary>
    /// Catálogo de responsables del ciclo activo (GET /api/v1/ciclos/{cicloId}/responsables —
    /// JEF: solo el suyo, SEC-07). Vacío si no hay ciclo activo.
    /// </summary>
    private async Task<List<ResponsableResponse>> ObtenerResponsablesAsync()
    {
        var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
        var cicloActivo = ciclos.FirstOrDefault(c => c.Estado == "Activo");
        if (cicloActivo is null)
            return [];

        return await _apiClient.GetAsync<List<ResponsableResponse>>($"/api/v1/ciclos/{cicloActivo.Id}/responsables");
    }
}
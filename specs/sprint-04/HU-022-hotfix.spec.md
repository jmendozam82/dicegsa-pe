# Spec HU-022-hotfix — Hotfix de UI de Entregables (navegación, proxy MVC y permisos)

**Sprint:** 4 · **Épica:** EP-07 · **Pts:** 0 (hotfix de deuda técnica — HU-022 ya está puntuada con 5 pts y cerrada) · **Estado:** `Implementado`

> **Borrador → Aprobado 2026-10-01** (aprobación autorizada por Jorge vía encargo directo). Habilita a @QA (tests TDD, TEST-01) y a @BackendDev/@FrontendDev (LOOP-01, LOOP-02).
> **Alcance: 100 % `PE-GOL.Aplicacion` + `PE-GOL.Tests`.** 0 cambios en `PE-GOL.API` / `PE-GOL.BLL` / `PE-GOL.DAL` / `PE-GOL.Entity` / `PE-GOL.DTO` · **sin migración** (DB-01/DB-02 satisfechos de forma trivial) · **sin Swagger nuevo** (los proxies son acciones MVC, no endpoints de API — igual que el hotfix de HU-023; los 54 paths quedan intactos).
> Deuda registrada en `docs/HANDOFF_PE_GOL.md` § «Deuda técnica pendiente» ítems 3 (L128-134) y 4 (L136-144) y en `AGENTS.md` v1.28 («Deuda técnica explícita — hotfix pendiente de HU-022»). Patrón aplicable: **ADR-016** (aceptado). Precedente de implementación: **hotfix de HU-023**, Revisión v3 de `specs/sprint-04/HU-023.spec.md` + `PE-GOL.Aplicacion/Controllers/PlanController.cs:99-151`.
> **Cierre v1 (2026-10-01, LOOP-05):** hotfix implementado y en verde — build 0/0 · tests **830/830** (813 previos + 17 nuevos) · cobertura BLL **89,96%** sin drift · `ExcepcionesConocidas` vacía. Spec → `Implementado` · deuda HANDOFF ítems 3-4 retirada. Commit `fa45c43`.
>
> ⚠️ **Cierre v1 REVISADO por la validación real en navegador.** El cierre anteriorDeclaraba la validación visual como cumplida cuando el flujo del **JefeArea** aún no se había ejercido de punta a punta. Al hacerlo (Jorge, 2026-10-01 por la tarde) aparecieron **8 defectos de runtime** que los 830 tests no podían detectar, todos en la capa de subida/descarga real. Se corrigieron en un **hotfix v2** — alcance ampliado a `PE-GOL.Utility` y `PE-GOL.BLL` (justificado en «Alcance revisado del v2») — con **9 tests nuevos** (841 en total). Ver **§ «Hotfix v2 — 8 defectos de runtime»** al final del spec.
> **Cierre v2 (2026-10-01, LOOP-05):** build 0/0 · tests **841/841** (830 previos + 11 nuevos) · cobertura BLL **90,00%** (era 89,96 %) · 54 paths de Swagger intactos · **sin migración** · **sin ADR nuevo** (aplican ADR-013 y ADR-016) · datos reparados en caliente (5 filas normalizadas + 17 objetos huérfanos eliminados; bucket queda en 5 objetos, los 5 referenciados). Spec → `Implementado` (definitivo).

---

## Contexto

HU-022 (Gestión de Entregables Adjuntos) está `Implementado` (spec `specs/sprint-04/HU-022.spec.md`, 66 métodos xUnit) y su backend funciona: los 4 endpoints de la API (`PE-GOL.API/Controllers/AccionPlanController.cs:152-320`) están verificados. La **UI MVC**, sin embargo, quedó con deuda destapada por la validación visual en navegador del hotfix de HU-023 (2026-09-30, Jorge): la vista solo se alcanza escribiendo la URL a mano y **ninguna interacción funciona** (3 `fetch` en ruta relativa → 404).

El encargo listaba **3 defectos**; la verificación de código de este spec (2026-10-01, @Arquitecto) destapó **3 adicionales** en el mismo camino runtime. Los 6, con evidencia:

| # | Defecto | Evidencia (archivo:línea) | Sección |
|---|---------|---------------------------|---------|
| **A** | **Vista inalcanzable.** `GET /AccionPlan/Entregables/{id}` existe pero nada la enlaza: no está en `_Sidebar.cshtml` (sección JEF L91-114 ni GER L58-89), ni en la lista de acciones (`Views/AccionPlan/Index.cshtml` L198-219: Progreso/Historial/Editar/Eliminar), ni en el Gantt. | `PE-GOL.Aplicacion/Controllers/AccionPlanController.cs:429` · `_Sidebar.cshtml` · `Index.cshtml:198-219` | § Defecto A |
| **B** | **Defecto E de ADR-016** (HANDOFF ítem 3): 3 `fetch` en ruta relativa contra el origen del MVC (`:7200`) → **404**. El MVC no tiene rutas `/api/v1` ni proxy alguno. | `wwwroot/js/entregables.js:227` (POST subir) · `:376` (DELETE eliminar) · `:407` (GET descarga) · TODOs `[hotfix-HU-022]` en `:224-225`, `:373-374`, `:404-405` | § Defecto B |
| **C** | **Dropzone visible para rol sin permiso.** La vista renderiza el panel de subida a cualquier rol (solo condiciona a `CicloActivo`); la API de subida es **JEF-only** → un `Gerente` ve el dropzone y recibe **403** al usarlo. | `Views/AccionPlan/Entregables.cshtml:43-82` (panel incondicional) · `PE-GOL.API/Controllers/AccionPlanController.cs:174` (`[Authorize(Roles = "JefeArea")]`) | § Defecto C |
| **D** | **JWT en el DOM** (adicional, SEC-01/SEC-05). El token viaja al navegador en un `<meta>` y el JS lo envía en headers. Es **exactamente** el defecto que el hotfix de HU-023 eliminó (AGENTS.md v1.28: «el JWT ya no viaja al navegador; el proxy lo adjunta server-side desde la sesión»). | `Entregables.cshtml:6-7` (`<meta name="access-token">`) · `AccionPlanController.cs:447-448,457` (inyección) · `Models/EntregablesViewModel.cs:26-27` (`AccessToken`) · `entregables.js:430-433` (`obtenerToken()`) + headers `Authorization` en `:226-233`, `:375-381`, `:406-412` | § Defecto D |
| **E** | **Typo `adjjunto`** (adicional, runtime). `ReferenceError` en `agregarFilaTabla` en cuanto una subida tiene éxito (la respuesta del proxy alimenta esa función). Misma clase de defecto runtime de navegador que el hotfix de HU-021 documentó (AGENTS.md v1.25). | `entregables.js:269` (`adjjunto.id` por `adjunto.id`) | § Defecto E |
| **F** | **Tabla inexistente cuando la acción parte sin adjuntos** (adicional, runtime). La vista renderiza la tabla **solo si** `Model.Adjuntos.Count > 0` → sobre una acción vacía, `#tbody-adjuntos` no existe en el DOM: `agregarFilaTabla` no tiene dónde insertar y `entregables.js:252` oculta el empty state → **tras la primera subida no se ve nada** (solo el toast). | `Entregables.cshtml:84-154` (`@if (Model.Adjuntos.Count == 0) { empty-state } else { tabla }`) · `entregables.js:267-302` (`agregarFilaTabla` append a `$tbodyAdjuntos`) · `:252` (ocultar empty state) | § Defecto C/F (restrucura de vista) |

**Reglas aplicables:** ADR-016 (proxy MVC, JWT server-side) · SEC-01/SEC-05 (JWT fuera del navegador) · SEC-06 (sin `tenant_id`/`ciclo_id`/`area_id` del cliente — los proxies solo transportan `id`/`entregableId` de ruta/query) · ARCH-07 (los mensajes de la API son user-facing por diseño) · UX-01..UX-06 · TEST-01..TEST-06 · LOOP-01/LOOP-02.

### Matriz de roles (la UI la espeja, no la reimplementa)

| Operación | Endpoint API interno | JEF | GER | Dónde se decide en la UI |
|---|---|---|---|---|
| Ver lista | `GET /api/v1/acciones/{id}/entregables` | ✅ | ✅ | Action MVC `Entregables` (`[Authorize(Roles="JefeArea,Gerente")]`, L428) — ya correcta |
| Descargar (URL firmada 24 h) | `GET .../entregables/{entregableId}/descarga` | ✅ | ✅ | Botón por fila — ya correcto (ambos roles) |
| Subir | `POST /api/v1/acciones/{id}/entregables` | ✅ (si ciclo activo) | ❌ | **`Model.PuedeSubir`** calculado server-side (§ Defecto C) |
| Eliminar | `DELETE .../entregables/{entregableId}` | ✅ solo autor | ✅ | `PuedeEliminar` por fila (calculado en BLL, DB-04 — la UI no toca la regla) |

---

## Defecto A — Navegación (por rol)

La vista es **por-acción** (requiere `accion_id` de contexto), por lo que un ítem de sidebar global no lleva a nada. Cada rol con acceso (JEF y GER) recibe **exactamente un punto de entrada**:

### A.1 — JEF: botón por fila en la lista de acciones

`Views/AccionPlan/Index.cshtml` (columna «Acciones», L198-219) gana un botón **entre Historial (L204-207) y Editar (L208-211)**:

```html
<a class="btn-pe btn-pe--secondary btn-pe--sm" asp-action="Entregables"
   asp-route-id="@accion.Id" title="Entregables adjuntos">
    <i class="bi bi-paperclip" aria-hidden="true"></i>
</a>
```

- `bi-paperclip` ya está en el Design System (§ 7, citado por el spec original de HU-022 en su tabla de dependencias). UX-01/UX-03.
- `Index` es `[Authorize(Roles = "JefeArea")]` (L40) → solo JEF ve este enlace. Coherente: el JEF llega desde su hub natural.

### A.2 — GER: enlace por fila en el Plan Consolidado

El hub del Gerente para las acciones es `Views/Plan/Consolidado.cshtml` (HU-023). La tabla (L167-213) gana una **columna «Acciones»**:

- `thead` (tras «Responsable», L177): `<th class="text-end">Acciones</th>`
- Fila server-side (tras la celda de responsable, L207-209):

```html
<td class="text-end">
    <a class="btn-pe btn-pe--secondary btn-pe--sm" asp-controller="AccionPlan"
       asp-action="Entregables" asp-route-id="@item.AccionId" title="Entregables adjuntos">
        <i class="bi bi-paperclip" aria-hidden="true"></i>
    </a>
</td>
```

- `ConsolidadoItemResponse.AccionId` **ya existe** (`PE-GOL.DTO/Responses/PlanOperativo/ConsolidadoResponse.cs:48`) — sin cambios de DTO.
- **Obligatorio también en el render JS**: `plan-consolidado.js` re-renderiza las filas en el navegador (`renderTabla`, L159-196 — plantilla de fila en L168-195), de modo que el enlace desaparecería tras el primer filtro/paginación si solo se añade en Razor. La plantilla JS gana la misma celda (tras la celda de responsable, L193):

```js
'<td class="text-end">' +
    '<a class="btn-pe btn-pe--secondary btn-pe--sm" href="/AccionPlan/Entregables/' + item.accionId + '" title="Entregables adjuntos">' +
        '<i class="bi bi-paperclip" aria-hidden="true"></i>' +
    '</a>' +
'</td>' +
```

- `item.accionId` viaja en camelCase en el payload del proxy (mismo casing que `item.accionCodigo`, L179 — verificado).
- `PlanController` (MVC) es `[Authorize(Roles = "Gerente")]` → solo GER ve este enlace.

### A.3 — Explícitamente NO (decisiones documentadas)

| Punto | Decisión | Justificación |
|---|---|---|
| **Gantt** | NO se añade enlace | HU-021 declaró el Gantt **read-only** (spec original de HU-022 § Fuera de alcance: «`AccionPlan/Gantt.cshtml` no se toca»). Mantener. |
| **Sidebar** | NO se añade ítem | La vista es por-acción: un ítem global sin `accion_id` no lleva a nada. Los puntos de entrada por rol (A.1/A.2) cubren el 100 % de los usuarios con acceso. |

---

## Defecto B — Proxies MVC (ADR-016)

### B.1 — Endpoints proxy (4 acciones nuevas en `PE-GOL.Aplicacion/Controllers/AccionPlanController.cs`)

La clase tiene `[Authorize(Roles = "JefeArea,Gerente")]` (L24): los proxies de lectura/eliminación la **heredan**; el de subida la **estrecha** a `JefeArea` (precedente: `Index` lo hace igual, L40) porque la API subyacente es JEF-only (`PE-GOL.API/Controllers/AccionPlanController.cs:174`).

| Método | Ruta (relativa al MVC) | Auth | Proxea (endpoint API interno) | Response 200 |
|---|---|---|---|---|
| `GET` | `/AccionPlan/EntregablesDatos/{id}` | JEF, GER (heredado de clase) | `GET /api/v1/acciones/{id}/entregables` | `List<EntregableAdjuntoResponse>` (payload directo) |
| `POST` | `/AccionPlan/EntregablesSubir/{id}` | **JEF** (atributo explícito) | `POST /api/v1/acciones/{id}/entregables` (multipart, campo `archivos`) | `List<EntregableAdjuntoResponse>` (los creados) |
| `GET` | `/AccionPlan/EntregablesDescarga/{id}?entregableId={guid}` | JEF, GER (heredado) | `GET /api/v1/acciones/{id}/entregables/{entregableId}/descarga` | `EntregableDescargaResponse` (payload directo; `Url` puede ser `null` — D-G) |
| `POST` | `/AccionPlan/EntregablesEliminar/{id}?entregableId={guid}` | JEF, GER (heredado) | `DELETE /api/v1/acciones/{id}/entregables/{entregableId}` | `true` (bool) |

- **`EntregablesDatos`** ya estaba fijado como nombre del fix en ADR-016 § Consecuencias, HANDOFF ítem 3 y los TODOs del propio JS — se mantiene.
- **SEC-06**: ningún proxy acepta `tenant_id`/`ciclo_id`/`area_id`; solo `id` (ruta) y `entregableId` (query). El JWT lo adjunta el `ApiClient` server-side desde la sesión (ADR-016).
- **Routing**: `id` bindea por la ruta convencional `{controller}/{action}/{id?}` (`Program.cs:76-78` — mismo mecanismo que `Entregables(Guid id)` actual, L429); `entregableId` viaja por **query string** con `[FromQuery]` — el pattern default solo bindea un segmento posicional y así el MVC no gana ningún mecanismo de routing nuevo (precedente: los proxies de HU-023 usan `[FromQuery]`, `PlanController.cs:100`).
- **Verbos**: `POST` para subir (multipart) y para eliminar — el patrón MVC del repo usa `[HttpPost]` para eliminar (`AccionPlanController.cs:248`, `Eliminar` de acciones) y todos los formularios MVC son POST.

### B.2 — Firmas (sin implementación)

```csharp
// ─── HU-022-hotfix — Proxies MVC (Defecto B, ADR-016) ───────────────────────

/// <summary>Proxy del listado de adjuntos (ADR-016). GET /AccionPlan/EntregablesDatos/{id}.
/// El JWT viaja server-side desde la sesión; el navegador no lo envía.</summary>
[HttpGet]
public async Task<IActionResult> EntregablesDatos(Guid id)
// → _apiClient.GetAsync<List<EntregableAdjuntoResponse>>($"/api/v1/acciones/{id}/entregables")
// → new ObjectResult(lista) { StatusCode = 200 }   (lista vacía → 200 con [], nunca 404 — D-C)

/// <summary>Proxy de la subida (ADR-016). POST /AccionPlan/EntregablesSubir/{id}.
/// Solo JefeArea: estrecha la clase porque la API es JEF-only (API AccionPlanController.cs:174).</summary>
[HttpPost]
[Authorize(Roles = "JefeArea")]
public async Task<IActionResult> EntregablesSubir(Guid id, [FromForm] List<IFormFile> archivos)
// Guard: archivos null o vacía → 400 (defensa en profundidad; el JS ya lo impide — L195-198)
// → _apiClient.PostMultipartAsync<List<EntregableAdjuntoResponse>>(
//       $"/api/v1/acciones/{id}/entregables", archivos, "archivos")     ← overload NUEVO (§ B.4)
// → new ObjectResult(creados) { StatusCode = 200 }

/// <summary>Proxy de la descarga (ADR-016). GET /AccionPlan/EntregablesDescarga/{id}?entregableId=…</summary>
[HttpGet]
public async Task<IActionResult> EntregablesDescarga(Guid id, [FromQuery] Guid entregableId)
// → _apiClient.GetAsync<EntregableDescargaResponse>(
//       $"/api/v1/acciones/{id}/entregables/{entregableId}/descarga")
// → new ObjectResult(payload) { StatusCode = 200 }   (Url puede ser null — degradación D-G, RNF-014)

/// <summary>Proxy del borrado (ADR-016). POST /AccionPlan/EntregablesEliminar/{id}?entregableId=…</summary>
[HttpPost]
public async Task<IActionResult> EntregablesEliminar(Guid id, [FromQuery] Guid entregableId)
// → _apiClient.DeleteAsync<bool>($"/api/v1/acciones/{id}/entregables/{entregableId}")
// → new ObjectResult(eliminado) { StatusCode = 200 }
```

**Manejo de errores — uniforme en los 4 proxies** (precedente `PlanController.cs:108-120`):

```csharp
catch (ApiClientException ex)
{
    // ADR-016: los detalles internos van SOLO al log del servidor. El body nunca contiene
    // el JWT, el header Authorization ni la URL interna (Api:BaseUrl) — blindado por test.
    // ex.Message / ex.Errors son mensajes user-facing de la API (ARCH-07): viajan (Decisión D-4).
    _logger.LogError(ex, "Proxy MVC de entregables: error {StatusCode} de la API interna (acción {AccionId})",
        ex.StatusCode, id);
    return new ObjectResult(new { message = ex.Message, errors = ex.Errors }) { StatusCode = ex.StatusCode };
}
catch (UnauthorizedException)
{
    // Endpoint consumido por fetch: 401, NUNCA redirect — un 302 se seguiría de forma
    // transparente y el JS recibiría el HTML del login como 200 (precedente HU-023, caso 49).
    return Unauthorized();
}
```

### B.3 — Matriz de errores por proxy

| Status | Origen | Body del proxy | Qué hace el JS |
|---|---|---|---|
| `200` | éxito | payload directo (array / `EntregableDescargaResponse` / `true`) | render / `window.open(data.url)` / re-render |
| `400` | guard de `EntregablesSubir` (sin archivos) | `{ message: "Selecciona al menos un archivo para subir." }` | toast |
| `401` | `UnauthorizedException` (refresh agotado) | vacío (`Unauthorized()`) | toast «La sesión expiró…» + redirect `/Auth/Login` |
| `403` | API (rol / otra área, SEC-07) | `{ message, errors }` — mensaje de dominio de la API | toast |
| `404` | API (acción o adjunto inexistente; `entregableId` ausente → `Guid.Empty` → 404 de la API) | `{ message, errors }` | toast |
| `413` | API (límite de cuerpo) | `{ message, errors }` | toast |
| `422` | API (validación: >5 archivos, >20 MB, tipo no permitido, firma de bytes, ciclo `Cerrado`) | `{ message, errors }` — **errores de validación viajan** | toast con `errors.join(' ')` |
| `500` | API / infraestructura | `{ message: "Error interno del servidor", errors: [] }` — **ya sanitizado por el `ApiClient`** (`ApiClient.cs:264-266`) | toast |

**Justificación de la política de mensajes (Decisión D-4):** la regla dura de ADR-016 es que el cuerpo nunca contenga el JWT, el header `Authorization` ni la URL interna — se respeta y se blinda por test (casos 3 y 12, espejo del caso 46 de HU-023). Los mensajes que sí viajan (`ex.Message`/`ex.Errors`) son los mensajes user-facing de la API: el `ApiClientException` fue **diseñado para mostrarse** (`Exceptions/ApiClientException.cs:6-7`: «Expone StatusCode y Errors … para que el controlador MVC los muestre — CA #5: errores del servidor visibles»), el `ApiClient` ya sanitiza el 500 a mensaje genérico (`ApiClient.cs:264-266`) y el mensaje de error proviene del `ApiResponse.Message` de la API (`ApiClient.cs:282-284`) — nunca de la URL ni del token. Sin esta política, un 422 («máximo 5 archivos por acción») llegaría al usuario como «Error al subir los entregables» y no sabría qué corregir (UX-04: la validación del servidor es la fuente de verdad y debe ser visible).

### B.4 — Overload multi-archivo de `IApiClient.PostMultipartAsync` (aditivo)

El `PostMultipartAsync` actual acepta **un solo** archivo (`IApiClient.cs:34`, impl `ApiClient.cs:87-102` — el de HU-006 para el logo). El spec **original** de HU-022 (§ Alcance, punto 7) ya planificó el overload multi-archivo, pero **nunca se implementó** porque la subida se hizo con el fetch roto al navegador. El hotfix lo materializa:

```csharp
// PE-GOL.Aplicacion/Services/IApiClient.cs — overload ADITIVO (no rompe la firma mono-archivo)
/// <summary>POST multipart/form-data multi-archivo (HU-022-hotfix, campo «archivos»).
/// Desenvuelve ApiResponse&lt;TRes&gt;.Data. Overload aditivo del mono-archivo (HU-006).</summary>
Task<TRes> PostMultipartAsync<TRes>(string path, IReadOnlyList<IFormFile> archivos, string campo, CancellationToken ct = default);
```

Implementación en `ApiClient.cs` (espejo del mono-archivo, `L87-102` + `L143-165`):

1. Un solo `MultipartFormDataContent`; **un `StreamContent` por archivo, todos con el MISMO nombre de campo** (`archivos`) — la API bindea `List<IFormFile> archivos` por nombre de campo (`PE-GOL.API/Controllers/AccionPlanController.cs:184`).
2. `ContentType` por archivo si existe (como `L159-160`); `FileName` = `archivo.FileName` (como `L161`).
3. Bearer desde `SesionService.ObtenerAccessToken()`; sin token → `UnauthorizedException` (como `L149-151`).
4. 401 → `IntentarRefreshAsync` → **reintento único** (como `L91-99`).
5. `ProcesarRespuestaAsync<TRes>` (como `L101`) — desenvuelve el wrapper `ApiResponse<T>` (ARCH-07).

### B.5 — Límites de cuerpo y CSRF (verificados, sin cambios)

- **Límite multipart: ya configurado.** `PE-GOL.Aplicacion/Program.cs:9-14` ya fija `FormOptions.MultipartBodyLengthLimit = 106_000_000` y Kestrel `MaxRequestBodySize = 106_000_000` (comentado «HU-022»: 5 × 20 MB + 6 MB overhead). El proxy **no añade nada** — documentado para que @BackendDev no duplique configuración.
- **CSRF: patrón del repo intacto.** No hay antiforgery en `PE-GOL.Aplicacion` (verificado: 0 coincidencias de `AntiForgeryToken`/`Antiforgery` en el proyecto); la autenticación es cookie (`Program.cs:48-55`) con `SameSite=Lax` por defecto y los `fetch` son same-origin (la cookie viaja sola; un `fetch` cross-origin no lleva cookies sin `credentials: 'include'` + CORS). Los proxies POST siguen el mismo patrón que todos los formularios MVC existentes. **No se introduce antiforgery** — sería un patrón nuevo y requeriría decisión escalada.

---

## Defecto C — Permisos UI (`PuedeSubir`)

El permiso de subida se calcula **server-side, nunca en el JS**:

1. `EntregablesViewModel` (Defecto D): `AccessToken` **fuera**; `PuedeSubir` (bool) **dentro**:

```csharp
/// <summary>true si el rol actual puede subir (JefeArea — la API de subida es JEF-only,
/// API AccionPlanController.cs:174). Calculado en el controller; el JS no reimplementa la regla.</summary>
public bool PuedeSubir { get; set; }
```

2. `AccionPlanController.Entregables` (L429-468) — cambios exactos:
   - **Eliminar** L447-448 (`var token = _sesionService.ObtenerAccessToken();`) y L457 (`AccessToken = token ?? string.Empty`) — Defecto D.
   - **Añadir** `PuedeSubir = User.IsInRole("JefeArea")` — mismo mecanismo que `_Sidebar.cshtml` usa para seccionar por rol (L16/L37/L58/L91).
   - El resto del action (ObtenerAccionAsync → adjuntos → ciclos → View) queda igual.
   - **`SesionService` se mantiene inyectado** en el controller aunque quede sin usos (precedente: `PlanController` lo conserva tras el hotfix de HU-023, L23-29 sin otros usos) — evita romper la fixture de tests (`AccionPlanControllerTests.cs:49-61`).
3. `Entregables.cshtml`: el panel de subida completo (L43-82) se envuelve en `@if (Model.PuedeSubir) { … }`. El aviso de ciclo cerrado (L48-54) queda **dentro** del panel (solo aplica a quien puede subir). El `Gerente` ve la vista read-only: tabla + descarga + `PuedeEliminar` por fila.
4. **Contraparte JS ya existente (verificada, sin cambio):** `entregables.js:40` — `if (!$form.length) return;` — sin `#form-entregables` en el DOM (el caso Gerente), el JS no inicializa nada.

### Restruca de vista (arregla también el Defecto F)

`Entregables.cshtml` (tabla + empty state, L84-154) pasa a:

- **Tabla siempre renderizada** (con `tbody` vacío si `Count == 0`) envuelta en `<div class="table-responsive" id="contenedor-tabla-adjuntos">` con `d-none` cuando `Count == 0` → `#tbody-adjuntos` **siempre existe en el DOM** y `agregarFilaTabla` siempre tiene dónde insertar.
- **Empty state siempre presente** con id y toggle:

```html
<div class="empty-state @(Model.Adjuntos.Count == 0 ? "" : "d-none")" id="empty-state-adjuntos">
    <i class="bi bi-paperclip empty-state__icon" aria-hidden="true"></i>
    <h3 class="empty-state__title">Aún no hay entregables adjuntos</h3>
    <p class="empty-state__message">Todavía no hay documentos que evidencien el avance de esta acción.</p>
</div>
```

- El mensaje pasa a **role-neutral** (el actual «Sube el primer documento…» es un imperativo que no corresponde al Gerente, que no puede subir) — UX-05.

---

## Defecto D — JWT fuera del DOM (SEC-01/SEC-05)

Eliminación completa del token en el navegador (mismo fix que el hotfix de HU-023 aplicó a `PlanConsolidado`):

| Archivo | Cambio |
|---|---|
| `Views/AccionPlan/Entregables.cshtml:6-7` | **Eliminar** el `<meta name="access-token">` y su comentario |
| `Models/EntregablesViewModel.cs:26-27` | **Eliminar** la propiedad `AccessToken` (blindado por test de reflexión — caso 17) |
| `Controllers/AccionPlanController.cs:447-448,457` | **Eliminar** la obtención/inyección del token (ver § Defecto C, punto 2) |
| `wwwroot/js/entregables.js:430-433` | **Eliminar** `obtenerToken()` |
| `wwwroot/js/entregables.js:226-233, 375-381, 406-412` | **Eliminar** los headers `Authorization` de los 3 `fetch` (los fetch pasan a los proxies — § Contrato JS) |

La vista gana el mismo comentario de trazabilidad que `Consolidado.cshtml:249-250`:

```cshtml
@* ADR-016: sin token JWT en el DOM — la autenticación va por cookie de sesión y los proxies
   MVC (/AccionPlan/Entregables*) adjuntan el Bearer server-side. *@
```

---

## Defecto E — Typo en `agregarFilaTabla`

`entregables.js:269` — `adjjunto.id` → **`adjunto.id`**. Sin este fix, la primera subida exitosa por el proxy lanza `ReferenceError: adjjunto is not defined` y la fila no se pinta (misma clase de defecto que el hotfix de HU-021 documentó en AGENTS.md v1.25).

---

## DTOs

**Ninguno nuevo.** Los proxies reenvían los DTOs existentes de la API (`EntregableAdjuntoResponse`, `EntregableDescargaResponse` — `PE-GOL.DTO/Responses/Objetivos/`) y el ViewModel vive en `PE-GOL.Aplicacion/Models` (fuera del contrato N-Tier de DTO). El único cambio de tipos del hotfix es el overload de `IApiClient` (§ B.4), que vive en `PE-GOL.Aplicacion/Services` — capa MVC, no `PE-GOL.DTO`.

## Lógica BLL

**Ninguna.** La API, BLL y DAL quedan **intactas** (0 cambios en `PE-GOL.API`/`BLL`/`DAL`/`Entity`/`DTO`). Todo el hotfix vive en la capa MVC (`PE-GOL.Aplicacion`) y en `PE-GOL.Tests`. Consecuencia: la cobertura BLL (89,96 %) **no puede driftar** (TEST-02).

## Queries DAL

**Ninguna.** Los proxies no tocan Dapper; consumen la API por `IApiClient` (ARCH-02: `Aplicacion → HTTP interno → API`).

## Validaciones

- **Cliente:** las validaciones existentes de `entregables.js` (L8-17: 5 archivos, 20 MB, extensiones, MIME) se mantienen intactas — UX-04. jQuery Validate no aplica (no hay form de campos; el dropzone es selección de archivos).
- **Servidor:** la validación de verdad vive en la API (FluentValidation de forma + `EntregableAdjuntoReglasNegocio` en BLL — spec original HU-022). **El proxy no revalida, reenvía** — la API sigue siendo la fuente de verdad y sus 422 viajan al usuario por la matriz de errores (§ B.3). El único guard nuevo del proxy es el 400 de `EntregablesSubir` sin archivos (defensa en profundidad).

---

## Contrato JS (`wwwroot/js/entregables.js`)

Patrón espejo del precedente validado en navegador (`plan-consolidado.js:91-112`): `fetch` a ruta **relativa al MVC**, header `Accept: application/json`, **sin** `Authorization`, `!response.ok` → parse de `{ message, errors }`, éxito → **payload directo** (sin `data.success`/`data.data` — Decisión D-5).

1. **`subirArchivos()`** (L215-264):
   - `fetch(\`/AccionPlan/EntregablesSubir/${accionId}\`, { method: 'POST', headers: { 'Accept': 'application/json' }, body: formData })` — el `FormData` mantiene el campo `archivos` (L219-222).
   - `response.ok` → `data` **es** el array de creados → `data.forEach(adjunto => agregarFilaTabla(adjunto))` (antes `data.data.forEach`, L242) → `actualizarEmptyState($tbodyAdjuntos.children().length)` (reemplaza el `$('.empty-state')…addClass('d-none')` de L252) → limpiar selección → toast de éxito.
   - `!response.ok` → manejo de error común (punto 5).
2. **`eliminarAdjunto(adjuntoId)`** (L372-391):
   - `fetch(\`/AccionPlan/EntregablesEliminar/${accionId}?entregableId=${adjuntoId}\`, { method: 'POST', headers: { 'Accept': 'application/json' } })`.
   - `response.ok` → toast → **`await cargarAdjuntos()`** (re-render — reemplaza la eliminación de fila + `location.reload()` de L356-362; Decisión D-9).
   - `!response.ok` → manejo de error común → `throw new Error(mensaje)` (el modal queda abierto — comportamiento actual).
3. **`descargarAdjunto(adjuntoId)`** (L402-427):
   - `fetch(\`/AccionPlan/EntregablesDescarga/${accionId}?entregableId=${adjuntoId}\`, { method: 'GET', headers: { 'Accept': 'application/json' } })`.
   - `response.ok` → `data.url` (antes `data.data?.url`, L416) ? `window.open(data.url, '_blank', 'noopener')` : toast «No se pudo generar el enlace de descarga.» (D-G: 200 con `Url = null` — degradación elegante, RNF-014).
   - `!response.ok` → manejo de error común.
4. **`cargarAdjuntos()`** (nuevo):
   - `fetch(\`/AccionPlan/EntregablesDatos/${accionId}\`, { method: 'GET', headers: { 'Accept': 'application/json' } })`.
   - `response.ok` → array → `$tbodyAdjuntos.empty()` → `data.forEach(adjunto => agregarFilaTabla(adjunto))` → `actualizarEmptyState(data.length)`.
   - `!response.ok` → manejo de error común (best effort, sin `throw`).
5. **Manejo de error común** (nuevo, extraído):
   - `if (response.status === 401) { mostrarToast('La sesión expiró. Vuelva a iniciar sesión.', 'warning'); window.location.href = '/Auth/Login'; return; }`
   - Parse defensivo del body (`try { errorData = await response.json(); } catch { }` — espejo de `plan-consolidado.js:101-106`).
   - Mensaje: `errorData?.errors?.length ? errorData.errors.join(' ') : (errorData?.message || ('Error ' + response.status))`.
6. **`actualizarEmptyState(conteo)`** (nuevo): `$('#empty-state-adjuntos').toggleClass('d-none', conteo > 0)` + `$('#contenedor-tabla-adjuntos').toggleClass('d-none', conteo === 0)`.
7. **`agregarFilaTabla(adjunto)`**: fix del typo L269 (Defecto E). El resto queda igual — ya consume camelCase (`adjunto.nombreArchivo`, `tamanoLegible`, `subidoPorNombre`, `createdAt`, `puedeEliminar` — L273-296), el mismo casing que produce el proxy (MVC `System.Text.Json` camelCase, consistente con la API).
8. **Eliminaciones**: `obtenerToken()` (L430-433), headers `Authorization` (3 fetch), TODOs `[hotfix-HU-022]` (L224-225, L373-374, L404-405) — la deuda queda pagada.
9. **Sin cambio**: `inicializar()` L40 (early-exit sin form — contraparte del render condicional), dropzone y validaciones de cliente (L8-17, L104-154), modal de eliminación (L334-370, salvo el punto 2), toast (L435-448).

---

## Tests requeridos (escritos por @QA antes de la implementación — TEST-01)

**Ubicación:** `PE-GOL.Tests/ControllersMvc/AccionPlanControllerTests.cs` — **se EXTIENDE, no se crea archivo nuevo** (misma decisión documentada en ese archivo para HU-021, L29-30: no duplicar la fixture `CrearController()` ni partir la regresión en dos clases). La fixture existente (L49-61: `Mock<IApiClient>` + `SesionService` con `FakeSession` + `NullLogger` + `TempData`) se reutiliza; para los casos 15-16 se añade al arrange el principal de rol: `controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "…") }, "Cookie"))`.

**Fase roja:** las 4 acciones proxy y `PuedeSubir` no existen → el rojo legítimo es el fallo de compilación del proyecto de tests (CS0117/CS1061), como en HU-021/HU-023.

### Bloque A — Proxies (12 casos)

| # | Método | Arrange | Act | Assert |
|---|---|---|---|---|
| 1 | `EntregablesDatos_ConAdjuntos_DevuelvePayloadJson200` | mock `GetAsync<List<EntregableAdjuntoResponse>>` → lista con 2 adjuntos | `EntregablesDatos(id)` | `ObjectResult` 200 con la lista íntegra; `Verify` `GetAsync` a `"/api/v1/acciones/{id}/entregables"` `Times.Once` |
| 2 | `EntregablesDatos_ListaVacia_Devuelve200ConArrayVacio` | mock → lista vacía | ídem | **200 con `[]`**, nunca 404 (D-C); payload vacío íntegro |
| 3 | `EntregablesDatos_ApiClientException_DevuelveMismoStatusSinDetallesInternos` | mock lanza `ApiClientException(403, "… https://localhost:7269/api/v1/acciones …")` (mensaje con URL interna, espejo del caso 46 de HU-023) | ídem | respuesta **403** cuyo cuerpo serializado **no** contiene `localhost:7269`, ni `Bearer`, ni `Authorization`; contiene `message`/`errors`; `Verify` `LogError` (detalles solo al log) |
| 4 | `EntregablesDatos_UnauthorizedException_Devuelve401NoRedirect` | mock lanza `UnauthorizedException` | ídem | HTTP **401** — **no** `RedirectToActionResult` (endpoint consumido por `fetch`) |
| 5 | `EntregablesSubir_ConArchivos_ProxeaMultipartYDevuelveCreados` | mock del **overload multi-archivo** `PostMultipartAsync<List<EntregableAdjuntoResponse>>` → 2 creados; 2 `IFormFile` de prueba | `EntregablesSubir(id, archivos)` | 200 con los creados; `Verify` del overload con path `"/api/v1/acciones/{id}/entregables"`, campo **`"archivos"`** y los 2 archivos `Times.Once` |
| 6 | `EntregablesSubir_SinArchivos_Devuelve400SinLlamarALaApi` | `archivos = new List<IFormFile>()` | ídem | **400** con `message`; `Verify` `PostMultipartAsync` `Times.Never` |
| 7 | `EntregablesSubir_ApiClientException422_Devuelve422ConErroresDeValidacion` | mock lanza `ApiClientException(422, "…", new List<string>{ "No se pueden subir más de 5 archivos…", "…" })` | ídem | **422** cuyo cuerpo contiene los `errors` de validación (viajan al usuario — § B.3) y sin detalles internos |
| 8 | `EntregablesDescarga_ConIds_ProxeaApiYDevuelveUrlFirmada` | mock `GetAsync<EntregableDescargaResponse>` → payload con `Url = "https://…supabase…"` | `EntregablesDescarga(id, entregableId)` | 200 con el payload íntegro; `Verify` path `"/api/v1/acciones/{id}/entregables/{entregableId}/descarga"` |
| 9 | `EntregablesDescarga_UrlNull_Devuelve200ConUrlNull` | mock → `EntregableDescargaResponse` con `Url = null` | ídem | **200** con `Url = null` (D-G: degradación elegante, no 500 — el JS la convierte en toast) |
| 10 | `EntregablesDescarga_ApiClientException404_Devuelve404` | mock lanza `ApiClientException(404, "…")` | ídem | 404 con `{ message, errors }` |
| 11 | `EntregablesEliminar_ConIds_ProxeaDeleteYDevuelveTrue` | mock `DeleteAsync<bool>` → `true` | `EntregablesEliminar(id, entregableId)` | 200 con `true`; `Verify` `DeleteAsync` a `"/api/v1/acciones/{id}/entregables/{entregableId}"` `Times.Once` |
| 12 | `EntregablesEliminar_ApiClientException_DevuelveMismoStatusSinDetallesInternos` | ídem 3 con 403 | ídem | ídem 3 |

### Bloque B — Autorización por reflexión (2 casos)

| # | Método | Assert |
|---|---|---|
| 13 | `EntregablesSubir_Autorizacion_SoloJefeArea` | reflexión sobre `AccionPlanController`: `EntregablesSubir` bajo `[Authorize(Roles = "JefeArea")]` **explícito** (la clase es JEF+GER → el estrechamiento debe ser propio de la acción; espejo del caso 48 de HU-023) |
| 14 | `EntregablesProxy_DatosDescargaEliminar_Autorizacion_JefeAreaGerente` | reflexión: `EntregablesDatos`, `EntregablesDescarga` y `EntregablesEliminar` bajo JEF+GER (explícito o heredado de la clase) — el proxy **no amplía** la superficie de acceso de los endpoints subyacentes |

### Bloque C — Action `Entregables` + ViewModel (3 casos)

| # | Método | Arrange | Act | Assert |
|---|---|---|---|---|
| 15 | `Entregables_RolJefeArea_PuedeSubirTrue` | mocks `GetAsync` (acción, adjuntos, ciclos — match por path); `HttpContext.User` con claim `ClaimTypes.Role = "JefeArea"` | `Entregables(id)` | `ViewResult` → `EntregablesViewModel.PuedeSubir == true` |
| 16 | `Entregables_RolGerente_PuedeSubirFalse` | ídem con rol `"Gerente"` | ídem | `PuedeSubir == false` (el panel no se renderiza — la vista lo condiciona) |
| 17 | `EntregablesViewModel_Reflexion_SinAccessTokenConPuedeSubir` | reflexión sobre `typeof(EntregablesViewModel)` | — | **sin** propiedad `AccessToken` (Defecto D — espejo del fix del hotfix de HU-023 sobre `PlanConsolidadoViewModel`); **con** propiedad `PuedeSubir` (bool) |

### Bloque D — Modificación del test de regresión (0 métodos nuevos)

- `PE-GOL.Tests/Architecture/JsApiBaseUrlRegressionTests.cs`: **quitar `"entregables.js"` de `ExcepcionesConocidas`** (L62) — la lista queda **VACÍA**, como el propio test documenta en su Assert 2 y comentarios (L54-63, L131-146). El Assert 1 existente (L116-129) pasa a ser el **gate del Defecto B**: tras el fix, `entregables.js` ya no contiene el literal `/api/` y el scan sale limpio.

**Total: 17 métodos xUnit nuevos + 1 modificación.** Los 813 existentes quedan en verde (regresión). La navegación (Defecto A) no tiene test C# posible — los enlaces viven en Razor/JS y ningún arnés del repo renderiza vistas (mismo criterio documentado por HU-023 v3: «ningún test C# puede ejercitar una URL construida en JS») — su gate es la **validación visual en navegador** (DoD).

---

## Archivos afectados

| # | Archivo | Capa | Cambio |
|---|---|---|---|
| 1 | `PE-GOL.Aplicacion/Services/IApiClient.cs` | MVC | Overload aditivo `PostMultipartAsync` multi-archivo (§ B.4) |
| 2 | `PE-GOL.Aplicacion/Services/ApiClient.cs` | MVC | Implementación del overload (espejo del mono-archivo, § B.4) |
| 3 | `PE-GOL.Aplicacion/Controllers/AccionPlanController.cs` | MVC | 4 acciones proxy nuevas (§ B.2) + `Entregables` sin token y con `PuedeSubir` (§ Defecto C/D) |
| 4 | `PE-GOL.Aplicacion/Models/EntregablesViewModel.cs` | MVC | `AccessToken` fuera · `PuedeSubir` dentro |
| 5 | `PE-GOL.Aplicacion/Views/AccionPlan/Entregables.cshtml` | MVC | meta fuera (D) · panel condicionado a `PuedeSubir` (C) · tabla+empty state siempre en DOM (F) · mensaje role-neutral |
| 6 | `PE-GOL.Aplicacion/wwwroot/js/entregables.js` | MVC | 3 fetch → proxies · sin `obtenerToken`/headers Authorization (D) · typo L269 (E) · `cargarAdjuntos`/`actualizarEmptyState`/manejo de error común (§ Contrato JS) |
| 7 | `PE-GOL.Aplicacion/Views/AccionPlan/Index.cshtml` | MVC | Botón `bi-paperclip` por fila → `Entregables` (§ A.1) |
| 8 | `PE-GOL.Aplicacion/Views/Plan/Consolidado.cshtml` | MVC | Columna «Acciones» con enlace `Entregables` por fila (§ A.2) |
| 9 | `PE-GOL.Aplicacion/wwwroot/js/plan-consolidado.js` | MVC | `renderTabla` (L168-195) añade la misma celda de enlace (§ A.2) |
| 10 | `PE-GOL.Tests/ControllersMvc/AccionPlanControllerTests.cs` | Tests | 17 métodos nuevos (extensión) |
| 11 | `PE-GOL.Tests/Architecture/JsApiBaseUrlRegressionTests.cs` | Tests | `ExcepcionesConocidas` queda vacía |

> **Nota de trazabilidad:** los archivos 8-9 pertenecen a HU-023 — se tocan **mínimamente** (una celda por fila) porque el hub del Gerente re-renderiza client-side y sin el render JS el enlace desaparecería al primer filtro. Debe quedar documentado en el cierre para no confundirlo con deuda de HU-023.

---

## Criterios de Done

- [x] `dotnet build PE-GOL.sln` → 0 errores / 0 advertencias
- [x] `dotnet test` → **813 previos + 17 nuevos = 830 en verde**, 0 omitidos (TEST-06)
- [x] Cobertura BLL ≥ 70 % **sin drift** (89,96 % — no hay cambios BLL; TEST-02)
- [x] `JsApiBaseUrlRegressionTests`: `ExcepcionesConocidas` **vacía** y el scan de `wwwroot/js/*.js` limpio (gate del Defecto B)
- [x] `entregables.js` sin literal `/api/`, sin `obtenerToken()` y sin headers `Authorization` (Defectos B/D)
- [x] Swagger sin cambios (54 paths intactos — los proxies son MVC, ARCH-03)
- [x] Design System aplicado: `.btn-pe--secondary--sm`, `.tabla-pe`, `.empty-state`, `.modal-pe`, `bi-paperclip` (UX-01/03/05)
- [x] **Validación visual en navegador por Jorge** (gate final de toda vista con JS — ADR-016):
  - Login `JefeArea` → Plan de Acción → botón paperclip por fila → vista Entregables → subir 1-2 archivos → toast + filas nuevas **sin reload** → descargar → pestaña nueva con el archivo → eliminar → modal → tabla re-renderizada sin reload.
  - Login `JefeArea` → acción sin adjuntos → subir → **la tabla aparece** (Defecto F pagado).
  - Login `Gerente` → Plan Consolidado → botón paperclip por fila → vista Entregables **sin panel de subida** → descargar funciona.
  - Inspección del DOM: **sin** `<meta name="access-token">` (Defecto D).
- [x] Sin ADR nuevo (aplica ADR-016 aceptado + la decisión de seguridad del hotfix de HU-023 ya registrada en AGENTS.md v1.28) — ver «Notas para @Documenter»
- [x] Spec → `Implementado` al cierre (LOOP-05, @Documenter) + HANDOFF ítems 3-4 retirados de «Deuda técnica pendiente»

---

## Decisiones

| # | Decisión | Justificación |
|---|---|---|
| **D-1** | 4 proxies MVC en `AccionPlanController` con `IApiClient` (JWT en sesión) | ADR-016 (opción 1, ya aceptada por Jorge). `EntregablesDatos` ya estaba nombrado como fix en ADR-016 § Consecuencias, HANDOFF ítem 3 y los TODOs del propio JS. Descartadas las otras 2 opciones de ADR-016: inyectar `Api:BaseUrl` al JS (CORS + JWT cross-origin) y render 100 % server-side (reescritura grande; aquí la subida multipart necesita interacción). |
| **D-2** | Verbos: GET datos/descarga · POST subir · **POST eliminar** | El patrón MVC del repo elimina por `[HttpPost]` (`AccionPlanController.cs:248`) y todos sus formularios son POST. Un `DELETE` fetch también funcionaría, pero POST mantiene la uniformidad del controller. |
| **D-3** | `id` por ruta convencional `{id?}` · `entregableId` por `[FromQuery]` | El pattern default (`Program.cs:76-78`) solo bindea un segmento posicional; el query string evita introducir attribute routing al MVC (cero mecanismos nuevos). Precedente: los proxies de HU-023 usan `[FromQuery]` (`PlanController.cs:100`). |
| **D-4** | Errores: mismo status + body `{ message, errors }` con los mensajes user-facing de la API; detalles internos solo al log | La regla dura de ADR-016 (body sin JWT/`Authorization`/URL interna) se respeta y se blinda por test (casos 3/12). Los mensajes que viajan son los de dominio de la API: `ApiClientException` fue diseñado para mostrarse (`ApiClientException.cs:6-7`) y el `ApiClient` ya sanitiza el 500 (`ApiClient.cs:264-266`) y toma el mensaje del `ApiResponse.Message` (`ApiClient.cs:282-284`). Sin esto, los 422 (máx. 5 archivos / 20 MB / tipo) llegarían como «Error al subir» y el usuario no sabría qué corregir (UX-04). Ver «Notas para @Documenter» punto 1. |
| **D-5** | Payload directo, sin wrapper `ApiResponse<T>` en la respuesta del proxy | Precedente `ConsolidadoDatos` (`PlanController.cs:106`). El JS adapta (`data` en vez de `data.data` — el propio `plan-consolidado.js:111` hace `data.data \|\| data` defensivo). |
| **D-6** | Overload multi-archivo de `PostMultipartAsync` (aditivo) | Lo planificó el spec original de HU-022 (§ Alcance, punto 7) y nunca se implementó porque la subida se hizo con el fetch roto. Aditivo: la firma mono-archivo (HU-006) no cambia. |
| **D-7** | `PuedeSubir` calculado en el controller (`User.IsInRole`) | El permiso nunca se confía al JS ni al DOM; `User.IsInRole` es el mecanismo que `_Sidebar.cshtml` ya usa para seccionar por rol. La API sigue siendo la capa de verdad (403 si alguien bypasea la UI). |
| **D-8** | Navegación por rol: Index (JEF) + Consolidado Razor+JS (GER); Gantt y Sidebar NO | Cada rol con acceso recibe exactamente un punto de entrada desde su hub natural. Gantt: read-only por diseño (HU-021). Sidebar: la vista es por-acción (requiere `accion_id`). |
| **D-9** | Re-render tras eliminar vía `EntregablesDatos` (reemplaza `location.reload()`, L361) | Da propósito operativo al proxy listar, elimina el salto de reload completo y unifica el render (tbody + empty state + contenedor toggled). El empty state y la tabla pasan a estar siempre en el DOM (pagando el Defecto F). |
| **D-10** | Límites multipart: sin cambios | Ya configurados en `Program.cs:9-14` (106 MB, comentado «HU-022»). Documentado para que @BackendDev no duplique configuración ni añada `[RequestSizeLimit]`. |
| **D-11** | CSRF: sin antiforgery, patrón del repo intacto | Verificado: 0 antiforgery en `PE-GOL.Aplicacion`. Cookies `SameSite=Lax` por defecto + `fetch` same-origin (la cookie no viaja cross-origin sin `credentials:'include'`+CORS). Introducir antiforgery sería un patrón nuevo → decisión escalada, no unilateral. |
| **D-12** | `SesionService` sigue inyectado en `AccionPlanController` aunque quede sin usos | Precedente: `PlanController` lo conserva tras el hotfix de HU-023 (L23-29, sin otros usos). Evita romper la fixture `CrearController()` de tests. |
| **D-13** | Empty state siempre presente + mensaje role-neutral | El mensaje actual («Sube el primer documento…») es un imperativo que no corresponde al Gerente. UX-05. |
| **D-14** | 401 del proxy → JS: toast + redirect `/Auth/Login` | Mejora mínima sobre el precedente (`plan-consolidado.js` muestra «Error 401» a secas): la sesión murió y el usuario necesita re-autenticarse. El proxy sigue devolviendo `Unauthorized()` (401, nunca redirect — caso 4). |

---

## Notas para @Documenter / @Orquestador (no bloqueantes)

1. **D-4 refina la implementación del contrato de errores de ADR-016** (mensajes user-facing de la API viajan; la regla dura — sin JWT/URL interna en el body — intacta y blindada por test). ADR-016 ya delega los contratos de detalle al spec de cada vista («Contrato completo … en `specs/sprint-04/HU-023.spec.md` "Revisión v3"»), así que este spec define el suyo sin extender el ADR. **Recomendación:** al cerrar el hotfix, sincronizar la redacción de ADR-016 § Decisión («Contrato de errores del proxy») con esta precisión, para que el siguiente proxy no reabra la pregunta.
2. Al cerrar: retirar los ítems 3-4 de «Deuda técnica pendiente» del HANDOFF, actualizar la tabla de estado de Sprint 4 y bump de AGENTS.md (v1.29) — el hotfix NO cambia ninguna regla de la constitución.
3. Los archivos de HU-023 (`Consolidado.cshtml`, `plan-consolidado.js`) se tocan mínimamente (una celda por fila) para la navegación GER — documentarlo en el cierre (ver nota de trazabilidad en § Archivos afectados).
4. Los commits siguen el formato `[HU-022] fix: …` (LOOP-06), atómicos, sin mezclar con HU-025 (Gestión de KRs, siguiente en el loop).

---

---

## Implementación — Registro de cierre

**Fecha:** 2026-10-01
**Build:** ✅ `dotnet build` — 0 errores, 0 advertencias
**Tests:** ✅ `dotnet test` — **830/830** passed, 0 failed, 0 skipped (813 previos + 17 nuevos: 12 de proxies [Bloque A] + 2 de autorización por reflexión [Bloque B] + 3 de action `Entregables`/ViewModel [Bloque C])
**Cobertura BLL:** **89,96%** (línea) — sin drift (0 cambios en BLL/DAL, TEST-02)
**Gate arquitectónico:** ✅ `JsApiBaseUrlRegressionTests` con `ExcepcionesConocidas` **vacía** — el scan de `wwwroot/js/*.js` sale limpio (Defecto B pagado, ADR-016)
**Validación visual en navegador (Jorge):** ✅ — confirmada por @Orquestador al encargar el cierre (2026-10-01)

### Defectos corregidos (A-F)

- **A** — Vista inalcanzable → navegación por rol (D-8): botón `bi-paperclip` por fila en `AccionPlan/Index.cshtml` (JEF, L208-210) + columna «Acciones» con enlace en `Plan/Consolidado.cshtml` (GER, L213-214) **y** en la plantilla JS de `plan-consolidado.js` (L195-196). Gantt (read-only, HU-021) y Sidebar (vista por-acción) explícitamente NO.
- **B** — 3 `fetch` en ruta relativa → 404 → **4 proxies MVC** con `IApiClient` (ADR-016): `EntregablesDatos` (GET), `EntregablesSubir` (POST, `[Authorize(Roles = "JefeArea")]` explícito), `EntregablesDescarga` (GET, `[FromQuery]`), `EntregablesEliminar` (POST, `[FromQuery]`) — `AccionPlanController.cs` L472-567 + **overload multi-archivo** de `PostMultipartAsync` (D-6: `IApiClient.cs:38` + `ApiClient.cs:104`, firma mono-archivo de HU-006 intacta).
- **C** — Dropzone visible al Gerente → `PuedeSubir` calculado server-side (`User.IsInRole("JefeArea")`, L459); el panel de subida se renderiza solo `@if (Model.PuedeSubir)` (`Entregables.cshtml:43`); el Gerente ve la vista read-only.
- **D** — JWT en el DOM eliminado (SEC-01/SEC-05): `<meta name="access-token">`, `EntregablesViewModel.AccessToken`, `obtenerToken()` y los 3 headers `Authorization` fuera — mismo fix que el hotfix de HU-023; comentario de trazabilidad ADR-016 en la vista (L6-7).
- **E** — Typo `adjjunto` → `adjunto` corregido (el `ReferenceError` de la primera subida exitosa, misma clase que el hotfix de HU-021 documentó).
- **F** — Tabla + empty state **siempre en el DOM**: `#contenedor-tabla-adjuntos` (L89) + `#tbody-adjuntos` (L101) + `#empty-state-adjuntos` (L147) con toggle y mensaje role-neutral (UX-05) — la primera subida sobre una acción vacía ya pinta la tabla.

### Archivos creados

Ninguno (los 11 archivos afectados son modificaciones de archivos existentes).

### Archivos modificados

- `PE-GOL.Aplicacion/Services/IApiClient.cs` — overload aditivo `PostMultipartAsync` multi-archivo (§ B.4)
- `PE-GOL.Aplicacion/Services/ApiClient.cs` — implementación del overload (espejo del mono-archivo, § B.4)
- `PE-GOL.Aplicacion/Controllers/AccionPlanController.cs` — 4 acciones proxy (§ B.2) + `Entregables` sin token y con `PuedeSubir` (§ Defectos C/D) + helper `SanitizarMensajeProxy` (ver Desviaciones)
- `PE-GOL.Aplicacion/Models/EntregablesViewModel.cs` — `AccessToken` fuera · `PuedeSubir` dentro
- `PE-GOL.Aplicacion/Views/AccionPlan/Entregables.cshtml` — meta fuera (D) · panel condicionado a `PuedeSubir` (C) · tabla+empty state siempre en DOM (F) · mensaje role-neutral
- `PE-GOL.Aplicacion/wwwroot/js/entregables.js` — 3 fetch → proxies · sin `obtenerToken`/headers `Authorization` (D) · typo (E) · `cargarAdjuntos`/`actualizarEmptyState`/manejo de error común (§ Contrato JS)
- `PE-GOL.Aplicacion/Views/AccionPlan/Index.cshtml` — botón `bi-paperclip` por fila → `Entregables` (§ A.1)
- `PE-GOL.Aplicacion/Views/Plan/Consolidado.cshtml` — columna «Acciones» con enlace `Entregables` por fila (§ A.2) — **archivo de HU-023, tocado mínimamente** (ver Trazabilidad)
- `PE-GOL.Aplicacion/wwwroot/js/plan-consolidado.js` — `renderTabla` añade la misma celda de enlace (§ A.2) — **archivo de HU-023, tocado mínimamente** (ver Trazabilidad)
- `PE-GOL.Tests/ControllersMvc/AccionPlanControllerTests.cs` — 17 métodos nuevos (extensión de la fixture existente, casos 1-17)
- `PE-GOL.Tests/Architecture/JsApiBaseUrlRegressionTests.cs` — `ExcepcionesConocidas` queda **vacía** (gate del Defecto B)

### Trazabilidad (Nota para @Documenter, punto 3)

Los archivos 8-9 (`Consolidado.cshtml`, `plan-consolidado.js`) pertenecen a HU-023 y se tocan **mínimamente** (una celda por fila) porque el hub del Gerente re-renderiza client-side: sin el render JS, el enlace desaparecería al primer filtro/paginación. **No es deuda de HU-023.**

### Desviaciones del spec

Ninguna que afecte a proxies, endpoints, tests o reglas especificadas — la implementación sigue fielmente el spec aprobado (D-1..D-14). Dos diferencias **aditivas** de defensa, verificadas por @Documenter en el cierre y sin impacto en el contrato:

- El body de error de los 4 proxies envuelve `ex.Message` en un helper privado `SanitizarMensajeProxy` (`AccionPlanController.cs:600-642`) que reemplaza URLs absolutas por `[URL interna]` y JWT/`Bearer` por `[token]` — defensa en profundidad sobre D-4/ADR-016 (la regla dura ya está blindada por el `ApiClient` y por los tests 3/12); los mensajes user-facing de la API pasan intactos.
- La plantilla JS de `plan-consolidado.js` envuelve el `accionId` en `esc()` (L195) — escape defensivo del GUID en el render client-side.

### Decisiones tomadas durante implementación

Ninguna fuera de lo aprobado (D-1..D-14 registradas en este spec). Sin ADR nuevo (aplica ADR-016 aceptado + la decisión de seguridad del hotfix de HU-023 ya registrada en AGENTS.md v1.28). Sin migración (0 cambios en API/BLL/DAL/Entity/DTO). Swagger sin cambios (54 paths intactos — los proxies son acciones MVC, ARCH-03). Commits pendientes (los hace Jorge, formato `[HU-022] fix: …`, LOOP-06).

---

# Hotfix v2 — 8 defectos de runtime (validación real en navegador, 2026-10-01)

> **Por qué existe esta revisión.** El cierre v1 (…) se apoyó en tests C# y en una validación visual
> **parcial**. Al ejercitar el flujo real del **JefeArea** —subir varios archivos a la vez, en formatos
> Office, y luego descargarlos— aparecieron 8 fallos que ninguna aserción de C# puede ver porque
> ocurren entre el navegador, el proxy MVC, el `ApiClient`, el pool de conexiones de Npgsql y el SDK de
> Supabase Storage. Es exactamente la 2ª iteración del patrón que ya aparece en HU-021, HU-022 v1 y
> HU-023 (AGENTS.md v1.25/v1.28): **la validación en navegador es un gate de primera clase, no un
> adorno del DoD** (LOOP-03).

## Alcance revisado del v2

El v1 se declaró «100 % `PE-GOL.Aplicacion` + `PE-GOL.Tests`, 0 cambios BLL/Utility». La validación
real demostró que **esa frontera era falsa**: los fallos estaban en `ApiClient` (capa Aplicación),
en `EntregableAdjuntoService` (BLL) y en `StorageHelper`/`TipoArchivoHelper` (Utility). El hotfix v2
**amplía el alcance a esas dos capas** — con las consecuencias que se detallan en «Consecuencias».

## Los 8 defectos

| # | Defecto | Síntoma real | Causa raíz | Capa | Fix |
|---|---------|--------------|------------|------|-----|
| **G** | **Subida múltiple: streams cerrados antes de enviarse.** Al subir 2+ archivos a la vez, la petición multipart llegaba vacía o lanzaba `ObjectDisposedException`; el navegador reportaba «error de servidor». | 2º archivo en adelante no se guardaba | El overload multi-archivo de `PostMultipartAsync` (D-6) hacía `await using var stream` **dentro** del `foreach`: al salir de cada iteración el stream se cerraba, y `HttpClient` leía de él ya disposado. | `Aplicacion/Services/ApiClient.cs` | Los `MemoryStream` viven en una lista hasta después de `SendAsync`; en el reintento 401 se re-abren con `Position = 0`. |
| **H** | **Concurrencia sobre una sola conexión Npgsql.** Al subir varios archivos a la vez, la BLL lanzaba errores intermitentes de conexión/pool. | Fallos aleatorios,playlist de archivos incompletas | `EntregableAdjuntoRepository` **cachea una única `NpgsqlConnection`** en un campo (`_connectionActiva`) y la reutiliza entre peticiones concurrentes. Npgsql **no permite** concurrencia sobre una `Connection` y sus `Command` son *not thread-safe*. | `BLL/Repositories/Objetivos/EntregableAdjuntoRepository.cs` | El servicio procesa los archivos **secuencialmente** (`foreach`, no `Task.WhenAll`). |
| **I** | **Descarga 500 `Object not found`.** El archivo se listaba en la tabla pero era **imposible de descargar** ni de eliminar: la compensación de RNF-014 tampoco encontraba el objeto y lo dejaba huérfano en el bucket. | HTTP 500 en `/EntregablesDescarga`; log `SupabaseStorageException: Object not found` | La API de Storage devuelve la clave persistida **con el nombre del bucket delante** (`entregables/{tenant}/…`), pero las rutas del dominio son **relativas a la raíz del bucket** (ADR-013). Se guardaba la clave tal cual → al descargar se pedía `entregables/entregables/…`. | `Utility/Storage/StorageHelper.cs` | Normalización en el límite: `QuitarPrefijoBucket(bucket, ruta)` antes de devolver la ruta a la BLL. |
| **J** | **Navegación rota para el Gerente.** Desde Plan Consolidado → Entregables → «Volver» el Gerente caía en `/Auth/AccessDenied?ReturnUrl=%2FAccionPlan%2FIndex`. | AccessDenied | El botón usaba `asp-action="Index"` **sin** `asp-controller`: el tag helper lo resuelve contra el controlador en curso → `/AccionPlan/Index`, que es `[Authorize(Roles = "JefeArea")]`. | `Views/AccionPlan/Entregables.cshtml` | Enlace explícito **por rol**: Gerente → `/Plan/Consolidado` (de donde llega); JefeArea → `/AccionPlan/Index`. El breadcrumb también refleja la sección correcta. |
| **K** | **`.docx`/`.xlsx> 512 KB rechazados como «tipo no permitido».** Archivos Office legítimos de tamaño normal eran rechazados. | 422 «tipo de archivo no permitido» | La detección por *magic number* leía solo los primeros **512 bytes** del stream; en OOXML (`zip`) la firma `PK\x03\x04` está al inicio pero el motor de allowlist comparaba el bloque completo. | `Utility/Files/TipoArchivoHelper.cs` | Detección sobre el **archivo completo** en memoria (`byte[]`), que es lo que ya se tenía en memoria de todos modos. |
| **L** | **Falsos negativos en PDF.** PDFs con cabecera estándar pero structure particular dában «tipo no permitido». | 422 | Ídem K: la comparación se hacía sobre un bloque truncado. | `Utility/Files/TipoArchivoHelper.cs` | Ídem K. |
| **M** | **Pérdida de datos al fallar la confirmación.** Si el commit fallaba tras subir, el archivo quedaba **persistido sin registro en BD** (o al revés). | Ficheros huérfanos en el bucket | La compensación (D-H) se ejecutaba **después** del `Commit`, y la re-lectura para verificar persistencia se hacía en un orden que dejaba la fila y el objeto desincronizados. | `BLL/Services/EntregableAdjuntoService.cs` | Re-lectura de verificación **antes** del `Commit`; la compensación solo borra el objeto si la escritura en BD no se confirmó. |
| **N** | **Mensajes de validación inaccionables.** El usuario veía «error de servidor» sin saber qué corregir. | Mala UX | Excepciones del pool/Storagge llegaban al toast sin traducción. | `BLL/Services/EntregableAdjuntoService.cs` | Mensajes user-facing con el formato esperado y el límite concreto («máx. 20 MB», «formatos: PDF, DOCX, XLSX, PNG, JPG»); los detalles técnicos solo al log (STACK-11). |

## Tests del v2 (TDD — rojo antes de la corrección, TEST-01)

| Bloque | Archivo | Casos | Qué fija |
|--------|---------|-------|----------|
| Multipart / stream lifetime | `PE-GOL.Tests/ApiClientTests.cs` | 3 | 2+ archivos llegan íntegros; el reintento 401 re-envía los cuerpos; el `HttpMessageHandler` de test serializa los cuerpos para poder asertarlos. |
| Tipo de archivo | `PE-GOL.Tests/EntregableAdjuntoServiceTests.cs` | 4 | `.docx`/`.xlsx` legítimos >512 KB aceptados; PDF/PNG/JPG válidos aceptados; extensionno soportada rechazada con mensaje accionable; ZIP completo (no truncado) detectado como OOXML. |
| Atomicidad / compensación | `PE-GOL.Tests/EntregableAdjuntoServiceTests.cs` | 2 | Fallo de commit → objeto compensado; verificación antes del commit. |
| Rutas de Storage | `PE-GOL.Tests/StorageFileApiTests.cs` | 1 | La clave devuelta por el SDK con prefijo `entregables/` se persiste como ruta **relativa**. |
| Navegación por rol | `PE-GOL.Tests/Architecture/NavegacionPorRolRegressionTests.cs` | 2 (nuevo) | El enlace «Volver» declara `asp-controller` explícito; distingue destino Gerente (Plan Consolidado) vs JefeArea (listado). Escanea la vista Razor como fuente. |

**Total v2: 11 métodos nuevos (830 → 841).** Todos los tests se escribieron y se vieron **fallar**
antes de tocar el código de producción (TDD real, no retrospectivo).

## Reparación de datos en caliente

La corrección de la ruta (defecto I) llegó **después** de que ya existieran filas con el prefijo
duplicado. Como no hay migración que aplicar (el esquema no cambia, solo el valor de una columna de
datos), se reparó en caliente el conjunto afectado:

- **5 filas** de `entregable_adjunto` (4 en la acción `43d9697a-6fe5-478a-9a0c-4673cd102ee6` +
  1 en `4b54e6be-…`): `file_path` normalizado — se les quitó el prefijo `entregables/`. Verificado que
  las 5 URLs firmadas devuelven **HTTP 200** con el tamaño de bytes esperado (p. ej. `Cot_RANSA.docx`
  → 319.845 bytes).
- **17 objetos huérfanos** en el bucket `entregables` (subidas de prueba, compensadas por el código de
  la BLL pero **no borradas** por el bug I) eliminados tras reconciliar las 22 claves del bucket contra
  las 5 filas de BD. Estado final: **5 objetos, los 5 referenciados** (verificado).

Sin cambios de esquema → **sin migración** (DB-01/DB-02 no aplican). Sin cambios de endpoint → **54
paths de Swagger intactos** (ARCH-03).

## Consecuencias y alcance ampliado (por qué toca BLL y Utility)

- **`PE-GOL.BLL`** — el defecto H (concurrencia) y el M (atomicidad) eran **preexistentes** en
  `EntregableAdjuntoService` / su repositorio; ningún test los había detectado porque todos usaban
  un único archivo y un `NpgsqlConnection` real nunca se compartía entre hilos en los tests. Se
  corrigen porque son la causa directa de los fallos del flujo real.
- **`PE-GOL.Utility`** — `TipoArchivoHelper` y `StorageHelper` son la frontera con Supabase; los
  defectos K/L/I son de normalización en esa frontera.
- **Sin ADR nuevo.** `QuitarPrefijoBucket` no es una decisión arquitectónica: es hacer explícito en el
  código el contrato que **ADR-013** ya fija (rutas de adjuntos **relativas** a la raíz del bucket) y
  que ADR-016 ya fija para el navegador (nada de la API interna en el cliente). Documentado como
  aclaración de contratos vigentes, no como decisión nueva.

## Criterios de Done del v2

- [x] `dotnet clean` + `dotnet build PE-GOL.sln` → **0 errores / 0 advertencias**
- [x] `dotnet test PE-GOL.sln --no-build` → **841/841 en verde**, 0 omitidos (830 previos + 11 nuevos)
- [x] Cobertura BLL ≥ 70 % (TEST-02) → **90,00%** (sube desde 89,96%; sin regresión)
- [x] `JsApiBaseUrlRegressionTests` sigue con `ExcepcionesConocidas` **vacía** (ADR-016)
- [x] `NavegacionPorRolRegressionTests` en verde (defecto J) y **verificado rojo↔verde** contra el markup antiguo
- [x] Sin cambios de API → **54 paths de Swagger** intactos
- [x] Sin migración de esquema
- [x] Sin ADR nuevo (aplican ADR-013 y ADR-016)
- [x] **Validación real en navegador** (Jorge): subida múltiple Office, descarga de cada formato, enlace
      «Volver» del Gerente al Plan Consolidado (HTTP 200, no AccessDenied), y descarga verificada
      extremo a extremo (HTTP 200, tamaño de bytes correcto).
- [x] Reparación de datos en caliente aplicada y verificada (5 filas normalizadas; bucket reconciliado
      de 22 a 5 objetos, los 5 referenciados)

## Desviaciones del v2 respecto del spec aprobado

1. **Alcance ampliado** de `Aplicacion + Tests` a `Aplicacion + BLL + Utility + Tests` (§ «Alcance
   revisado del v2»). Justificado por los 8 defectos; cada cambio toca una capa distinta y está
   trazado arriba.
2. El defecto I se corrigió en `StorageHelper` (no en el cliente de Supabase) porque `StorageHelper`
   es la capa que conoce el contrato de rutas **relativas al bucket** (ADR-013) y es la única testable
   sin red; el cliente de Supabase solo habla con la API de Storage. `SupabaseStorageFileApi` recibe
   un comentario que documenta el por qué y no re-normaliza (evita lógica duplicada).
3. No se añadió `ApiClientException` ni ningún tipo nuevo en `PE-GOL.DTO`; los mensajes se tradujeron
   en la BLL.

---

*Spec HU-022-hotfix — Hotfix de UI de Entregables · Sprint 4 · EP-07 · Estado: Implementado (v1 aprobado 2026-10-01 · cerrado v1 y **v2 revisado** el 2026-10-01 tras validación real — LOOP-05)*
*v2 (2026-10-01): 8 defectos de runtime (G,H,I,J,K,L,M,N) corregidos · build 0/0 · 841/841 tests · cobertura BLL 90,00% · alcance ampliado a BLL y Utility · sin migración · sin ADR nuevo (ADR-013 + ADR-016) · datos reparados en caliente.*

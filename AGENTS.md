# AGENTS.md — Constitución del Proyecto PE-GOL SaaS
> Este archivo es la fuente de verdad del proyecto. Todo agente, spec, ADR y plan debe leerlo antes de actuar.
> Versión: 1.31 · Fecha: 2026-10-02 · Autor: Jorge (Dicegsa)
> v1.31 (2026-10-02): **HU-025 (Gestión de Key Results) Implementada — Sprint 4 completado (5/5 HU, 28 de 28 pts)**. Tests: **917/917** en verde (841 previos + 76 nuevos: 56 BLL + 13 DAL + 7 MVC) · build 0 errores / 0 advertencias · Cobertura BLL **90,2%** (≥ 70% — TEST-02) · **sin ADR nuevo** (tabla `key_result`, su `UNIQUE (okr_id, codigo)` y su RLS `key_result_policy` ya existían en el DDL base; sin migración; decisiones F0–F7 ya resueltas en el spec aprobado) · `KeyResultController` nuevo (API, 6 endpoints JEF-only — incluido el masivo `PUT .../key-results/pesos` de F0) + `KeyResultService`/`IKeyResultService` + `KeyResultRepository`/`IKeyResultRepository` + DTOs de KR + vistas Razor `KeyResult/Index|Crear|Editar` + `keyresults.js` + botón «KRs» (`bi-bullseye`) en `Okr/Index` con `asp-controller="KeyResult"` explícito · **Sprint 4 cerrado: 5/5 HU — 28 de 28 pts**. Pendiente: **Sprint 5** (HU-026 Registro Mensual Valores KRs, HU-027 Consolidado OKRs, HU-028 CRUD CAPEX, HU-029 Desembolso CAPEX). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.30 (2026-10-01): **hotfix v2 de HU-022 (UI de Entregables) aplicado y cerrado** - la validación real en navegador del flujo del **JefeArea** (subida múltiple de Office + descarga) destapó **8 defectos de runtime** que los 830 tests no podían detectar, todos en la interacción navegador/proxy/`ApiClient`/pool Npgsql/SDK de Storage: **(G)** `PostMultipartAsync` multi-archivo cerraba cada `MemoryStream` dentro del `foreach` (`await using` por iteración) -> el 2° archivo en adelante llegaba vacío/disposado al `SendAsync`; ahora los streams viven en lista y el reintento 401 los re-abre con `Position=0` · **(H)** `EntregableAdjuntoRepository` cachea una única `NpgsqlConnection` en campo y la reutiliza entre peticiones concurrentes (no es thread-safe) -> la BLL procesa los archivos **secuencialmente** en vez de `Task.WhenAll` · **(I)** la API de Storage devuelve la clave **con el bucket delante** (`entregables/{tenant}/...`) y se persistía tal cual -> la descarga pedía `entregables/entregables/...` y respondía **500 `Object not found`**; normalización en el límite con `QuitarPrefijoBucket` en `StorageHelper` (hace explícito el contrato de rutas **relativas** de ADR-013); la compensación de RNF-014 tampoco encontraba el objeto y lo dejaba huérfano · **(J)** el botón «Volver» de `Entregables.cshtml` usaba `asp-action="Index"` **sin** `asp-controller`, que el tag helper resuelve contra el controlador en curso -> `/AccionPlan/Index` (`[Authorize(Roles="JefeArea")]`): el Gerente caía en `AccessDenied?ReturnUrl=%2FAccionPlan%2FIndex`; ahora el enlace es **explícito por rol** (Gerente -> `/Plan/Consolidado`, JefeArea -> `/AccionPlan/Index`) con breadcrumb coherente · **(K/L)** la detección por magic number leía solo los **512 primeros bytes**, así que `.docx`/`.xlsx`/PDF legítimos de más de 512 KB se rechazaban como «tipo no permitido»; ahora se detecta sobre el archivo completo (ya estaba en memoria) · **(M)** la re-lectura de verificación se hacía **después** del `Commit`, pudiendo dejar objeto persistido sin fila de BD; ahora se verifica **antes** de confirmar · **(N)** los fallos del pool/Storage llegaban al toast como «error de servidor» sin acción; ahora hay mensajes user-facing con el límite concreto. Alcance **ampliado** a `PE-GOL.BLL` y `PE-GOL.Utility` (el v1 declaraba 0 cambios ahí: la validación real demostró que esa frontera era falsa). Tests: **11 nuevos** verificados **rojo antes de código** (TDD real), blindados además con `NavegacionPorRolRegressionTests` (arquitectura de plantillas, mismo criterio que `JsApiBaseUrlRegressionTests`: los enlaces viven en Razor y ningún test C# renderiza vistas) - **841/841** en verde, 0 omitidos. Cobertura BLL **90,00%** (sube desde 89,96%, sin regresión - TEST-02). Build `dotnet clean` + `dotnet build` â **0 errores / 0 advertencias**. Sin migración (el esquema no cambia, solo el valor de datos) · **sin ADR nuevo** (aplican **ADR-013** rutas relativas al bucket y **ADR-016** navegador sin API interna) · Swagger **54 paths intactos**. **Reparación de datos en caliente**: 5 filas de `entregable_adjunto` normalizadas (`file_path` sin prefijo) y bucket reconciliado de 22 a **5 objetos, los 5 referenciados** (17 huérfanos del bug I eliminados); las 5 URLs firmadas verificadas **HTTP 200** con el tamaño de bytes correcto. **Lección operativa registrada**: tras editar un `.cshtml` hay que **recompilar y reiniciar** - `Copy-Item` preserva el `LastWriteTime`, el build incremental puede **no** recompilar la vista y el servidor sigue sirviendo el markup viejo (por eso el fix de J parecía no funcionar); el test de arquitectura escanea el **fuente**, la página servida viene del **DLL compilado**, y ambos pueden divergir. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.29 (2026-10-01): **hotfix de HU-022 (UI de Entregables) aplicado y cerrado** ÔÇö spec `specs/sprint-04/HU-022-hotfix.spec.md` ÔåÆ `Implementado` (6 defectos A-F corregidos; alcance 100% `PE-GOL.Aplicacion` + `PE-GOL.Tests`, 0 cambios en API/BLL/DAL/Entity/DTO, sin migraci├│n, **sin ADR nuevo** ÔÇö aplica ADR-016): **(A)** vista inalcanzable ÔåÆ navegaci├│n por rol: bot├│n `bi-paperclip` por fila en `AccionPlan/Index` (JEF) + columna ┬½Acciones┬╗ con enlace en `Plan/Consolidado` Razor **y** render JS de `plan-consolidado.js` (GER) ÔÇö Gantt y Sidebar expl├¡citamente NO (D-8) ┬À **(B)** 3 `fetch` en ruta relativa ÔåÆ 404 ÔåÆ 4 proxies MVC (`EntregablesDatos`/`EntregablesSubir`/`EntregablesDescarga`/`EntregablesEliminar`) con `IApiClient` (ADR-016) + overload multi-archivo de `PostMultipartAsync` (D-6) ┬À **(C)** dropzone visible al Gerente ÔåÆ `PuedeSubir` calculado server-side (`User.IsInRole`) ┬À **(D)** JWT en el DOM eliminado (`<meta access-token>`, `EntregablesViewModel.AccessToken`, `obtenerToken()`, headers `Authorization`) ÔÇö SEC-01/SEC-05 ┬À **(E)** typo `adjjunto` corregido ┬À **(F)** tabla + empty state siempre en el DOM (la primera subida sobre una acci├│n vac├¡a ya pinta la tabla). Gates: build **0 errores / 0 advertencias** ┬À tests **830/830** en verde, 0 omitidos (813 previos + 17 nuevos: 12 proxies + 2 autorizaci├│n por reflexi├│n + 3 action/ViewModel) ┬À `JsApiBaseUrlRegressionTests` con `ExcepcionesConocidas` **vac├¡a** (scan de `wwwroot/js/*.js` limpio ÔÇö gate del Defecto B) ┬À cobertura BLL **89,96%** sin drift (0 cambios BLL) ┬À Swagger 54 paths intactos (los proxies son acciones MVC) ┬À **validaci├│n visual en navegador por Jorge confirmada**. HANDOFF: ├¡tems 3-4 de ┬½Deuda t├®cnica pendiente┬╗ retirados (deuda pagada). Sprint 4 sigue en curso (4/5 HU, 23 de 28 pts); pendiente **HU-025 (Gesti├│n de KRs)**. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.28 (2026-09-30): **hotfix de HU-023 (Vista Consolidada del Plan) aplicado y validado** — la validación visual en navegador (Jorge, login real `Gerente`, `/Plan/Consolidado`) destapó **4 defectos de runtime** que los 797 tests no podían detectar: **(A)** el MVC llamaba `GET /api/v1/areas` (ruta inexistente) y el `catch` de cualquier 404 lo etiquetaba falsamente como «sin ciclo activo» — ahora las 4 llamadas reales (`GET /api/v1/ciclos` → `FirstOrDefault(Estado=="Activo")` es el único determinante; luego `/ciclos/{id}/areas`, `/objetivos-cg/consolidado`, `/planes/consolidado`) y el `when (ex.StatusCode == 404)` desapareció · **(B)** los CGs daban 403 al Gerente (llamaba al endpoint JEF `/api/v1/objetivos-cg`) — ahora `GET /api/v1/objetivos-cg/consolidado` y `ObjetivosCg` es `List<ObjetivoCgConsolidadoResponse>` · **(C)** HTTP 500 `PostgresException 42P08` en `/api/v1/planes/consolidado` (`DateTime?` en null sin tipo declarado contra columna `DATE`) — `DynamicParameters` con `DbType.Date` explícito (**ADR-015**) · **(E)** `plan-consolidado.js` hacía `fetch('/api/v1/...')` relativo al MVC (`:7200`) → 404 — proxy MVC `/Plan/ConsolidadoDatos` + `/Plan/ConsolidadoExportar` con `IApiClient` (**ADR-016**: el navegador nunca consume la API interna). **Seguridad reforzada (SEC-01/SEC-05):** eliminados el `<input type="hidden" id="accessToken">` del DOM y `PlanConsolidadoViewModel.AccessToken` — el JWT ya no viaja al navegador; el proxy lo adjunta server-side desde la sesión. Gates: build **0 errores / 0 advertencias** · tests **813/813** en verde, 0 omitidos (797 previos + 16 nuevos: 8 del hotfix v2 + 7 del v3 + 1 de regresión arquitectónica `JsApiBaseUrlRegressionTests`, que blinda ADR-016 escaneando `wwwroot/js/*.js`) · cobertura BLL **89,96%** (línea) ≥ 70% · validación contra la API real (4 endpoints → 200; 5 escenarios de filtros de fecha → 200) · **validación visual en navegador por Jorge confirmada** («Revisado desde UI todo funcional hasta ahora»). **Deuda técnica explícita — hotfix pendiente de HU-022 (NO mezclado con este, siguiente paso acordado con Jorge):** vista `Entregables` inalcanzable (nada la enlaza) + mismo Defecto E en `entregables.js` (L225/L372/L401 — HANDOFF «Deuda técnica pendiente» ítem 3) + dropzone de subida visible para roles sin permiso (la API es JEF-only, `AccionPlanController.cs:174`). Sprint 4 sigue en curso (4/5 HU, 23 de 28 pts); pendientes el hotfix de HU-022 y HU-025 (Gestión de KRs). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.27 (2026-09-30): HU-024 (CRUD de OKRs) Implementada — Sprint 4 en curso (4/5 HU, 23 de 28 pts). Tests: **797/797** en verde (735 previos + 62 nuevos: 39 casos BLL + 18 DAL + 5 MVC) · build 0 errores / 0 advertencias · Cobertura BLL ≥ 70% · **sin ADR nuevo** (tabla `okr`, su `UNIQUE (area_id, codigo)`, `idx_okr_area` y RLS `okr_policy` ya existían en el DDL base; auditoría según ADR-003; repositorio dedicado según precedente HU-017) · `OkrController` nuevo (API, 5 endpoints JEF-only) + `OkrService`/`OkrRepository` + vistas Razor `Okr/*` + ítem de menú «OKRs» · validación visual en navegador por Jorge confirmada (login real `JefeArea`, `/Okr`). Pendiente HU-025 (Gestión de KRs). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.26 (2026-09-29): HU-023 (Vista Consolidada del Plan) Implementada — Sprint 4 en curso (3/5 HU). Tests: **735/735** en verde · Cobertura BLL ≥ 70% · ADR-014 (ClosedXML en BLL) · `PlanController` nuevo · 9 commits atómicos. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.25 (2026-09-27): la **validación visual en navegador de HU-021** (realizada por **Jorge** con login real `JefeArea`/`Gerente` el 2026-09-27, en `/AccionPlan/Gantt`) destapó y corrigió **4 defectos de runtime** que ningún test C# podía detectar — (1) `ReferenceError` en `tokenPx()` por un identificador mal escrito, que abortaba `iniciar()` antes de `gantt.init()`; (2) `Invalid start_date argument for calculateEndDate` por filas `type:"project"` sin `start_date` (ahora unión min/max de las hijas, con fallback al ciclo); (3) la escala anclada con `gantt.config.min_date`/`max_date`, **inexistentes como claves de config** en DHTMLX 10 (ahora `start_date`/`end_date`), y eliminación de `read_date_format`, inerte desde la v8; (4) el contenedor `.gantt-pe` sin altura, que provocaba un **loop de retroalimentación de altura** (scroll infinito) — más **2 problemas de UX** de la misma sesión: el filtro no eliminaba las tareas filtradas (`gantt.parse()` fusiona por `id`; ahora `clearAll()` + `parse()` dentro de `gantt.silent()`) y el tooltip heredaba `white-space: nowrap`, con lo que su `max-width` no limitaba nada. Gates finales: **0 advertencias / 0 errores**, **584/584**, cobertura BLL **89,02%** (sin drift, no hubo cambios en C#). `docs/07_DESIGN_SYSTEM.md` § 12 **sincronizado con `gantt.css`**: tokens `--gantt-altura` y `--gantt-tooltip-ancho` añadidos, más las **2 restricciones** que los dividen de los que lee `tokenPx()`. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.24 (2026-09-27): cierre documental previo a los commits de HU-021 — **`[STACK-07]` precisada** (dhtmlx-gantt 10.0.3, variante Community MIT, vendorizada con `npm pack` en `wwwroot/lib/dhtmlx-gantt/` y anclada por su sha1) y **HU-021 cerrada** (spec `Implementado`, **22/22 DoD**, 5 commits atómicos). Sprint 4 en curso (1/5 HU). **No cambia ninguna otra regla**: STACK-01..06 y STACK-08..11, ARCH, SEC, TEST, DB, UX y LOOP intactos.
> v1.23 (2026-09-27): HU-021 (Vista Gantt del Plan de Acción) implementada — **Sprint 4 en curso (1/5 HU)**. Tests: **584/584** en verde (baseline real previo a HU-021: **556**, no 554 — ver nota de trazabilidad en el pie). Cobertura BLL **89.02%** (3139/3526) ≥ 70%. ADR-011 (DHTMLX Gantt Community MIT v10.0.3, vendorizado desde el registro oficial de npm) y ADR-012 + `V005` aplicados y verificados en Supabase.
> v1.22 (2026-09-21): auditoría post-Sprint 3 resuelta 100% (3 críticos, 6 medios, 8 mejoras — ver HANDOFF «Cierre de auditoría post-Sprint 3» y ADR-010). Tests: 554/554 en verde *(conteo drift: el real verificado era 556/556 — corregido en v1.23)*. Pre-Sprint 4, sin HU abiertas.
> v1.21 (2026-09-20): tabla de estado actualizada tras implementar HU-045 (UI de Gestión de Tenants — alcance ampliado) — Sprint 3 completado (6/6 HU).
> v1.20 (2026-09-20): tabla de estado actualizada tras implementar HU-020 (Actualización de Progreso de Acciones) — Sprint 3 en curso.
> v1.19 (2026-09-20): tabla de estado actualizada tras implementar HU-019 (CRUD Acciones del Plan) — Sprint 3 en curso.
> v1.18 (2026-09-20): tabla de estado actualizada tras implementar HU-018 (Vista Consolidada CGs) — Sprint 3 en curso.
> v1.17 (2026-09-20): tabla de estado actualizada tras implementar HU-016 (Tablero Inicio Gerente) y HU-017 (CRUD Objetivos CG) — Sprint 3 en curso.
> v1.16 (2026-09-20): tabla de estado actualizada tras implementar HU-015 (Tablero de Inicio Jefe de Área) — Sprint 2 completado (8/8 HU).
> v1.15 (2026-09-20): tabla de estado actualizada tras implementar HU-014 (Objetivos de Área por Trimestre en Pilares) — Sprint 2 en curso (7/8 HU).
> v1.14 (2026-09-20): tabla de estado actualizada tras implementar HU-013 (CRUD de Pilares Estratégicos) — Sprint 2 en curso (6/8 HU).
> v1.13 (2026-09-20): tabla de estado actualizada tras implementar HU-012 (Gestión de Valores Corporativos) — Sprint 2 en curso (5/8 HU).
> v1.12 (2026-09-20): tabla de estado actualizada tras implementar HU-011 (Registro de Visión y Misión) — Sprint 2 en curso (4/8 HU).
> v1.11 (2026-09-18): tabla de estado actualizada tras implementar HU-010 (Gestión de Responsables) — Sprint 2 en curso (3/8 HU).
> v1.10 (2026-09-17): tabla de estado actualizada tras implementar HU-009 (Gestión de Áreas Estratégicas) — Sprint 2 en curso (2/8 HU).
> v1.9 (2026-09-17): tabla de estado actualizada tras implementar HU-005 (Log de Auditoría) — Sprint 2 en curso (1/8 HU).
> v1.8 (2026-09-17): tabla de estado actualizada tras implementar HU-008 (Configuración de Umbrales de Semáforo).
> v1.6 (2026-09-14): tabla de estado actualizada tras implementar HU-006 (Configuración de la Empresa/Tenant).
> v1.7 (2026-09-14): tabla de estado actualizada tras implementar HU-007 (Gestión de Ciclos Anuales).
> v1.5 (2026-09-13): tabla de estado actualizada tras implementar HU-004 (Autenticación y Seguridad).
> v1.4 (2026-09-13): tabla de estado actualizada tras implementar HU-003 (Gestión de Usuarios del Tenant).
> v1.3 (2026-09-13): tabla de estado y backlog actualizados — 45 HU · 243 pts · 9 Sprints · HU-003 spec Aprobada · HU-045 movida al Sprint 3.
> v1.2 (2026-09-13): tabla de estado actualizada tras implementar HU-002 (Gestión de Planes de Suscripción).
> v1.1 (2026-09-13): tabla de estado actualizada tras implementar HU-001.

---

## 📁 Mapa de Documentos del Proyecto

Antes de actuar sobre cualquier módulo, el agente debe leer los documentos relevantes en este orden:

```
dicegsa-pe/
├── AGENTS.md                          ← SIEMPRE leer primero (este archivo)
│
├── docs/
│   ├── 01_AS-IS_PE_GOL.md            ← Situación actual (Excel → SaaS)
│   ├── 02_REQUERIMIENTOS.md          ← RF (66) · RNF (26) · RN (42)
│   ├── 03_BACKLOG.md                 ← 45 HU · 243 pts · 9 Sprints
│   ├── 04_ARQUITECTURA.md            ← N-Tier 8 proyectos · Capas · Batch Jobs
│   ├── 05_DOMINIO.md                 ← 5 dominios · 22 entidades · 12 reglas RC
│   ├── 06_MODELO_DATOS.md            ← 25 tablas DDL · RLS · Vistas · Seed
│   └── 07_DESIGN_SYSTEM.md          ← Tokens · Componentes · Layout · Chart.js
│
├── specs/
│   └── sprint-XX/
│       └── HU-XXX.spec.md            ← Spec técnica por Historia de Usuario
│
├── adrs/
│   └── ADR-XXX.md                    ← Architecture Decision Records
│
└── agents/
    ├── @Orquestador.md
    ├── @Arquitecto.md
    ├── @BackendDev.md
    ├── @FrontendDev.md
    ├── @QA.md
    └── @Documenter.md
```

---

## ⚙️ Sección 1 — Stack Tecnológico (Inmutable)

> Declaraciones EARS: "El sistema **deberá** usar..."

- **[STACK-01]** El sistema **deberá** implementarse en **ASP.NET Core MVC + Web API (.NET 8)** para la capa de presentación y API REST.
- **[STACK-02]** El sistema **deberá** usar **Supabase (PostgreSQL 15)** como base de datos principal, con Row-Level Security habilitado en todas las tablas de dominio.
- **[STACK-03]** El acceso a datos **deberá** realizarse exclusivamente con **Dapper** + queries SQL parametrizadas. Queda prohibido el uso de Entity Framework Core o cualquier ORM de mapeo completo.
- **[STACK-04]** La validación de DTOs en la API **deberá** implementarse con **FluentValidation.AspNetCore**.
- **[STACK-05]** El frontend **deberá** usar **Bootstrap 5.3** + **jQuery** + **Bootstrap Icons 1.11** tal como define el Design System (`07_DESIGN_SYSTEM.md`).
- **[STACK-06]** Las gráficas **deberán** implementarse con **Chart.js**, siguiendo la configuración de paleta y estilos definida en `07_DESIGN_SYSTEM.md § 9`.
- **[STACK-07]** dhtmlx-gantt 10.0.3 (MIT, npm pack vendorizado en wwwroot/lib/dhtmlx-gantt/, sha1: 4c1c896b9d465892e72647cff335bf755d0ca910)
- **[STACK-08]** La generación de archivos Excel **deberá** realizarse con **ClosedXML**.
- **[STACK-09]** La generación de PDFs **deberá** realizarse con **DinkToPdf**.
- **[STACK-10]** El envío de correos **deberá** realizarse con **MailKit** (SMTP / SendGrid).
- **[STACK-11]** El logging **deberá** ser estructurado en JSON usando **Serilog.AspNetCore** con niveles: Debug, Info, Warning, Error, filtrable por tenant/usuario/módulo.

---

## 🏗️ Sección 2 — Arquitectura (Inmutable)

- **[ARCH-01]** La solución **deberá** mantener exactamente **8 proyectos** N-Tier: `PE-GOL.Aplicacion` · `PE-GOL.API` · `PE-GOL.BLL` · `PE-GOL.DAL` · `PE-GOL.Entity` · `PE-GOL.DTO` · `PE-GOL.IOC` · `PE-GOL.Utility`. No se crearán proyectos adicionales sin un ADR aprobado.
- **[ARCH-02]** El flujo de dependencias **deberá** seguir estrictamente: `Aplicacion → API → BLL → DAL → Entity`. Ninguna capa inferior puede referenciar una superior.
- **[ARCH-03]** Todo endpoint de la API **deberá** estar documentado automáticamente con **Swagger/OpenAPI 3.0** (Swashbuckle).
- **[ARCH-04]** El aislamiento multi-tenant **deberá** implementarse en dos capas simultáneas: (1) `TenantMiddleware` en la API inyecta el `TenantContext` desde los claims del JWT, y (2) RLS en PostgreSQL valida `tenant_id` desde el claim del JWT. Ambas capas son obligatorias y complementarias.
- **[ARCH-05]** Los procesos programados (batch jobs) **deberán** implementarse como **Hosted Services** (`IHostedService`) dentro del proyecto `PE-GOL.API`. Se prohíbe el uso de Hangfire u otras dependencias de scheduling externas en v1.0.
- **[ARCH-06]** Los archivos adjuntos (entregables) **deberán** almacenarse en **Supabase Storage**, en el bucket `entregables`, bajo la ruta `/{tenant_id}/{ciclo_id}/{accion_id}/{uuid}.{ext}`. El acceso se realizará siempre mediante URLs firmadas con expiración de 24 horas.
- **[ARCH-07]** La API **deberá** responder siempre con el wrapper estándar `ApiResponse<T>` definido en `PE-GOL.DTO`. Los códigos HTTP usados son: 200, 201, 400, 401, 403, 404, 422, 500.

---

## 🔐 Sección 3 — Seguridad (Inmutable)

- **[SEC-01]** La autenticación **deberá** implementarse con **JWT** (access token 60 min, refresh token 7 días). No se implementará OAuth2 externo en v1.0.
- **[SEC-02]** Las contraseñas **deberán** almacenarse con **BCrypt** (factor de coste ≥ 12). Queda prohibido MD5, SHA-1 o cualquier hash sin salt.
- **[SEC-03]** El sistema **deberá** bloquear una cuenta de usuario tras **5 intentos fallidos** consecutivos durante **15 minutos**.
- **[SEC-04]** Toda comunicación **deberá** realizarse sobre **HTTPS/TLS 1.2+**. El servidor no responderá peticiones HTTP en producción.
- **[SEC-05]** Los endpoints de la API **deberán** protegerse contra **CSRF, XSS e inyección SQL**. Todas las queries de Dapper usarán parámetros nombrados; se prohíbe la concatenación de strings SQL.
- **[SEC-06]** El `TenantContext` (tenant_id, user_id, rol, area_id) **deberá** propagarse desde el JWT hacia la BLL y DAL. Ningún endpoint puede aceptar `tenant_id` como parámetro del request body o query string.
- **[SEC-07]** Un usuario con rol **JefeArea** solo podrá acceder a datos de su `area_id`. La DAL **deberá** incluir `AND area_id = @AreaId` en todas las queries de entidades de área cuando el rol sea `JefeArea`.

---

## 🧪 Sección 4 — Testing (Inmutable)

- **[TEST-01]** El agente `@QA` **deberá** escribir los tests unitarios **antes** de que `@BackendDev` implemente el servicio (TDD). El test que falla es la especificación.
- **[TEST-02]** Los tests unitarios **deberán** cubrir mínimo el **70% de la capa BLL**. No se aceptan commits que bajen la cobertura de este umbral.
- **[TEST-03]** El framework de testing **deberá** ser **xUnit** + **Moq** para mocks de repositorios.
- **[TEST-04]** Cada servicio de la BLL **deberá** tener su clase de test correspondiente en el proyecto `PE-GOL.Tests` con el patrón de nombre: `[NombreServicio]Tests.cs`.
- **[TEST-05]** Los tests **deberán** seguir el patrón **Arrange / Act / Assert** y el nombre del método el patrón: `[Metodo]_[Escenario]_[ResultadoEsperado]`.
- **[TEST-06]** Ningún agente puede marcar una HU como **Done** si hay tests fallando o si la cobertura BLL cae por debajo del 70%.

---

## 🗃️ Sección 5 — Base de Datos (Inmutable)

- **[DB-01]** El schema de base de datos es el definido en `06_MODELO_DATOS.md`. Toda nueva tabla o modificación requiere un **ADR aprobado** antes de ejecutar la migración.
- **[DB-02]** Las migraciones de base de datos **deberán** ejecutarse con **scripts SQL versionados** en la carpeta `db/migrations/`, nombrados como `V{NNN}__{descripcion}.sql`. No se usa EF Core Migrations.
- **[DB-03]** Toda tabla de dominio **deberá** tener la columna `tenant_id UUID NOT NULL REFERENCES tenant(id)` y su política RLS correspondiente.
- **[DB-04]** Los campos calculados (progreso, puntuacion_final, semaforo, status, totales) **deberán** calcularse en la **BLL** antes de persistirse. No se usarán triggers PostgreSQL para lógica de negocio.
- **[DB-05]** Los valores de `UNIQUE` en entidades con `tenant_id` **deberán** incluir `tenant_id` en el índice único compuesto cuando corresponda (ej: `UNIQUE (ciclo_id, codigo)` ya incluye tenant implícitamente vía FK).
- **[DB-06]** Los UPSERTs de `valor_mensual_kr`, `desembolso_capex` y `presupuesto_opex` **deberán** usar `INSERT ... ON CONFLICT (columnas_unique) DO UPDATE SET` en lugar de SELECT + UPDATE separados.

---

## 🎨 Sección 6 — Frontend y UX (Inmutable)

- **[UX-01]** Todos los estilos visuales **deberán** seguir las variables CSS y componentes definidos en `07_DESIGN_SYSTEM.md`. No se crearán estilos inline ni clases custom fuera del design system sin un ADR.
- **[UX-02]** Los semáforos (Verde/Amarillo/Rojo) **deberán** renderizarse siempre con los colores y clases CSS definidos: `.semaforo--verde`, `.semaforo--amarillo`, `.semaforo--rojo`.
- **[UX-03]** Todas las tablas de datos **deberán** usar la clase `.tabla-pe` y sus variantes definidas en el Design System.
- **[UX-04]** Los formularios **deberán** validarse en el cliente con **jQuery Validate** y en el servidor con **FluentValidation**. La validación del servidor es siempre la fuente de verdad.
- **[UX-05]** Los estados vacíos (sin datos) **deberán** renderizarse con el componente `.empty-state` definido en el Design System. Nunca se mostrará una tabla vacía sin estado vacío.
- **[UX-06]** La interfaz **deberá** ser funcional en resoluciones ≥ 768px. El design system define el comportamiento responsive en § 11.

---

## 📋 Sección 7 — Reglas del Loop de Trabajo

- **[LOOP-01]** El flujo de trabajo por cada HU es **siempre**: `Spec → Plan → Tests → Implement → Review`. Ningún agente puede saltar de Spec directamente a código.
- **[LOOP-02]** Antes de iniciar cualquier HU, el agente `@Orquestador` **deberá** verificar que el spec esté aprobado (`specs/sprint-XX/HU-XXX.spec.md` existe y tiene estado `Aprobado`).
- **[LOOP-03]** El agente `@BackendDev` **deberá** correr `dotnet build` y `dotnet test` antes de marcar cualquier tarea como completa. Si hay errores, **deberá** corregirlos antes de continuar.
- **[LOOP-04]** Cuando el agente encuentre un error de compilación o test fallido, **deberá** leer el mensaje completo, identificar la causa raíz y corregirla. No se permite suprimir warnings con `#pragma` sin un ADR.
- **[LOOP-05]** Al final de cada HU implementada, el agente `@Documenter` **deberá** actualizar el spec con el estado `Implementado` y registrar cualquier decisión tomada durante la implementación como entrada en el ADR correspondiente.
- **[LOOP-06]** El agente **deberá** tratar cada commit como si fuera revisado por un humano. Mensajes de commit en formato: `[HU-XXX] tipo: descripción breve` (ej: `[HU-004] feat: implementar autenticación JWT`).

---

## 📝 Sección 8 — Formato de Specs y ADRs

### Spec (por HU)
```markdown
# Spec HU-XXX — [Nombre]
**Sprint:** X · **Épica:** EP-XX · **Pts:** N · **Estado:** Borrador | Aprobado | Implementado

## Contexto
[Referencia a la HU del backlog]

## Endpoints (si aplica)
| Método | Ruta | Auth | Descripción |
[tabla]

## DTOs
[Request y Response con tipos]

## Lógica BLL (paso a paso)
[lista numerada de pasos que el servicio debe ejecutar]

## Queries DAL
[SQL parametrizado]

## Validaciones FluentValidation
[lista de reglas]

## Tests requeridos (escritos por @QA antes de la impl.)
[lista de casos de prueba Arrange/Act/Assert]

## Criterios de Done
- [ ] Tests pasan (dotnet test ✅)
- [ ] Cobertura BLL ≥ 70%
- [ ] Swagger documentado
- [ ] Design System aplicado (si tiene UI)
- [ ] ADR creado (si hay decisión arquitectónica)
```

### ADR
```markdown
# ADR-XXX — [Título de la decisión]
**Fecha:** YYYY-MM-DD · **Estado:** Propuesto | Aceptado | Obsoleto
**Autor:** [Agente o Jorge]

## Contexto
[Por qué se necesita esta decisión]

## Opciones consideradas
1. [Opción A]
2. [Opción B]

## Decisión
[Opción elegida y razón]

## Consecuencias
[Impacto positivo y trade-offs aceptados]
```

---

## 🚦 Sección 9 — Estado del Proyecto

| Fase | Entregable | Estado |
|------|-----------|--------|
| AS-IS | `01_AS-IS_PE_GOL.md` | ✅ Completo |
| Requerimientos | `02_REQUERIMIENTOS.md` | ✅ Completo |
| Backlog | `03_BACKLOG.md` | ✅ Completo |
| Arquitectura | `04_ARQUITECTURA.md` | ✅ Completo |
| Dominio | `05_DOMINIO.md` | ✅ Completo |
| Modelo de Datos | `06_MODELO_DATOS.md` | ✅ Completo |
| Design System | `07_DESIGN_SYSTEM.md` | ✅ Completo |
| **AGENTS.md** | Este archivo | ✅ Completo |
| Agentes | `agents/*.md` | ✅ Completo |
| Specs Sprint 1 | `specs/sprint-01/*.spec.md` | ✅ Completo — HU-001, HU-002, HU-003, HU-004, HU-006, HU-007 y HU-008 Implementadas (Sprint 1 sin pendientes) |
| Specs Sprint 2 | `specs/sprint-02/*.spec.md` | ✅ Completo — HU-005, HU-009..HU-015 Implementadas (Sprint 2: 8/8 HU — Sprint 2 sin pendientes) |
| Specs Sprint 3 | `specs/sprint-03/*.spec.md` | ✅ Completo — HU-016..HU-020 y HU-045 Implementadas (Sprint 3: 6/6 HU — Sprint 3 sin pendientes) |
| Specs Sprint 4 | `specs/sprint-04/*.spec.md` | ✅ Completo — HU-021, HU-022 (hotfix v1+v2), HU-023 (hotfix), HU-024 y HU-025 Implementadas (Sprint 4: 5/5 HU — 28 de 28 pts) |
| Implementación | Código fuente | ⏳ Pendiente |

---

## 🔑 Sección 10 — Roles y Capacidades del Equipo de Agentes

| Agente | Archivo | Responsabilidad principal |
|--------|---------|--------------------------|
| `@Orquestador` | `agents/@Orquestador.md` | Coordina el Graph · asigna HUs · verifica estado del loop |
| `@Arquitecto` | `agents/@Arquitecto.md` | Diseña specs · escribe ADRs · valida decisiones técnicas |
| `@BackendDev` | `agents/@BackendDev.md` | Implementa API · BLL · DAL · Entity · DTO · IOC |
| `@FrontendDev` | `agents/@FrontendDev.md` | Implementa vistas Razor · JS · CSS · integración API |
| `@QA` | `agents/@QA.md` | Escribe tests antes de la implementación (TDD) · valida cobertura |
| `@Documenter` | `agents/@Documenter.md` | Actualiza specs · ADRs · AGENTS.md al cierre de cada HU |

---

*AGENTS.md — Constitución PE-GOL SaaS · Versión 1.30 · 2026-10-01*
*v1.30 (2026-10-01): **hotfix v2 de HU-022 (UI de Entregables) aplicado y cerrado** — la validación real en navegador del flujo del **JefeArea** (subida múltiple de Office + descarga) destapó **8 defectos de runtime** que los 830 tests no podían detectar, todos en la interacción navegador/proxy/`ApiClient`/pool Npgsql/SDK de Storage: **(G)** `PostMultipartAsync` multi-archivo cerraba cada `MemoryStream` dentro del `foreach` → el 2º archivo en adelante llegaba vacío/disposado al `SendAsync`; ahora los streams viven en lista y el reintento 401 los re-abre con `Position=0` · **(H)** `EntregableAdjuntoRepository` cachea una única `NpgsqlConnection` en campo y la reutiliza entre peticiones concurrentes (no es thread-safe) → la BLL procesa los archivos **secuencialmente** en vez de `Task.WhenAll` · **(I)** la API de Storage devuelve la clave **con el bucket delante** (`entregables/{tenant}/…`) y se persistía tal cual → la descarga pedía `entregables/entregables/…` y respondía **500 `Object not found`**; normalización en el límite con `QuitarPrefijoBucket` en `StorageHelper` (hace explícito el contrato de rutas **relativas** de ADR-013); la compensación de RNF-014 tampoco encontraba el objeto y lo dejaba huérfano · **(J)** el botón «Volver» de `Entregables.cshtml` usaba `asp-action="Index"` **sin** `asp-controller`, que el tag helper resuelve contra el controlador en curso → `/AccionPlan/Index` (`[Authorize(Roles="JefeArea")]`): el Gerente caía en `AccessDenied?ReturnUrl=%2FAccionPlan%2FIndex`; ahora el enlace es **explícito por rol** (Gerente → `/Plan/Consolidado`, JefeArea → `/AccionPlan/Index`) · **(K/L)** la detección por magic number leía solo los **512 primeros bytes**, así que `.docx`/`.xlsx`/PDF legítimos de más de 512 KB se rechazaban como «tipo no permitido»; ahora se detecta sobre el archivo completo (ya estaba en memoria) · **(M)** la re-lectura de verificación se hacía **después** del `Commit`, pudiendo dejar objeto persistido sin fila de BD; ahora se verifica **antes** de confirmar · **(N)** los fallos del pool/Storage llegaban al toast como «error de servidor» sin acción; ahora hay mensajes user-facing con el límite concreto. Alcance **ampliado** a `PE-GOL.BLL` y `PE-GOL.Utility`. Tests: **11 nuevos** verificados **rojo antes de código** (TDD real) y blindados con `NavegacionPorRolRegressionTests` (arquitectura de plantillas, mismo criterio que `JsApiBaseUrlRegressionTests`: los enlaces viven en Razor y ningún test C# renderiza vistas) — **841/841** en verde, 0 omitidos · cobertura BLL **90,00%** (sube desde 89,96%, sin regresión — TEST-02) · build `dotnet clean` + `dotnet build` **0 errores / 0 advertencias** · sin migración · **sin ADR nuevo** (aplican **ADR-013** y **ADR-016**) · Swagger **54 paths intactos**. **Reparación de datos en caliente**: 5 filas de `entregable_adjunto` normalizadas y bucket reconciliado de 22 a **5 objetos, los 5 referenciados** (17 huérfanos eliminados); las 5 URLs firmadas verificadas **HTTP 200** con el tamaño de bytes correcto. **Lección operativa registrada**: tras editar un `.cshtml` hay que **recompilar y reiniciar** — `Copy-Item` preserva el `LastWriteTime`, el build incremental puede no recompilar la vista y el servidor sigue sirviendo el markup viejo (por eso el fix de J pareció no funcionar); el test de arquitectura escanea el **fuente** y la página servida viene del **DLL compilado**, y ambos pueden divergir. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.28 (2026-09-30): **hotfix de HU-023 (Vista Consolidada del Plan) aplicado y validado** — la validación visual en navegador (Jorge, login real `Gerente`, `/Plan/Consolidado`) destapó **4 defectos de runtime** que los 797 tests no podían detectar: **(A)** el MVC llamaba `GET /api/v1/areas` (ruta inexistente) y el `catch` de cualquier 404 lo etiquetaba falsamente como «sin ciclo activo» — ahora las 4 llamadas reales (`GET /api/v1/ciclos` → `FirstOrDefault(Estado=="Activo")` es el único determinante; luego `/ciclos/{id}/areas`, `/objetivos-cg/consolidado`, `/planes/consolidado`) y el `when (ex.StatusCode == 404)` desapareció · **(B)** los CGs daban 403 al Gerente (llamaba al endpoint JEF `/api/v1/objetivos-cg`) — ahora `GET /api/v1/objetivos-cg/consolidado` y `ObjetivosCg` es `List<ObjetivoCgConsolidadoResponse>` · **(C)** HTTP 500 `PostgresException 42P08` en `/api/v1/planes/consolidado` (`DateTime?` en null sin tipo declarado contra columna `DATE`) — `DynamicParameters` con `DbType.Date` explícito (**ADR-015**) · **(E)** `plan-consolidado.js` hacía `fetch('/api/v1/...')` relativo al MVC (`:7200`) → 404 — proxy MVC `/Plan/ConsolidadoDatos` + `/Plan/ConsolidadoExportar` con `IApiClient` (**ADR-016**: el navegador nunca consume la API interna). **Seguridad reforzada (SEC-01/SEC-05):** eliminados el `<input type="hidden" id="accessToken">` del DOM y `PlanConsolidadoViewModel.AccessToken` — el JWT ya no viaja al navegador; el proxy lo adjunta server-side desde la sesión. Gates: build **0 errores / 0 advertencias** · tests **813/813** en verde, 0 omitidos (797 previos + 16 nuevos: 8 del hotfix v2 + 7 del v3 + 1 de regresión arquitectónica `JsApiBaseUrlRegressionTests`, que blinda ADR-016 escaneando `wwwroot/js/*.js`) · cobertura BLL **89,96%** (línea) ≥ 70% · validación contra la API real (4 endpoints → 200; 5 escenarios de filtros de fecha → 200) · **validación visual en navegador por Jorge confirmada** («Revisado desde UI todo funcional hasta ahora»). **Deuda técnica explícita — hotfix pendiente de HU-022 (NO mezclado con este, siguiente paso acordado con Jorge):** vista `Entregables` inalcanzable (nada la enlaza) + mismo Defecto E en `entregables.js` (L225/L372/L401 — HANDOFF «Deuda técnica pendiente» ítem 3) + dropzone de subida visible para roles sin permiso (la API es JEF-only, `AccionPlanController.cs:174`). Sprint 4 sigue en curso (4/5 HU, 23 de 28 pts); pendientes el hotfix de HU-022 y HU-025 (Gestión de KRs). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.29 (2026-10-01): **hotfix de HU-022 (UI de Entregables) aplicado y cerrado** — spec `specs/sprint-04/HU-022-hotfix.spec.md` → `Implementado` (6 defectos A-F: **A** vista inalcanzable → navegación por rol [botón `bi-paperclip` en `AccionPlan/Index` JEF + columna «Acciones» en `Plan/Consolidado` GER, Razor **y** render JS — Gantt y Sidebar NO] · **B** 3 `fetch` relativos → 404 → 4 proxies MVC `EntregablesDatos|Subir|Descarga|Eliminar` con `IApiClient` [ADR-016] + overload multi-archivo de `PostMultipartAsync` · **C** dropzone visible al Gerente → `PuedeSubir` server-side [`User.IsInRole`] · **D** JWT en el DOM eliminado [SEC-01/SEC-05] · **E** typo `adjjunto` · **F** tabla + empty state siempre en el DOM). Alcance 100% `PE-GOL.Aplicacion` + `PE-GOL.Tests`, 0 cambios API/BLL/DAL/Entity/DTO, sin migración, **sin ADR nuevo** (aplica ADR-016). Gates: build 0/0 · tests **830/830** en verde, 0 omitidos (813 previos + 17 nuevos: 12 proxies + 2 autorización por reflexión + 3 action/ViewModel) · `JsApiBaseUrlRegressionTests` con `ExcepcionesConocidas` **vacía** (scan de `wwwroot/js/*.js` limpio) · cobertura BLL **89,96%** sin drift · Swagger 54 paths intactos · **validación visual en navegador por Jorge confirmada**. HANDOFF: ítems 3-4 de «Deuda técnica pendiente» retirados (deuda pagada). Sprint 4 sigue en curso (4/5 HU, 23 de 28 pts); pendiente **HU-025 (Gestión de KRs)**. **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.28 (2026-09-30): **hotfix de HU-023 (Vista Consolidada del Plan) aplicado y validado** — la validación visual en navegador (Jorge, login real `Gerente`, `/Plan/Consolidado`) destapó **4 defectos de runtime** que los 797 tests no podían detectar: **(A)** el MVC llamaba `GET /api/v1/areas` (ruta inexistente) y el `catch` de cualquier 404 lo etiquetaba falsamente como «sin ciclo activo» — ahora las 4 llamadas reales (`GET /api/v1/ciclos` → `FirstOrDefault(Estado=="Activo")` es el único determinante; luego `/ciclos/{id}/areas`, `/objetivos-cg/consolidado`, `/planes/consolidado`) y el `when (ex.StatusCode == 404)` desapareció · **(B)** los CGs daban 403 al Gerente (llamaba al endpoint JEF `/api/v1/objetivos-cg`) — ahora `GET /api/v1/objetivos-cg/consolidado` y `ObjetivosCg` es `List<ObjetivoCgConsolidadoResponse>` · **(C)** HTTP 500 `PostgresException 42P08` en `/api/v1/planes/consolidado` (`DateTime?` en null sin tipo declarado contra columna `DATE`) — `DynamicParameters` con `DbType.Date` explícito (**ADR-015**) · **(E)** `plan-consolidado.js` hacía `fetch('/api/v1/...')` relativo al MVC (`:7200`) → 404 — proxy MVC `/Plan/ConsolidadoDatos` + `/Plan/ConsolidadoExportar` con `IApiClient` (**ADR-016**: el navegador nunca consume la API interna). **Seguridad reforzada (SEC-01/SEC-05):** eliminados el `<input type="hidden" id="accessToken">` del DOM y `PlanConsolidadoViewModel.AccessToken` — el JWT ya no viaja al navegador; el proxy lo adjunta server-side desde la sesión. Gates: build **0 errores / 0 advertencias** · tests **813/813** en verde, 0 omitidos (797 previos + 16 nuevos: 8 del hotfix v2 + 7 del v3 + 1 de regresión arquitectónica `JsApiBaseUrlRegressionTests`, que blinda ADR-016 escaneando `wwwroot/js/*.js`) · cobertura BLL **89,96%** (línea) ≥ 70% · validación contra la API real (4 endpoints → 200; 5 escenarios de filtros de fecha → 200) · **validación visual en navegador por Jorge confirmada** («Revisado desde UI todo funcional hasta ahora»). **Deuda técnica explícita — hotfix pendiente de HU-022 (NO mezclado con este, siguiente paso acordado con Jorge):** vista `Entregables` inalcanzable (nada la enlaza) + mismo Defecto E en `entregables.js` (L225/L372/L401 — HANDOFF «Deuda técnica pendiente» ítem 3) + dropzone de subida visible para roles sin permiso (la API es JEF-only, `AccionPlanController.cs:174`). Sprint 4 sigue en curso (4/5 HU, 23 de 28 pts); pendientes el hotfix de HU-022 y HU-025 (Gestión de KRs). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.27 (2026-09-30): HU-024 (CRUD de OKRs) Implementada — Sprint 4 en curso (4/5 HU, 23 de 28 pts). Tests **797/797** en verde (735 previos + 62 nuevos: 39 casos BLL + 18 DAL + 5 MVC) · build 0 errores / 0 advertencias · cobertura BLL ≥ 70% · **sin ADR nuevo** (tabla `okr` y su RLS ya en el DDL base, sin migración; auditoría según ADR-003; repositorio dedicado según precedente HU-017) · `OkrController` (API, 5 endpoints JEF-only) + `OkrService`/`IOkrService` + `OkrRepository`/`IOkrRepository` + DTOs de OKR + vistas `Okr/Index|Crear|Editar` + `okrs.js` + ítem «OKRs» en `_Sidebar` · validación visual en navegador por Jorge confirmada (login real `JefeArea`, `/Okr`). Pendiente HU-025 (Gestión de KRs). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.25 (2026-09-27): la **validación visual de HU-021** (Jorge, login real `JefeArea`/`Gerente`, `/AccionPlan/Gantt`) destapó y corrigió **4 defectos de runtime de navegador** que ningún test C# podía detectar — `ReferenceError` en `tokenPx()` por un identificador mal escrito, que abortaba `iniciar()` antes de `gantt.init()` · `Invalid start_date argument for calculateEndDate` por los `type:"project"` sin `start_date` (ahora unión min/max de sus hijas, con fallback al ciclo) · la escala anclada con `gantt.config.min_date`/`max_date`, **inexistentes como claves de config** en DHTMLX 10 (ahora `start_date`/`end_date`), y el `read_date_format` **inerte desde la v8** eliminado · el contenedor `.gantt-pe` **sin altura**, que generaba un **loop de retroalimentación de altura** (scroll infinito) — más **2 problemas de UX** detectados en la misma sesión: el filtro **no eliminaba** las tareas filtradas (`gantt.parse()` fusiona por `id`; ahora `clearAll()` + `parse()` dentro de `gantt.silent()`) y el tooltip **heredaba** `white-space: nowrap`, con lo que su `max-width` no limitaba nada (ahora `white-space: normal` + `overflow-wrap: anywhere`). Gates finales: build **0 advertencias / 0 errores** · tests **584/584** · cobertura BLL **89,02%** (sin drift: sin cambios en C#). `docs/07_DESIGN_SYSTEM.md` § 12 sincronizado con `gantt.css`: tokens `--gantt-altura: clamp(420px, 68vh, 760px)` y `--gantt-tooltip-ancho: 360px` añadidos, más las **2 restricciones** que los separan de los tokens que lee `tokenPx()`. HU-021 sigue `Implementado`; Sprint 4 en curso (1/5 HU). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.24 (2026-09-27): cierre documental previo a los commits de HU-021 — **`[STACK-07]` precisada** (dhtmlx-gantt 10.0.3, variante Community MIT, vendorizada con `npm pack` en `wwwroot/lib/dhtmlx-gantt/` y anclada por el sha1 `4c1c896b9d465892e72647cff335bf755d0ca910`) y **HU-021 cerrada** (spec `Implementado`, **22/22 DoD**, tests 584/584, cobertura BLL 89.02%, 5 commits atómicos). Sprint 4 en curso (1/5 HU). **No cambia ninguna otra regla**: STACK-01..06 y STACK-08..11, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*v1.23 (2026-09-27): HU-021 (Vista Gantt del Plan de Acción) Implementada — Sprint 4 en curso (1/5 HU). Tests 584/584 en verde (build 0 advertencias / 0 errores) · cobertura BLL 89.02% (3139/3526) · migración `V005__indice_accion_plan_gantt.sql` aplicada y verificada en Supabase Cloud · DHTMLX Gantt Community MIT v10.0.3 vendorizado desde el registro oficial de npm (ADR-011) · `STACK-07` sin cambios, pendiente de precisión de versión/variante por Jorge.*
*v1.22 (2026-09-21): auditoría post-Sprint 3 resuelta 100% (3 críticos, 6 medios, 8 mejoras — ver HANDOFF y ADR-010). Tests: 554/554 en verde *(conteo drift: el real verificado era 556/556; corregido en v1.23)*. Pre-Sprint 4, sin HU abiertas.*
*v1.21 (2026-09-20): tabla de estado actualizada tras implementar HU-045 (UI de Gestión de Tenants — alcance ampliado) — Sprint 3 completado (6/6 HU).*
*v1.20 (2026-09-20): tabla de estado actualizada tras implementar HU-020 (Actualización de Progreso de Acciones) — Sprint 3 en curso.*
*v1.19 (2026-09-20): tabla de estado actualizada tras implementar HU-019 (CRUD Acciones del Plan) — Sprint 3 en curso.*
*v1.18 (2026-09-20): tabla de estado actualizada tras implementar HU-018 (Vista Consolidada CGs) — Sprint 3 en curso.*
*v1.17 (2026-09-20): tabla de estado actualizada tras implementar HU-016 (Tablero Inicio Gerente) y HU-017 (CRUD Objetivos CG) — Sprint 3 en curso.*
*v1.16 (2026-09-20): tabla de estado actualizada tras implementar HU-015 (Tablero de Inicio Jefe de Área) — Sprint 2 completado (8/8 HU).*
*v1.15 (2026-09-20): tabla de estado actualizada tras implementar HU-014 (Objetivos de Área por Trimestre en Pilares) — Sprint 2 en curso (7/8 HU).*
*v1.14 (2026-09-20): tabla de estado actualizada tras implementar HU-013 (CRUD de Pilares Estratégicos) — Sprint 2 en curso (6/8 HU).*
*v1.13 (2026-09-20): tabla de estado actualizada tras implementar HU-012 (Gestión de Valores Corporativos) — Sprint 2 en curso (5/8 HU).*
*v1.12 (2026-09-20): tabla de estado actualizada tras implementar HU-011 (Registro de Visión y Misión) — Sprint 2 en curso (4/8 HU).*
*v1.11 (2026-09-18): tabla de estado actualizada tras implementar HU-010 (Gestión de Responsables) — Sprint 2 en curso (3/8 HU).*
*v1.10 (2026-09-17): tabla de estado actualizada tras implementar HU-009 (Gestión de Áreas Estratégicas) — Sprint 2 en curso (2/8 HU).*
*v1.9 (2026-09-17): tabla de estado actualizada tras implementar HU-005 (Log de Auditoría) — Sprint 2 en curso (1/8 HU).*
*v1.8 (2026-09-17): tabla de estado actualizada tras implementar HU-008 (Configuración de Umbrales de Semáforo).*
*v1.6 (2026-09-14): tabla de estado actualizada tras implementar HU-006 (Configuración de la Empresa/Tenant).*
*v1.7 (2026-09-14): tabla de estado actualizada tras implementar HU-007 (Gestión de Ciclos Anuales).*
*v1.5 (2026-09-13): tabla de estado actualizada tras implementar HU-004 (Autenticación y Seguridad).*
*v1.4 (2026-09-13): tabla de estado actualizada tras implementar HU-003 (Gestión de Usuarios del Tenant).*
*v1.3 (2026-09-13): tabla de estado y backlog actualizados — 45 HU · 243 pts · 9 Sprints · HU-003 spec Aprobada · HU-045 movida al Sprint 3.*
*v1.2 (2026-09-13): tabla de estado actualizada tras implementar HU-002 (Gestión de Planes de Suscripción).*
*v1.1 (2026-09-13): tabla de estado actualizada tras implementar HU-001.*
*v1.31 (2026-10-02): **HU-025 (Gestión de Key Results) Implementada — Sprint 4 completado (5/5 HU, 28 de 28 pts)**. Tests: **917/917** en verde (841 previos + 76 nuevos: 56 BLL + 13 DAL + 7 MVC) · build 0 errores / 0 advertencias · Cobertura BLL **90,2%** (≥ 70% — TEST-02) · **sin ADR nuevo** (tabla `key_result`, su `UNIQUE (okr_id, codigo)` y su RLS `key_result_policy` ya existían en el DDL base; sin migración; decisiones F0–F7 ya resueltas en el spec aprobado) · `KeyResultController` nuevo (API, 6 endpoints JEF-only — incluido el masivo `PUT .../key-results/pesos` de F0) + `KeyResultService`/`IKeyResultService` + `KeyResultRepository`/`IKeyResultRepository` + DTOs de KR + vistas Razor `KeyResult/Index|Crear|Editar` + `keyresults.js` + botón «KRs» (`bi-bullseye`) en `Okr/Index` with `asp-controller="KeyResult"` explícito · **Sprint 4 cerrado: 5/5 HU — 28 de 28 pts**. Pendiente: **Sprint 5** (HU-026 Registro Mensual Valores KRs, HU-027 Consolidado OKRs, HU-028 CRUD CAPEX, HU-029 Desembolso CAPEX). **No cambia ninguna otra regla**: STACK, ARCH, SEC, TEST, DB, UX y LOOP intactos.*
*Toda modificación a este archivo requiere consenso del equipo y bump de versión.*

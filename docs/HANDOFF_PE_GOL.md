# 🔁 HANDOFF — PE-GOL SaaS
## Documento de Contexto para Nuevas Conversaciones
### Proyecto: Sistema de Plan Estratégico y Presupuesto · Dicegsa

---

## ¿Qué es este documento?

Este archivo es el **puente de contexto** entre conversaciones dentro del Project de Claude.
Toda conversación nueva debe leerlo antes de actuar. Contiene el estado exacto del proyecto,
la estructura de archivos disponibles en el contexto del Project y las reglas de cómo continuar.

---

## 👤 Contexto del Cliente

| Campo | Valor |
|-------|-------|
| **Cliente** | Jorge — Dicegsa, Nicaragua |
| **Rol** | IT & Telecommunications Engineer · Data Analyst en Operaciones |
| **Proyecto** | PE-GOL SaaS — sistema web multi-tenant para gestionar el Plan Estratégico y Presupuesto anual por gerencia |
| **Stack** | ASP.NET Core .NET 8 + Supabase (PostgreSQL 15) + Dapper + Bootstrap 5.3 + jQuery |
| **Metodología** | Scrum · Sprints de 2 semanas · Velocidad 30 pts/sprint |
| **Marco de agentes** | Loop (Spec→Plan→Tests→Implement→Review) + Graph (6 agentes especializados) |

---

## 📁 Archivos disponibles en el contexto del Project

Todos los archivos listados abajo están subidos en la sección de contexto del Project.
**Leerlos en este orden antes de responder cualquier consulta técnica:**

| # | Archivo | Contenido | Leer cuando... |
|---|---------|-----------|----------------|
| 1 | `AGENTS.md` | Constitución del proyecto — reglas EARS, stack, seguridad, testing, loop | **Siempre, primero** |
| 2 | `01_AS-IS_PE_GOL.md` | Análisis del Excel actual — 14 módulos, 7 áreas, pain points | Consultas de dominio |
| 3 | `02_REQUERIMIENTOS.md` | 66 RF · 26 RNF · 42 RN organizados por épica | Cualquier spec |
| 4 | `03_BACKLOG.md` | 45 HU · 243 pts · 9 Sprints con planning | Sprint planning |
| 5 | `04_ARQUITECTURA.md` | N-Tier 8 proyectos · Flujo capas · Batch Jobs · NuGet | Cualquier implementación backend |
| 6 | `05_DOMINIO.md` | 5 dominios · 22 entidades · 12 reglas RC | Specs y modelo |
| 7 | `06_MODELO_DATOS.md` | 25 tablas DDL · RLS · Vistas · Seed | DAL · migraciones · queries |
| 8 | `07_DESIGN_SYSTEM.md` | Tokens CSS · Componentes · Layout · Chart.js | Cualquier vista frontend |
| 9 | `@Orquestador.md` | Agente coordinador — protocolo de inicio, Loop, Graph | Conversaciones de Sprint |
| 10 | `@Arquitecto.md` | Agente de specs y ADRs | Generación de specs |
| 11 | `@BackendDev.md` | Agente de implementación backend | Implementación API/BLL/DAL |
| 12 | `@FrontendDev.md` | Agente de vistas Razor/JS/CSS | Implementación frontend |
| 13 | `@QA.md` | Agente TDD — tests antes del código | Generación de tests |
| 14 | `@Documenter.md` | Agente de cierre de HUs y ADRs | Cierre de sprint |

---

## 🗓️ Estado Actual del Proyecto

### Fase completada: **Análisis + Diseño (Fases 0, 1 y 2)**

| Entregable | Estado |
|-----------|--------|
| AS-IS (análisis del Excel) | ✅ Completo |
| Requerimientos (RF/RNF/RN) | ✅ Completo |
| Backlog (45 HU · 243 pts · 9 Sprints) | ✅ Completo |
| Arquitectura N-Tier | ✅ Completo |
| Modelo de Dominio | ✅ Completo |
| Modelo de Datos DDL + RLS | ✅ Completo |
| Design System | ✅ Completo |
| AGENTS.md (Constitución) | ✅ Completo |
| 6 Agentes configurados | ✅ Completo |
| **Specs Sprint 1** | ⏳ **Siguiente paso** |
| Implementación | ⏳ Pendiente |

### Próxima fase: **Sprint 1 — Specs + Implementación**

---

## 📋 Sprint 1 — Referencia Rápida

**Objetivo:** Plataforma operativa con tenants, usuarios y autenticación segura.
**Puntos:** 29 pts · **HUs:** 7

| HU | Nombre | Pts | Agente principal |
|----|--------|-----|-----------------|
| HU-001 | Gestión de Tenants | 5 | @Arquitecto → @BackendDev |
| HU-002 | Planes de Suscripción | 3 | @Arquitecto → @BackendDev |
| HU-003 | Usuarios y Roles (Super Admin) | 5 | @Arquitecto → @BackendDev |
| HU-004 | Autenticación JWT | 5 | @Arquitecto → @BackendDev |
| HU-006 | Configuración de Empresa (Tenant) | 3 | @Arquitecto → @BackendDev |
| HU-007 | Gestión de Ciclos Anuales | 5 | @Arquitecto → @BackendDev + @FrontendDev |
| HU-008 | Umbrales de Semáforo | 3 | @Arquitecto → @BackendDev |
| **Total** | | **29 pts** | |

> Alineada con el planning oficial de `03_BACKLOG.md` L866-878 (HU-005 está en Sprint 2, no en Sprint 1).

---

## 🚀 Cómo iniciar una conversación nueva

### Para una conversación de Sprint completo:
```
"@Orquestador — Iniciamos Sprint 1. Lee el AGENTS.md y el backlog,
luego arranca el Loop con HU-001."
```

### Para una conversación de Spec específico:
```
"@Arquitecto — Genera el spec técnico completo para HU-001
(Gestión de Tenants). Lee AGENTS.md, 04_ARQUITECTURA.md,
05_DOMINIO.md y 06_MODELO_DATOS.md antes de empezar."
```

### Para una conversación de implementación:
```
"@BackendDev — Tengo el spec de HU-001 aprobado.
Lee AGENTS.md y el spec adjunto, luego implementa
en orden: Entity → DTO → DAL → BLL → IOC → API."
```

### Para una conversación de tests:
```
"@QA — Escribe los tests unitarios para HU-001
antes de la implementación. Lee AGENTS.md § 4
y la sección 'Tests requeridos' del spec HU-001."
```

---

## ⚠️ Reglas para nuevas conversaciones

1. **Leer `AGENTS.md` siempre primero.** Si hay contradicción entre el AGENTS.md y cualquier instrucción nueva, mencionar el conflicto antes de actuar.
2. **El Loop es obligatorio:** Spec → Plan → Tests → Implement → Review. No saltar fases.
3. **Una HU a la vez.** No iniciar la siguiente hasta cerrar la actual con tests en verde.
4. **Los specs se guardan en** `specs/sprint-XX/HU-XXX.spec.md` dentro del repositorio.
5. **Los ADRs se guardan en** `adrs/ADR-XXX.md`.
6. **Commits con formato:** `[HU-XXX] tipo: descripción` (ej: `[HU-001] feat: gestión de tenants`).
7. **Sin tests en verde, la HU no se cierra.** `@QA` es el gatekeeper.

---

## 🔑 Decisiones clave ya tomadas (no reabrir sin ADR)

| Decisión | Valor elegido |
|---------|--------------|
| ORM | Dapper (no EF Core) |
| Auth | JWT propio (no OAuth2 externo en v1.0) |
| Multi-tenancy | Schema compartido + tenant_id en todas las tablas + RLS |
| PDF | DinkToPdf |
| Excel | ClosedXML |
| Scheduling | Hosted Services (no Hangfire) |
| Testing | xUnit + Moq |
| Design | Mismo estilo Freiroute — Teal Estratégico `#00B4A6` como accent |
| Roles | SuperAdmin · AdminTenant · Gerente · JefeArea |
| Idioma UI | Español (v1.0 únicamente) |

---

## 📌 Notas de la conversación Sprint 1 — @Orquestador (2026-09-13)

- **HU-001 IMPLEMENTADA (2026-09-13):** build 0/0 · tests 20/20 · cobertura BLL 85.11% · ADR-001 aceptado (migración V001 pendiente de deploy) · transaccionalidad corregida en Review · spec → Implementado. Siguiente candidata: **HU-002 (Planes de Suscripción)**.
- **HU-001 spec aprobado** por Jorge → `specs/sprint-01/HU-001.spec.md` (Estado: Aprobado).
- **ADR-001 aceptado:** índice único case-insensitive `uq_tenant_nombre ON tenant (LOWER(nombre))` → migración `db/migrations/V001__unique_tenant_nombre.sql`. Requisito para @BackendDev antes de implementar.
- **Deuda técnica (NO ADR, decisión de Jorge):** `usuario` y `refresh_token` tienen `tenant_id` pero quedaron **fuera del RLS** (sin `ENABLE ROW LEVEL SECURITY` en `06_MODELO_DATOS.md`). Mitigación actual: capa de aplicación + `[Authorize(Roles="SuperAdmin")]` + TenantMiddleware — suficiente para Sprint 1. **Revisar antes del Sprint de hardening.**
- **Coordinar antes de HU-003:** unificar glosario de roles — backlog dice "Administrador Tenant" (HU-003), dominio/DDL dicen `AdminTenant`, requerimientos/arquitectura dicen "Gerente". Propuesta: `SuperAdmin · AdminTenant · Gerente · JefeArea` (según HANDOFF § Decisiones clave).
- **HU-045 creada** en `03_BACKLOG.md` (EP-01, 3 pts, Sprint 2 propuesto): UI de Gestión de Tenants. ⚠️ Sprint 2 sumaría **32 pts** (velocidad 30) → validar asignación o mover otra HU.
- **Fase Tests HU-001 completada (TDD rojo):** `PE-GOL.Tests/TenantServiceTests.cs` — 20 tests red (stubs `NotImplementedException`). Pendiente implementación de @BackendDev para verde.
- **Pendientes de alineación arquitectónica detectados por @QA:** (a) `RootNamespace` de plantillas quedó `PE_GOL.*` (guion bajo) vs. nombres de proyecto `PE-GOL.*` → unificar al armar la solución; (b) excepciones viven temporalmente en `PE-GOL.BLL/Exceptions` y `TenantEntity` en `PE-GOL.DAL/Entities` → evaluar reubicación a `PE-GOL.Utility` / `PE-GOL.Entity` con el resto de proyectos.

---

## HU-002 · Gestión de Planes de Suscripción — CERRADA

**Fecha de cierre:** 2026-09-13 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-002.spec.md` → `Implementado`

**Entregado:** endpoints `api/v1/planes` (CRUD SuperAdmin + 401/403), `PlanService`/`PlanLimitValidator` puro (reutilizable en HU-003/HU-009), `PlanRepository` DAL-P1..P9, ctor de `TenantService` + validación de límites al cambiar plan (422), migración `V002__unique_plan_nombre.sql`.

**Verificación:** build 0/0 · tests 50/50 (30 HU-002 + 20 regresión HU-001) · cobertura BLL 84.48% (≥ 70%, TEST-02) · ADR-002 aceptado.

**Flags para Jorge (@Orquestador lo escalará):**

1. **Inconsistencia #1 — RESUELTO (Decisión de Jorge, 2026-09-13):** RN-010 alineada a **1..20 áreas** (02_REQUERIMIENTOS.md L281). **NO se emite V003** — el seed ya es correcto (`06_MODELO_DATOS.md` L583-586: Básico=5, Estándar=10, Premium=20; verificado por @Orquestador) y el `DEFAULT 10` del DDL (L55) es intencional: cada plan declara su valor explícito. El desfase era solo aparente (datos correctos; sin cambios de esquema ni seed). → ver sección **«Decisiones de Jorge — 2026-09-13»**, punto 1.
2. **Deploy pendiente en Supabase (pendiente de ejecución — instrucciones completas en «Decisiones de Jorge — 2026-09-13», punto 4):** ejecutar `V001__unique_tenant_nombre.sql` y `V002__unique_plan_nombre.sql` **en orden**.
3. **RLS (D8/Inconsistencia #4) — confirmado por Jorge («Decisiones de Jorge — 2026-09-13», punto 4b):** el connection string del DAL SaaS para `ObtenerConteosUsoAsync` (DAL-P8) usará rol `service_role` (BYPASSRLS) — verificar en deployment.
4. **Decisión técnica del BackendDev — RESUELTO (2026-09-13):** `UnsafeRelaxedJsonEscaping` al serializar `valor_anterior/valor_nuevo` en auditoría (JSON legible no-ASCII, RNF-023) → se creó **ADR-003** y el patrón se implementará a partir de HU-003 en `UsuarioService`. → ver «Decisiones de Jorge — 2026-09-13», punto 5.
5. **CA #2 de la HU:** `ValidarLimitesParaTenantAsync` DEBE ser consumido por HU-003 (Sprint 1, usuarios) y HU-009 (Sprint 2, áreas) para cerrar el criterio de aceptación.
6. **Pendientes globales — parcialmente resueltos (Decisión de Jorge, 2026-09-13):** capacidad Sprint 2 → resuelto (HU-045 movida a Sprint 3, ver punto 3); glosario de roles → resuelto (4 roles definidos, ver punto 2); prioridad de HU-002 respecto a HU-003 (ya reordenado en sprint planning).

---

## HU-003 · Gestión de Usuarios del Tenant — CERRADA

**Fecha de cierre:** 2026-09-13 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-003.spec.md` → `Implementado`

**Entregado:** endpoints `api/v1/usuarios` (7, SuperAdmin), `UsuarioService`/`UsuarioRepository` (U1-U10 + U3b + U6b/U6c), validators FluentValidation, IOC, BCrypt 12, `requiere_cambio_pwd=TRUE` (creación/reset), auditoría ADR-003 sin hash, límites vía IPlanService (Crear/Activar D7/cambio tenant D6), sin DELETE físico (estados), desactivar revoca refresh tokens.

**Verificación:** build 0/0 · tests 89/89 · cobertura BLL 85.94% · ADR-003 aplicado.

**Flags menores no bloqueantes (de @QA en Review):**
1. El orden del listado (`ORDER BY created_at DESC`) vive solo en la query DAL U6b → pendiente prueba de integración DAL cuando haya BD (deploy).
2. Los filtros rol/estado de `ListarAsync` no se revalidan contra los enums en BLL → sugerencia validar en API/FluentValidation en el siguiente ciclo (anotado como mejora).
3. Saneo de paginación confirmado (page≥1, pageSize 1..100).

**Siguientes dependencias:** HU-004 (Autenticación, mismo Sprint 1) consumirá `requiere_cambio_pwd` para el cambio de contraseña en primer login; correo de bienvenida queda en HU de Notificaciones (MailKit, Sprint 2). CA #2 de HU-002 también debe usarse en HU-009 (Sprint 2, áreas).

---

## HU-004 · Autenticación y Seguridad — CERRADA

**Fecha de cierre:** 2026-09-13 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-004.spec.md` → `Implementado`

**Entregado:** 4 endpoints `/api/v1/auth` (login, refresh, logout, cambiar-contrasena), `AuthService`/`AuthRepository` (DAL-A1..A10), `JwtTokenHelper`/`JwtOptions` en `PE-GOL.Utility` (ADR-004), `TenantContext` con `TenantId` nullable (D17), `TenantMiddleware` registrado tras `UseAuthentication` (ARCH-04), 4 validators FluentValidation, `ExceptionMiddleware` → 401 para `UnauthorizedException`, wiring D6 (`TenantContext.UserId`) en `UsuarioService`/`TenantService`/`PlanService`.

**Verificación:** build 0/0 · tests 130/130 (41 HU-004 + 89 regresión HU-001/002/003) · cobertura BLL 86.86% (≥ 70%, TEST-02) · ADR-004 aceptado e implementado.

**Flags menores no bloqueantes (de @QA en Review):**
1. La descripción de Swagger en `Program.cs` L39 aún menciona solo HU-001/HU-002 — cosmético, se actualizará en el siguiente ciclo.
2. Limpieza de refresh tokens huérfanos diferida a hardening por decisión de Jorge (Flag #7 del spec HU-004): el rechazo en validación ya cubre el riesgo; solo deuda de almacenamiento.
3. Incidente resuelto en Review: bug del helper de tests `CrearUsuarioAuth` (Caso 13) corregido por @QA (línea 70: `tenantId ?? (rol == "SuperAdmin" ? null : Guid.NewGuid())`) — sin cambio de contrato.

**Siguientes dependencias:** HU-006 (Configuración de Empresa, Sprint 1) es la próxima HU; HU-005 (Log de Auditoría, Sprint 2) consumirá la auditoría LOGIN/LOGOUT ya registrada; la UI de login pertenece a las HUs de frontend.

---

## HU-006 · Configuración de la Empresa (Tenant) — CERRADA

**Fecha de cierre:** 2026-09-14 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-006.spec.md` → `Implementado`

**Entregado (100% backend):** endpoints `api/v1/empresa` — GET (multi-rol `AdminTenant,Gerente,JefeArea`) · PUT (`AdminTenant`) · POST `/api/v1/empresa/logo` (multipart, `AdminTenant`); `EmpresaService`/`IEmpresaService` (BLL), extensión de `TenantRepository` (DAL-E1..E5), `ZonaHorariaHelper` + `ZonasIANA` (Utility), `StorageHelper`/`IStorageHelper`/`SupabaseStorageOptions` (Utility — ADR-005), `EmpresaUpdateRequestValidator`, registro IOC.

**Verificación:** build 0/0 · tests 156/156 (26 HU-006 + 130 regresión HU-001..HU-004) · cobertura BLL 87.51% (≥ 70%, TEST-02) · commit `f1e7289` `[HU-006] feat: configuración de empresa (tenant)` · ADR-005 implementado (paquete `Supabase` 8.1.1 fijado por @BackendDev; bucket `logos-tenant`).

**Flags menores no bloqueantes (de @QA en Review):**
1. **UI diferida (decisión de Jorge, Flag #2):** la vista Razor de configuración y el logo en el encabezado se entregan en HUs de frontend (HU-045 y siguientes), cuando `PE-GOL.Aplicacion` tenga sesión/ApiClient. **@Orquestador debe reflejarlo en el planning del Sprint 2.** HU-006 se entregó 100% backend; el contrato de endpoints/DTOs queda listo para consumir.
2. **Mejora opcional (de @QA):** añadir test `SubirLogo_RolNoAdminTenant` para blindar D12 (re-validación de rol en BLL en el endpoint de logo) — anotado para el siguiente ciclo.
3. **Discrepancia documental corregida:** la tabla de «Tests requeridos» del spec declaraba "17 casos" listando 18 filas; el criterio de Done declaraba `EmpresaServiceTests` (18) cuando el archivo real tiene 22 (18 del spec + 4 extras autorizados por @Orquestador: tenant inexistente en GET, lectura multi-rol, nombre vacío/151 chars, no tocar campos del SuperAdmin). Conteos corregidos a 22 + 4 = 26 tests.

**Siguientes dependencias:** HU-007 (Gestión de Ciclos Anuales, Sprint 1) es la próxima HU candidata; la UI de configuración de empresa se planificará con el cimiento de frontend (HU-045+).

---

## HU-007 · Gestión de Ciclos Anuales — CERRADA

**Fecha de cierre:** 2026-09-14 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-007.spec.md` → `Implementado`

**Entregado (100% backend — UI diferida a HU-045+):** 7 endpoints `api/v1/ciclos` (CRUD sin DELETE + activar/cerrar/clonar), `CicloService`/`ICicloService`/`CicloRepository`/`ICicloRepository`/`CicloController`, entidades `CicloEntity`/`UmbralSemaforoEntity`, DTOs y validators FluentValidation, registro IOC. `CrearAsync` inserta 2 filas default de umbrales (0.90/0.70) por ciclo; `ClonarAsync` copia los umbrales del origen con defensivo de defaults (contrato abierto para extenderlo en HU-009/HU-010).

**Verificación:** build 0/0 · tests **192/192** (35 HU-007 + regresión HU-001..HU-006) · cobertura BLL **87.26%** (≥ 70%, TEST-02) · commit `5b4c447` `[HU-007] feat: gestión de ciclos anuales (API+BLL+DAL+DTO+Entity+IOC)` · ADR-006 aceptado e implementado · migración `V003__unico_ciclo_activo.sql` **pendiente de aplicar en Supabase** (junto con V001/V002).

**Decisiones de Jorge (flags resueltos, 2026-09-14):**
1. **ADM activa / GER cierra** → RN-003 y RF-007 actualizados en `02_REQUERIMIENTOS.md`.
2. **CA #5 parcial:** umbrales se clonan en HU-007; áreas/responsables se completan en HU-009/HU-010 (Sprint 2). Contrato `ClonarAsync` queda abierto para extenderlo.
3. **UI diferida:** HU-007 se entregó **100% backend**; la vista Razor de gestión de ciclos se planificará con el cimiento de frontend (HU-045+). @Orquestador lo reflejó en el planning del Sprint 2.
4. **ADR-006 + `V003__unico_ciclo_activo.sql` aprobados** (índice parcial `uq_ciclo_unico_activo`, RC-01 a nivel BD) — requisito aplicado antes de implementar `ActivarAsync`.
5. **RC-01 estricto en Sprint 1 — nota de revisión futura:** `max_ciclos_activos=2` del plan Premium no es alcanzable con RC-01; revisar cuando un cliente real necesite más de un ciclo activo.

**Siguientes dependencias:** HU-008 (Umbrales de Semáforo, Sprint 1) consumió los defaults/clonación de umbrales de HU-007; HU-009 (Sprint 2) extenderá `ClonarAsync` para áreas/responsables.

---

## HU-008 · Configuración de Umbrales de Semáforo — CERRADA

**Fecha de cierre:** 2026-09-17 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-01/HU-008.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006/HU-007):** 2 endpoints `api/v1/ciclos/{cicloId}/umbrales` — GET (multi-rol `AdminTenant,Gerente,JefeArea`) · PUT (`AdminTenant`); `CicloService.ObtenerUmbralesAsync`/`ActualizarUmbralesAsync` (UPSERT conjunto KPI+PlanAccion + auditoría `UPDATE` entidad `UmbralSemaforo` en UNA transacción · normalización 2 decimales AwayFromZero · captura 23514 → 422 · defensivo de defaults sin escritura); `CicloRepository.UpsertUmbralAsync` (DAL-U2 `ON CONFLICT (ciclo_id, tipo) DO UPDATE`); validators `UmbralesUpdateRequestValidator`/`UmbralCategoriaRequestValidator`; DTOs `UmbralesUpdateRequest`/`UmbralCategoriaRequest`/`UmbralesCicloResponse`/`UmbralCategoriaResponse`.

**Verificación:** build 0/0 · tests **219/219** (27 HU-008 + 192 regresión HU-001..HU-007) · cobertura BLL **87.98%** (≥ 70%, TEST-02 — mejora el 87.26% de HU-007). **Sin migración ni ADR nuevos** (el DDL `umbral_semaforo` ya cubre CHECKs rango/estricto, defaults 0.90/0.70 y `UNIQUE (ciclo_id, tipo)`).

**Flags:** **ninguno.** Spec aprobado por Jorge el 2026-09-17 sin observaciones; única corrección de Review aplicada: caso de prueba #21 sin ejemplos negativos (con `MidpointRounding.AwayFromZero`, `-0.005` redondearía a `-0.01` y el validador de rango lo rechazaría antes de persistir).

**Siguientes dependencias:** **HU-009 (Gestión de Áreas Estratégicas, Sprint 2)** — consumirá `ValidarLimitesParaTenantAsync` de HU-002 y extenderá `ClonarAsync` de HU-007.

---

## HU-005 · Log de Auditoría — CERRADA

**Fecha de cierre:** 2026-09-17 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-005.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006/HU-007/HU-008):** 2 endpoints GET `/api/v1/log-auditoria` — listado paginado con filtros (tenant, usuario, rango de fechas, acción) + detalle por id (con `valor_anterior`/`valor_nuevo` como JSON crudo, ADR-003); `LogAuditoriaService`/`ILogAuditoriaService` (solo lectura — CA #3 inmutabilidad, sin métodos de escritura); `LogAuditoriaRepository`/`ILogAuditoriaRepository` (DAL-L1..L4, Dapper, LEFT JOIN a tenant/usuario, `ORDER BY created_at DESC`); `LogAuditoriaLimpiezaService` (HostedService ARCH-05, batch diario 03:00, `Auditoria:RetencionDias` default 90 — CA #4); `LogAuditoriaFiltrosValidator` (FluentValidation); `LogAuditoriaController` (zona Saas, `[Authorize(Roles="SuperAdmin")]`); registros IOC; `LogAuditoriaEntity` en `PE-GOL.Entity/Saas/`; DTOs (`LogAuditoriaFiltrosRequest`/`LogAuditoriaResponse`/`LogAuditoriaDetalleResponse`/`LogAuditoriaFiltrosDto`).

**Verificación:** build 0/0 · tests **243/243** (24 HU-005 + 219 regresión/contrato HU-001..HU-008) · cobertura BLL **88.61%** (≥ 70%, TEST-02 — mejora el 87.98% de HU-008) · commit `ba07c81` `[HU-005] feat: log de auditoría (API+BLL+DAL+DTO+Entity+HostedService)` · **sin ADR nuevo ni migración** (el DDL de `log_auditoria` ya cubre todo; decisiones enmarcadas en ARCH-05, ADR-003 y la decisión de Jorge sobre RLS global).

**Flags resueltos por Jorge en la aprobación del spec (2026-09-17):**
1. **Alcance de roles:** solo SuperAdmin (fiel al backlog). Acceso de AdminTenant a su log diferido al Sprint de hardening — requeriría ADR (RLS en `log_auditoria`) y no es deseable por riesgo de negocio (el ADM podría ver cuándo/cómo intervino el SA sobre su tenant).
2. **Retención vía HostedService confirmada** (`Auditoria:RetencionDias` configurable, default 90) — margen para clientes con retención más larga sin tocar código.
3. **Batch de limpieza no viola CA #3:** la inmutabilidad aplica a roles vía API; la retención es política de almacenamiento del sistema. Blindado por tests de contrato #18/#19 (reflexión: `ILogAuditoriaService`/`ILogAuditoriaRepository` sin Update/Delete públicos).

**Hallazgos de arquitectura documentados (para @Orquestador):**
1. `AuditMiddleware.cs` (`04_ARQUITECTURA.md` L117) **no existe en el código** — la auditoría vive en BLL vía `InsertLogAsync` transaccional (patrón HU-001..008). `04_ARQUITECTURA.md` actualizado para reflejar la realidad.
2. `LogAuditoria` ubicada en `PE-GOL.Entity/Saas/` (no `Sistema/` como decía `04_ARQUITECTURA.md` L163) — alineado con el dominio SaaS del modelo de datos. `04_ARQUITECTURA.md` actualizado.

**Siguientes dependencias:** **HU-009 (Gestión de Áreas Estratégicas, Sprint 2)** — consumirá `ValidarLimitesParaTenantAsync` de HU-002 y extenderá `ClonarAsync` de HU-007 (áreas/responsables).

---

## HU-009 · Gestión de Áreas Estratégicas — CERRADA

**Fecha de cierre:** 2026-09-17 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-009.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006/HU-007/HU-008):** 6 endpoints `/api/v1/ciclos/{cicloId}/areas` — GET listado (multi-rol `AdminTenant,Gerente,JefeArea`; JEF solo su área, SEC-07) · GET detalle (multi-rol) · POST (ADM) · PUT (ADM) · PUT `.../desactivar` (ADM, sin DELETE — CA #5) · GET `.../responsables` (ADM/GER, puente HU-010); `AreaService`/`IAreaService` (BLL, ctor D13 + overload `ILogger<AreaService>?`); DAL-A1..A11 en `CicloRepository` (áreas = hijos del agregado Ciclo, D-I); `IPlanService.ObtenerLimitesAsync` (aditivo, RN-010 con doble chequeo por ciclo + tenant — D-D); `CicloService.ClonarAsync` extendido (clonación de áreas + sync `usuario.area_id` + auditoría CREATE por área — CA #5 HU-007); validators `AreaCreateRequestValidator`/`AreaUpdateRequestValidator`; registro IOC; ADR-007 aceptado + migración `V004__unico_responsable_area.sql` (pendiente de deploy junto a V001/V002/V003).

**Verificación:** build 0/0 · tests **276/276** (33 HU-009 + 243 regresión/contrato HU-001..HU-008) · cobertura BLL **88.81%** (≥ 70%, TEST-02 — mejora el 88.61% de HU-005) · commit `9347188` `[HU-009] feat: gestión de áreas estratégicas (API+BLL+DAL+DTO+Entity+Validators+IOC)`.

**Flags resueltos por Jorge en la aprobación del spec (2026-09-17):** los 7 — (1) ADR-007 + `V004__unico_responsable_area.sql` (RN-012 a nivel BD, patrón ADR-006/V003); (2) `IPlanService.ObtenerLimitesAsync` aditivo confirmado; (3) escritura ADM-only, GER lectura (RN-006 actualizada en `02_REQUERIMIENTOS.md`); (4) gestión de áreas en Borrador + Activo, Cerrado solo lectura (RC-12); (5) sync `usuario.area_id` confirmado con limitación de FK única; (6) puente HU-010 (candidatos = usuarios JefeArea Activos); (7) RN-012 solo sobre áreas activas (`activa = TRUE`).

**Nota Flag #5 (modelo futuro):** en una versión futura con múltiples ciclos activos simultáneos, el modelo `usuario.area_id` (FK única) requiere repensarse — un JefeArea no puede tener áreas distintas en ciclos distintos (el acceso histórico a ciclos anteriores queda limitado). Aceptable para v1.0 (AS-IS: cada jefe lidera un área estable año a año).

**Siguientes dependencias:** **HU-010 (Sprint 3)** reemplazará/extenderá el puente de candidatos a responsable (`GET .../areas/responsables`); la UI de gestión de áreas se planificará con el cimiento de frontend (HU-045+).

---

## HU-011 · Registro de Visión y Misión — CERRADA

**Fecha de cierre:** 2026-09-20 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-011.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006/HU-007/HU-008/HU-009/HU-010):** 2 endpoints `GET/PUT /api/v1/ciclos/{cicloId}/filosofia` — GET (multi-rol `AdminTenant,Gerente,JefeArea`; defensivo de defaults vacíos D-H, sin escritura) · PUT (solo `Gerente`, RN-006; UPSERT `ON CONFLICT (tenant_id, ciclo_id) DO UPDATE` DB-06; 422 en ciclo `Cerrado` RC-12); `FilosofiaService`/`IFilosofiaService` (BLL, ctor D13 + overload `ILogger<FilosofiaService>?`); DAL-F1/F2 en `CicloRepository` (filosofía = hija del agregado Ciclo, D-I); `HtmlSanitizerHelper` en `PE-GOL.Utility/Security/` (allowlist `p, br, b, strong, i, em, ul, ol, li`, sin atributos, máx 5000 chars — D-C, XSS SEC-05); CA #2 implementado en `CicloService.ActivarAsync` (validación dura 422 "Debe registrar la Visión y Misión del ciclo antes de activarlo", sin cambio de firma); auditoría ADR-003 (UPDATE, entidad `Filosofia`, `valor_anterior` snapshot, null en primer guardado — CA #4); `FilosofiaUpdateRequestValidator` (FluentValidation); `FilosofiaEntity` en `PE-GOL.Entity/Estrategia/`; DTOs (`FilosofiaUpdateRequest`/`FilosofiaResponse`/`FilosofiaUpsertDto`); registro IOC.

**Verificación:** build 0/0 · tests **329/329** (18 HU-011 + 311 regresión/contrato HU-001..HU-010) · cobertura BLL **88.9%** (≥ 70%, TEST-02 — mejora el 88.81% de HU-009) · commit `6952053` `[HU-011] feat: registro de visión y misión (API+BLL+DAL+DTO+Entity+Validators+IOC)` · **sin ADR nuevo ni migración** (la tabla `filosofia` ya existía en el DDL con `UNIQUE (tenant_id, ciclo_id)` y RLS `filosofia_policy`; la sanitización HTML es helper propio, sin librería externa — confirmado por @Arquitecto).

**Flags resueltos por Jorge en la aprobación del spec (2026-09-20):** los 5 — (1) CA #2 validación dura 422 en `ActivarAsync`; (2) UPSERT en primer guardado, sin fila default en `CrearAsync`; (3) allowlist `p, br, b, strong, i, em, ul, ol, li` sin atributos, máx 5000 chars, sin enlaces/imágenes en v1.0; (4) auditoría `UPDATE` con `valor_anterior = null` en primer guardado (sin bifurcación CREATE); (5) GET multi-rol ADM/GER/JEF, PUT solo GER, SEC-07 no aplica.

**Hallazgos documentados (para @Orquestador):**
1. **SEC-07 NO APLICA a `filosofia`** (D-E): es corporativa — una fila por ciclo por tenant, sin `area_id`; RLS `filosofia_policy` solo por tenant. El JEF lee la filosofía completa del ciclo (RN-007: solo lectura). Verificado en DAL-F1/F2.
2. **Clonación de filosofía: NO** (decisión de Jorge, hallazgo no bloqueante): RF-011 lista "áreas, responsables y umbrales" como contenido clonable — no incluye filosofía. El GER la redacta de nuevo por ciclo. `ClonarAsync` no se toca.
3. **Fixtures 24/25 de `CicloServiceTests` actualizados por @QA** (Review): setup de `ObtenerFilosofiaAsync` añadido al mock de `ICicloRepository` en los casos de regresión de `ActivarAsync`. Conflicto de **contrato legítimo** (la extensión de esta HU añadió la llamada DAL-F1 al flujo de activación), **no bug de producción** — sin cambio de contrato público ni de comportamiento.
4. **Discrepancia de backlog corregida:** HU-011 figuraba con "Sprint: 3" en su encabezado (`03_BACKLOG.md` L238) pero la tabla de Sprint Planning la asigna al Sprint 2 (L896) — corregido a "Sprint: 2" al cierre (mismo patrón que HU-010 en EP-02).

**Siguientes dependencias:** **HU-012 (Valores Corporativos, Sprint 2)** — usará la columna `valores` (JSONB) de la **misma tabla `filosofia`** (ya existe en el DDL, `DEFAULT '[]'`); la UI de Visión/Misión/Valores se planificará con el cimiento de frontend (HU-045+).

---

## HU-012 · Gestión de Valores Corporativos — CERRADA

**Fecha de cierre:** 2026-09-20 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-012.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006/HU-007/HU-008/HU-009/HU-010/HU-011):** endpoint nuevo `PUT /api/v1/ciclos/{cicloId}/filosofia/valores` (solo `Gerente`, RN-006; UPSERT `ON CONFLICT (tenant_id, ciclo_id) DO UPDATE` DB-06 que **solo toca `valores`/`updated_by`/`updated_at`**, sin pisar vision/mision; 422 en ciclo `Cerrado` RC-12, 0 valores, >15, valor vacío tras trim, >100 chars, duplicados case-insensitive — CA #4); `GET /api/v1/ciclos/{cicloId}/filosofia` **extendido aditivamente** con `Valores` (`List<string>`, parseo del JSONB en `MapToResponse`; `[]` defensivo si no existe fila — D-H); `FilosofiaService.ActualizarValoresAsync` (extensión de `IFilosofiaService`/`FilosofiaService`, ctor D13 intacto); DAL-F3 `UpsertFilosofiaValoresAsync` en `CicloRepository` (extensión del agregado Ciclo, D-I); auditoría ADR-003 (UPDATE, entidad `Filosofia`, snapshot `{"valores":[...]}`, `valor_anterior = null` en primer guardado — D-D); `ValoresUpdateRequestValidator` (FluentValidation); DTOs (`ValoresUpdateRequest`/`FilosofiaValoresUpsertDto`/`FilosofiaResponse` aditivo); registro IOC.

**Verificación:** build 0/0 · tests **347/347** (18 HU-012 + 329 regresión/contrato HU-001..HU-011) · cobertura BLL **88.6%** (≥ 70%, TEST-02) · commit `727386c` `[HU-012] feat: gestión de valores corporativos (API+BLL+DAL+DTO+Validators)` · **sin ADR nuevo ni migración** (la columna `valores` JSONB ya existía en el DDL con `DEFAULT '[]'` y el `UNIQUE (tenant_id, ciclo_id)` sirve de target del `ON CONFLICT` — confirmado por @Arquitecto).

**Flags resueltos por Jorge en la aprobación del spec (2026-09-20):** los 7 — (1) endpoints aditivos (contrato HU-011 intacto); (2) orden = índice del array JSONB, sin columna `orden`; (3) validaciones CA #4 (min 1, max 15, trim, 100 chars, sin duplicados case-insensitive); (4) auditoría solo del array `valores` como `{"valores":[...]}`; (5) PUT solo GER, GET multi-rol ADM/GER/JEF, SEC-07 no aplica; (6) PUT en Borrador/Activo, bloqueado en Cerrado (RC-12); (7) activación NO exige ≥1 valor (el "mínimo 1" aplica solo al guardar).

**Hallazgos documentados (para @Orquestador):**
1. **Contrato HU-011 intacto (verificado):** `FilosofiaUpdateRequest` sin cambios · `PUT /filosofia` sin cambios · `FilosofiaResponse` solo aditivo (campo `Valores`) — los 329 tests de regresión HU-001..HU-011 en verde lo confirman.
2. **Fix sintáctico CS0854 en tests de @QA (validado, no bloqueante):** 4 ocurrencias de `Deserialize(..., (JsonSerializerOptions?)null)` en `FilosofiaServiceTests.cs` corregidas por @BackendDev — puramente sintáctico (sobrecarga de `JsonSerializer.Deserialize`), **sin cambio de contrato ni de comportamiento**.
3. **Cobertura de `FilosofiaService` en 83.2%** (por debajo del promedio BLL 88.6%) por caminos de error fuera del alcance del spec (UPSERT 0 filas, 23505, catch general, JSON inválido). @QA recomienda **4 tests suplementarios** para el próximo ciclo (no bloqueantes): `ActualizarValores_UpsertCeroFilas_LanzaInvalidOperation`, `ActualizarValores_23505_LanzaValidacion`, `ActualizarValores_CatchGeneral_Rethrow`, `Obtener_JsonInvalido_RetornaListaVacia`.
4. **SEC-07 NO APLICA** (D-E): `filosofia` es corporativa — sin `area_id`, RLS `filosofia_policy` solo por tenant. Verificado en DAL-F3 (sin `AND area_id` para ningún rol).

**Siguientes dependencias:** **HU-013 (CRUD Pilares Estratégicos, Sprint 2)** — nueva tabla `pilar` (EP-04); la UI de Visión/Misión/Valores se planificará con el cimiento de frontend (HU-045+).

---

## HU-013 · CRUD de Pilares Estratégicos — CERRADA

**Fecha de cierre:** 2026-09-20 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-013.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006..HU-012):** 5 endpoints `/api/v1/ciclos/{cicloId}/pilares` — GET listado (multi-rol `AdminTenant,Gerente,JefeArea`, conteos CG/OKRs CA #5, `ORDER BY orden ASC, codigo ASC`) · GET detalle (multi-rol, conteos) · POST (solo `Gerente`, RN-006; código PEC-N auto-generado CA #1, máx 8 CA #2, 422 RC-12) · PUT (solo `Gerente`; código NO editable) · DELETE (solo `Gerente`; DELETE físico D-A, validación de dependencias CA #3 DAL-P9 + capa 2 FK 23503 → 422); `PilarService`/`IPilarService` (BLL, ctor D13 + overload `ILogger<PilarService>?`); DAL-P1..P9 en `CicloRepository` (extensión del agregado Ciclo, D-I); `PilarEntity` en `PE-GOL.Entity/Estrategia/`; 8 DTOs (`PilarCreateRequest`/`PilarUpdateRequest`/`PilarResponse`/`PilarInsertDto`/`PilarUpdateDto`/`SiguienteSecuenciaPilarDto`/`PilarConteosDto`/`PilarDependenciasDto`); 2 validators FluentValidation (`PilarCreateRequestValidator`/`PilarUpdateRequestValidator`); registro IOC; auditoría ADR-003 (CREATE/UPDATE/DELETE, entidad `Pilar`, snapshot `{"codigo","nombre","estrategia_victoria","orden"}` — D-K).

**Verificación:** build 0/0 · tests **381/381** (34 HU-013 + 347 regresión/contrato HU-001..HU-012) · cobertura BLL **88.68%** (≥ 70%, TEST-02 — mejora el 88.6% de HU-012) · **sin ADR nuevo ni migración** (la tabla `pilar` ya existía en el DDL con `UNIQUE (ciclo_id, codigo)` y RLS `pilar_policy`; sin librería externa, sin proyecto nuevo — ARCH-01 intacto; el patrón 23505/23503 → 422 ya está en ADR-007/HU-002 — confirmado por @Arquitecto).

**Flags resueltos por Jorge en la aprobación del spec (2026-09-20):** los 8 — (1) DELETE físico (sin columna `activa`, validación BLL + FK 23503 como capa 2); (2) código PEC-N auto-generado secuencial por ciclo sin padding, capa 2 `UNIQUE (ciclo_id, codigo)` 23505 → 422; (3) máximo 8 pilares por ciclo en BLL con carrera TOCTOU aceptada; (4) límite 2000 chars para `estrategia_victoria`; (5) POST/PUT/DELETE solo GER, GET multi-rol ADM/GER/JEF, SEC-07 NO APLICA; (6) escritura en Borrador/Activo, bloqueada en Cerrado (RC-12); (7) `orden` opcional con default secuencial, sin endpoint de reordenación masiva; (8) auditoría solo del alcance HU-013 (sin `objetivo_q1..q4`).

**Hallazgos documentados (para @Orquestador):**
1. **Discrepancia de backlog corregida:** HU-013 figuraba con "Sprint: 3" en su encabezado (`03_BACKLOG.md` L278) pero la tabla de Sprint Planning la asigna al Sprint 2 (L898) — corregido a "Sprint: 2" al cierre (mismo patrón que HU-011 L238 y HU-012 L254).
2. **FK 23503 como capa 2 del CA #3:** `objetivo_cg.pilar_id` (L220) y `okr.pilar_id` (L238) son `UUID NOT NULL REFERENCES pilar(id)` **sin `ON DELETE CASCADE`** → el DELETE físico de un pilar con dependencias falla en BD con `foreign_key_violation` (23503) → 422 amigable (patrón ADR-007 extendido a 23503, precedente HU-002 D2). La BLL DAL-P9 es la capa 1 con mensaje específico.
3. **Carrera TOCTOU en CA #2 (máximo 8) aceptada:** sin constraint BD viable (DB-04 prohíbe triggers; un CHECK no puede contar filas), dos GER creando en paralelo pueden superar el límite (peor caso: 9 pilares). Aceptada como limitación documentada — regla de negocio blanda, impacto mínimo.
4. **`objetivo_q1..q4` fuera de alcance (explícito):** las columnas existen en el DDL (L183-186) pero pertenecen a HU-014. HU-013 no las lee, no las escribe, no las expone en DTOs/entidad/snapshot de auditoría. HU-014 las añadirá aditivamente sin romper este contrato.
5. **SEC-07 NO APLICA** (D-E): `pilar` es corporativa — sin `area_id`, RLS `pilar_policy` solo por tenant. El JEF lee **todos** los pilares del ciclo (RN-007: solo lectura). Verificado en DAL-P1..P9 (sin `AND area_id` para ningún rol).

**Siguientes dependencias:** **HU-014 (Objetivos de Área por Trimestre en Pilares, Sprint 2)** — usará las columnas `objetivo_q1..q4` de la **misma tabla `pilar`** (ya existen en el DDL, L183-186, intocadas por HU-013); la UI de Pilares se planificará con el cimiento de frontend (HU-045+).

---

## HU-014 · Objetivos de Área por Trimestre en Pilares — CERRADA

**Fecha de cierre:** 2026-09-20 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-014.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006..HU-013):** endpoint nuevo `PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId}/objetivos-trimestrales` (solo `Gerente`, RN-006; body `ObjetivosTrimestralesUpdateRequest` con 4 campos opcionales CA #2; UPDATE solo `objetivo_q1..q4` + `updated_at` — contrato HU-013 intacto; 422 RC-12 ciclo `Cerrado`, >2000 chars D-C; 403 rol ≠ GER; 404 ciclo/pilar inexistente o sin tenant); GET listado/detalle **extendidos aditivamente** con `ObjetivoQ1..Q4` (`string?`) en `PilarResponse` (CA #1, CA #3: JEF lee como guía — SEC-07 NO APLICA, D-E); `PilarService.ActualizarObjetivosTrimestralesAsync` (extensión de `IPilarService`/`PilarService`, ctor D13 intacto); DAL-P10 `ActualizarObjetivosTrimestralesAsync` en `CicloRepository` (extensión del agregado Ciclo, D-J) + DAL-P1/P2/P3 extendidas aditivamente; `PilarEntity`/`PilarConteosDto`/`PilarResponse` extendidos aditivamente con `ObjetivoQ1..Q4`; `ObjetivosTrimestralesUpdateRequest`/`PilarObjetivosTrimestralesUpdateDto` (DTOs nuevos); `ObjetivosTrimestralesUpdateRequestValidator` (FluentValidation); registro IOC; auditoría ADR-003 (UPDATE, entidad `Pilar`, snapshot solo 4 campos `{"objetivo_q1","objetivo_q2","objetivo_q3","objetivo_q4"}` — D-I; `SnapshotPilar` de HU-013 sin cambios).

**Verificación:** build 0/0 · tests **397/397** (16 HU-014 + 381 regresión/contrato HU-001..HU-013) · cobertura BLL **88.66%** (≥ 70%, TEST-02; `PilarService` 100%, `ActualizarObjetivosTrimestralesAsync` 85.07%) · **sin ADR nuevo ni migración** (las columnas `objetivo_q1..q4` ya existían en el DDL `06_MODELO_DATOS.md` L183-186, `TEXT` nullable; sin librería externa, sin proyecto nuevo — ARCH-01 intacto; patrón N-Tier sin cambios — confirmado por @Arquitecto).

**Flags resueltos por Jorge en la aprobación del spec (2026-09-20):** los 8 — (1) endpoint de escritura NUEVO aditivo (contrato HU-013 intacto); (2) límite 2000 chars/trimestre; (3) texto plano sin HTML; (4) auditoría solo los 4 campos `{"objetivo_q1".."objetivo_q4"}`; (5) PUT solo GER, GET multi-rol ADM/GER/JEF, SEC-07 NO APLICA; (6) escritura en Borrador/Activo, bloqueada en Cerrado (RC-12); (7) null como "sin contenido" (trimestres opcionales CA #2); (8) Sprint 2 según tabla de planning (backlog L293 corregido al cierre).

**Hallazgos documentados (para @Orquestador):**
1. **Discrepancia de backlog corregida:** HU-014 figuraba con "Sprint: 4" en su encabezado (`03_BACKLOG.md` L293) pero la tabla de Sprint Planning la asigna al Sprint 2 (L899) — corregido a "Sprint: 2" al cierre (mismo patrón que HU-011 L238, HU-012 L254 y HU-013 L278).
2. **Sin migración ni ADR de esquema:** las columnas `objetivo_q1..q4` ya existían en el DDL (`06_MODELO_DATOS.md` L183-186, `TEXT` nullable) — HU-013 las declaró fuera de alcance y no las tocó; HU-014 las gestiona aditivamente (entidad + DTOs + DAL) sin romper el contrato HU-013.
3. **Contrato HU-013 intacto (verificado):** `PilarUpdateRequest` sin cambios · `PUT /pilares/{pilarId}` sin cambios · `PilarResponse` solo aditivo (4 campos) · `PilarEntity` solo aditivo · `SnapshotPilar` sin cambios · DAL-P1/P2/P3 solo ganan columnas en el SELECT — los 34 tests de HU-013 en verde lo confirman.
4. **SEC-07 NO APLICA** (D-E): `pilar` es corporativa — sin `area_id`, RLS `pilar_policy` solo por tenant. El JEF lee **todos** los pilares del ciclo con sus objetivos trimestrales (RN-007, CA #3). Verificado en DAL-P10 (sin `AND area_id` para ningún rol).
5. **CA #4 → HU-017 (Sprint 4):** el CA #4 de HU-014 ("se muestran en la pantalla de creación de Objetivos CG como referencia contextual") es el CA #3 de HU-017 (backlog L343-355, L351). HU-014 entrega el **contrato de datos** (GET /pilares con `ObjetivoQ1..Q4`) para que HU-017 los consuma. No se crea UI ni endpoint adicional en esta HU.

**Siguientes dependencias:** **HU-017 (CRUD de Objetivos CG, Sprint 4)** — consumirá `ObjetivoQ1..Q4` del GET /pilares como referencia contextual en su pantalla de creación de CGs (CA #3 de HU-017); la UI de Pilares se planificará con el cimiento de frontend (HU-045+).

---

## HU-015 · Tablero de Inicio del Jefe de Área — CERRADA

**Fecha de cierre:** 2026-09-20 · **Cerrada por:** @Documenter (LOOP-05) · **Spec:** `specs/sprint-02/HU-015.spec.md` → `Implementado`

**Entregado (100% backend — misma decisión de UI diferida de HU-006..HU-014):** endpoint `GET /api/v1/dashboard/jefe-area` (solo `JefeArea`, ruta standalone F6, sin body/query params SEC-06); `DashboardService`/`IDashboardService` (BLL, D12 rol 403 → tenant 404 → DAL-D1 ciclo activo CA #4 → AreaId SEC-07 → DAL-A2 área → DAL-C10 umbrales con defaults 0.90/0.70 → DAL-D3/D4/D5 → atrasadas RN-017 regla 4 recalculadas en BLL D-D → semáforos D-E con `SemaforoHelper`); `SemaforoHelper` en `PE-GOL.Utility/Helpers/` (puro, D-I, reutilizable HU-016/017/024/038/039); DAL-D1/D3/D4/D5 en `CicloRepository` (agregado Ciclo, D-K, SEC-07 `AND area_id`); `DashboardController` (Swagger completo); 6 DTOs en `PE-GOL.DTO/Responses/Dashboard/`; registro IOC. 100% backend — vista Razor diferida a HU-045+ (Sprint 3, `.kpi-card` DS § 6.1).

**Verificación:** build 0/0 · tests **422/422** (25 HU-015 + 397 regresión) · cobertura BLL **88.9%** (≥ 70%, TEST-02 — mejora el 88.66% de HU-014; `DashboardService` 85.71%) · **sin ADR nuevo ni migración** (`SemaforoHelper` es código propio pre-documentado en `04_ARQUITECTURA.md` L173; ARCH-01 intacto).

**Flags resueltos por Jorge (2026-09-20):** los 8 — (1) F1 backend-only confirmado; (2) F2 promedio numérico vs umbrales KPI; (3) F3 solo JefeArea; (4) F4 atrasadas recalculadas en BLL; (5) F5 vacío → "Rojo" fórmula pura; (6) F6 ruta standalone; (7) F7 Sprint 2 según planning; (8) F8 TenantContext sin CicloId no se corrige.

**Hallazgos documentados (para @Orquestador):**
1. **Semántica `DiasAlVencimientoMasCercano` fijada por tests 10/11/12/17 y confirmada por Jorge (2026-09-20):** *"mínimo sobre pendientes futuras; si todas vencidas, máximo fecha de vencimiento (más reciente) como días negativos; null si sin pendientes"* — la fórmula literal del spec (`min` sobre todas las pendientes) quedó reemplazada.
2. **Discrepancia de backlog corregida:** HU-015 figuraba con "Sprint: 4" en su encabezado (`03_BACKLOG.md` L316) pero la tabla de Sprint Planning la asigna al Sprint 2 (L900) — corregido a "Sprint: 2" al cierre (mismo patrón que HU-011 L238, HU-012 L254, HU-013 L278 y HU-014 L293).
3. **H1:** `TenantContext` sin `CicloId` (discrepancia con `04_ARQUITECTURA.md` § 4.2) — no corregido; DAL-D1 resuelve el ciclo activo (F8).
4. **H2:** `DashboardController`/`DashboardService`/`SemaforoHelper` creados completando `04_ARQUITECTURA.md` L113/L134/L146/L173.

**Siguientes dependencias:** **HU-016 (Tablero de Inicio Gerente, Sprint 3)** — el `DashboardController` puede albergar su endpoint sin romper contratos; `SemaforoHelper` reutilizable; la vista Razor del tablero JEF se planifica con el cimiento de frontend (HU-045+, Sprint 3).

---

## Decisiones de Jorge — 2026-09-13 (resuelven flags del Sprint 1)

**Contexto:** Jorge resolvió los 5 puntos bloqueantes del Sprint 1 señalados como flags en la sección HU-002. Ninguna decisión obliga a cambios de esquema ni de seed; la única pieza nueva es el **ADR-003** (aplicable a partir de HU-003).

1. **Tope real de áreas (flag #1):** NO se emite V003. El seed ya es correcto (`06_MODELO_DATOS.md` L583-586: Básico=5, Estándar=10, Premium=20; verificado por @Orquestador). El `DEFAULT 10` del DDL (L55) es intencional y no obliga a ningún plan — cada plan declara su valor explícito. El desfase era solo aparente (datos correctos; sin cambios de esquema ni seed).
2. **Definición de roles (glosario — desbloquea HU-003):** `rol_usuario` tiene 4 roles distintos: `SuperAdmin` (SaaS-level, NO cuenta en `max_usuarios`, sin `tenant_id`), `AdminTenant`/ADM (administra el tenant → gestiona usuarios internos, configura ciclos; SÍ cuenta), `Gerente`/GER (usuario operativo: consolidados de todas las áreas, dashboards y reportes; NO administra el tenant; SÍ cuenta), `JefeArea` (responsable de área; SÍ cuenta). "Usuario que ocupa plaza" (D5 HU-002 resuelto) = todos los usuarios con `tenant_id` (AdminTenant+Gerente+JefeArea), incl. Inactivo/Bloqueado. Aplica desde HU-002 (DAL-P8) y se manifiesta en HU-003.
3. **Capacidad Sprint 2:** HU-045 (UI Gestión de Tenants, 3 pts) se mueve al Sprint 3 (ver `03_BACKLOG.md`). **Decisión final de Jorge: Sprint 2 queda en 29 pts** (la tabla nunca incluyó a HU-045 en el subtotal; 32−3=29). Holgura sana de 1 pt bajo velocidad 30; no se incorpora HU adicional; el margen absorbe trabajo emergente (bugs, deploy Supabase).
4. **Deploy Supabase → fase de deploy:** cuando haya acceso al proyecto Supabase — (a) ejecutar `V001__unique_tenant_nombre.sql` y `V002__unique_plan_nombre.sql` **en orden**; (b) configurar el connection string del DAL SaaS con rol `service_role` (BYPASSRLS) para `ObtenerConteosUsoAsync` (DAL-P8); (c) verificar que las políticas RLS de `plan` permitan lectura al rol `anon`/`authenticated` según el flujo de login. El equipo sigue desarrollando contra Postgres 15 local (Docker) con las migraciones aplicadas localmente.
5. **Serialización JSON de auditoría:** se crea **ADR-003** (estandarizar `UnsafeRelaxedJsonEscaping` en todos los servicios que emiten JSON de auditoría), implementado a partir de HU-003 en `UsuarioService`. NO se parchea HU-003 con comentarios inline; el ADR documenta el patrón antes de que llegue a servicios de ciclo/área.

---

## Decisiones de Jorge — 2026-09-20 (Sprint 3)

**Contexto:** Al iniciar la fase de diseño para `objetivo_cg` (HU-017), se evaluó si este debía ser manejado mediante el `CicloRepository` (como las demás entidades del ciclo del Sprint 1 y 2). 

1. **Punto de Inflexión Arquitectónica (Repositorios para Entidades Estratégicas):** Se decide no extender `CicloRepository` para evitar que se convierta en un *God Object*. A partir de la Épica EP-06 (Objetivos CG, OKRs, Acciones, Presupuestos), cada entidad de negocio con un CRUD completo independiente tendrá su propio repositorio dedicado (`IObjetivoCgRepository`, `IOkrRepository`, etc.). Esta decisión establece el nuevo patrón de acceso a datos para las capas estratégicas y operativas del sistema.

---

## Cierre HU-045 y Sprint 3 — 2026-09-20

**Contexto:** HU-045 (UI de Gestión de Tenants — alcance ampliado) se cerró el 2026-09-20 con todos los criterios de Done cumplidos. Con este cierre, el **Sprint 3 queda completado (6/6 HU)**.

1. **HU-045 Implementada (spec → `Implementado`):** cimiento de frontend (ApiClient con JWT + refresh 401 + desenvolvimiento `ApiResponse<T>` · SesionService · AuthController MVC · Layout DS) + **15 vistas diferidas** de HU-006..HU-020 + migraciones V000–V004 ejecutadas y verificadas en **Supabase Cloud (PostgreSQL 17.6)**. Tests: **553/553 en verde** (27 TDD + 22 humo + regresión HU-001..HU-020). Cobertura BLL sin cambios (88.9%).
2. **ADR-009 creado** (dos decisiones): (1) cimiento de frontend dentro de HU-045 — aprobada por Jorge 2026-09-20, no se crea HU-046, desviación ~30 pts efectivos en ticket de 3 (nota en backlog); (2) funciones auxiliares de RLS en schema `public` en lugar de `auth.*` — aceptada por @Orquestador (Supabase restringe CREATE en `auth`, 42501 permission denied; comportamiento idéntico, leen `request.jwt.claims`). Desviación documentada en el header de `V000__schema_base.sql`.
3. **Sprint 3 completado (6/6 HU):** HU-016..HU-020 y HU-045 Implementadas. Sprint 4 (Plan de Acción Completo — HU-021..HU-025) queda como siguiente sprint, con el cimiento de frontend como activo reutilizable para todas las UI futuras.

---

*HANDOFF PE-GOL SaaS · Generado: 2026-09-13 · Actualizado: 2026-09-20 (Sprint 3 completado — 6/6 HU) · Conversación origen: Análisis y Diseño completo*
*Siguiente conversación recomendada: Continuar con el Loop de HU-021 (@QA, TDD) — Sprint 4*

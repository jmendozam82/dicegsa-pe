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
| HU-006 | Configuración de Empresa (Tenant) | 3 | @Arquitecto → @BackendDev + @FrontendDev |
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

## Decisiones de Jorge — 2026-09-13 (resuelven flags del Sprint 1)

**Contexto:** Jorge resolvió los 5 puntos bloqueantes del Sprint 1 señalados como flags en la sección HU-002. Ninguna decisión obliga a cambios de esquema ni de seed; la única pieza nueva es el **ADR-003** (aplicable a partir de HU-003).

1. **Tope real de áreas (flag #1):** NO se emite V003. El seed ya es correcto (`06_MODELO_DATOS.md` L583-586: Básico=5, Estándar=10, Premium=20; verificado por @Orquestador). El `DEFAULT 10` del DDL (L55) es intencional y no obliga a ningún plan — cada plan declara su valor explícito. El desfase era solo aparente (datos correctos; sin cambios de esquema ni seed).
2. **Definición de roles (glosario — desbloquea HU-003):** `rol_usuario` tiene 4 roles distintos: `SuperAdmin` (SaaS-level, NO cuenta en `max_usuarios`, sin `tenant_id`), `AdminTenant`/ADM (administra el tenant → gestiona usuarios internos, configura ciclos; SÍ cuenta), `Gerente`/GER (usuario operativo: consolidados de todas las áreas, dashboards y reportes; NO administra el tenant; SÍ cuenta), `JefeArea` (responsable de área; SÍ cuenta). "Usuario que ocupa plaza" (D5 HU-002 resuelto) = todos los usuarios con `tenant_id` (AdminTenant+Gerente+JefeArea), incl. Inactivo/Bloqueado. Aplica desde HU-002 (DAL-P8) y se manifiesta en HU-003.
3. **Capacidad Sprint 2:** HU-045 (UI Gestión de Tenants, 3 pts) se mueve al Sprint 3 (ver `03_BACKLOG.md`). **Decisión final de Jorge: Sprint 2 queda en 29 pts** (la tabla nunca incluyó a HU-045 en el subtotal; 32−3=29). Holgura sana de 1 pt bajo velocidad 30; no se incorpora HU adicional; el margen absorbe trabajo emergente (bugs, deploy Supabase).
4. **Deploy Supabase → fase de deploy:** cuando haya acceso al proyecto Supabase — (a) ejecutar `V001__unique_tenant_nombre.sql` y `V002__unique_plan_nombre.sql` **en orden**; (b) configurar el connection string del DAL SaaS con rol `service_role` (BYPASSRLS) para `ObtenerConteosUsoAsync` (DAL-P8); (c) verificar que las políticas RLS de `plan` permitan lectura al rol `anon`/`authenticated` según el flujo de login. El equipo sigue desarrollando contra Postgres 15 local (Docker) con las migraciones aplicadas localmente.
5. **Serialización JSON de auditoría:** se crea **ADR-003** (estandarizar `UnsafeRelaxedJsonEscaping` en todos los servicios que emiten JSON de auditoría), implementado a partir de HU-003 en `UsuarioService`. NO se parchea HU-003 con comentarios inline; el ADR documenta el patrón antes de que llegue a servicios de ciclo/área.

---

*HANDOFF PE-GOL SaaS · Generado: 2026-09-13 · Conversación origen: Análisis y Diseño completo*
*Siguiente conversación recomendada: Sprint 1 — Implementación HU-001 (fase 4 · @BackendDev)*

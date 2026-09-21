# AGENTS.md — Constitución del Proyecto PE-GOL SaaS
> Este archivo es la fuente de verdad del proyecto. Todo agente, spec, ADR y plan debe leerlo antes de actuar.
> Versión: 1.20 · Fecha: 2026-09-20 · Autor: Jorge (Dicegsa)
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
- **[STACK-07]** El Gantt del Plan de Acción **deberá** implementarse con **DHTMLX Gantt** (versión Open Source).
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
| Specs Sprint 3 | `specs/sprint-03/*.spec.md` | ⏳ En curso — HU-016..HU-020 Implementadas. |
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

*AGENTS.md — Constitución PE-GOL SaaS · Versión 1.20 · 2026-09-20*
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
*Toda modificación a este archivo requiere consenso del equipo y bump de versión.*

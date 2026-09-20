# 📦 Backlog — PE-GOL SaaS
## Épicas · Historias de Usuario · Sprint Planning
### Stack: ASP.NET Core .NET 8 + Supabase · Metodología: Scrum · Versión 1.0

---

## Convenciones

| Campo | Descripción |
|-------|-------------|
| **ID** | HU-XXX — numeración secuencial global |
| **Como** | Rol que ejecuta la acción |
| **Quiero** | Funcionalidad deseada |
| **Para** | Valor de negocio obtenido |
| **Pts** | Story Points (Fibonacci: 1 · 2 · 3 · 5 · 8 · 13) |
| **Prioridad** | Alta / Media / Baja |
| **Sprint** | Sprint asignado |

**Roles:**
- **SA** — Super Admin SaaS
- **ADM** — Administrador Tenant
- **GER** — Gerente
- **JEF** — Jefe de Área

**Velocidad estimada del equipo:** 30 pts/sprint · Sprints de 2 semanas

---

## EP-01 · Administración SaaS y Tenants

> Gestión centralizada de la plataforma: tenants, planes, usuarios globales y auditoría.

### HU-001 — Gestión de Tenants
**Como** SA  
**Quiero** crear, editar, activar y desactivar Tenants (Gerencias) con nombre, descripción, plan de suscripción y estado  
**Para** administrar las gerencias que usan la plataforma

**Criterios de Aceptación:**
- [ ] Formulario con: nombre, descripción, plan (Básico / Estándar / Premium), estado (Activo/Inactivo)
- [ ] Listado paginado de tenants con filtro por estado y plan
- [ ] Al desactivar un tenant, sus usuarios no pueden iniciar sesión
- [ ] Validación: nombre único en la plataforma
- [ ] Log de auditoría registra create/update/deactivate

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 1

---

### HU-002 — Gestión de Planes de Suscripción
**Como** SA  
**Quiero** definir planes de suscripción con límites de áreas, usuarios y ciclos activos  
**Para** controlar la capacidad asignada a cada tenant

**Criterios de Aceptación:**
- [ ] Catálogo de planes con: nombre, max_areas, max_usuarios, max_ciclos_activos
- [ ] El sistema valida los límites del plan al momento de crear áreas o usuarios en el tenant
- [ ] El SA puede asignar o cambiar el plan de un tenant existente

**Pts:** 3 · **Prioridad:** Media · **Sprint:** 1

---

### HU-003 — Gestión de Usuarios Globales y Roles
**Como** SA  
**Quiero** crear usuarios Administrador Tenant y asignarlos a un tenant específico  
**Para** delegar la administración interna de cada gerencia

**Criterios de Aceptación:**
- [ ] Crear usuario con: nombre, correo, contraseña temporal, rol (Admin Tenant), tenant asignado
- [ ] El usuario ADM solo ve y gestiona su tenant; nunca ve otros tenants
- [ ] Correo de bienvenida con enlace de activación y cambio de contraseña obligatorio en primer inicio
- [ ] El SA puede desactivar cualquier usuario

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 1

---

### HU-004 — Autenticación y Seguridad
**Como** cualquier usuario  
**Quiero** iniciar sesión con correo y contraseña, recibir un JWT y mantener mi sesión con refresh token  
**Para** acceder de forma segura a las funciones de mi rol

**Criterios de Aceptación:**
- [ ] Login con correo + contraseña; respuesta JWT (60 min) + refresh token (7 días)
- [ ] Bloqueo de cuenta tras 5 intentos fallidos por 15 minutos
- [ ] Endpoint `/auth/refresh` renueva el JWT sin requerir contraseña
- [ ] Logout invalida el refresh token activo
- [ ] Contraseñas almacenadas con BCrypt (coste ≥ 12)

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 1

---

### HU-005 — Log de Auditoría
**Como** SA  
**Quiero** visualizar un log de auditoría con usuario, acción, entidad, tenant y fecha/hora  
**Para** rastrear cambios críticos en la plataforma

**Criterios de Aceptación:**
- [ ] Todas las operaciones de escritura (POST/PUT/DELETE) generan entrada en log
- [ ] Vista de log con filtros: tenant, usuario, rango de fechas, tipo de acción
- [ ] Log es inmutable: ningún rol puede editar o eliminar entradas
- [ ] Retención mínima de 90 días

**Pts:** 3 · **Prioridad:** Media · **Sprint:** 2

---

### HU-045 — Administración SaaS: UI de Gestión de Tenants
**Como** SA  
**Quiero** una interfaz para gestionar tenants (listado paginado con filtros, formulario crear/editar, botones activar/desactivar)  
**Para** administrar las gerencias de la plataforma sin necesidad de API

**Criterios de Aceptación:**
- [ ] Listado paginado de tenants con filtros por estado y plan (consume GET /api/v1/tenants)
- [ ] Formulario create/edit con nombre, descripción, plan y estado
- [ ] Botones activar/desactivar con confirmación (consumen POST /api/v1/tenants/{id}/activar y /desactivar)
- [ ] Estados vacíos con componente `.empty-state` (UX-05) y tabla con clase `.tabla-pe` (UX-03)
- [ ] Validación client-side con jQuery Validate + mensajes de error del servidor visibles

**Origen:** gap detectado en spec HU-001 (CA #1 "Formulario") — el alcance de HU-001 es backend-only. Creada por decisión de Jorge (2026-09-13).
**Pts:** 3 · **Prioridad:** Media · **Sprint:** 3 (movida de Sprint 2 a Sprint 3 el 2026-09-13 por decisión de Jorge — no asumir sobrecapacidad; Sprint 2 queda en 29 pts, ver nota de planificación)

---

**Subtotal EP-01:** 24 pts · 6 HU

---

## EP-02 · Configuración del Tenant y Ciclo Anual

> El Administrador Tenant configura los parámetros de la empresa, crea ciclos anuales y gestiona el catálogo de áreas y responsables.

### HU-006 — Configuración de la Empresa (Tenant)
**Como** ADM  
**Quiero** configurar los datos globales de la empresa: nombre, slogan, logo y zona horaria  
**Para** personalizar la plataforma con la identidad de la gerencia

**Criterios de Aceptación:**
- [ ] Formulario con: nombre empresa, slogan, logo (PNG/JPG ≤ 2 MB), zona horaria
- [ ] Logo se almacena en bucket privado y se muestra en el encabezado de la app
- [ ] Cambios aplican inmediatamente para todos los usuarios del tenant
- [ ] Solo el rol ADM puede modificar estos datos

> **Nota (2026-09-14, decisión de Jorge):** UI diferida a HUs de frontend (HU-045 y siguientes) — HU-006 entregada **100% backend** en Sprint 1; la vista Razor de configuración de empresa se planificará con el cimiento de frontend. Pts y sprint sin cambios.

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 2

---

### HU-007 — Gestión de Ciclos Anuales
**Como** ADM  
**Quiero** crear y gestionar ciclos anuales con año fiscal, mes de inicio y estado  
**Para** organizar la planificación estratégica por período

**Criterios de Aceptación:**
- [ ] Crear ciclo con: nombre (ej: PE 2026), año fiscal, mes de inicio, estado (Borrador)
- [ ] No pueden existir dos ciclos con el mismo año fiscal en el mismo tenant
- [ ] El ADM puede activar un ciclo (pasa a estado Activo); solo un ciclo puede estar Activo a la vez
- [ ] El GER puede cerrar el ciclo activo (pasa a Cerrado → solo lectura)
- [ ] Clonar ciclo anterior: copia áreas, responsables y umbrales al nuevo ciclo

> **Nota (2026-09-14, decisión de Jorge):** UI diferida a HUs de frontend (HU-045 y siguientes) — HU-007 se entrega **100% backend** en Sprint 1; la vista Razor de gestión de ciclos se planificará con el cimiento de frontend. Pts y sprint sin cambios.

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 2

---

### HU-008 — Configuración de Umbrales de Semáforo
**Como** ADM  
**Quiero** configurar los umbrales de semáforo para KPIs y Plan de Acción por ciclo  
**Para** adaptar los criterios de alerta a las metas de cada período

**Criterios de Aceptación:**
- [ ] Configurar umbrales independientes para: KPIs y Plan de Acción
- [ ] Campos por categoría: umbral verde (≥ X), umbral amarillo (≥ Y), rojo (< Y)
- [ ] Valores deben estar en rango 0.0–1.0 y verde > amarillo obligatoriamente
- [ ] Valores por defecto al crear ciclo: Verde ≥ 0.9 · Amarillo ≥ 0.7
- [ ] No modificables una vez el ciclo pasa a estado Activo

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 2

---

### HU-009 — Gestión de Áreas Estratégicas
**Como** ADM  
**Quiero** registrar, editar y desactivar las Áreas Estratégicas del ciclo  
**Para** estructurar las unidades operativas que participan en el plan

**Criterios de Aceptación:**
- [ ] Crear área con: código GOL (auto-generado secuencial), nombre, comentarios opcionales
- [ ] Asignar un Jefe de Área responsable del catálogo de responsables
- [ ] Validar que un responsable no esté asignado a más de un área en el mismo ciclo
- [ ] Límite de áreas según plan de suscripción del tenant
- [ ] Al desactivar un área, sus datos permanecen en solo lectura

> **Nota (2026-09-14):** la clonación de áreas del CA #5 de HU-007 se completa en esta HU (contrato `ClonarAsync` de HU-007 queda abierto para extenderlo).

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 2

---

### HU-010 — Gestión de Responsables (Jefes de Área)
**Como** ADM  
**Quiero** crear y gestionar el catálogo de responsables del ciclo con nombre, correo y rol Jefe de Área  
**Para** habilitar a los jefes para que ingresen datos de su área en la plataforma

**Criterios de Aceptación:**
- [ ] Crear usuario Jefe de Área con: nombre, correo, área asignada
- [ ] Sistema envía correo de activación con contraseña temporal
- [ ] El Jefe solo puede acceder a los módulos de su área asignada
- [ ] El ADM puede reasignar un responsable a otra área (si el área origen queda sin responsable, el sistema advierte)
- [ ] Listado de responsables con estado (activo/inactivo) y área asignada

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 3

---

**Subtotal EP-02:** 21 pts · 5 HU

---

## EP-03 · Filosofía Corporativa

> El Gerente define la Visión, Misión y Valores que enmarcan el plan estratégico del ciclo.

### HU-011 — Registro de Visión y Misión
**Como** GER  
**Quiero** redactar y editar la Visión y Misión de la gerencia para el ciclo activo  
**Para** establecer el norte estratégico que orienta todos los objetivos del año

**Criterios de Aceptación:**
- [ ] Editor de texto con formato básico (negrita, cursiva, listas) para Visión y Misión
- [ ] Ambos campos son obligatorios para activar el ciclo
- [ ] Los Jefes de Área pueden leer la Visión y Misión pero no editarlas
- [ ] Cambios se registran en log de auditoría con versión anterior

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 2

---

### HU-012 — Gestión de Valores Corporativos
**Como** GER  
**Quiero** registrar, ordenar y eliminar los Valores Corporativos de la gerencia para el ciclo  
**Para** comunicar los principios que guían la actuación de todas las áreas

**Criterios de Aceptación:**
- [ ] Lista de valores con campo de texto por valor (ej: "Liderazgo", "Excelencia")
- [ ] Agregar y eliminar valores individualmente
- [ ] Reordenar valores mediante drag-and-drop o flechas de orden
- [ ] Mínimo 1 valor requerido; máximo 15
- [ ] Visualización en modo lectura para Jefes de Área

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 2

---

**Subtotal EP-03:** 6 pts · 2 HU

---

## EP-04 · Pilares Estratégicos Corporativos

> El Gerente define los Pilares Estratégicos que agrupan y enmarcan los objetivos de todas las áreas.

### HU-013 — CRUD de Pilares Estratégicos
**Como** GER  
**Quiero** crear, editar y eliminar Pilares Estratégicos con código, nombre y estrategia de victoria  
**Para** definir los ejes temáticos que estructuran el plan estratégico corporativo

**Criterios de Aceptación:**
- [ ] Crear pilar con: código auto-generado (PEC-N), nombre, descripción de estrategia de victoria
- [ ] Máximo 8 pilares por ciclo
- [ ] No se puede eliminar un pilar si tiene Objetivos CG o OKRs asociados
- [ ] Visualización de pilares en modo lectura para Jefes de Área
- [ ] Listado con indicador de cuántos CG y OKRs tiene cada pilar

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 3

---

### HU-014 — Objetivos de Área por Trimestre en Pilares
**Como** GER  
**Quiero** asociar a cada pilar los objetivos esperados por trimestre (Q1–Q4) como texto de referencia  
**Para** comunicar a las áreas qué se espera de ellas en cada período del año

**Criterios de Aceptación:**
- [ ] Por cada pilar, campos de texto libre para Q1, Q2, Q3 y Q4
- [ ] Los trimestres son opcionales; no todos deben tener contenido
- [ ] Visible en modo lectura para Jefes de Área como guía al crear sus CGs
- [ ] Se muestran en la pantalla de creación de Objetivos CG como referencia contextual

**Pts:** 2 · **Prioridad:** Media · **Sprint:** 4

---

**Subtotal EP-04:** 5 pts · 2 HU

---

## EP-05 · Áreas Estratégicas — Vista del Jefe

> El Jefe de Área accede a su tablero de área y navega hacia todos los módulos que le corresponden.

### HU-015 — Tablero de Inicio del Jefe de Área
**Como** JEF  
**Quiero** ver al ingresar un tablero resumen de mi área con el estado actual de OKRs, acciones y presupuesto  
**Para** tener visibilidad inmediata del avance de mi área sin navegar entre módulos

**Criterios de Aceptación:**
- [ ] Tarjetas de resumen: Total OKRs · OKRs alcanzados · % avance plan de acción · acciones atrasadas · días al vencimiento más cercano
- [ ] Semáforo global del área (calculado como promedio de OKRs y plan de acción)
- [ ] Acceso directo desde el tablero a: mis Objetivos CG, Plan de Acción, OKRs, CAPEX, OPEX
- [ ] El tablero solo muestra datos del ciclo activo

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 4

---

### HU-016 — Tablero de Inicio del Gerente
**Como** GER  
**Quiero** ver al ingresar un tablero consolidado con el estado de todas las áreas del ciclo activo  
**Para** tener visibilidad ejecutiva del avance global sin necesidad de revisar área por área

**Criterios de Aceptación:**
- [ ] Panel por área con: nombre, semáforo global, % avance OKRs, % avance plan de acción
- [ ] Totales consolidados del ciclo: total acciones, acciones atrasadas, promedio OKRs
- [ ] Click en un área lleva al detalle de esa área (drill-down)
- [ ] Indicador de áreas con alertas activas (color rojo con cantidad)

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 4

---

**Subtotal EP-05:** 10 pts · 2 HU

---

## EP-06 · Componentes Estratégicos — Objetivos CG

> Cada Jefe de Área define los Objetivos Estratégicos (CG) de su área, alineados a los Pilares Corporativos.

### HU-017 — CRUD de Objetivos CG
**Como** JEF  
**Quiero** crear, editar y eliminar Objetivos CG de mi área con código, descripción, pilar asociado y trimestre objetivo  
**Para** registrar formalmente los compromisos estratégicos de mi área para el ciclo

**Criterios de Aceptación:**
- [ ] Código auto-generado: `GOLn.CGm` (n = número área, m = secuencial del objetivo)
- [ ] Campos obligatorios: descripción completa, pilar estratégico asociado, trimestre objetivo (Q1–Q4)
- [ ] El sistema muestra los objetivos trimestrales del pilar seleccionado como referencia
- [ ] No se puede eliminar un CG que tenga acciones registradas en el Plan de Acción
- [ ] Listado de CGs del área con % avance y semáforo

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 4

---

### HU-018 — Visualización Consolidada de CGs (Gerente)
**Como** GER  
**Quiero** ver todos los Objetivos CG de todas las áreas en una vista consolidada  
**Para** revisar el panorama completo de compromisos estratégicos del ciclo

**Criterios de Aceptación:**
- [ ] Tabla agrupada por área con todos los CGs, % avance y semáforo
- [ ] Filtros por: área, pilar, trimestre, estado (semáforo)
- [ ] Exportable a Excel con todos los campos visibles
- [ ] Solo lectura para el Gerente en esta vista (la edición es responsabilidad del Jefe)

**Pts:** 3 · **Prioridad:** Alta · **Sprint:** 5

---

**Subtotal EP-06:** 8 pts · 2 HU

---

## EP-07 · Plan de Acción con Gantt

> Módulo central de captura operativa: cada acción tiene fechas, responsable, entregable, peso y seguimiento de progreso.

### HU-019 — CRUD de Acciones del Plan
**Como** JEF  
**Quiero** crear, editar y eliminar acciones dentro de cada Objetivo CG de mi área  
**Para** desglosar cada objetivo en pasos concretos con fechas y responsables

**Criterios de Aceptación:**
- [ ] Campos: descripción acción, descripción entregable, responsable, fecha inicio, fecha vencimiento, clasificación (Proyecto/Iniciativa/Operativa), tipo (OPEX/CAPEX), peso (%), aclaraciones
- [ ] Fecha vencimiento no puede ser anterior a fecha inicio
- [ ] Ambas fechas deben caer dentro del año fiscal del ciclo
- [ ] Validación: la suma de pesos de acciones del mismo CG debe ser ≤ 1.0 con advertencia si ≠ 1.0
- [ ] Código auto-generado de acción: `n.m.k` (área.objetivo.acción)

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 5

---

### HU-020 — Actualización de Progreso de Acciones
**Como** JEF  
**Quiero** actualizar el % de progreso de cada acción y ver el status calculado automáticamente  
**Para** reflejar el avance real de las actividades de mi área

**Criterios de Aceptación:**
- [ ] Campo % progreso: valor numérico 0–100 con validación de rango
- [ ] Status calculado automáticamente: No iniciado / En progreso / Terminado / Atrasado (según RN-017)
- [ ] % avance del CG se recalcula en tiempo real al guardar
- [ ] Historial de cambios de progreso: fecha, valor anterior, valor nuevo, usuario
- [ ] La acción muestra su status con semáforo de color

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 5

---

### HU-021 — Vista Gantt del Plan de Acción
**Como** JEF  
**Quiero** ver el Plan de Acción de mi área en formato Gantt con barras por mes  
**Para** identificar visualmente la distribución temporal de las acciones y los posibles cuellos de botella

**Criterios de Aceptación:**
- [ ] Gantt con escala mensual (12 meses del año fiscal)
- [ ] Barras coloreadas por status: Verde (Terminado), Amarillo (En progreso), Rojo (Atrasado), Gris (No iniciado)
- [ ] Agrupación por Objetivo CG
- [ ] Tooltip al hover con: nombre acción, fechas, % progreso, responsable
- [ ] Filtro por: CG, status, clasificación (Proyecto/Iniciativa/Operativa)

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 6

---

### HU-022 — Gestión de Entregables Adjuntos
**Como** JEF  
**Quiero** adjuntar archivos (PDF, DOCX, XLSX, PNG, JPG) a cada acción del plan  
**Para** evidenciar el cumplimiento de la acción con los documentos generados

**Criterios de Aceptación:**
- [ ] Subir hasta 5 archivos por acción; tamaño máximo 20 MB por archivo
- [ ] Tipos permitidos: PDF, DOCX, XLSX, PNG, JPG
- [ ] Lista de archivos adjuntos con: nombre, fecha de subida, usuario que subió, tamaño
- [ ] Descarga de archivos mediante URL firmada de acceso temporal (24 h)
- [ ] Eliminar adjunto con confirmación (solo quien lo subió o el GER)

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 6

---

### HU-023 — Vista Consolidada del Plan (Gerente)
**Como** GER  
**Quiero** ver el Plan de Acción consolidado de todas las áreas con filtros y conteos por status  
**Para** identificar rápidamente las áreas con más acciones atrasadas y los cuellos de botella del ciclo

**Criterios de Aceptación:**
- [ ] Tabla consolidada con todas las acciones de todas las áreas
- [ ] Filtros: área, CG, status, clasificación, tipo (OPEX/CAPEX), rango de fechas
- [ ] Resumen de conteo por status (No iniciado / En progreso / Terminado / Atrasado) en cabecera
- [ ] Exportable a Excel con todos los campos
- [ ] Vista de solo lectura para el Gerente

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 6

---

**Subtotal EP-07:** 31 pts · 5 HU

---

## EP-08 · OKRs — Seguimiento de Resultados Clave

> Cada Jefe registra OKRs con sus KRs ponderados y actualiza los valores reales mes a mes.

### HU-024 — CRUD de OKRs
**Como** JEF  
**Quiero** crear, editar y eliminar OKRs de mi área con pilar asociado y descripción del objetivo  
**Para** formalizar los resultados que mi área se compromete a alcanzar en el ciclo

**Criterios de Aceptación:**
- [ ] Código auto-generado: `OKR.N` (secuencial por área)
- [ ] Campos: descripción del objetivo, pilar estratégico asociado
- [ ] No se puede eliminar un OKR con KRs que tengan valores reales registrados
- [ ] Listado de OKRs del área con puntuación final y semáforo
- [ ] Máximo 9 OKRs por área por ciclo

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 7

---

### HU-025 — Gestión de Key Results (KRs)
**Como** JEF  
**Quiero** crear, editar y eliminar KRs dentro de cada OKR con descripción y peso ponderado  
**Para** definir las métricas concretas que evidenciarán el logro del objetivo

**Criterios de Aceptación:**
- [ ] Campos por KR: código (KR.N), descripción, peso (%)
- [ ] Entre 1 y 5 KRs por OKR
- [ ] Validación: suma de pesos de KRs del mismo OKR debe ser 1.0 (100%); error si difiere
- [ ] No se puede eliminar un KR con valores reales registrados en el ciclo activo

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 7

---

### HU-026 — Registro Mensual de Valores Reales de KRs
**Como** JEF  
**Quiero** ingresar el valor real mensual de cada KR (escala 0.0–1.0) para los 12 meses del año  
**Para** registrar el avance real de cada resultado clave mes a mes

**Criterios de Aceptación:**
- [ ] Grilla de 12 columnas (ENE–DIC) por KR; valor entre 0.0 y 1.0 con 1 decimal
- [ ] Solo se pueden editar los meses que ya transcurrieron o el mes actual
- [ ] Meses futuros bloqueados para edición (solo lectura)
- [ ] Cálculo automático de puntuación trimestral por KR al guardar
- [ ] Cálculo automático de puntuación final y ponderada del OKR

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 7

---

### HU-027 — Visualización Consolidada de OKRs (Gerente)
**Como** GER  
**Quiero** ver todos los OKRs de todas las áreas en una vista consolidada con puntuaciones y semáforos  
**Para** evaluar el desempeño estratégico global del ciclo

**Criterios de Aceptación:**
- [ ] Vista agrupada por área: OKR, descripción, pilar, puntuación final, semáforo
- [ ] Filtros: área, pilar, estado (Alcanzado / En Peligro / No Alcanzado)
- [ ] Resumen global: promedio de OKRs alcanzados, % por estado
- [ ] Exportable a Excel

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 8

---

**Subtotal EP-08:** 23 pts · 4 HU

---

## EP-09 · CAPEX — Presupuesto de Capital

> Cada Jefe registra los proyectos de inversión de su área con desembolso mensual planificado y real ejecutado.

### HU-028 — CRUD de Proyectos CAPEX
**Como** JEF  
**Quiero** crear, editar y eliminar proyectos CAPEX de mi área  
**Para** registrar las inversiones de capital planificadas y su justificación

**Criterios de Aceptación:**
- [ ] Campos: área de colaboración, colaborador, nombre del proyecto, impacto/justificación, presupuesto aprobado (C$)
- [ ] Presupuesto aprobado en Córdobas, entero positivo
- [ ] Listado de proyectos con: nombre, presupuesto aprobado, total planeado, total real, status, cumplimiento
- [ ] No se puede eliminar un proyecto con desembolsos reales registrados

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 8

---

### HU-029 — Registro de Desembolso CAPEX (Planeado y Real)
**Como** JEF  
**Quiero** ingresar el plan de desembolso mensual y el monto real ejecutado por mes de cada proyecto CAPEX  
**Para** controlar la ejecución presupuestal del capital de inversión de mi área

**Criterios de Aceptación:**
- [ ] Grilla: 12 meses con columnas Planeado y Real por mes, subtotales Q1–Q4 y total anual
- [ ] Total planeado no puede superar el presupuesto aprobado (advertencia al usuario)
- [ ] Status automático del proyecto: No iniciado / En proceso / Ejecutado / Atrasado (según RN-031)
- [ ] Cumplimiento automático: Cumple / No Cumple (según RN-032)
- [ ] Variación calculada por mes y acumulada: real − planeado

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 8

---

### HU-030 — Vista Consolidada CAPEX (Gerente)
**Como** GER  
**Quiero** ver el consolidado de todos los proyectos CAPEX de todas las áreas con totales y estados  
**Para** controlar la ejecución del presupuesto de capital del ciclo

**Criterios de Aceptación:**
- [ ] Tabla con todos los proyectos CAPEX agrupados por área
- [ ] Totales por trimestre y total anual de planeado vs. real
- [ ] Semáforo por proyecto según cumplimiento
- [ ] Filtros: área, status del proyecto, cumplimiento
- [ ] Exportable a Excel

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 9

---

**Subtotal EP-09:** 18 pts · 3 HU

---

## EP-10 · OPEX — Presupuesto Operativo

> Gestión de gastos operativos por cuentas y subcuentas, con memoria de cálculo para rubros de materiales.

### HU-031 — Gestión de Catálogo de Cuentas y Subcuentas OPEX
**Como** JEF  
**Quiero** crear y gestionar el catálogo de cuentas y subcuentas OPEX de mi área  
**Para** estructurar el presupuesto operativo según la clasificación contable de la gerencia

**Criterios de Aceptación:**
- [ ] Crear Cuenta (nivel 1) con: código, nombre
- [ ] Crear Subcuenta (nivel 2) bajo una Cuenta con: código, nombre, flag de Memoria de Cálculo (Sí/No)
- [ ] No se puede eliminar una Cuenta con subcuentas con valores registrados
- [ ] No se puede eliminar una Subcuenta con valores presupuestados o reales registrados
- [ ] El catálogo es reutilizable entre ciclos (se clona al crear un nuevo ciclo)

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 9

---

### HU-032 — Registro de Presupuesto y Real OPEX por Subcuenta
**Como** JEF  
**Quiero** ingresar el presupuesto mensual y el gasto real mensual por subcuenta OPEX  
**Para** hacer seguimiento del cumplimiento presupuestal operativo de mi área mes a mes

**Criterios de Aceptación:**
- [ ] Grilla: subcuentas en filas, 12 meses en columnas con campo Presupuesto y Real por mes
- [ ] Subtotales trimestrales (Q1–Q4) y total anual calculados automáticamente
- [ ] Totales por Cuenta calculados como suma de sus Subcuentas
- [ ] Variación = Real − Presupuesto mostrada en color: verde (ahorro), rojo (sobreejercicio)
- [ ] Totales del área: suma de todas las cuentas

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 9

---

### HU-033 — Memoria de Cálculo OPEX (Rubros de Materiales)
**Como** JEF  
**Quiero** registrar el detalle de rubros de materiales para las subcuentas con Memoria de Cálculo (Gastos de Oficina, Gastos de Limpieza)  
**Para** sustentar el monto presupuestado de esas subcuentas con un cálculo transparente por ítem

**Criterios de Aceptación:**
- [ ] Tabla de rubros por subcuenta: nombre del material, unidad de medida, cantidad, precio unitario (C$), total = cantidad × precio unitario
- [ ] Total de la subcuenta = suma de totales de todos sus rubros (fluye automáticamente al presupuesto de la subcuenta)
- [ ] Agregar, editar y eliminar rubros individualmente
- [ ] Mínimo 1 rubro requerido en subcuentas con Memoria de Cálculo activada
- [ ] Vista de memoria de cálculo disponible en los reportes como anexo

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 10

---

### HU-034 — Vista Consolidada OPEX (Gerente)
**Como** GER  
**Quiero** ver el consolidado OPEX de todas las áreas con totales por cuenta y comparativa presupuesto vs. real  
**Para** controlar la ejecución del presupuesto operativo del ciclo de forma global

**Criterios de Aceptación:**
- [ ] Vista agrupada por área → cuenta → subcuenta con presupuesto, real y variación por trimestre
- [ ] Totales por área y total general del ciclo
- [ ] Resaltar en rojo las subcuentas con sobreejercicio > 10%
- [ ] Exportable a Excel manteniendo la jerarquía de cuentas
- [ ] Drill-down a la Memoria de Cálculo de cada subcuenta desde la vista consolidada

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 10

---

**Subtotal EP-10:** 29 pts · 4 HU

---

## EP-11 · Reportes

> Generación de informes estructurados del Plan Estratégico y Plan de Acción, exportables en PDF y Excel.

### HU-035 — Informe del Plan Estratégico por Área
**Como** JEF y GER  
**Quiero** generar el Informe del Plan Estratégico de un área con Pilares, Objetivos CG, OKRs, KRs, puntajes y semáforos  
**Para** tener un documento formal de presentación del estado estratégico del área

**Criterios de Aceptación:**
- [ ] Secciones: Filosofía → Pilares → Objetivos CG por pilar → OKRs con KRs y puntajes → semáforos
- [ ] Semáforos visuales (Verde/Amarillo/Rojo) en cada indicador
- [ ] El Jefe ve solo su área; el Gerente elige cualquier área
- [ ] Exportable a PDF con formato de informe corporativo (logo, encabezado, fecha)

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 11

---

### HU-036 — Informe del Plan de Acción por Área
**Como** JEF y GER  
**Quiero** generar el Informe del Plan de Acción de un área con conteo de acciones por status y % de progreso por CG  
**Para** evidenciar el avance operativo del área en un documento formal

**Criterios de Aceptación:**
- [ ] Secciones por Objetivo CG: acciones con status, % progreso, responsable, fechas, entregables adjuntos listados
- [ ] Conteo de acciones por status en cabecera de cada CG
- [ ] % progreso del CG con semáforo
- [ ] Exportable a PDF y a Excel
- [ ] El Jefe ve solo su área; el Gerente elige cualquier área

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 11

---

### HU-037 — Informes Consolidados (Gerente)
**Como** GER  
**Quiero** generar versiones consolidadas del Informe Plan Estratégico y del Informe Plan de Acción que integren todas las áreas  
**Para** presentar a la alta dirección el estado global del ciclo estratégico

**Criterios de Aceptación:**
- [ ] Informe consolidado: todas las áreas en un solo documento, organizadas por sección
- [ ] Resumen ejecutivo en primera página: métricas globales, áreas en peligro, acciones atrasadas
- [ ] Exportable a PDF (un solo archivo con todas las áreas)
- [ ] Exportable a Excel con hoja por área y hoja de resumen global

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 12

---

**Subtotal EP-11:** 24 pts · 3 HU

---

## EP-12 · Dashboards

> Visualización ejecutiva del estado del ciclo para el Jefe de Área y el Gerente.

### HU-038 — Dashboard del Jefe de Área
**Como** JEF  
**Quiero** ver un dashboard visual de mi área con métricas de OKRs, Plan de Acción y Presupuesto  
**Para** monitorear el estado de mi área en tiempo real desde un solo lugar

**Criterios de Aceptación:**
- [ ] Tarjetas de métricas: Total OKRs · OKRs alcanzados · % OKRs alcanzados · OKRs en peligro · Total acciones · Progreso promedio · Acciones atrasadas · % atrasadas · Días al vencimiento próximo
- [ ] Gráfica de barras: acciones por status
- [ ] Gráfica circular: OKRs Alcanzado / En Peligro / No Alcanzado
- [ ] Gráfica de barras horizontales: % avance por Objetivo CG
- [ ] Semáforos de color en todas las métricas según umbrales configurados
- [ ] Actualización automática de datos al ingresar a la vista

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 12

---

### HU-039 — Dashboard Consolidado del Gerente
**Como** GER  
**Quiero** ver un dashboard consolidado con el estado de todas las áreas del ciclo  
**Para** tomar decisiones ejecutivas basadas en el estado real de cada área

**Criterios de Aceptación:**
- [ ] Panel por área: nombre, semáforo global, % OKRs, % plan de acción, acciones atrasadas
- [ ] Métricas globales del ciclo: total acciones, total atrasadas, promedio OKRs global
- [ ] Gráfica comparativa de avance OKRs por área (barras agrupadas)
- [ ] Gráfica de progreso del Plan de Acción por área
- [ ] Drill-down: clic en área lleva al dashboard individual del área (solo lectura para el GER)
- [ ] Panel de alertas activas en la parte superior (áreas con rojo)

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 13

---

### HU-040 — Dashboard OKRs (Detalle por Área)
**Como** GER y JEF  
**Quiero** ver un dashboard específico de OKRs con progreso mensual por KR y puntuación ponderada  
**Para** hacer seguimiento fino del desempeño de cada resultado clave a lo largo del año

**Criterios de Aceptación:**
- [ ] Tabla por OKR con KRs: valor de cada mes, puntuación trimestral, puntuación final, ponderada
- [ ] Gráfica de línea: evolución mensual de la puntuación del OKR a lo largo del año
- [ ] Semáforo por KR y por OKR
- [ ] Escala de revisión visible (0.0–1.0)
- [ ] El Jefe ve solo su área; el GER puede seleccionar cualquier área

**Pts:** 8 · **Prioridad:** Alta · **Sprint:** 13

---

**Subtotal EP-12:** 24 pts · 3 HU

---

## EP-13 · Notificaciones y Alertas

> Sistema de alertas automáticas por acciones atrasadas y OKRs en peligro, más notificaciones in-app.

### HU-041 — Alertas por Acciones Atrasadas
**Como** JEF  
**Quiero** recibir un correo electrónico automático cuando una de mis acciones pase a estado Atrasado  
**Para** ser notificado oportunamente y tomar acción correctiva sin depender de revisar el sistema

**Criterios de Aceptación:**
- [ ] Proceso batch diario (nocturno) detecta acciones que pasaron a Atrasado en las últimas 24 h
- [ ] Correo enviado al Jefe responsable con: nombre acción, CG asociado, fecha vencimiento, % progreso actual
- [ ] La alerta se envía una sola vez por acción (no se repite cada día)
- [ ] Si el Jefe actualiza el progreso y la acción sale de Atrasado, se puede re-alertar si vuelve a atrasar

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 14

---

### HU-042 — Alertas por OKRs en Peligro
**Como** JEF y GER  
**Quiero** recibir un correo cuando la puntuación de un OKR caiga por debajo del umbral amarillo  
**Para** identificar y atender a tiempo los objetivos estratégicos que están en riesgo

**Criterios de Aceptación:**
- [ ] Proceso batch mensual (al cierre de cada mes) evalúa la puntuación acumulada de OKRs
- [ ] Correo al Jefe del área y con copia al Gerente: nombre OKR, puntuación actual, umbral, área
- [ ] No se reenvía la alerta si el OKR sigue en peligro el mes siguiente (evitar spam)
- [ ] El Gerente puede ver el historial de alertas enviadas por ciclo

**Pts:** 5 · **Prioridad:** Alta · **Sprint:** 14

---

### HU-043 — Resumen Periódico al Gerente
**Como** GER  
**Quiero** recibir un correo semanal con el resumen consolidado del estado de todas las áreas  
**Para** mantenerme informado del avance global del ciclo sin necesidad de ingresar al sistema

**Criterios de Aceptación:**
- [ ] Envío automático cada lunes (o quincenal según configuración del ciclo)
- [ ] Contenido: por área — % avance plan de acción, OKRs en peligro, acciones atrasadas
- [ ] Resumen global al inicio del correo
- [ ] El Gerente puede activar/desactivar este resumen en su perfil
- [ ] Configuración de frecuencia (semanal/quincenal) gestionada por el ADM en la configuración del ciclo

**Pts:** 5 · **Prioridad:** Media · **Sprint:** 14

---

### HU-044 — Notificaciones In-App
**Como** GER  
**Quiero** ver notificaciones dentro de la plataforma cuando un Jefe actualiza el progreso de una acción o un OKR  
**Para** estar al tanto de los cambios sin esperar el correo semanal

**Criterios de Aceptación:**
- [ ] Ícono de campana en el encabezado con contador de notificaciones no leídas
- [ ] Lista de notificaciones con: tipo, descripción, área, fecha, enlace directo al elemento
- [ ] Marcar como leída individualmente o "marcar todas como leídas"
- [ ] Notificaciones se eliminan automáticamente después de 30 días
- [ ] El Jefe recibe notificación in-app cuando el Gerente agrega un comentario en su área

**Pts:** 5 · **Prioridad:** Media · **Sprint:** 14

---

**Subtotal EP-13:** 20 pts · 4 HU

---

## Resumen del Backlog

| Épica | HUs | Pts | Prioridad |
|-------|-----|-----|-----------|
| EP-01 Administración SaaS | 6 | 24 | Alta |
| EP-02 Configuración Tenant y Ciclo | 5 | 21 | Alta |
| EP-03 Filosofía Corporativa | 2 | 6 | Alta |
| EP-04 Pilares Estratégicos | 2 | 5 | Alta |
| EP-05 Tableros de Inicio | 2 | 10 | Alta |
| EP-06 Objetivos CG | 2 | 8 | Alta |
| EP-07 Plan de Acción + Gantt | 5 | 31 | Alta |
| EP-08 OKRs / KRs | 4 | 23 | Alta |
| EP-09 CAPEX | 3 | 18 | Alta |
| EP-10 OPEX + Memoria de Cálculo | 4 | 29 | Alta |
| EP-11 Reportes | 3 | 24 | Alta |
| EP-12 Dashboards | 3 | 24 | Alta |
| EP-13 Alertas y Notificaciones | 4 | 20 | Alta/Media |
| **TOTAL** | **45 HU** | **243 pts** | — |

---

## Sprint Planning

> Velocidad: **30 pts/sprint** · Duración: **2 semanas** · Total sprints: **9**

---

### 🟢 Sprint 1 — Fundación SaaS
**Objetivo:** Plataforma operativa con tenants, usuarios y autenticación segura.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-001 | Gestión de Tenants | 5 |
| HU-002 | Planes de Suscripción | 3 |
| HU-003 | Usuarios y Roles (Super Admin) | 5 |
| HU-004 | Autenticación JWT | 5 |
| HU-006 | Configuración de Empresa (Tenant) | 3 |
| HU-007 | Gestión de Ciclos Anuales | 5 |
| HU-008 | Umbrales de Semáforo | 3 |
| **Total** | | **29 pts** |

---

### 🟢 Sprint 2 — Estructura Organizacional del Ciclo
**Objetivo:** ADM puede configurar áreas, responsables y estructura completa del ciclo.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-005 | Log de Auditoría | 3 |
| HU-009 | Gestión de Áreas Estratégicas | 5 |
| HU-010 | Gestión de Responsables | 5 |
| HU-011 | Visión y Misión | 3 |
| HU-012 | Valores Corporativos | 3 |
| HU-013 | CRUD Pilares Estratégicos | 3 |
| HU-014 | Objetivos Trimestrales en Pilares | 2 |
| HU-015 | Tablero de Inicio Jefe de Área | 5 |
| **Total** | | **29 pts** |

---

### 🟢 Sprint 3 — Objetivos Estratégicos y Tableros
**Objetivo:** Jefe puede definir sus Objetivos CG y el Gerente tiene su vista consolidada.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-016 | Tablero de Inicio Gerente | 5 |
| HU-017 | CRUD Objetivos CG | 5 |
| HU-018 | Vista Consolidada CGs (Gerente) | 3 |
| HU-019 | CRUD Acciones del Plan | 8 |
| HU-020 | Actualización de Progreso | 5 |
| HU-045 | UI de Gestión de Tenants (EP-01) | 3 |
| **Total** | | **29 pts** |

---

### 🟢 Sprint 4 — Plan de Acción Completo
**Objetivo:** Plan de Acción totalmente operativo con Gantt, entregables y vista consolidada.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-021 | Vista Gantt | 8 |
| HU-022 | Entregables Adjuntos | 5 |
| HU-023 | Vista Consolidada Plan (Gerente) | 5 |
| HU-024 | CRUD OKRs | 5 |
| HU-025 | Gestión de KRs | 5 |
| **Total** | | **28 pts** |

---

### 🟢 Sprint 5 — OKRs y Seguimiento
**Objetivo:** Sistema de OKRs completamente funcional con seguimiento mensual y consolidado gerencial.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-026 | Registro Mensual Valores KRs | 8 |
| HU-027 | Vista Consolidada OKRs (Gerente) | 5 |
| HU-028 | CRUD Proyectos CAPEX | 5 |
| HU-029 | Registro Desembolso CAPEX | 8 |
| **Total** | | **26 pts** |

---

### 🟢 Sprint 6 — CAPEX y OPEX Base
**Objetivo:** Módulos de presupuesto de capital y operativo funcionales para captura.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-030 | Vista Consolidada CAPEX (Gerente) | 5 |
| HU-031 | Catálogo Cuentas y Subcuentas OPEX | 5 |
| HU-032 | Presupuesto y Real OPEX | 8 |
| HU-033 | Memoria de Cálculo OPEX | 8 |
| **Total** | | **26 pts** |

---

### 🟢 Sprint 7 — OPEX Consolidado y Reportes
**Objetivo:** OPEX consolidado y primeros informes exportables.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-034 | Vista Consolidada OPEX (Gerente) | 8 |
| HU-035 | Informe Plan Estratégico por Área | 8 |
| HU-036 | Informe Plan de Acción por Área | 8 |
| **Total** | | **24 pts** |

---

### 🟢 Sprint 8 — Reportes Consolidados y Dashboards
**Objetivo:** Informes consolidados y dashboards visuales operativos.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-037 | Informes Consolidados (Gerente) | 8 |
| HU-038 | Dashboard Jefe de Área | 8 |
| HU-039 | Dashboard Consolidado Gerente | 8 |
| **Total** | | **24 pts** |

---

### 🟢 Sprint 9 — Dashboard OKRs y Alertas
**Objetivo:** Dashboard OKRs detallado y sistema completo de alertas y notificaciones.

| HU | Descripción | Pts |
|----|-------------|-----|
| HU-040 | Dashboard OKRs Detalle | 8 |
| HU-041 | Alertas Acciones Atrasadas | 5 |
| HU-042 | Alertas OKRs en Peligro | 5 |
| HU-043 | Resumen Periódico Gerente | 5 |
| HU-044 | Notificaciones In-App | 5 |
| **Total** | | **28 pts** |

---

## Resumen del Sprint Planning

| Sprint | Objetivo Principal | HUs | Pts |
|--------|--------------------|-----|-----|
| Sprint 1 | Fundación SaaS | 7 | 29 |
| Sprint 2 | Estructura Organizacional | 8 | 29 |
| Sprint 3 | Objetivos y Tableros | 6 | 29 |
| Sprint 4 | Plan de Acción Completo | 5 | 28 |
| Sprint 5 | OKRs y Seguimiento | 4 | 26 |
| Sprint 6 | CAPEX y OPEX Base | 4 | 26 |
| Sprint 7 | OPEX Consolidado + Reportes | 3 | 24 |
| Sprint 8 | Reportes Consolidados + Dashboards | 3 | 24 |
| Sprint 9 | Dashboard OKRs + Alertas | 5 | 28 |
| **TOTAL** | | **45 HU** | **243 pts** |

**Duración estimada:** 9 sprints × 2 semanas = **18 semanas (~4.5 meses)**

---

*Documento generado el 13/09/2026 — Fase de Diseño.*
*Siguiente fase: Specs técnicas (endpoints, modelos de datos, validaciones) y ADRs por Sprint.*

> **Nota 2026-09-13 (decisión de Jorge):** HU-045 movida de Sprint 2 a Sprint 3 — no asumir sobrecapacidad. Ninguna HU del Sprint 2 depende de HU-045 (verificado; no es bloqueante). **Decisión final de Jorge: Sprint 2 queda en 29 pts** (8 HU) — holgura sana de 1 pt bajo velocidad 30; NO se incorpora ninguna HU adicional. El margen absorbe trabajo emergente (bugs arrastrados, deploy Supabase).

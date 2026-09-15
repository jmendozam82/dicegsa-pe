# 📑 Catálogo de Requerimientos — PE-GOL SaaS
## Sistema de Plan Estratégico y Presupuesto Multi-Tenant
### Empresa: Dicegsa · Stack: ASP.NET Core .NET 8 + Supabase · Versión 1.0

---

## Índice

1. [Requerimientos Funcionales (RF)](#1-requerimientos-funcionales)
2. [Requerimientos No Funcionales (RNF)](#2-requerimientos-no-funcionales)
3. [Reglas de Negocio (RN)](#3-reglas-de-negocio)
4. [Matriz de Trazabilidad](#4-matriz-de-trazabilidad-módulo--requerimiento)

---

## 1. Requerimientos Funcionales

### EP-01 · Administración SaaS y Tenants

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-001 | El sistema debe permitir crear, editar, activar y desactivar **Tenants** (Gerencias). Cada tenant representa una gerencia independiente con su propio espacio de datos aislado. | Alta |
| RF-002 | El sistema debe permitir crear **ciclos anuales** por tenant (ej: PE 2026, PE 2027). Un ciclo es la unidad de planificación anual que agrupa todos los módulos. | Alta |
| RF-003 | El sistema debe permitir registrar y gestionar **usuarios** con nombre, correo, contraseña y estado (activo/inactivo), asociados a un tenant. | Alta |
| RF-004 | El sistema debe implementar **control de acceso basado en roles (RBAC)** con al menos tres roles: Administrador SaaS, Gerente y Jefe de Área. | Alta |
| RF-005 | El sistema debe permitir al Administrador SaaS configurar el **plan de suscripción** por tenant (número máximo de áreas, usuarios y ciclos activos). | Media |
| RF-006 | El sistema debe registrar un **log de auditoría** con usuario, acción, entidad afectada, fecha y hora para todas las operaciones de escritura. | Media |

---

### EP-02 · Configuración del Ciclo Anual

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-007 | El sistema debe permitir al **AdminTenant** configurar los **parámetros globales del ciclo**: nombre del ciclo, año fiscal y mes de inicio (estructura del ciclo, HU-006/HU-007). El **Gerente** gestiona el contenido del ciclo (Filosofía: Visión, Misión y Valores, HU-011/HU-012). *(Actualizado 2026-09-14 por decisión de Jorge: el ADM gestiona la estructura del ciclo; el GER gestiona el contenido — ver spec HU-007 Flag #1.)* | Alta |
| RF-008 | El sistema debe permitir configurar los **umbrales de semáforo** para KPIs y Plan de Acción de forma independiente, definiendo los valores de corte para Verde, Amarillo y Rojo (expresados en escala 0.0–1.0). | Alta |
| RF-009 | El sistema debe permitir registrar y gestionar el **catálogo de Áreas Estratégicas** del ciclo, con campos: ID, nombre del área y responsable (Jefe de Área). | Alta |
| RF-010 | El sistema debe permitir registrar y gestionar el **catálogo de Responsables** (Jefes de Área) del ciclo, con nombre completo y correo electrónico. | Alta |
| RF-011 | El sistema debe permitir **clonar la configuración** de un ciclo anterior como punto de partida para un nuevo ciclo, incluyendo áreas, responsables y umbrales. | Media |

---

### EP-03 · Filosofía Corporativa

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-012 | El sistema debe permitir al Gerente registrar y editar la **Visión** de la gerencia para el ciclo activo, en formato texto enriquecido. | Alta |
| RF-013 | El sistema debe permitir al Gerente registrar y editar la **Misión** de la gerencia para el ciclo activo, en formato texto enriquecido. | Alta |
| RF-014 | El sistema debe permitir al Gerente registrar y gestionar la lista de **Valores corporativos** del ciclo (lista de ítems de texto). | Alta |

---

### EP-04 · Pilares Estratégicos Corporativos

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-015 | El sistema debe permitir al Gerente crear, editar y eliminar **Pilares Estratégicos**, con campos: código (PEC-N), nombre del pilar y estrategia de victoria (descripción). | Alta |
| RF-016 | El sistema debe permitir asociar a cada pilar sus **Objetivos de Área por Trimestre** (Q1, Q2, Q3, Q4) como texto de referencia. | Alta |
| RF-017 | El sistema debe mostrar los pilares estratégicos como **referencia de navegación** en los módulos de Objetivos CG y OKRs. | Media |

---

### EP-05 · Áreas Estratégicas (¿Dónde Jugaremos?)

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-018 | El sistema debe permitir al Gerente registrar las **Áreas Estratégicas** del ciclo con campos: código GOL, nombre del área y comentarios opcionales. | Alta |
| RF-019 | Cada área debe tener asignado un **Jefe de Área responsable** del catálogo de responsables del ciclo. | Alta |
| RF-020 | El sistema debe restringir el acceso de cada **Jefe de Área** exclusivamente a los módulos y datos de su área asignada, salvo los módulos de solo lectura de referencia corporativa. | Alta |

---

### EP-06 · Componentes Estratégicos — Objetivos CG (¿Cómo Ganaremos?)

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-021 | El sistema debe permitir al Jefe de Área crear, editar y eliminar **Objetivos CG** de su área, con campos: código auto-generado (GOLn.CGm), descripción completa, pilar estratégico asociado y trimestre objetivo (Q1–Q4). | Alta |
| RF-022 | El sistema debe calcular y mostrar el **% de avance acumulado** de cada objetivo CG en base al progreso ponderado de sus acciones del plan de acción. | Alta |
| RF-023 | El sistema debe mostrar el **semáforo de estado** de cada objetivo CG según los umbrales configurados. | Alta |
| RF-024 | El sistema debe mostrar en la vista del Gerente un **consolidado de todos los objetivos CG** de todas las áreas del ciclo. | Alta |

---

### EP-07 · Plan de Acción con Gantt

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-025 | El sistema debe permitir al Jefe de Área crear, editar y eliminar **acciones** dentro de cada objetivo CG, con los campos: descripción de la acción, descripción del entregable, responsable, fecha de inicio, fecha de vencimiento, clasificación (Proyecto / Iniciativa / Operativa), tipo presupuesto (OPEX / CAPEX), peso (%), aclaraciones. | Alta |
| RF-026 | El sistema debe permitir al Jefe de Área actualizar el **% de progreso** de cada acción (valor de 0 a 100%) y registrar el **status** automáticamente según reglas de negocio: No iniciado / En progreso / Terminado / Atrasado. | Alta |
| RF-027 | El sistema debe calcular automáticamente la **puntuación ponderada** de cada acción: `puntuación = peso × (% progreso / 100)`. | Alta |
| RF-028 | El sistema debe calcular automáticamente el **% de progreso general del objetivo CG** como suma de las puntuaciones ponderadas de sus acciones. | Alta |
| RF-029 | El sistema debe mostrar una **vista Gantt** de las acciones del plan, con barras de duración entre fecha de inicio y fecha de vencimiento, coloreadas por status. | Alta |
| RF-030 | El sistema debe permitir al Jefe de Área **adjuntar archivos entregables** a cada acción (PDF, DOCX, XLSX, imágenes), con nombre del archivo, fecha de subida y usuario que lo adjuntó. | Alta |
| RF-031 | El sistema debe mostrar el **conteo de acciones por status** (No iniciado / En progreso / Terminado / Atrasado) a nivel de objetivo CG y de área. | Media |
| RF-032 | El sistema debe mostrar el consolidado del Plan de Acción de **todas las áreas** visible solo para el Gerente. | Alta |

---

### EP-08 · OKRs — Seguimiento de Resultados Clave

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-033 | El sistema debe permitir al Jefe de Área crear, editar y eliminar **OKRs** de su área, con campos: código (OKR.N), pilar estratégico asociado, descripción del objetivo. | Alta |
| RF-034 | El sistema debe permitir registrar hasta **3 Key Results (KRs)** por OKR, con campos: código (KR.N), descripción, peso (%) — la suma de pesos de los KRs de un OKR debe ser 1.0. | Alta |
| RF-035 | El sistema debe permitir al Jefe de Área registrar el **valor real mensual** de cada KR (ENE–DIC), en escala 0.0 a 1.0 con incrementos de 0.1. | Alta |
| RF-036 | El sistema debe calcular automáticamente la **puntuación trimestral** de cada KR como el promedio de los meses del trimestre con valores ingresados. | Alta |
| RF-037 | El sistema debe calcular la **puntuación final** del KR y la **puntuación ponderada** del OKR: `puntuación ponderada = puntuación_final × peso_KR`. | Alta |
| RF-038 | El sistema debe calcular la **puntuación final del OKR** como suma de las puntuaciones ponderadas de sus KRs. | Alta |
| RF-039 | El sistema debe mostrar el **semáforo de estado** por KR y por OKR según umbrales configurados. | Alta |
| RF-040 | El sistema debe mostrar el consolidado de **todos los OKRs de todas las áreas** visible solo para el Gerente. | Alta |

---

### EP-09 · CAPEX — Presupuesto de Capital

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-041 | El sistema debe permitir al Jefe de Área crear, editar y eliminar **proyectos CAPEX** de su área, con campos: área de colaboración, colaborador, nombre del proyecto, impacto/justificación, presupuesto aprobado (en moneda local C$). | Alta |
| RF-042 | El sistema debe permitir registrar el **plan de desembolso mensual** del proyecto CAPEX (ENE–DIC) en montos en Córdobas, calculando automáticamente subtotales trimestrales (Q1–Q4) y total anual. | Alta |
| RF-043 | El sistema debe permitir registrar el **monto real ejecutado por mes** de cada proyecto CAPEX, calculando la variación presupuestado vs. real. | Alta |
| RF-044 | El sistema debe calcular y mostrar el **status del proyecto** CAPEX: En proceso / Ejecutado / Atrasado, según las fechas de desembolso y el real ingresado. | Alta |
| RF-045 | El sistema debe mostrar el **estado de cumplimiento**: Cumple / No Cumple, comparando el total real con el total presupuestado. | Media |
| RF-046 | El sistema debe mostrar el **consolidado CAPEX** de todas las áreas visible para el Gerente, con totales por trimestre y total anual. | Alta |

---

### EP-10 · OPEX — Presupuesto Operativo

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-047 | El sistema debe permitir al Jefe de Área gestionar el **catálogo de cuentas y subcuentas OPEX** de su área, con campos: código de cuenta, nombre, tipo (Cuenta / Subcuenta). | Alta |
| RF-048 | El sistema debe permitir registrar el **presupuesto mensual por subcuenta OPEX** (ENE–DIC) en Córdobas, con cálculo automático de totales trimestrales y anuales. | Alta |
| RF-049 | El sistema debe permitir registrar el **gasto real mensual por subcuenta OPEX** (ENE–DIC), con cálculo automático de variación presupuesto vs. real. | Alta |
| RF-050 | El sistema debe soportar una **Memoria de Cálculo detallada** para las subcuentas especiales (al menos: Gastos de Oficina y Gastos de Limpieza), con registro de: nombre del rubro/material, unidad de medida, cantidad presupuestada, precio unitario y total calculado automáticamente. | Alta |
| RF-051 | El sistema debe calcular el **total de la subcuenta** a partir de la suma de los rubros de su memoria de cálculo cuando aplique. | Alta |
| RF-052 | El sistema debe mostrar el **consolidado OPEX** de todas las áreas visible para el Gerente, con comparativa presupuesto vs. real por cuenta, trimestre y total anual. | Alta |

---

### EP-11 · Reportes

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-053 | El sistema debe generar el **Informe del Plan Estratégico** por área, mostrando: Pilares, Objetivos CG, OKRs con KRs, puntajes y semáforos. | Alta |
| RF-054 | El sistema debe generar el **Informe del Plan de Acción** por área, mostrando: acciones por objetivo CG con conteo por status, % progreso y semáforo. | Alta |
| RF-055 | El sistema debe generar versiones **consolidadas** de ambos informes que integren todas las áreas del ciclo, visibles solo para el Gerente. | Alta |
| RF-056 | El sistema debe permitir **exportar los reportes** en formato PDF. | Media |
| RF-057 | El sistema debe permitir **exportar los reportes** en formato Excel (.xlsx). | Media |

---

### EP-12 · Dashboards

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-058 | El sistema debe mostrar el **Dashboard por Área** al Jefe de Área con las siguientes métricas: Total OKRs · OKRs alcanzados · % OKRs alcanzados · OKRs en peligro · % OKRs en peligro · Promedio puntuación objetivos · # Total acciones · Promedio progreso · # Acciones vencidas · % Acciones atrasadas · Días hasta fecha límite más cercana. | Alta |
| RF-059 | El sistema debe mostrar un **Dashboard Consolidado** al Gerente con las métricas del RF-058 agregadas de todas las áreas, más: Comparativa por área, Progreso de acciones por metas, Avance por área estratégica. | Alta |
| RF-060 | El sistema debe mostrar los dashboards con **gráficas visuales**: barras de progreso por área, gráfica de acciones por status, gráfica OKRs alcanzados vs. en peligro. | Alta |
| RF-061 | El sistema debe aplicar el **semáforo de colores** (Verde / Amarillo / Rojo) en todos los indicadores de los dashboards según umbrales configurados. | Alta |
| RF-062 | El sistema debe mostrar en el Dashboard del Gerente un **panel de seguimiento por área** que permita hacer drill-down hacia el detalle de acciones y OKRs de cada área. | Media |

---

### EP-13 · Notificaciones y Alertas

| ID | Requerimiento | Prioridad |
|----|---------------|-----------|
| RF-063 | El sistema debe enviar **alertas automáticas por correo** al Jefe de Área cuando una acción del plan pase a estado Atrasado (fecha de vencimiento < fecha actual y % progreso < 100%). | Alta |
| RF-064 | El sistema debe enviar **resúmenes de avance** al Gerente de forma periódica (semanal o quincenal, configurable) con el estado consolidado de OKRs y acciones de todas las áreas. | Media |
| RF-065 | El sistema debe mostrar **notificaciones in-app** al Gerente cuando un Jefe de Área actualice el progreso de una acción o un OKR. | Media |
| RF-066 | El sistema debe generar **alertas de OKR en peligro** cuando la puntuación ponderada de un OKR sea inferior al umbral amarillo configurado, notificando al Jefe y al Gerente. | Alta |

---

## 2. Requerimientos No Funcionales

### RNF-01 · Rendimiento

| ID | Requerimiento | Métrica |
|----|---------------|---------|
| RNF-001 | Las páginas principales (Dashboard, Plan de Acción, OKRs) deben cargar en menos de **2 segundos** bajo carga normal. | < 2 s en p95 |
| RNF-002 | Las operaciones de escritura (crear/editar acción, actualizar % progreso) deben responder en menos de **1 segundo**. | < 1 s en p95 |
| RNF-003 | El sistema debe soportar al menos **50 usuarios concurrentes** por tenant sin degradación de rendimiento. | 50 usuarios/tenant |
| RNF-004 | La generación de reportes PDF o XLSX no debe superar **10 segundos** para conjuntos de datos de hasta 200 acciones. | < 10 s |

---

### RNF-02 · Seguridad

| ID | Requerimiento |
|----|---------------|
| RNF-005 | El sistema debe implementar **autenticación mediante JWT** con tokens de acceso de corta duración (60 min) y refresh token de larga duración (7 días). |
| RNF-006 | Todas las comunicaciones deben realizarse sobre **HTTPS/TLS 1.2+**. No se permitirá HTTP en producción. |
| RNF-007 | El sistema debe implementar **aislamiento de datos por tenant** a nivel de base de datos mediante Row-Level Security (RLS) en Supabase/PostgreSQL, asegurando que un tenant nunca acceda a datos de otro. |
| RNF-008 | Los archivos adjuntos (entregables) deben almacenarse en un **bucket de almacenamiento privado** con URLs firmadas de acceso temporal. |
| RNF-009 | El sistema debe implementar **protección contra CSRF, XSS e inyección SQL** en todos los endpoints de la API. |
| RNF-010 | Las contraseñas de usuario deben almacenarse usando **hashing BCrypt** con factor de coste ≥ 12. |
| RNF-011 | El sistema debe bloquear una cuenta de usuario tras **5 intentos fallidos** de autenticación consecutivos durante 15 minutos. |

---

### RNF-03 · Disponibilidad y Confiabilidad

| ID | Requerimiento | Métrica |
|----|---------------|---------|
| RNF-012 | El sistema debe garantizar una **disponibilidad del 99.5%** mensual (uptime), equivalente a máximo ~3.6 horas de caída al mes. | ≥ 99.5% |
| RNF-013 | El sistema debe contar con **respaldos automáticos diarios** de la base de datos, con retención de 30 días. | Diario, 30 días |
| RNF-014 | El sistema debe implementar **manejo de errores graceful**: ante fallos de componentes externos (correo, almacenamiento), la operación principal no debe fallar; los errores secundarios se registran en log. | — |

---

### RNF-04 · Escalabilidad

| ID | Requerimiento |
|----|---------------|
| RNF-015 | La arquitectura debe permitir escalar horizontalmente el servidor de aplicaciones sin cambios de código, utilizando el patrón stateless en la API. |
| RNF-016 | El modelo de datos debe soportar al menos **20 tenants activos simultáneos**, cada uno con hasta **10 áreas**, **50 objetivos CG** y **500 acciones** por ciclo, sin degradación. |

---

### RNF-05 · Usabilidad

| ID | Requerimiento |
|----|---------------|
| RNF-017 | La interfaz de usuario debe ser **responsive**, funcionando correctamente en resoluciones de escritorio (≥ 1280px) y tablet (≥ 768px). |
| RNF-018 | El sistema debe operar **en idioma español** como idioma único de la interfaz en v1.0. |
| RNF-019 | El sistema debe proporcionar **mensajes de validación claros** en formularios, indicando el campo con error y la razón específica. |
| RNF-020 | Los dashboards y gráficas deben ser **comprensibles sin capacitación técnica** para un usuario de gerencia media. |

---

### RNF-06 · Mantenibilidad

| ID | Requerimiento |
|----|---------------|
| RNF-021 | El código debe seguir la **arquitectura N-Tier** establecida (Aplicación / API / BLL / DAL / Entity / DTO / IOC / Utility), consistente con el estándar de Freiroute. |
| RNF-022 | Todos los endpoints de la API deben estar **documentados con Swagger/OpenAPI 3.0**, generado automáticamente. |
| RNF-023 | El sistema debe implementar **logging estructurado** (JSON) con niveles: Debug, Info, Warning, Error, y permitir filtrado por tenant, usuario y módulo. |
| RNF-024 | El sistema debe contar con **pruebas unitarias** con cobertura mínima del 70% en la capa BLL (lógica de negocio). |

---

### RNF-07 · Compatibilidad

| ID | Requerimiento |
|----|---------------|
| RNF-025 | El sistema debe ser compatible con los navegadores: **Chrome ≥ 110, Edge ≥ 110, Firefox ≥ 110**. |
| RNF-026 | La API debe respetar el estándar **REST** con respuestas en JSON y códigos HTTP estándar (200, 201, 400, 401, 403, 404, 422, 500). |

---

## 3. Reglas de Negocio

### RN-01 · Multi-tenancy y Ciclos

| ID | Regla |
|----|-------|
| RN-001 | Cada **Gerencia (Tenant)** tiene datos completamente aislados. No existe visibilidad cruzada entre tenants bajo ningún rol, incluyendo el Administrador SaaS (quien solo gestiona metadatos del tenant, no sus datos). |
| RN-002 | Un **ciclo anual** tiene exactamente un año fiscal asociado. No pueden existir dos ciclos activos para el mismo año en el mismo tenant. |
| RN-003 | Un ciclo puede estar en estado: **Borrador** (en construcción), **Activo** (en ejecución, año en curso) o **Cerrado** (año concluido, solo lectura). **Solo el AdminTenant puede activar un ciclo (Borrador → Activo); solo el Gerente puede cerrarlo (Activo → Cerrado).** *(Actualizado 2026-09-14 por decisión de Jorge: la activación es del ADM, el cierre del GER — ver spec HU-007 Flag #1.)* |
| RN-004 | Los datos de un ciclo **Cerrado** son de solo lectura. No se pueden crear, editar ni eliminar registros en un ciclo cerrado. |

---

### RN-02 · Roles y Permisos

| ID | Regla |
|----|-------|
| RN-005 | El rol **Administrador SaaS** gestiona tenants y sus suscripciones. No tiene acceso a los datos del plan estratégico ni del presupuesto de ningún tenant. |
| RN-006 | El rol **Gerente** tiene acceso de lectura y escritura a todos los módulos del tenant: Configuración, Filosofía, Pilares, Áreas, y tiene vista consolidada de todas las áreas. Puede crear y gestionar usuarios y asignar roles dentro del tenant. |
| RN-007 | El rol **Jefe de Área** tiene acceso de lectura y escritura **únicamente a los datos de su área asignada**: Objetivos CG, Plan de Acción, OKRs, CAPEX y OPEX de su área. Tiene acceso de **solo lectura** a Filosofía, Pilares y configuración del ciclo. |
| RN-008 | Un **Jefe de Área no puede ver** los datos de otra área estratégica: objetivos, acciones, OKRs, CAPEX ni OPEX de otras áreas, salvo en reportes consolidados donde los datos son agregados sin detalle. |
| RN-009 | Un usuario solo puede pertenecer a **un tenant** y tener **un rol activo** a la vez dentro de ese tenant. |

---

### RN-03 · Áreas y Responsables

| ID | Regla |
|----|-------|
| RN-010 | Un ciclo puede tener entre **1 y 20 áreas estratégicas** activas por ciclo. Este límite es configurable por el plan de suscripción del tenant. *(Actualizado 2026-09-13 por decisión de Jorge: el seed Premium define max_areas=20; RN-010 quedó alineado con el modelo de datos.)* |
| RN-011 | Cada área estratégica debe tener **exactamente un responsable (Jefe de Área)** asignado. No puede quedar un área sin responsable en estado Activo. |
| RN-012 | Un responsable (Jefe de Área) puede ser asignado a **una sola área** por ciclo. No puede ser responsable de dos áreas simultáneamente en el mismo ciclo. |

---

### RN-04 · Objetivos CG (¿Cómo Ganaremos?)

| ID | Regla |
|----|-------|
| RN-013 | El código del objetivo CG se genera automáticamente con el formato `GOLn.CGm`, donde `n` es el número secuencial del área y `m` es el número secuencial del objetivo dentro del área. |
| RN-014 | Un objetivo CG debe estar asociado a **exactamente un Pilar Estratégico** corporativo. |
| RN-015 | No se puede **eliminar** un objetivo CG que tenga acciones registradas en el Plan de Acción. Primero deben eliminarse todas sus acciones. |

---

### RN-05 · Plan de Acción

| ID | Regla |
|----|-------|
| RN-016 | La suma de los **pesos (%)** de las acciones de un mismo objetivo CG debe ser igual a **1.0 (100%)**. El sistema debe validar esta condición al guardar o advertir al usuario si la suma difiere. |
| RN-017 | El **status de una acción** se determina automáticamente según las siguientes reglas de precedencia: (1) Si `% progreso = 100%` → **Terminado**. (2) Si `% progreso > 0%` y `fecha actual ≤ fecha vencimiento` → **En progreso**. (3) Si `% progreso = 0%` y `fecha actual ≤ fecha inicio` → **No iniciado**. (4) Si `fecha actual > fecha vencimiento` y `% progreso < 100%` → **Atrasado**. |
| RN-018 | El **% de progreso general** de un objetivo CG se calcula como: `Σ (peso_acción × % progreso_acción)`. El resultado es un valor entre 0 y 1. |
| RN-019 | No se puede registrar una acción con **fecha de vencimiento anterior a la fecha de inicio**. |
| RN-020 | Los **archivos adjuntos** (entregables) tienen un tamaño máximo de **20 MB** por archivo. Los tipos permitidos son: PDF, DOCX, XLSX, PNG, JPG. |
| RN-021 | La **fecha de inicio y vencimiento** de las acciones deben estar dentro del año fiscal del ciclo activo. |

---

### RN-06 · OKRs y KRs

| ID | Regla |
|----|-------|
| RN-022 | Cada OKR puede tener entre **1 y 5 KRs** asociados. |
| RN-023 | La suma de los **pesos de los KRs** de un mismo OKR debe ser igual a **1.0 (100%)**. El sistema debe validar esta condición. |
| RN-024 | Los **valores reales mensuales** de un KR se expresan en escala **0.0 a 1.0** con hasta 1 decimal. No se permiten valores fuera de este rango. |
| RN-025 | La **puntuación trimestral** de un KR se calcula como el promedio de los meses del trimestre que tengan valor registrado. Los meses sin valor no afectan el promedio. |
| RN-026 | La **puntuación final del OKR** es la suma de las puntuaciones ponderadas de todos sus KRs. Valor máximo posible: 1.0. |
| RN-027 | Un OKR con puntuación final **≥ umbral verde** se considera **Alcanzado**. Un OKR con puntuación entre el umbral amarillo y el verde se considera **En Peligro**. Un OKR con puntuación **< umbral amarillo** se considera **No Alcanzado**. |
| RN-028 | No se puede **eliminar** un OKR que tenga KRs con valores reales registrados en el ciclo activo. Primero deben borrarse los valores del período. |

---

### RN-07 · CAPEX

| ID | Regla |
|----|-------|
| RN-029 | El **total de desembolso planeado** (suma de los 12 meses) no puede superar el **presupuesto aprobado** del proyecto. El sistema debe advertir si ocurre. |
| RN-030 | Los montos de CAPEX se expresan en **Córdobas (C$)** sin decimales. |
| RN-031 | El **status del proyecto** CAPEX se determina automáticamente: **Ejecutado** si el total real = total planeado; **En proceso** si real < planeado y hay al menos un desembolso real registrado; **No iniciado** si no hay ningún desembolso real; **Atrasado** si hay meses pasados con desembolso planeado > 0 y real = 0. |
| RN-032 | El **cumplimiento** del proyecto CAPEX es **Cumple** si `total real ≥ total presupuestado`; de lo contrario **No Cumple**. |

---

### RN-08 · OPEX y Memoria de Cálculo

| ID | Regla |
|----|-------|
| RN-033 | Las cuentas OPEX siguen una jerarquía de **dos niveles**: Cuenta principal → Subcuentas. El presupuesto y el real se registran únicamente a nivel de **Subcuenta**. Los totales de Cuenta se calculan sumando sus subcuentas. |
| RN-034 | Las subcuentas configuradas con **Memoria de Cálculo** (al menos Gastos de Oficina y Gastos de Limpieza) calculan su monto presupuestado automáticamente como la **suma de (cantidad × precio unitario)** de todos sus rubros de materiales. Este total se transfiere al presupuesto mensual de la subcuenta. |
| RN-035 | Los montos de OPEX se expresan en **Córdobas (C$)** con hasta 2 decimales. |
| RN-036 | La **variación** entre presupuesto y real de una subcuenta se calcula como: `variación = real - presupuesto`. Un valor positivo indica sobreejercicio; un valor negativo indica ahorro. |
| RN-037 | No se puede eliminar una **Cuenta OPEX** que tenga subcuentas con valores presupuestados o reales registrados en el ciclo activo. |

---

### RN-09 · Semáforos

| ID | Regla |
|----|-------|
| RN-038 | El **semáforo** aplica de forma uniforme a OKRs, Objetivos CG y acciones del Plan de Acción, usando los umbrales configurados en el ciclo. Por defecto: Verde ≥ 0.9 · Amarillo 0.7–0.9 · Rojo < 0.7. |
| RN-039 | Los umbrales de semáforo se configuran **por ciclo** y aplican a todo el tenant durante ese ciclo. No son modificables retroactivamente una vez el ciclo está en estado Activo. |

---

### RN-10 · Alertas y Notificaciones

| ID | Regla |
|----|-------|
| RN-040 | El sistema verifica diariamente (proceso batch nocturno) el estado de todas las acciones del plan. Si una acción tiene `fecha vencimiento < fecha actual` y `% progreso < 100%`, se envía alerta al responsable del área. La alerta se envía **una sola vez** al momento en que pasa a estado Atrasado. |
| RN-041 | Los **resúmenes periódicos** al Gerente se generan los **lunes** de cada semana o quincenalmente según la configuración del ciclo. |
| RN-042 | Las notificaciones in-app se marcan como **leídas** manualmente por el usuario o automáticamente después de **7 días**. |

---

## 4. Matriz de Trazabilidad — Módulo ↔ Requerimiento

| Módulo | RFs | RNFs clave | RNs clave |
|--------|-----|-----------|-----------|
| Admin SaaS | RF-001 a RF-006 | RNF-005, 007 | RN-001, 005 |
| Configuración Ciclo | RF-007 a RF-011 | RNF-018 | RN-002, 003, 004 |
| Filosofía Corporativa | RF-012 a RF-014 | RNF-017 | RN-007 |
| Pilares Estratégicos | RF-015 a RF-017 | — | RN-007 |
| Áreas Estratégicas | RF-018 a RF-020 | RNF-007 | RN-010, 011, 012 |
| Objetivos CG | RF-021 a RF-024 | RNF-001 | RN-013, 014, 015 |
| Plan de Acción + Gantt | RF-025 a RF-032 | RNF-001, 002, 020 | RN-016 a RN-021 |
| OKRs / KRs | RF-033 a RF-040 | RNF-001 | RN-022 a RN-028 |
| CAPEX | RF-041 a RF-046 | RNF-002 | RN-029 a RN-032 |
| OPEX + Memoria Cálculo | RF-047 a RF-052 | RNF-002 | RN-033 a RN-037 |
| Reportes | RF-053 a RF-057 | RNF-004 | RN-003, 004 |
| Dashboards | RF-058 a RF-062 | RNF-001, 017, 020 | RN-038, 039 |
| Alertas y Notificaciones | RF-063 a RF-066 | RNF-014 | RN-040 a RN-042 |

---

## Resumen de Conteos

| Tipo | Total |
|------|-------|
| Requerimientos Funcionales (RF) | **66** |
| Requerimientos No Funcionales (RNF) | **26** |
| Reglas de Negocio (RN) | **42** |
| **TOTAL** | **134** |

---

*Documento generado el 13/09/2026 — Fase de Levantamiento de Requerimientos.*
*Fuente: AS-IS_PE_GOL_Situacion_Actual.md + entrevista con Jorge (Dicegsa).*
*Siguiente fase: Diseño — Épicas, Historias de Usuario y Sprint Planning.*

# 📋 AS-IS — Situación Actual del Sistema
## Plan Estratégico y Presupuesto (PE-GOL)
### Empresa: Dicegsa · Año: 2026 · Herramienta actual: Microsoft Excel

---

## 1. Contexto General

La **Gerencia de Operaciones Logística** de Dicegsa gestiona anualmente su **Plan Estratégico y Presupuesto** a través de un archivo Excel multi-hoja denominado `2026_PE_GOL_TEMPLATE.xlsx`. Este archivo concentra **toda la planificación estratégica, seguimiento operativo y control presupuestario** de la gerencia y sus 7 áreas subordinadas.

La solución SaaS a construir digitaliza y escala este proceso, convirtiendo cada **Gerencia** en un **Tenant** independiente dentro de una plataforma multi-inquilino.

---

## 2. Estructura Organizacional Actual (Contexto del Tenant)

| ID | Área (Donde Jugaremos) | Jefe / Responsable |
|----|------------------------|--------------------|
| GOL1 | CEDIS FARMA | Brandon Jonathan Hernández Areas |
| GOL2 | CEDIS CONSUMO | Trylce Mercedes Martínez Delgado |
| GOL3 | ALMACENES STOCK | Denis Sobalvarro Muñoz |
| GOL4 | DISTRIBUCIÓN | Wilfredo Ramon Guevara Aleman |
| GOL5 | GESTIÓN DE FLOTA | Hermes Javier Velasquez Fletes |
| GOL6 | EXPERIENCIA AL CLIENTE (SAC) | Sergio Nicolás Carcache |
| GOL7 | BUENAS PRÁCTICAS (BPADT) | Larisa Argentina Martínez Córdoba |

> **Nota:** Límite actual del Excel: 8 áreas estratégicas y 8 responsables por gerencia.

---

## 3. Mapa de Componentes del Excel (Hojas por Módulo)

El archivo cuenta con **~65 hojas** agrupadas por función. A continuación el inventario estructurado:

### 3.1 Configuración Global (Settings)

| Hoja | Contenido |
|------|-----------|
| `Settings` | Idioma, mes de inicio, año fiscal, nombre empresa, eslogan |
| `SA` | Catálogo de Áreas Estratégicas con ID y responsable |
| `Teams` | Catálogo de responsables de área |
| `Lan` | Traducciones de etiquetas (bilingüe ES/EN) |

**Configuración de semáforos (umbrales de color):**
- KPIs: Verde ≥ 0.9 · Amarillo 0.7–0.9 · Rojo < 0.7
- Plan de acción: Verde ≥ 0.9 · Amarillo 0.7–0.9 · Rojo < 0.7

---

### 3.2 Filosofía Corporativa

| Hoja | Componente | Contenido actual |
|------|------------|-----------------|
| `Statement` | **Visión** | "Ser la empresa más exitosa en la comercialización de productos farmacéuticos y de consumo, brindando a nuestros clientes marcas líderes con el mejor servicio a nivel nacional." |
| `Statement` | **Misión** | Declaración general de cómo logrará la visión (capturada por texto libre) |
| `Statement` | **Valores** | Liderazgo · Excelencia · Integridad · Calidad del servicio · Innovación · Compromiso |

---

### 3.3 Pilares Estratégicos Corporativos (¿Cómo Ganaremos?)

Hoja: `Pilares`

| ID Pilar | Nombre | Estrategia de Victoria |
|----------|--------|------------------------|
| PEC1 | Excelencia Operativa | Modernizar CEDIS y logística digital para fill rate ≥ 95% |
| PEC2 | Cliente-centrismo | Implementar CRM omnicanal y modelo CX basado en datos |
| PEC3 | Gobernanza Estratégica | Formalizar gobierno corporativo y comité de planificación |
| PEC4 | Gestión de Stakeholders | Crear comité y protocolo de comunicación institucional |
| PEC5 | Cultura y Liderazgo | Fortalecer liderazgo medio y cultura colaborativa |

**Campos por pilar:** ID · Nombre · Estrategia de Victoria · Objetivos de Área por Trimestre (Q1/Q2/Q3/Q4)

---

### 3.4 Áreas Estratégicas (¿Dónde Jugaremos?)

Hoja: `DondeJugaremos`

Registra cada área operativa con su código GOL (GOL1–GOL7) y permite agregar comentarios. Conecta las áreas con los objetivos específicos y con el plan de acción.

---

### 3.5 Componentes Estratégicos GOL (Objetivos por Área)

Hojas: `AGoal`, `AGoal DJ2` … `AGoal DJ7` (una por área)

Cada área tiene sus propios **Componentes Estratégicos (CG)**, que equivalen a objetivos estratégicos concretos con métricas. Ejemplo real del Área GOL1:

| Código | Objetivo |
|--------|----------|
| GOL1.CG1 | Diseñar un CEDI fármaco temporal con cap. de expedición para 95K und/día en Q4 2026 |
| GOL1.CG2 | Diseñar almacén temporal de fórmulas para reducir lead time de pedidos en 50% en Q4 2026 |
| GOL1.CG3 | Implementar esquema de compensación por incentivos (productividad + calidad) desde Q1 2027 |
| GOL1.CG4 | Optimizar OPEX del CEDI fármaco para ahorrar 10% vs 2025 desde Q4 2026 |
| GOL1.CG5 | Mejorar entregas certificadas reduciendo reclamos en 20% vía innovación en packing Q4 2026 |

**Campos por objetivo:** Código CG · Descripción · Semáforo (% progreso) · Lista de acciones con estado

---

### 3.6 Plan de Acción con Gantt

Hojas: `AP DJ1` … `AP DJ7` (una por área)

Es el componente más denso del sistema. Por cada objetivo (CG) se registran múltiples acciones:

| Campo | Descripción |
|-------|-------------|
| ¿Dónde Ganaremos? | Área GOL de referencia |
| ¿Cómo Ganamos? | Código y descripción del objetivo CG |
| Plan de Acción | Descripción de la acción específica (ej: "1.2.1 Construir análisis situación actual") |
| Entregable | Documento o artefacto esperado (ej: "Caso de negocio y presentación de indicadores") |
| Responsable | Nombre del jefe de área |
| Fecha de Inicio | Fecha inicio de la acción |
| Fecha de Vencimiento | Fecha límite |
| Clasificación | Tipo: Proyecto / Iniciativa / Operativa |
| % Progreso General | Avance acumulado de la acción (0–1) |
| Status | No iniciado / En progreso / Terminado / Atrasado |
| Atrasado | Flag booleano |
| Tipo presupuesto | OPEX / CAPEX |
| Peso (%) | Ponderación de la acción sobre el objetivo (suma = 1.0) |
| Puntuación ponderada | Peso × % Progreso |

**Visualización Gantt:** columnas mensuales (ENE–DIC) con marcadores de duración.

---

### 3.7 OKRs — Seguimiento de Resultados Clave

Hojas: `OKRS DJ1` … `OKRS DJ7` (una por área)

Estructura OKR real del área GOL1:

| OKR | Pilar | Objetivo | KRs | Peso |
|-----|-------|----------|-----|------|
| OKR.1 | Excelencia Operativa | Mejorar Rentabilidad y Eficiencia | KR1: Reducir OPEX almacenamiento 15% · KR2: Reducir lead time 10% · KR3: Fill Rate ≥ 95% | 0.4 / 0.3 / 0.3 |
| OKR.2 | Cliente-céntrico | Modelo Operativo 100% Cliente-Céntrico | KR1: Reducir quejas/devoluciones 50% · KR2: OTIF de 11% a 85% · KR3: Encuestas NPS | 0.4 / 0.3 / 0.3 |
| OKR.3 | Gobernanza | Gestión Ordenada de Proyectos y Presupuesto | KR1: 100% hitos cumplidos · KR2: CAPEX+OPEX en presupuesto · KR3: Tablero KPIs Power BI | 0.4 / 0.3 / 0.3 |
| OKR.4 | Stakeholders | Gestionar Actores Principales | KR1: Renegociar 2 contratos clave · KR2: 80% personal capacitado · KR3: Plan gestión cambio | 0.4 / 0.3 / 0.3 |
| OKR.5 | Cultura | Innovación y Digitalización Supply Chain | KR1: TMS 100% · KR2: 70% procesos BPDAT digitalizados · KR3: CRM+TMS+WMS en Power BI | 0.4 / 0.3 / 0.3 |

**Campos por KR:** Descripción · Peso (%) · Valor mensual real (ENE–DIC) · Puntuación trimestral · Puntuación final · Puntuación ponderada

**Escala de revisión:** 0.0 a 1.0 en incrementos de 0.1

---

### 3.8 CAPEX — Capital Expenditure por Área

Hojas: `CX DJ1` … `CX DJ7` (una por área)

Registra proyectos de inversión de capital. Ejemplo real área GOL1:

| Campo | Descripción |
|-------|-------------|
| ¿Dónde Ganaremos? | Área GOL |
| Responsable | Jefe del área |
| Área de Colaboración | Área de soporte (Servicios Generales / Compras y Seguros) |
| Colaborador | Persona específica |
| Nombre del proyecto | Descripción del activo/inversión |
| Impacto | Justificación del CAPEX |
| Presupuesto Aprobado | Monto total en Córdobas (C$) |
| Periodo de Desembolso | Distribución mensual (ENE–DIC) con subtotales por trimestre (Q1–Q4) |
| Status del proyecto | En proceso / Ejecutado / Atrasado |
| Total | Suma de desembolsos |
| Status total | Cumple / No Cumple |

**Proyectos reales CEDIS FARMA 2026:**

| Proyecto | Presupuesto (C$) | Trimestre |
|----------|-----------------|-----------|
| Renovación rack de almacenamiento | 366,200 | Q1 |
| Máquina de burbujas de aire para empaque | 109,860 | Q2 |
| Renovación termos cadena de frío (7 vacuna + 10+3 Yeti) | 219,720 | Q1 |
| Elevador de carga para mesanines | 732,400 | Q4 |
| 8 escaleras para alisto picking | 70,000 | Q1–Q2 |
| Semáforo de aviso descarga de camiones | 29,296 | Q1 |
| 5 trajes para trabajo en baja temperatura | 45,775 | Q1 |
| Instalación aire cuarto climatizado 1 (redundancia) | 102,536 | Q1 |
| Cambio aire climatizado 2 (36,000 BTU) | 102,536 | Q2 |
| Freezers portables envíos pasivos | 70,000 | Q1 |
| Estación validación de pedidos adicional | 146,480 | Q2 |

---

### 3.9 OPEX — Gastos Operativos por Área

Hojas: `CG DJ1` … `CG DJ7` (una por área)

Registra el presupuesto de gastos operativos con estructura de **cuentas y subcuentas**. Incluye distribución mensual de presupuestado vs. real.

**Subcuentas con detalle especial de materiales (memoria de cálculo):**
- **Gastos de Oficina:** detalle por rubro de materiales presupuestados
- **Gastos de Limpieza:** detalle por rubro de materiales presupuestados

Estas dos subcuentas requieren un nivel adicional de granularidad (ítem de material, cantidad, precio unitario, total) que funciona como memoria de cálculo del presupuesto.

---

### 3.10 Reportes (Informes)

| Hoja | Contenido |
|------|-----------|
| `AGoal` | 3.2 Informe Plan de Acción — consolidado de acciones por objetivo de todas las áreas |
| `SGoal` / `SG DJ2`…`SG DJ7` | 3.1 Informe Plan Estratégico — resumen de objetivos CG por área con OKRs y estatus |

**Campos del informe de plan de acción:**
- Acciones por objetivo (No iniciado / En progreso / Concluido)
- \# Acciones totales
- Acciones atrasadas
- % Progreso general
- Semáforo de estado (●)

---

### 3.11 Dashboards

| Hoja | Dashboard |
|------|-----------|
| `SDashboard` / `SD DJ1`…`SD DJ7` | 4.2 Dashboard Plan de Acción por área |
| `APDashboard` | 4.2 Dashboard general Plan de Acción |
| `AuxDash` | Datos auxiliares para gráficas |
| `AuxPlan` | Datos auxiliares plan estratégico |
| `AuxR` | Datos auxiliares reportes |
| `Data` | Tabla maestra de datos del sistema |

**Métricas mostradas en los dashboards:**
- Total de OKRs · OKRs alcanzados · % OKRs alcanzados
- OKRs en peligro · % OKRs en peligro
- Promedio de puntuación de objetivos alcanzados
- Promedio de OKRs alcanzados
- \# Total acciones · Progreso promedio
- \# Acciones vencidas · % Acciones atrasadas
- Días hasta fecha límite
- Total de objetivos · Objetivos alcanzados · % Objetivos alcanzados
- Objetivos en peligro · % Objetivos en peligro
- Acciones por status (gráfica)
- Acciones por objetivo y status
- Progreso de acciones por metas
- Avances de acciones por área estratégica
- OKRs alcanzados por objetivo

---

## 4. Flujo del Proceso Actual (AS-IS)

```
[Inicio de ciclo anual]
        │
        ▼
[Gerencia configura el archivo Excel]
  - Año fiscal, empresa, mes inicio
  - Registra áreas y responsables
        │
        ▼
[Cada Jefe de Área llena su sección]
  - Filosofía aplicada al área (opcl.)
  - Objetivos CG (¿Cómo Ganaremos?)
  - OKRs con KRs y pesos
  - Plan de Acción con Gantt
  - CAPEX: proyectos de inversión
  - OPEX: presupuesto operativo
        │
        ▼
[Durante el año — actualización mensual/trimestral]
  - Jefe actualiza % progreso de acciones
  - Jefe actualiza valores reales de KRs mes a mes
  - Jefe actualiza real vs. presupuesto CAPEX/OPEX
        │
        ▼
[Gerencia consolida y revisa]
  - Revisa dashboards por área
  - Revisa reportes consolidados
  - Identifica acciones atrasadas u OKRs en peligro
        │
        ▼
[Fin de ciclo — evaluación anual]
  - Cierre de OKRs con puntuación final
  - Informe de ejecución presupuestal
```

---

## 5. Problemáticas Identificadas (Pain Points del AS-IS)

| # | Problema | Impacto |
|---|----------|---------|
| P1 | **Un solo archivo Excel por gerencia** — todos los módulos en un mismo archivo de 65+ hojas | Difícil de mantener, propenso a errores de fórmulas y corrupción |
| P2 | **Sin control de versiones** — si dos personas editan, se pierden cambios | No hay trazabilidad de modificaciones |
| P3 | **Sin separación de roles** — cualquier usuario con acceso puede editar cualquier hoja | Jefes pueden ver y modificar datos de otras áreas |
| P4 | **Actualización manual desconectada** — cada jefe actualiza su sección sin notificación a la gerencia | La gerencia no sabe en tiempo real cuándo hay cambios |
| P5 | **Fórmulas frágiles entre hojas** — los dashboards dependen de referencias cruzadas entre 65 hojas | Un error en una celda rompe toda la cadena de cálculos |
| P6 | **No escalable a múltiples gerencias** — habría que duplicar el archivo por cada gerencia | Gestión caótica si se adopta en toda la empresa |
| P7 | **Sin gestión de entregables adjuntos** — el plan de acción menciona entregables pero no los almacena | Los documentos viven dispersos en correo/SharePoint desconectados |
| P8 | **Sin alertas automáticas** — acciones vencidas o atrasadas no notifican al responsable | Depende de que alguien revise el dashboard manualmente |
| P9 | **Sin historial de ejecución por períodos** — se sobreescribe el mismo archivo año a año | Comparación histórica difícil o imposible |
| P10 | **CAPEX/OPEX sin integración con contabilidad** — el real se ingresa manualmente desde reportes externos | Doble captura de datos, riesgo de inconsistencia |

---

## 6. Glosario de Términos del Negocio

| Término | Definición |
|---------|------------|
| **PE** | Plan Estratégico — planificación anual de objetivos y acciones |
| **GOL** | Gerencia de Operaciones Logística — entidad organizacional (Tenant en la solución) |
| **Área / DJ** | "¿Dónde Jugaremos?" — Área operativa bajo la gerencia (ej: CEDIS FARMA) |
| **CG** | Componente Estratégico GOL — objetivo estratégico específico de un área |
| **KR** | Key Result — resultado clave medible asociado a un OKR |
| **OKR** | Objectives and Key Results — marco de medición de desempeño trimestral/anual |
| **CAPEX** | Capital Expenditure — presupuesto de inversión en activos |
| **OPEX** | Operating Expenditure — presupuesto de gastos operativos recurrentes |
| **Fill Rate** | % de pedidos despachados completos vs. total de pedidos recibidos |
| **OTIF** | On Time In Full — % de pedidos entregados a tiempo y completos |
| **BPADT** | Buenas Prácticas de Almacenamiento, Distribución y Transporte |
| **Semáforo** | Indicador visual de estado: Verde ≥ 0.9 · Amarillo 0.7–0.9 · Rojo < 0.7 |
| **Pilar Estratégico** | Marco temático corporativo que agrupa los objetivos (ej: Excelencia Operativa) |
| **Q1/Q2/Q3/Q4** | Trimestres del año fiscal (Ene–Mar / Abr–Jun / Jul–Sep / Oct–Dic) |
| **Entregable** | Documento o artefacto concreto que evidencia el cumplimiento de una acción |
| **Memoria de Cálculo** | Detalle por rubros/ítems que sustenta el presupuesto de una subcuenta OPEX |

---

## 7. Resumen de Componentes — Inventario para la Solución TO-BE

| # | Módulo | Sub-componentes | Multiárea | Multigerencia |
|---|--------|----------------|-----------|---------------|
| M1 | Configuración | Año, empresa, idioma, mes inicio, umbrales semáforo | No | Sí (por tenant) |
| M2 | Filosofía Corporativa | Visión, Misión, Valores | No | Sí (por tenant) |
| M3 | Pilares Estratégicos | ID, Nombre, Estrategia de Victoria | No | Sí (por tenant) |
| M4 | Áreas Estratégicas | ID GOL, Nombre, Responsable | Sí | Sí |
| M5 | Componentes Estratégicos (Objetivos CG) | Código, Descripción, Pilar asociado, Trimestre objetivo | Sí | Sí |
| M6 | Plan de Acción + Gantt | Acción, Entregable, Responsable, Fechas, Clasificación, % Progreso, Status, Peso, Tipo OPEX/CAPEX | Sí | Sí |
| M7 | OKRs / KRs | OKR, Pilar, KR, Peso, Valor mensual real, Puntuación | Sí | Sí |
| M8 | CAPEX | Proyecto, Área colaboración, Impacto, Presupuesto, Desembolso mensual/trimestral, Status | Sí | Sí |
| M9 | OPEX | Cuentas, Subcuentas, Presupuesto mensual, Real mensual, Memoria de cálculo (Oficina/Limpieza) | Sí | Sí |
| M10 | Gestión de Entregables | Adjunto de documentos a acciones del plan | Sí | Sí |
| M11 | Reportes | Informe Plan Estratégico, Informe Plan de Acción (por área y consolidado) | Sí | Sí |
| M12 | Dashboards | Por área + consolidado gerencia: OKRs, acciones, CAPEX, OPEX, semáforos | Sí | Sí |
| M13 | Notificaciones y Alertas | Acciones vencidas, OKRs en peligro, actualizaciones de jefes | Sí | Sí |
| M14 | Administración SaaS | Tenants (Gerencias), Usuarios, Roles, Ciclos anuales | N/A | N/A |

---

*Documento generado el 13/09/2026 — Fase AS-IS previo a levantamiento de requerimientos.*
*Fuente: Análisis del archivo `2026_PE_GOL_-_TEMPLATE.xlsx` proporcionado por Jorge (Dicegsa).*

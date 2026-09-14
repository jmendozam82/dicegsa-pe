# 🎨 Design System — PE-GOL SaaS
## Guía de Diseño UI/UX · Bootstrap 5.3 · Versión 1.0
### Alineado al estilo Freiroute TMS

---

## 1. Identidad Visual

PE-GOL SaaS comparte la misma base visual de Freiroute. Se diferencia mediante el **Accent Color** que identifica el módulo de Planificación Estratégica: se sustituye el Cyan `#00D4FF` por un **Teal Estratégico** `#00B4A6` que evoca gestión, crecimiento y análisis.

---

## 2. Paleta de Colores

### 2.1 Colores Primarios

| Token | Nombre | Hex | Uso |
|-------|--------|-----|-----|
| `--color-navy` | Navy | `#0B2545` | Sidebar, headers de sección, backgrounds oscuros |
| `--color-navy-light` | Navy Light | `#1A3A5C` | Hover en sidebar, navbar secundaria |
| `--color-action` | Action Blue | `#1A73E8` | Botones primarios, links, íconos de acción |
| `--color-action-hover` | Action Hover | `#1557B0` | Estado hover de botones primarios |
| `--color-accent` | Teal Estratégico | `#00B4A6` | Badges, highlights de KPIs, indicadores clave |
| `--color-accent-light` | Teal Light | `#E0F7F5` | Backgrounds de tarjetas de métricas |

### 2.2 Colores de Semáforo

| Token | Nombre | Hex | Uso |
|-------|--------|-----|-----|
| `--color-verde` | Verde | `#34A853` | Indicador Alcanzado / En tiempo |
| `--color-verde-bg` | Verde Fondo | `#E6F4EA` | Background de celdas/tarjetas verdes |
| `--color-amarillo` | Amarillo | `#F9AB00` | Indicador En Peligro / Advertencia |
| `--color-amarillo-bg` | Amarillo Fondo | `#FEF7E0` | Background de celdas/tarjetas amarillas |
| `--color-rojo` | Rojo | `#EA4335` | Indicador No Alcanzado / Atrasado |
| `--color-rojo-bg` | Rojo Fondo | `#FCE8E6` | Background de celdas/tarjetas rojas |

### 2.3 Colores Neutros

| Token | Nombre | Hex | Uso |
|-------|--------|-----|-----|
| `--color-bg-page` | Page Background | `#F0F4F8` | Fondo general de la app |
| `--color-bg-card` | Card Background | `#FFFFFF` | Fondo de tarjetas y paneles |
| `--color-border` | Border | `#E2E8F0` | Bordes de tarjetas, tablas, inputs |
| `--color-border-dark` | Border Dark | `#CBD5E1` | Bordes con más contraste |
| `--color-text-primary` | Text Primary | `#1E293B` | Texto principal, títulos |
| `--color-text-secondary` | Text Secondary | `#64748B` | Subtítulos, labels, texto de apoyo |
| `--color-text-disabled` | Text Disabled | `#94A3B8` | Inputs deshabilitados, placeholders |

---

## 3. Tipografía

```css
/* Importar desde Google Fonts */
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=DM+Sans:wght@400;500;700&display=swap');
```

| Token | Fuente | Peso | Tamaño | Uso |
|-------|--------|------|--------|-----|
| `--font-heading` | DM Sans | 700 | Variable | Títulos de módulo, encabezados de sección |
| `--font-body` | Inter | 400 | 14px | Texto general, celdas de tabla |
| `--font-label` | Inter | 500 | 12px | Labels de formulario, badges |
| `--font-metric` | DM Sans | 700 | 24–32px | Números en tarjetas de KPI |
| `--font-code` | JetBrains Mono | 400 | 13px | Códigos (GOL1.CG1, OKR.1) |

### Escala tipográfica

| Clase | Tamaño | Peso | Uso |
|-------|--------|------|-----|
| `.h-page` | 22px | 700 (DM Sans) | Título de la página |
| `.h-section` | 16px | 600 (DM Sans) | Encabezado de sección / card |
| `.h-subsection` | 14px | 600 (Inter) | Subtítulo dentro de sección |
| `.body-md` | 14px | 400 (Inter) | Texto normal |
| `.body-sm` | 13px | 400 (Inter) | Texto secundario, metadata |
| `.label` | 12px | 500 (Inter) | Labels, badges, códigos |
| `.metric-xl` | 32px | 700 (DM Sans) | Métrica principal de KPI card |
| `.metric-lg` | 24px | 700 (DM Sans) | Métrica secundaria |

---

## 4. Layout General

### 4.1 Estructura de Página

```
┌─────────────────────────────────────────────────────────┐
│  NAVBAR SUPERIOR (64px · Navy #0B2545)                  │
│  [Logo] [Nombre Tenant · Ciclo activo]  [🔔] [Avatar]  │
├──────────────┬──────────────────────────────────────────┤
│              │                                          │
│   SIDEBAR    │         CONTENT AREA                    │
│   (240px)    │         (flex-grow)                     │
│   Navy       │         bg: #F0F4F8                     │
│   #0B2545    │                                         │
│              │  ┌──────────────────────────────────┐  │
│  [Módulos]   │  │  PAGE HEADER (título + breadcrumb) │  │
│              │  └──────────────────────────────────┘  │
│              │                                          │
│              │  ┌──────────────────────────────────┐  │
│              │  │  CONTENT CARDS                   │  │
│              │  └──────────────────────────────────┘  │
│              │                                         │
└──────────────┴─────────────────────────────────────────┘
```

### 4.2 Sidebar

```css
.sidebar {
    width: 240px;
    background: var(--color-navy);
    height: 100vh;
    position: fixed;
    overflow-y: auto;
}

.sidebar-logo {
    padding: 20px 16px;
    border-bottom: 1px solid rgba(255,255,255,0.1);
}

.sidebar-nav-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 16px;
    color: rgba(255,255,255,0.75);
    font: 500 13px/1 'Inter', sans-serif;
    border-radius: 6px;
    margin: 2px 8px;
    cursor: pointer;
    transition: all 0.15s;
}

.sidebar-nav-item:hover,
.sidebar-nav-item.active {
    background: rgba(26,115,232,0.25);
    color: #FFFFFF;
}

.sidebar-nav-item.active {
    border-left: 3px solid var(--color-accent);
}

.sidebar-section-label {
    padding: 16px 16px 4px;
    font: 600 10px/1 'Inter', sans-serif;
    color: rgba(255,255,255,0.4);
    text-transform: uppercase;
    letter-spacing: 0.08em;
}
```

### 4.3 Navbar Superior

```css
.navbar-top {
    height: 64px;
    background: var(--color-navy);
    border-bottom: 1px solid rgba(255,255,255,0.08);
    display: flex;
    align-items: center;
    padding: 0 24px 0 16px;
    position: fixed;
    width: 100%;
    z-index: 100;
}

.navbar-tenant-info {
    font: 500 13px 'Inter';
    color: rgba(255,255,255,0.85);
}

.navbar-ciclo-badge {
    background: rgba(0,180,166,0.2);
    color: var(--color-accent);
    padding: 3px 8px;
    border-radius: 4px;
    font: 600 11px 'Inter';
}
```

### 4.4 Sidebar: Estructura de Navegación por Rol

**JefeArea:**
```
[Icono] Inicio (Mi Área)
────── PLANIFICACIÓN ──────
[Icono] Objetivos CG
[Icono] Plan de Acción
[Icono] OKRs
────── PRESUPUESTO ──────
[Icono] CAPEX
[Icono] OPEX
```

**Gerente:**
```
[Icono] Dashboard Consolidado
────── ESTRATEGIA ──────
[Icono] Filosofía Corporativa
[Icono] Pilares
[Icono] Objetivos CG (todas las áreas)
────── PLAN OPERATIVO ──────
[Icono] Plan de Acción
[Icono] OKRs
────── PRESUPUESTO ──────
[Icono] CAPEX
[Icono] OPEX
────── REPORTES ──────
[Icono] Informe Estratégico
[Icono] Informe Plan de Acción
```

**AdminTenant:**
```
[Icono] Panel de Administración
────── CONFIGURACIÓN ──────
[Icono] Empresa y Ciclos
[Icono] Áreas
[Icono] Responsables (Usuarios)
[Icono] Semáforos
```

---

## 5. Componentes Base

### 5.1 Cards de KPI (Tarjetas de Métricas)

```html
<!-- KPI Card estándar -->
<div class="kpi-card">
    <div class="kpi-card__icon">
        <i class="bi bi-check-circle"></i>
    </div>
    <div class="kpi-card__body">
        <span class="kpi-card__label">OKRs Alcanzados</span>
        <span class="kpi-card__value">7</span>
        <span class="kpi-card__total">de 10 OKRs</span>
    </div>
    <div class="kpi-card__semaforo kpi-card__semaforo--verde"></div>
</div>
```

```css
.kpi-card {
    background: var(--color-bg-card);
    border: 1px solid var(--color-border);
    border-radius: 10px;
    padding: 20px;
    display: flex;
    align-items: flex-start;
    gap: 16px;
    position: relative;
    overflow: hidden;
    box-shadow: 0 1px 3px rgba(0,0,0,0.06);
}

.kpi-card__icon {
    width: 44px;
    height: 44px;
    background: var(--color-accent-light);
    border-radius: 10px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--color-accent);
    font-size: 20px;
    flex-shrink: 0;
}

.kpi-card__label  { font: 500 12px 'Inter'; color: var(--color-text-secondary); display: block; }
.kpi-card__value  { font: 700 32px 'DM Sans'; color: var(--color-text-primary); display: block; line-height: 1.1; }
.kpi-card__total  { font: 400 12px 'Inter'; color: var(--color-text-secondary); }

/* Barra de color semáforo en borde izquierdo */
.kpi-card::before {
    content: '';
    position: absolute;
    left: 0; top: 0; bottom: 0;
    width: 4px;
    border-radius: 10px 0 0 10px;
}
.kpi-card--verde::before  { background: var(--color-verde); }
.kpi-card--amarillo::before { background: var(--color-amarillo); }
.kpi-card--rojo::before   { background: var(--color-rojo); }
```

### 5.2 Indicador de Semáforo (Badge)

```html
<span class="semaforo semaforo--verde">●  Verde</span>
<span class="semaforo semaforo--amarillo">●  En Peligro</span>
<span class="semaforo semaforo--rojo">●  Atrasado</span>
```

```css
.semaforo {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    padding: 3px 10px;
    border-radius: 20px;
    font: 600 11px 'Inter';
}
.semaforo--verde    { background: var(--color-verde-bg);    color: #1E6B35; }
.semaforo--amarillo { background: var(--color-amarillo-bg); color: #8A5E00; }
.semaforo--rojo     { background: var(--color-rojo-bg);     color: #B71C1C; }
```

### 5.3 Progress Bar con Semáforo

```html
<div class="progress-semaforo">
    <div class="progress-semaforo__bar">
        <div class="progress-semaforo__fill progress-semaforo__fill--verde"
             style="width: 92%"></div>
    </div>
    <span class="progress-semaforo__label">92%</span>
</div>
```

```css
.progress-semaforo {
    display: flex;
    align-items: center;
    gap: 10px;
}
.progress-semaforo__bar {
    flex: 1;
    height: 8px;
    background: var(--color-border);
    border-radius: 4px;
    overflow: hidden;
}
.progress-semaforo__fill {
    height: 100%;
    border-radius: 4px;
    transition: width 0.4s ease;
}
.progress-semaforo__fill--verde    { background: var(--color-verde); }
.progress-semaforo__fill--amarillo { background: var(--color-amarillo); }
.progress-semaforo__fill--rojo     { background: var(--color-rojo); }
.progress-semaforo__label { font: 600 12px 'Inter'; min-width: 36px; color: var(--color-text-primary); }
```

### 5.4 Tabla de Datos Estándar

```css
.tabla-pe {
    width: 100%;
    border-collapse: separate;
    border-spacing: 0;
    background: var(--color-bg-card);
    border-radius: 10px;
    overflow: hidden;
    border: 1px solid var(--color-border);
}

.tabla-pe thead th {
    background: #F8FAFC;
    padding: 11px 14px;
    font: 600 12px 'Inter';
    color: var(--color-text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    border-bottom: 1px solid var(--color-border);
    white-space: nowrap;
}

.tabla-pe tbody td {
    padding: 12px 14px;
    font: 400 13px 'Inter';
    color: var(--color-text-primary);
    border-bottom: 1px solid var(--color-border);
    vertical-align: middle;
}

.tabla-pe tbody tr:last-child td { border-bottom: none; }

.tabla-pe tbody tr:hover { background: #F8FAFC; }

/* Celda de código (GOL1.CG1) */
.tabla-pe .cell-codigo {
    font-family: 'JetBrains Mono', monospace;
    font-size: 12px;
    color: var(--color-action);
    background: #EEF4FF;
    padding: 2px 7px;
    border-radius: 4px;
    display: inline-block;
}
```

### 5.5 Botones

```css
/* Primario */
.btn-pe {
    padding: 8px 18px;
    border-radius: 7px;
    font: 500 13px 'Inter';
    border: none;
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 6px;
    transition: all 0.15s;
}

.btn-pe--primary {
    background: var(--color-action);
    color: #FFFFFF;
}
.btn-pe--primary:hover { background: var(--color-action-hover); }

/* Secundario outline */
.btn-pe--secondary {
    background: transparent;
    border: 1px solid var(--color-border-dark);
    color: var(--color-text-primary);
}
.btn-pe--secondary:hover { background: #F8FAFC; }

/* Peligro */
.btn-pe--danger {
    background: var(--color-rojo);
    color: #FFFFFF;
}
.btn-pe--danger:hover { background: #C62828; }

/* Tamaños */
.btn-pe--sm { padding: 5px 12px; font-size: 12px; border-radius: 5px; }
.btn-pe--lg { padding: 11px 24px; font-size: 14px; }
```

### 5.6 Form Inputs

```css
.form-pe-label {
    font: 500 12px 'Inter';
    color: var(--color-text-secondary);
    margin-bottom: 5px;
    display: block;
}

.form-pe-input,
.form-pe-select {
    width: 100%;
    padding: 9px 12px;
    border: 1px solid var(--color-border-dark);
    border-radius: 7px;
    font: 400 13px 'Inter';
    color: var(--color-text-primary);
    background: #FFFFFF;
    transition: border-color 0.15s, box-shadow 0.15s;
    outline: none;
}

.form-pe-input:focus,
.form-pe-select:focus {
    border-color: var(--color-action);
    box-shadow: 0 0 0 3px rgba(26,115,232,0.12);
}

.form-pe-input.is-invalid {
    border-color: var(--color-rojo);
}

.form-pe-input.is-invalid:focus {
    box-shadow: 0 0 0 3px rgba(234,67,53,0.12);
}

.form-pe-error {
    font: 400 11px 'Inter';
    color: var(--color-rojo);
    margin-top: 4px;
}
```

### 5.7 Modal Estándar

```css
.modal-pe .modal-content {
    border: none;
    border-radius: 12px;
    box-shadow: 0 20px 60px rgba(0,0,0,0.15);
}

.modal-pe .modal-header {
    background: var(--color-navy);
    color: #FFFFFF;
    padding: 16px 24px;
    border-radius: 12px 12px 0 0;
    border: none;
}

.modal-pe .modal-header .modal-title {
    font: 600 16px 'DM Sans';
}

.modal-pe .modal-body { padding: 24px; }

.modal-pe .modal-footer {
    padding: 16px 24px;
    border-top: 1px solid var(--color-border);
}
```

### 5.8 Page Header

```html
<div class="page-header">
    <div class="page-header__breadcrumb">
        <span>Plan de Acción</span>
        <i class="bi bi-chevron-right"></i>
        <span class="active">CEDIS FARMA</span>
    </div>
    <h1 class="page-header__title">Plan de Acción</h1>
    <p class="page-header__subtitle">GOL1 · CEDIS FARMA · Ciclo PE 2026</p>
</div>
```

```css
.page-header {
    margin-bottom: 24px;
}
.page-header__breadcrumb {
    font: 400 12px 'Inter';
    color: var(--color-text-secondary);
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 8px;
}
.page-header__breadcrumb .active { color: var(--color-text-primary); font-weight: 500; }
.page-header__title    { font: 700 22px 'DM Sans'; color: var(--color-text-primary); margin: 0 0 4px; }
.page-header__subtitle { font: 400 13px 'Inter'; color: var(--color-text-secondary); margin: 0; }
```

---

## 6. Componentes Específicos del Dominio

### 6.1 Card de Área (Dashboard Gerente)

```html
<div class="area-card area-card--amarillo">
    <div class="area-card__header">
        <span class="area-card__codigo">GOL1</span>
        <span class="semaforo semaforo--amarillo">● En Peligro</span>
    </div>
    <div class="area-card__nombre">CEDIS FARMA</div>
    <div class="area-card__responsable">
        <i class="bi bi-person"></i> Brandon Hernández
    </div>
    <div class="area-card__metrics">
        <div class="area-card__metric">
            <span class="area-card__metric-value">72%</span>
            <span class="area-card__metric-label">OKRs</span>
        </div>
        <div class="area-card__metric">
            <span class="area-card__metric-value">68%</span>
            <span class="area-card__metric-label">Plan</span>
        </div>
        <div class="area-card__metric area-card__metric--alerta">
            <span class="area-card__metric-value">3</span>
            <span class="area-card__metric-label">Atrasadas</span>
        </div>
    </div>
</div>
```

### 6.2 Fila de Acción del Plan (tabla)

Columnas estándar para la tabla del Plan de Acción:

| Columna | Ancho | Contenido |
|---------|-------|-----------|
| Código | 90px | Badge monoespacio `1.1.1` |
| Acción | auto | Texto descripción truncado a 2 líneas |
| Responsable | 130px | Nombre corto |
| Fechas | 140px | `Inicio → Venc.` formato corto |
| Tipo | 90px | Badge Proyecto/Iniciativa/Operativa |
| Peso | 60px | `30%` alineado a la derecha |
| Progreso | 140px | Progress bar + `%` |
| Status | 100px | Semáforo badge |
| Acciones | 80px | Botones icono: Editar · Adjuntos · Historial |

### 6.3 Grilla OKR/KR

```html
<div class="okr-card">
    <div class="okr-card__header">
        <span class="cell-codigo">OKR.1</span>
        <span class="okr-card__descripcion">Mejorar Rentabilidad y Eficiencia</span>
        <span class="semaforo semaforo--verde">● Alcanzado</span>
        <span class="okr-card__puntuacion">0.91</span>
    </div>
    <div class="okr-card__krs">
        <!-- KR row con grilla de 12 meses -->
        <div class="kr-row">
            <div class="kr-row__info">
                <span class="kr-row__codigo">KR.1</span>
                <span class="kr-row__descripcion">Reducir OPEX almacenamiento 15%</span>
                <span class="kr-row__peso">40%</span>
            </div>
            <div class="kr-row__meses">
                <!-- 12 celdas input ENE-DIC -->
            </div>
            <div class="kr-row__totales">
                <span>Q1: 0.8</span>
                <span>Q2: 0.9</span>
                <span>Final: 0.93</span>
                <span>Pond.: 0.37</span>
            </div>
        </div>
    </div>
</div>
```

### 6.4 Grilla CAPEX/OPEX Mensual

Patrón de grilla para presupuestos:

```css
.grid-presupuesto {
    display: grid;
    grid-template-columns: [nombre] 200px [meses] repeat(12, 1fr) [q1] 80px [q2] 80px [q3] 80px [q4] 80px [total] 90px;
    overflow-x: auto;
}

.grid-presupuesto__header-mes {
    font: 600 11px 'Inter';
    color: var(--color-text-secondary);
    text-align: center;
    padding: 8px 4px;
    background: #F8FAFC;
    border-bottom: 2px solid var(--color-border);
}

.grid-presupuesto__cell-input {
    padding: 4px;
}

.grid-presupuesto__cell-input input {
    width: 100%;
    text-align: right;
    border: 1px solid transparent;
    border-radius: 4px;
    padding: 5px 6px;
    font: 400 12px 'Inter';
}

.grid-presupuesto__cell-input input:focus {
    border-color: var(--color-action);
    background: #EEF4FF;
}

/* Real vs presupuesto */
.cell-real-positivo { color: var(--color-rojo);   background: var(--color-rojo-bg); }
.cell-real-negativo { color: var(--color-verde);  background: var(--color-verde-bg); }
```

---

## 7. Iconografía

Se usa **Bootstrap Icons** (`bootstrap-icons@1.11.x`) — ya incluido con Bootstrap 5.3.

| Módulo | Ícono BS |
|--------|----------|
| Dashboard | `bi-speedometer2` |
| Filosofía / Valores | `bi-lightbulb` |
| Pilares | `bi-columns-gap` |
| Áreas | `bi-diagram-3` |
| Objetivos CG | `bi-bullseye` |
| Plan de Acción | `bi-list-check` |
| Gantt | `bi-bar-chart-steps` |
| OKRs | `bi-graph-up-arrow` |
| CAPEX | `bi-coin` |
| OPEX | `bi-receipt` |
| Memoria de Cálculo | `bi-calculator` |
| Reportes | `bi-file-earmark-bar-graph` |
| Notificaciones | `bi-bell` |
| Configuración | `bi-gear` |
| Usuarios | `bi-people` |
| Adjuntar | `bi-paperclip` |
| Semáforo Verde | `bi-circle-fill` (color verde) |
| Semáforo Amarillo | `bi-circle-fill` (color amarillo) |
| Semáforo Rojo | `bi-circle-fill` (color rojo) |

---

## 8. Estados Vacíos (Empty States)

```html
<div class="empty-state">
    <i class="bi bi-clipboard-x empty-state__icon"></i>
    <h3 class="empty-state__title">No hay acciones registradas</h3>
    <p class="empty-state__message">
        Agrega la primera acción a este objetivo para comenzar el seguimiento.
    </p>
    <button class="btn-pe btn-pe--primary">
        <i class="bi bi-plus-lg"></i> Nueva Acción
    </button>
</div>
```

```css
.empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 60px 24px;
    text-align: center;
}
.empty-state__icon    { font-size: 48px; color: var(--color-text-disabled); margin-bottom: 16px; }
.empty-state__title   { font: 600 16px 'DM Sans'; color: var(--color-text-primary); margin-bottom: 8px; }
.empty-state__message { font: 400 13px 'Inter'; color: var(--color-text-secondary); max-width: 320px; margin-bottom: 24px; }
```

---

## 9. Gráficas (Chart.js)

Configuración base para Chart.js alineada al design system:

```javascript
// Colores globales para gráficas
const PE_COLORS = {
    verde:    '#34A853',
    amarillo: '#F9AB00',
    rojo:     '#EA4335',
    action:   '#1A73E8',
    accent:   '#00B4A6',
    navy:     '#0B2545',
    gray:     '#E2E8F0',
    // Para series múltiples (áreas)
    series: ['#1A73E8','#00B4A6','#F9AB00','#EA4335','#9C27B0','#FF5722','#4CAF50']
};

// Plugin global de Chart.js
Chart.defaults.font.family = "'Inter', sans-serif";
Chart.defaults.font.size = 12;
Chart.defaults.color = '#64748B';
Chart.defaults.plugins.legend.labels.boxWidth = 12;
Chart.defaults.plugins.legend.labels.padding = 16;

// Configuración base de tooltips
const tooltipBase = {
    backgroundColor: '#1E293B',
    titleColor: '#FFFFFF',
    bodyColor: 'rgba(255,255,255,0.8)',
    padding: 10,
    cornerRadius: 6,
    displayColors: true
};
```

### Tipos de gráficas usados por módulo

| Gráfica | Módulo | Tipo Chart.js |
|---------|--------|---------------|
| Acciones por status | Dashboard | `doughnut` |
| OKRs alcanzados vs en peligro | Dashboard | `doughnut` |
| Avance por objetivo CG | Dashboard | `bar` horizontal |
| Comparativa por área | Dashboard Gerente | `bar` agrupado |
| Evolución mensual KR | Dashboard OKRs | `line` |
| Progreso del Plan de Acción por área | Dashboard Gerente | `bar` |

---

## 10. Variables CSS Completas (custom.css)

```css
:root {
    /* Colores Primarios */
    --color-navy:           #0B2545;
    --color-navy-light:     #1A3A5C;
    --color-action:         #1A73E8;
    --color-action-hover:   #1557B0;
    --color-accent:         #00B4A6;
    --color-accent-light:   #E0F7F5;

    /* Semáforo */
    --color-verde:          #34A853;
    --color-verde-bg:       #E6F4EA;
    --color-amarillo:       #F9AB00;
    --color-amarillo-bg:    #FEF7E0;
    --color-rojo:           #EA4335;
    --color-rojo-bg:        #FCE8E6;

    /* Neutros */
    --color-bg-page:        #F0F4F8;
    --color-bg-card:        #FFFFFF;
    --color-border:         #E2E8F0;
    --color-border-dark:    #CBD5E1;
    --color-text-primary:   #1E293B;
    --color-text-secondary: #64748B;
    --color-text-disabled:  #94A3B8;

    /* Tipografía */
    --font-heading:         'DM Sans', sans-serif;
    --font-body:            'Inter', sans-serif;

    /* Layout */
    --sidebar-width:        240px;
    --navbar-height:        64px;
    --border-radius-card:   10px;
    --border-radius-btn:    7px;
    --shadow-card:          0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04);
    --shadow-modal:         0 20px 60px rgba(0,0,0,0.15);

    /* Transiciones */
    --transition-fast:      0.15s ease;
    --transition-normal:    0.25s ease;
}
```

---

## 11. Responsive

| Breakpoint | Comportamiento |
|------------|---------------|
| `≥ 1280px` (Desktop) | Sidebar fija 240px · content area fluida |
| `≥ 1024px` (Laptop) | Sidebar fija 200px · grillas presupuesto con scroll horizontal |
| `≥ 768px` (Tablet) | Sidebar colapsada a iconos (48px) · Hamburger para expandir |
| `< 768px` | Sidebar como drawer overlay · Tablas colapsadas a cards |

---

*Documento generado el 13/09/2026 · Fase 2 — Design System.*
*Alineado al estilo Freiroute TMS · Bootstrap 5.3 · Bootstrap Icons 1.11.*

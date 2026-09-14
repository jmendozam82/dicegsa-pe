# 🧩 Modelo de Dominio — PE-GOL SaaS
## Entidades, Agregados y Relaciones del Negocio · Versión 1.0

---

## 1. Mapa de Dominios

El sistema se divide en **5 dominios funcionales** con sus entidades y relaciones:

```
┌──────────────────────────────────────────────────────────────────┐
│  DOMINIO SaaS                                                    │
│  Tenant · Plan · Usuario · Rol · LogAuditoria                   │
└──────────────────────────┬───────────────────────────────────────┘
                           │ 1:N
┌──────────────────────────▼───────────────────────────────────────┐
│  DOMINIO CICLO                                                   │
│  Ciclo · ConfiguracionCiclo · UmbralSemaforo                    │
└──────┬────────────────────┬────────────────────┬─────────────────┘
       │ 1:N                │ 1:N                │ 1:N
┌──────▼──────┐    ┌────────▼────────┐   ┌──────▼──────────────────┐
│  DOMINIO    │    │  DOMINIO        │   │  DOMINIO                │
│  ESTRATEGIA │    │  PLAN OPERATIVO │   │  PRESUPUESTO            │
│             │    │                 │   │                         │
│  Filosofia  │    │  AccionPlan     │   │  ProyectoCapex          │
│  Pilar      │    │  Entregable     │   │  DesembolsoCapex        │
│  Area       │    │  HistorialProg  │   │  CuentaOpex             │
│  ObjetivoCG │    │                 │   │  SubcuentaOpex          │
│  OKR        │    └─────────────────┘   │  PresupuestoOpex        │
│  KeyResult  │                          │  MemoriaCalculo         │
│  ValorMKR   │    ┌─────────────────┐   │  RubroMaterial          │
└─────────────┘    │  DOMINIO        │   └─────────────────────────┘
                   │  COMUNICACION   │
                   │                 │
                   │  Notificacion   │
                   │  Alerta         │
                   │  ResumenEmail   │
                   └─────────────────┘
```

---

## 2. Dominio SaaS

### Tenant
Representa una **Gerencia** — unidad organizacional raíz del sistema. Cada tenant es completamente aislado.

```
Tenant
├── id                  UUID PK
├── nombre              VARCHAR(150)       — "Gerencia de Operaciones Logística"
├── descripcion         TEXT
├── plan_id             UUID FK → Plan
├── logo_url            TEXT               — URL en Supabase Storage
├── eslogan             VARCHAR(200)
├── zona_horaria        VARCHAR(50)        — "America/Managua"
├── estado              ENUM(Activo, Inactivo)
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### Plan
Define los límites de suscripción del tenant.

```
Plan
├── id                  UUID PK
├── nombre              VARCHAR(50)        — Básico | Estándar | Premium
├── max_areas           INT                — máximo de áreas por ciclo
├── max_usuarios        INT
├── max_ciclos_activos  INT
└── descripcion         TEXT
```

### Usuario
Cualquier persona que accede al sistema.

```
Usuario
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── nombre              VARCHAR(150)
├── correo              VARCHAR(200) UNIQUE
├── password_hash       TEXT               — BCrypt coste 12
├── rol                 ENUM(SuperAdmin, AdminTenant, Gerente, JefeArea)
├── area_id             UUID FK → Area     — solo para JefeArea, nullable
├── estado              ENUM(Activo, Inactivo, Bloqueado)
├── intentos_fallidos   INT DEFAULT 0
├── bloqueado_hasta     TIMESTAMPTZ        — nullable
├── ultimo_login        TIMESTAMPTZ
├── requiere_cambio_pwd BOOLEAN DEFAULT true
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### RefreshToken
```
RefreshToken
├── id                  UUID PK
├── usuario_id          UUID FK → Usuario
├── token               TEXT UNIQUE
├── expira_en           TIMESTAMPTZ
├── revocado            BOOLEAN DEFAULT false
└── created_at          TIMESTAMPTZ
```

### LogAuditoria
Inmutable. Solo inserts.

```
LogAuditoria
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant   — nullable (acciones del SuperAdmin)
├── usuario_id          UUID FK → Usuario
├── accion              VARCHAR(50)        — CREATE | UPDATE | DELETE | LOGIN | LOGOUT
├── entidad             VARCHAR(100)       — nombre de la tabla/entidad afectada
├── entidad_id          TEXT               — PK del registro afectado
├── valor_anterior      JSONB              — snapshot antes del cambio
├── valor_nuevo         JSONB              — snapshot después del cambio
└── created_at          TIMESTAMPTZ
```

---

## 3. Dominio Ciclo

### Ciclo
Unidad de planificación anual. Un tenant puede tener múltiples ciclos pero solo uno Activo.

```
Ciclo
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── nombre              VARCHAR(100)       — "PE 2026"
├── año_fiscal          INT                — 2026
├── mes_inicio          INT                — 1=Enero ... 12=Diciembre
├── estado              ENUM(Borrador, Activo, Cerrado)
├── created_by          UUID FK → Usuario
├── activated_at        TIMESTAMPTZ        — cuando pasó a Activo
├── closed_at           TIMESTAMPTZ        — cuando fue Cerrado
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### UmbralSemaforo
Configuración de semáforos por ciclo. No modificable una vez el ciclo está Activo.

```
UmbralSemaforo
├── id                  UUID PK
├── ciclo_id            UUID FK → Ciclo
├── tipo                ENUM(KPI, PlanAccion)
├── umbral_verde        DECIMAL(3,2)       — ej: 0.90
├── umbral_amarillo     DECIMAL(3,2)       — ej: 0.70
└── updated_at          TIMESTAMPTZ
```

**Lógica de semáforo:**
- Verde   → valor >= umbral_verde
- Amarillo → umbral_amarillo <= valor < umbral_verde
- Rojo    → valor < umbral_amarillo

---

## 4. Dominio Estrategia

### Filosofia
Una por ciclo por tenant.

```
Filosofia
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── vision              TEXT
├── mision              TEXT
├── valores             JSONB              — ["Liderazgo","Excelencia","Integridad",...]
├── updated_by          UUID FK → Usuario
└── updated_at          TIMESTAMPTZ
```

### Pilar
Ejes estratégicos corporativos.

```
Pilar
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── codigo              VARCHAR(10)        — "PEC-1", auto-generado
├── nombre              VARCHAR(150)
├── estrategia_victoria TEXT
├── objetivo_q1         TEXT               — nullable
├── objetivo_q2         TEXT               — nullable
├── objetivo_q3         TEXT               — nullable
├── objetivo_q4         TEXT               — nullable
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### Area
Unidad operativa. Un Jefe por área.

```
Area
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── codigo              VARCHAR(10)        — "GOL1", auto-generado
├── nombre              VARCHAR(150)       — "CEDIS FARMA"
├── comentarios         TEXT               — nullable
├── responsable_id      UUID FK → Usuario  — JefeArea asignado
├── orden               INT
├── activa              BOOLEAN DEFAULT true
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### ObjetivoCG
Objetivo estratégico de un área. "¿Cómo Ganaremos?"

```
ObjetivoCG
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── area_id             UUID FK → Area
├── pilar_id            UUID FK → Pilar
├── codigo              VARCHAR(15)        — "GOL1.CG1", auto-generado
├── descripcion         TEXT
├── trimestre_objetivo  ENUM(Q1, Q2, Q3, Q4)
├── progreso            DECIMAL(5,4)       — 0.0000 a 1.0000, calculado
├── semaforo            ENUM(Verde, Amarillo, Rojo)  — calculado
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### OKR
Objetivo con resultados clave. Alineado a un pilar.

```
OKR
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── area_id             UUID FK → Area
├── pilar_id            UUID FK → Pilar
├── codigo              VARCHAR(10)        — "OKR.1", auto-generado por área
├── descripcion         TEXT
├── puntuacion_final    DECIMAL(4,3)       — 0.000 a 1.000, calculado
├── semaforo            ENUM(Verde, Amarillo, Rojo)  — calculado
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### KeyResult
Resultado clave medible de un OKR.

```
KeyResult
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── okr_id              UUID FK → OKR
├── codigo              VARCHAR(10)        — "KR.1"
├── descripcion         TEXT
├── peso                DECIMAL(4,3)       — 0.000 a 1.000 (suma KRs del OKR = 1.0)
├── puntuacion_q1       DECIMAL(4,3)       — calculado
├── puntuacion_q2       DECIMAL(4,3)       — calculado
├── puntuacion_q3       DECIMAL(4,3)       — calculado
├── puntuacion_q4       DECIMAL(4,3)       — calculado
├── puntuacion_final    DECIMAL(4,3)       — calculado
├── puntuacion_ponderada DECIMAL(4,3)      — puntuacion_final × peso, calculado
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### ValorMensualKR
Registro del valor real mensual ingresado por el Jefe.

```
ValorMensualKR
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── key_result_id       UUID FK → KeyResult
├── mes                 INT                — 1=Enero ... 12=Diciembre
├── valor               DECIMAL(2,1)       — 0.0 a 1.0
├── registrado_por      UUID FK → Usuario
└── updated_at          TIMESTAMPTZ
```

**Restricción:** UNIQUE (key_result_id, mes). Solo un valor por KR por mes.

---

## 5. Dominio Plan Operativo

### AccionPlan
Acción concreta que implementa un Objetivo CG.

```
AccionPlan
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── area_id             UUID FK → Area
├── objetivo_cg_id      UUID FK → ObjetivoCG
├── codigo              VARCHAR(20)        — "1.1.1" (área.cg.acción), auto-generado
├── descripcion         TEXT
├── descripcion_entregable TEXT
├── responsable_id      UUID FK → Usuario
├── fecha_inicio        DATE
├── fecha_vencimiento   DATE
├── clasificacion       ENUM(Proyecto, Iniciativa, Operativa)
├── tipo_presupuesto    ENUM(OPEX, CAPEX)
├── peso                DECIMAL(4,3)       — 0.000 a 1.000
├── aclaraciones        TEXT               — nullable
├── progreso            DECIMAL(5,2)       — 0.00 a 100.00 (%)
├── puntuacion_ponderada DECIMAL(6,5)      — peso × (progreso/100), calculado
├── status              ENUM(NoIniciado, EnProgreso, Terminado, Atrasado)  — calculado
├── alerta_enviada      BOOLEAN DEFAULT false  — control de alerta de atraso
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### EntregableAdjunto
Archivo adjunto a una acción del plan.

```
EntregableAdjunto
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── accion_id           UUID FK → AccionPlan
├── nombre_archivo      VARCHAR(255)
├── file_path           TEXT               — path en Supabase Storage
├── file_size_bytes     INT
├── tipo_mime           VARCHAR(100)
├── subido_por          UUID FK → Usuario
└── created_at          TIMESTAMPTZ
```

### HistorialProgreso
Trazabilidad de cambios de progreso en acciones.

```
HistorialProgreso
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── accion_id           UUID FK → AccionPlan
├── progreso_anterior   DECIMAL(5,2)
├── progreso_nuevo      DECIMAL(5,2)
├── status_anterior     ENUM(NoIniciado, EnProgreso, Terminado, Atrasado)
├── status_nuevo        ENUM(NoIniciado, EnProgreso, Terminado, Atrasado)
├── registrado_por      UUID FK → Usuario
└── created_at          TIMESTAMPTZ
```

---

## 6. Dominio Presupuesto

### ProyectoCapex
Proyecto de inversión de capital por área.

```
ProyectoCapex
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── area_id             UUID FK → Area
├── area_colaboracion   VARCHAR(150)       — nullable
├── colaborador         VARCHAR(150)       — nullable
├── nombre_proyecto     VARCHAR(255)
├── impacto             TEXT
├── presupuesto_aprobado DECIMAL(15,2)    — en Córdobas C$
├── total_planeado      DECIMAL(15,2)      — calculado (suma desembolsos planeados)
├── total_real          DECIMAL(15,2)      — calculado (suma desembolsos reales)
├── variacion           DECIMAL(15,2)      — total_real - total_planeado, calculado
├── status              ENUM(NoIniciado, EnProceso, Ejecutado, Atrasado)  — calculado
├── cumplimiento        ENUM(Cumple, NoCumple)  — calculado
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### DesembolsoCapex
Distribución mensual planeada y real del CAPEX.

```
DesembolsoCapex
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── proyecto_capex_id   UUID FK → ProyectoCapex
├── mes                 INT                — 1 a 12
├── monto_planeado      DECIMAL(15,2)      — C$
├── monto_real          DECIMAL(15,2)      — C$, nullable
└── updated_at          TIMESTAMPTZ
```

**Restricción:** UNIQUE (proyecto_capex_id, mes).

### CuentaOpex
Categoría principal del gasto operativo.

```
CuentaOpex
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── ciclo_id            UUID FK → Ciclo
├── area_id             UUID FK → Area
├── codigo              VARCHAR(20)
├── nombre              VARCHAR(150)
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### SubcuentaOpex
Subcategoría de gasto dentro de una Cuenta.

```
SubcuentaOpex
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── cuenta_opex_id      UUID FK → CuentaOpex
├── codigo              VARCHAR(20)
├── nombre              VARCHAR(150)
├── tiene_memoria_calculo BOOLEAN DEFAULT false  — Gastos Oficina, Gastos Limpieza
├── orden               INT
├── created_at          TIMESTAMPTZ
└── updated_at          TIMESTAMPTZ
```

### PresupuestoOpex
Valores planeados y reales mensuales por subcuenta.

```
PresupuestoOpex
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── subcuenta_opex_id   UUID FK → SubcuentaOpex
├── mes                 INT                — 1 a 12
├── monto_presupuestado DECIMAL(15,2)      — C$, fluye desde memoria_calculo si aplica
├── monto_real          DECIMAL(15,2)      — C$, nullable
└── updated_at          TIMESTAMPTZ
```

**Restricción:** UNIQUE (subcuenta_opex_id, mes).

### RubroMaterial
Ítem de la memoria de cálculo de una subcuenta especial.

```
RubroMaterial
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── subcuenta_opex_id   UUID FK → SubcuentaOpex
├── nombre_material     VARCHAR(200)       — ej: "Resma de papel carta"
├── unidad_medida       VARCHAR(50)        — ej: "Resma", "Galón", "Unidad"
├── cantidad            DECIMAL(10,2)
├── precio_unitario     DECIMAL(15,2)      — C$
├── total               DECIMAL(15,2)      — calculado: cantidad × precio_unitario
├── orden               INT
└── updated_at          TIMESTAMPTZ
```

---

## 7. Dominio Comunicación

### Notificacion
Notificación in-app para usuarios.

```
Notificacion
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── usuario_destino_id  UUID FK → Usuario
├── tipo                ENUM(ProgresoActualizado, OKRPeligro, AccionAtrasada, ResumenSemanal)
├── titulo              VARCHAR(200)
├── mensaje             TEXT
├── entidad_tipo        VARCHAR(100)       — "AccionPlan" | "OKR" | etc.
├── entidad_id          UUID               — ID del registro relacionado
├── leida               BOOLEAN DEFAULT false
├── leida_en            TIMESTAMPTZ        — nullable
└── created_at          TIMESTAMPTZ
```

### AlertaEnviada
Control de alertas de correo enviadas (evita duplicados).

```
AlertaEnviada
├── id                  UUID PK
├── tenant_id           UUID FK → Tenant
├── tipo                ENUM(AccionAtrasada, OKRPeligro, ResumenGerencial)
├── entidad_id          UUID               — accion_id u okr_id
├── periodo             VARCHAR(10)        — "2026-09" para alertas mensuales
├── enviada_en          TIMESTAMPTZ
└── destinatario        VARCHAR(200)       — correo destinatario
```

---

## 8. Diagrama de Relaciones Simplificado

```
Tenant (1)
  ├──(N) Usuario
  ├──(N) Ciclo (1)
  │         ├──(N) UmbralSemaforo
  │         ├──(1) Filosofia
  │         ├──(N) Pilar
  │         └──(N) Area (1)
  │                   ├──(N) ObjetivoCG (1)
  │                   │         └──(N) AccionPlan (1)
  │                   │                   └──(N) EntregableAdjunto
  │                   │                   └──(N) HistorialProgreso
  │                   ├──(N) OKR (1)
  │                   │         └──(N) KeyResult (1)
  │                   │                   └──(N) ValorMensualKR
  │                   ├──(N) ProyectoCapex (1)
  │                   │         └──(12) DesembolsoCapex
  │                   └──(N) CuentaOpex (1)
  │                             └──(N) SubcuentaOpex (1)
  │                                       ├──(12) PresupuestoOpex
  │                                       └──(N)  RubroMaterial
  └──(N) Notificacion
  └──(N) AlertaEnviada
  └──(N) LogAuditoria
```

---

## 9. Reglas de Consistencia del Dominio

| Regla | Entidad | Descripción |
|-------|---------|-------------|
| RC-01 | Ciclo | Solo un ciclo con estado=Activo por tenant a la vez |
| RC-02 | Area | Un responsable no puede ser JefeArea de dos áreas en el mismo ciclo |
| RC-03 | ObjetivoCG | El código se auto-genera y nunca se edita manualmente |
| RC-04 | AccionPlan | `fecha_vencimiento >= fecha_inicio` siempre |
| RC-05 | AccionPlan | Suma pesos de acciones por ObjetivoCG debe tender a 1.0 |
| RC-06 | KeyResult | Suma pesos de KRs por OKR debe ser exactamente 1.0 |
| RC-07 | ValorMensualKR | Un solo registro por (key_result_id, mes); upsert en lugar de insert |
| RC-08 | DesembolsoCapex | Un solo registro por (proyecto_capex_id, mes); upsert |
| RC-09 | PresupuestoOpex | Un solo registro por (subcuenta_opex_id, mes); upsert |
| RC-10 | RubroMaterial | `total = cantidad × precio_unitario`, calculado en la BLL antes de guardar |
| RC-11 | AccionPlan | `status` se calcula en BLL, nunca se persiste manualmente por el usuario |
| RC-12 | Ciclo=Cerrado | Ninguna entidad hija puede ser modificada si el ciclo está Cerrado |

---

*Documento generado el 13/09/2026 · Fase 2 — Modelo de Dominio.*

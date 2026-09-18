# 🗄️ Modelo de Datos — PE-GOL SaaS
## DDL PostgreSQL 15 · Row-Level Security · Supabase · Versión 1.0

---

## 1. Convenciones

| Convención | Detalle |
|------------|---------|
| PK | `UUID DEFAULT gen_random_uuid()` |
| FK | `ON DELETE RESTRICT` salvo indicación contraria |
| Timestamps | `TIMESTAMPTZ DEFAULT NOW()` |
| Soft delete | No se usa — se usa campo `estado` o `activa` |
| Schema | `public` (schema único, RLS como aislamiento) |
| Prefijo tabla | Sin prefijo — nombres en snake_case singular |
| tenant_id | Presente en todas las tablas del dominio (excepto `plan`, `log_auditoria` global) |

---

## 2. DDL Completo

```sql
-- ============================================================
-- EXTENSIONES
-- ============================================================
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- ENUMS
-- ============================================================
CREATE TYPE estado_tenant      AS ENUM ('Activo', 'Inactivo');
CREATE TYPE rol_usuario        AS ENUM ('SuperAdmin', 'AdminTenant', 'Gerente', 'JefeArea');
CREATE TYPE estado_usuario     AS ENUM ('Activo', 'Inactivo', 'Bloqueado');
CREATE TYPE estado_ciclo       AS ENUM ('Borrador', 'Activo', 'Cerrado');
CREATE TYPE tipo_umbral        AS ENUM ('KPI', 'PlanAccion');
CREATE TYPE semaforo_color     AS ENUM ('Verde', 'Amarillo', 'Rojo');
CREATE TYPE trimestre          AS ENUM ('Q1', 'Q2', 'Q3', 'Q4');
CREATE TYPE clasificacion_accion AS ENUM ('Proyecto', 'Iniciativa', 'Operativa');
CREATE TYPE tipo_presupuesto   AS ENUM ('OPEX', 'CAPEX');
CREATE TYPE status_accion      AS ENUM ('NoIniciado', 'EnProgreso', 'Terminado', 'Atrasado');
CREATE TYPE status_capex       AS ENUM ('NoIniciado', 'EnProceso', 'Ejecutado', 'Atrasado');
CREATE TYPE cumplimiento_capex AS ENUM ('Cumple', 'NoCumple');
CREATE TYPE tipo_notificacion  AS ENUM ('ProgresoActualizado', 'OKRPeligro', 'AccionAtrasada', 'ResumenSemanal');
CREATE TYPE tipo_alerta        AS ENUM ('AccionAtrasada', 'OKRPeligro', 'ResumenGerencial');
CREATE TYPE accion_auditoria   AS ENUM ('CREATE', 'UPDATE', 'DELETE', 'LOGIN', 'LOGOUT', 'ACTIVATE', 'DEACTIVATE');

-- ============================================================
-- DOMINIO SAAS
-- ============================================================

CREATE TABLE plan (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nombre              VARCHAR(50)  NOT NULL,
    max_areas           INT          NOT NULL DEFAULT 10,
    max_usuarios        INT          NOT NULL DEFAULT 20,
    max_ciclos_activos  INT          NOT NULL DEFAULT 1,
    descripcion         TEXT,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
-- ADR-002 (2026-09-13): unicidad case-insensitive de nombre de plan exigida por HU-002 CA#1
-- Migración V002__unique_plan_nombre.sql (ADR-002 aceptado) — equivalente a ADR-001 (uq_tenant_nombre) para `plan`.
-- Validado en BLL (DAL-P4) + captura SQLSTATE 23505 → HTTP 422.
CREATE UNIQUE INDEX uq_plan_nombre ON plan (LOWER(nombre));

CREATE TABLE tenant (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nombre          VARCHAR(150) NOT NULL,
    descripcion     TEXT,
    plan_id         UUID         NOT NULL REFERENCES plan(id),
    logo_url        TEXT,
    eslogan         VARCHAR(200),
    zona_horaria    VARCHAR(50)  NOT NULL DEFAULT 'America/Managua',
    estado          estado_tenant NOT NULL DEFAULT 'Activo',
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
-- ADR-001 (2026-09-13): unicidad case-insensitive de nombre exigida por HU-001 CA#4
CREATE UNIQUE INDEX uq_tenant_nombre ON tenant (LOWER(nombre));

CREATE TABLE usuario (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         REFERENCES tenant(id) ON DELETE CASCADE,
    nombre                VARCHAR(150) NOT NULL,
    correo                VARCHAR(200) NOT NULL UNIQUE,
    password_hash         TEXT         NOT NULL,
    rol                   rol_usuario  NOT NULL,
    area_id               UUID,        -- FK → area, se agrega después (circular)
    estado                estado_usuario NOT NULL DEFAULT 'Activo',
    intentos_fallidos     INT          NOT NULL DEFAULT 0,
    bloqueado_hasta       TIMESTAMPTZ,
    ultimo_login          TIMESTAMPTZ,
    requiere_cambio_pwd   BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_usuario_tenant    ON usuario(tenant_id);
CREATE INDEX idx_usuario_correo    ON usuario(correo);

CREATE TABLE refresh_token (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    usuario_id  UUID         NOT NULL REFERENCES usuario(id) ON DELETE CASCADE,
    token       TEXT         NOT NULL UNIQUE,
    expira_en   TIMESTAMPTZ  NOT NULL,
    revocado    BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_refresh_token_usuario ON refresh_token(usuario_id);

CREATE TABLE log_auditoria (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         REFERENCES tenant(id),
    usuario_id      UUID         REFERENCES usuario(id),
    accion          accion_auditoria NOT NULL,
    entidad         VARCHAR(100) NOT NULL,
    entidad_id      TEXT,
    valor_anterior  JSONB,
    valor_nuevo     JSONB,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_log_tenant   ON log_auditoria(tenant_id);
CREATE INDEX idx_log_usuario  ON log_auditoria(usuario_id);
CREATE INDEX idx_log_fecha    ON log_auditoria(created_at DESC);

-- ============================================================
-- DOMINIO CICLO
-- ============================================================

CREATE TABLE ciclo (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         NOT NULL REFERENCES tenant(id),
    nombre          VARCHAR(100) NOT NULL,
    año_fiscal      INT          NOT NULL,
    mes_inicio      INT          NOT NULL DEFAULT 1 CHECK (mes_inicio BETWEEN 1 AND 12),
    estado          estado_ciclo NOT NULL DEFAULT 'Borrador',
    created_by      UUID         NOT NULL REFERENCES usuario(id),
    activated_at    TIMESTAMPTZ,
    closed_at       TIMESTAMPTZ,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, año_fiscal)
    -- Índice parcial (ADR-006/V003): CREATE UNIQUE INDEX uq_ciclo_unico_activo ON ciclo (tenant_id) WHERE estado = 'Activo';  -- RC-01: solo un ciclo Activo por tenant
);
CREATE INDEX idx_ciclo_tenant ON ciclo(tenant_id);

CREATE TABLE umbral_semaforo (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ciclo_id            UUID         NOT NULL REFERENCES ciclo(id) ON DELETE CASCADE,
    tenant_id           UUID         NOT NULL REFERENCES tenant(id),
    tipo                tipo_umbral  NOT NULL,
    umbral_verde        DECIMAL(3,2) NOT NULL DEFAULT 0.90,
    umbral_amarillo     DECIMAL(3,2) NOT NULL DEFAULT 0.70,
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (ciclo_id, tipo),
    CHECK (umbral_verde > umbral_amarillo),
    CHECK (umbral_verde  BETWEEN 0.00 AND 1.00),
    CHECK (umbral_amarillo BETWEEN 0.00 AND 1.00)
);

-- ============================================================
-- DOMINIO ESTRATEGIA
-- ============================================================

CREATE TABLE filosofia (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id    UUID         NOT NULL REFERENCES ciclo(id) ON DELETE CASCADE,
    vision      TEXT,
    mision      TEXT,
    valores     JSONB        NOT NULL DEFAULT '[]',
    updated_by  UUID         REFERENCES usuario(id),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, ciclo_id)
);

CREATE TABLE pilar (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id            UUID         NOT NULL REFERENCES ciclo(id) ON DELETE CASCADE,
    codigo              VARCHAR(10)  NOT NULL,
    nombre              VARCHAR(150) NOT NULL,
    estrategia_victoria TEXT,
    objetivo_q1         TEXT,
    objetivo_q2         TEXT,
    objetivo_q3         TEXT,
    objetivo_q4         TEXT,
    orden               INT          NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (ciclo_id, codigo)
);
CREATE INDEX idx_pilar_ciclo ON pilar(ciclo_id);

CREATE TABLE area (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id        UUID         NOT NULL REFERENCES ciclo(id),
    codigo          VARCHAR(10)  NOT NULL,
    nombre          VARCHAR(150) NOT NULL,
    comentarios     TEXT,
    responsable_id  UUID         REFERENCES usuario(id),
    orden           INT          NOT NULL DEFAULT 0,
    activa          BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (ciclo_id, codigo)
    -- Índice parcial (ADR-007/V004): CREATE UNIQUE INDEX uq_area_responsable_unico ON area (ciclo_id, responsable_id) WHERE responsable_id IS NOT NULL AND activa = TRUE;  -- RN-012: un responsable por área activa por ciclo (migración V004, pendiente de deploy)
);
CREATE INDEX idx_area_ciclo ON area(ciclo_id);

-- FK circular usuario → area
ALTER TABLE usuario ADD CONSTRAINT fk_usuario_area
    FOREIGN KEY (area_id) REFERENCES area(id);

CREATE TABLE objetivo_cg (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id            UUID         NOT NULL REFERENCES ciclo(id),
    area_id             UUID         NOT NULL REFERENCES area(id),
    pilar_id            UUID         NOT NULL REFERENCES pilar(id),
    codigo              VARCHAR(15)  NOT NULL,
    descripcion         TEXT         NOT NULL,
    trimestre_objetivo  trimestre    NOT NULL,
    progreso            DECIMAL(5,4) NOT NULL DEFAULT 0.0000,
    semaforo            semaforo_color NOT NULL DEFAULT 'Rojo',
    orden               INT          NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (ciclo_id, codigo)
);
CREATE INDEX idx_objetivo_cg_area ON objetivo_cg(area_id);

CREATE TABLE okr (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id         UUID         NOT NULL REFERENCES ciclo(id),
    area_id          UUID         NOT NULL REFERENCES area(id),
    pilar_id         UUID         NOT NULL REFERENCES pilar(id),
    codigo           VARCHAR(10)  NOT NULL,
    descripcion      TEXT         NOT NULL,
    puntuacion_final DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    semaforo         semaforo_color NOT NULL DEFAULT 'Rojo',
    orden            INT          NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (area_id, codigo)
);
CREATE INDEX idx_okr_area ON okr(area_id);

CREATE TABLE key_result (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL REFERENCES tenant(id),
    okr_id               UUID         NOT NULL REFERENCES okr(id) ON DELETE CASCADE,
    codigo               VARCHAR(10)  NOT NULL,
    descripcion          TEXT         NOT NULL,
    peso                 DECIMAL(4,3) NOT NULL CHECK (peso > 0 AND peso <= 1),
    puntuacion_q1        DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    puntuacion_q2        DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    puntuacion_q3        DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    puntuacion_q4        DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    puntuacion_final     DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    puntuacion_ponderada DECIMAL(4,3) NOT NULL DEFAULT 0.000,
    orden                INT          NOT NULL DEFAULT 0,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (okr_id, codigo)
);

CREATE TABLE valor_mensual_kr (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         NOT NULL REFERENCES tenant(id),
    key_result_id   UUID         NOT NULL REFERENCES key_result(id) ON DELETE CASCADE,
    mes             INT          NOT NULL CHECK (mes BETWEEN 1 AND 12),
    valor           DECIMAL(2,1) NOT NULL CHECK (valor BETWEEN 0.0 AND 1.0),
    registrado_por  UUID         NOT NULL REFERENCES usuario(id),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (key_result_id, mes)
);

-- ============================================================
-- DOMINIO PLAN OPERATIVO
-- ============================================================

CREATE TABLE accion_plan (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id                UUID         NOT NULL REFERENCES ciclo(id),
    area_id                 UUID         NOT NULL REFERENCES area(id),
    objetivo_cg_id          UUID         NOT NULL REFERENCES objetivo_cg(id),
    codigo                  VARCHAR(20)  NOT NULL,
    descripcion             TEXT         NOT NULL,
    descripcion_entregable  TEXT,
    responsable_id          UUID         REFERENCES usuario(id),
    fecha_inicio            DATE         NOT NULL,
    fecha_vencimiento       DATE         NOT NULL,
    clasificacion           clasificacion_accion NOT NULL,
    tipo_presupuesto        tipo_presupuesto     NOT NULL,
    peso                    DECIMAL(4,3) NOT NULL CHECK (peso > 0 AND peso <= 1),
    aclaraciones            TEXT,
    progreso                DECIMAL(5,2) NOT NULL DEFAULT 0.00
                                CHECK (progreso BETWEEN 0.00 AND 100.00),
    puntuacion_ponderada    DECIMAL(6,5) NOT NULL DEFAULT 0.00000,
    status                  status_accion NOT NULL DEFAULT 'NoIniciado',
    alerta_enviada          BOOLEAN      NOT NULL DEFAULT FALSE,
    orden                   INT          NOT NULL DEFAULT 0,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CHECK (fecha_vencimiento >= fecha_inicio)
);
CREATE INDEX idx_accion_plan_objetivo ON accion_plan(objetivo_cg_id);
CREATE INDEX idx_accion_plan_area     ON accion_plan(area_id);
CREATE INDEX idx_accion_plan_status   ON accion_plan(status);

CREATE TABLE entregable_adjunto (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID         NOT NULL REFERENCES tenant(id),
    accion_id       UUID         NOT NULL REFERENCES accion_plan(id) ON DELETE CASCADE,
    nombre_archivo  VARCHAR(255) NOT NULL,
    file_path       TEXT         NOT NULL,
    file_size_bytes INT          NOT NULL,
    tipo_mime       VARCHAR(100) NOT NULL,
    subido_por      UUID         NOT NULL REFERENCES usuario(id),
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_entregable_accion ON entregable_adjunto(accion_id);

CREATE TABLE historial_progreso (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         UUID         NOT NULL REFERENCES tenant(id),
    accion_id         UUID         NOT NULL REFERENCES accion_plan(id) ON DELETE CASCADE,
    progreso_anterior DECIMAL(5,2) NOT NULL,
    progreso_nuevo    DECIMAL(5,2) NOT NULL,
    status_anterior   status_accion NOT NULL,
    status_nuevo      status_accion NOT NULL,
    registrado_por    UUID         NOT NULL REFERENCES usuario(id),
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ============================================================
-- DOMINIO PRESUPUESTO
-- ============================================================

CREATE TABLE proyecto_capex (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id             UUID         NOT NULL REFERENCES ciclo(id),
    area_id              UUID         NOT NULL REFERENCES area(id),
    area_colaboracion    VARCHAR(150),
    colaborador          VARCHAR(150),
    nombre_proyecto      VARCHAR(255) NOT NULL,
    impacto              TEXT,
    presupuesto_aprobado DECIMAL(15,2) NOT NULL CHECK (presupuesto_aprobado > 0),
    total_planeado       DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    total_real           DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    variacion            DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    status               status_capex  NOT NULL DEFAULT 'NoIniciado',
    cumplimiento         cumplimiento_capex NOT NULL DEFAULT 'NoCumple',
    orden                INT          NOT NULL DEFAULT 0,
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_capex_area ON proyecto_capex(area_id);

CREATE TABLE desembolso_capex (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL REFERENCES tenant(id),
    proyecto_capex_id   UUID         NOT NULL REFERENCES proyecto_capex(id) ON DELETE CASCADE,
    mes                 INT          NOT NULL CHECK (mes BETWEEN 1 AND 12),
    monto_planeado      DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    monto_real          DECIMAL(15,2),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (proyecto_capex_id, mes)
);

CREATE TABLE cuenta_opex (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID         NOT NULL REFERENCES tenant(id),
    ciclo_id    UUID         NOT NULL REFERENCES ciclo(id),
    area_id     UUID         NOT NULL REFERENCES area(id),
    codigo      VARCHAR(20)  NOT NULL,
    nombre      VARCHAR(150) NOT NULL,
    orden       INT          NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (area_id, codigo)
);

CREATE TABLE subcuenta_opex (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             UUID         NOT NULL REFERENCES tenant(id),
    cuenta_opex_id        UUID         NOT NULL REFERENCES cuenta_opex(id) ON DELETE RESTRICT,
    codigo                VARCHAR(20)  NOT NULL,
    nombre                VARCHAR(150) NOT NULL,
    tiene_memoria_calculo BOOLEAN      NOT NULL DEFAULT FALSE,
    orden                 INT          NOT NULL DEFAULT 0,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (cuenta_opex_id, codigo)
);

CREATE TABLE presupuesto_opex (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            UUID         NOT NULL REFERENCES tenant(id),
    subcuenta_opex_id    UUID         NOT NULL REFERENCES subcuenta_opex(id) ON DELETE CASCADE,
    mes                  INT          NOT NULL CHECK (mes BETWEEN 1 AND 12),
    monto_presupuestado  DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    monto_real           DECIMAL(15,2),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (subcuenta_opex_id, mes)
);

CREATE TABLE rubro_material (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         UUID         NOT NULL REFERENCES tenant(id),
    subcuenta_opex_id UUID         NOT NULL REFERENCES subcuenta_opex(id) ON DELETE CASCADE,
    nombre_material   VARCHAR(200) NOT NULL,
    unidad_medida     VARCHAR(50)  NOT NULL,
    cantidad          DECIMAL(10,2) NOT NULL CHECK (cantidad > 0),
    precio_unitario   DECIMAL(15,2) NOT NULL CHECK (precio_unitario >= 0),
    total             DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    orden             INT          NOT NULL DEFAULT 0,
    updated_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_rubro_subcuenta ON rubro_material(subcuenta_opex_id);

-- ============================================================
-- DOMINIO COMUNICACIÓN
-- ============================================================

CREATE TABLE notificacion (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID         NOT NULL REFERENCES tenant(id),
    usuario_destino_id  UUID         NOT NULL REFERENCES usuario(id),
    tipo                tipo_notificacion NOT NULL,
    titulo              VARCHAR(200) NOT NULL,
    mensaje             TEXT         NOT NULL,
    entidad_tipo        VARCHAR(100),
    entidad_id          UUID,
    leida               BOOLEAN      NOT NULL DEFAULT FALSE,
    leida_en            TIMESTAMPTZ,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_notificacion_usuario ON notificacion(usuario_destino_id);
CREATE INDEX idx_notificacion_leida   ON notificacion(leida) WHERE leida = FALSE;

CREATE TABLE alerta_enviada (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   UUID         NOT NULL REFERENCES tenant(id),
    tipo        tipo_alerta  NOT NULL,
    entidad_id  UUID         NOT NULL,
    periodo     VARCHAR(10),
    enviada_en  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    destinatario VARCHAR(200) NOT NULL,
    UNIQUE (tipo, entidad_id, destinatario, periodo)
);
```

---

## 3. Row-Level Security (RLS)

```sql
-- ============================================================
-- HABILITAR RLS EN TODAS LAS TABLAS DE NEGOCIO
-- ============================================================
ALTER TABLE ciclo            ENABLE ROW LEVEL SECURITY;
ALTER TABLE umbral_semaforo  ENABLE ROW LEVEL SECURITY;
ALTER TABLE filosofia        ENABLE ROW LEVEL SECURITY;
ALTER TABLE pilar            ENABLE ROW LEVEL SECURITY;
ALTER TABLE area             ENABLE ROW LEVEL SECURITY;
ALTER TABLE objetivo_cg      ENABLE ROW LEVEL SECURITY;
ALTER TABLE okr              ENABLE ROW LEVEL SECURITY;
ALTER TABLE key_result       ENABLE ROW LEVEL SECURITY;
ALTER TABLE valor_mensual_kr ENABLE ROW LEVEL SECURITY;
ALTER TABLE accion_plan      ENABLE ROW LEVEL SECURITY;
ALTER TABLE entregable_adjunto ENABLE ROW LEVEL SECURITY;
ALTER TABLE historial_progreso ENABLE ROW LEVEL SECURITY;
ALTER TABLE proyecto_capex   ENABLE ROW LEVEL SECURITY;
ALTER TABLE desembolso_capex ENABLE ROW LEVEL SECURITY;
ALTER TABLE cuenta_opex      ENABLE ROW LEVEL SECURITY;
ALTER TABLE subcuenta_opex   ENABLE ROW LEVEL SECURITY;
ALTER TABLE presupuesto_opex ENABLE ROW LEVEL SECURITY;
ALTER TABLE rubro_material   ENABLE ROW LEVEL SECURITY;
ALTER TABLE notificacion     ENABLE ROW LEVEL SECURITY;
ALTER TABLE alerta_enviada   ENABLE ROW LEVEL SECURITY;

-- ============================================================
-- FUNCIÓN AUXILIAR: extrae tenant_id del JWT
-- ============================================================
CREATE OR REPLACE FUNCTION auth.tenant_id() RETURNS UUID AS $$
    SELECT NULLIF(current_setting('request.jwt.claims', true)::json->>'tenant_id', '')::UUID;
$$ LANGUAGE SQL STABLE;

CREATE OR REPLACE FUNCTION auth.user_rol() RETURNS TEXT AS $$
    SELECT NULLIF(current_setting('request.jwt.claims', true)::json->>'rol', '');
$$ LANGUAGE SQL STABLE;

CREATE OR REPLACE FUNCTION auth.area_id() RETURNS UUID AS $$
    SELECT NULLIF(current_setting('request.jwt.claims', true)::json->>'area_id', '')::UUID;
$$ LANGUAGE SQL STABLE;

-- ============================================================
-- POLÍTICAS RLS — Patrón: tenant_id debe coincidir
-- (El DAL de .NET también filtra por tenant_id — doble capa)
-- ============================================================

-- Ciclo: acceso solo a su tenant
CREATE POLICY ciclo_tenant_policy ON ciclo
    USING (tenant_id = auth.tenant_id());

-- Área: JefeArea solo ve su área; otros roles ven todas las de su tenant
CREATE POLICY area_tenant_policy ON area
    USING (
        tenant_id = auth.tenant_id()
        AND (
            auth.user_rol() IN ('SuperAdmin', 'AdminTenant', 'Gerente')
            OR id = auth.area_id()
        )
    );

-- Objetivo CG: JefeArea solo ve su área
CREATE POLICY objetivo_cg_policy ON objetivo_cg
    USING (
        tenant_id = auth.tenant_id()
        AND (
            auth.user_rol() IN ('SuperAdmin', 'AdminTenant', 'Gerente')
            OR area_id = auth.area_id()
        )
    );

-- Acción Plan: mismo patrón que objetivo_cg
CREATE POLICY accion_plan_policy ON accion_plan
    USING (
        tenant_id = auth.tenant_id()
        AND (
            auth.user_rol() IN ('SuperAdmin', 'AdminTenant', 'Gerente')
            OR area_id = auth.area_id()
        )
    );

-- OKR
CREATE POLICY okr_policy ON okr
    USING (
        tenant_id = auth.tenant_id()
        AND (
            auth.user_rol() IN ('SuperAdmin', 'AdminTenant', 'Gerente')
            OR area_id = auth.area_id()
        )
    );

-- Notificación: cada usuario solo ve las suyas
CREATE POLICY notificacion_policy ON notificacion
    USING (
        tenant_id = auth.tenant_id()
        AND usuario_destino_id = (
            current_setting('request.jwt.claims', true)::json->>'user_id'
        )::UUID
    );

-- Políticas simples por tenant (misma lógica) para el resto
CREATE POLICY filosofia_policy         ON filosofia         USING (tenant_id = auth.tenant_id());
CREATE POLICY pilar_policy             ON pilar             USING (tenant_id = auth.tenant_id());
CREATE POLICY umbral_semaforo_policy   ON umbral_semaforo   USING (tenant_id = auth.tenant_id());
CREATE POLICY key_result_policy        ON key_result        USING (tenant_id = auth.tenant_id());
CREATE POLICY valor_mensual_kr_policy  ON valor_mensual_kr  USING (tenant_id = auth.tenant_id());
CREATE POLICY entregable_policy        ON entregable_adjunto USING (tenant_id = auth.tenant_id());
CREATE POLICY historial_policy         ON historial_progreso USING (tenant_id = auth.tenant_id());
CREATE POLICY proyecto_capex_policy    ON proyecto_capex    USING (tenant_id = auth.tenant_id());
CREATE POLICY desembolso_capex_policy  ON desembolso_capex  USING (tenant_id = auth.tenant_id());
CREATE POLICY cuenta_opex_policy       ON cuenta_opex       USING (tenant_id = auth.tenant_id());
CREATE POLICY subcuenta_opex_policy    ON subcuenta_opex    USING (tenant_id = auth.tenant_id());
CREATE POLICY presupuesto_opex_policy  ON presupuesto_opex  USING (tenant_id = auth.tenant_id());
CREATE POLICY rubro_material_policy    ON rubro_material    USING (tenant_id = auth.tenant_id());
CREATE POLICY alerta_enviada_policy    ON alerta_enviada    USING (tenant_id = auth.tenant_id());
```

---

## 4. Datos Semilla (Seed)

```sql
-- ============================================================
-- PLAN DE SUSCRIPCIÓN INICIAL
-- ============================================================
INSERT INTO plan (id, nombre, max_areas, max_usuarios, max_ciclos_activos, descripcion) VALUES
    (gen_random_uuid(), 'Básico',    5,  10, 1, 'Plan básico para gerencias pequeñas'),
    (gen_random_uuid(), 'Estándar', 10,  25, 1, 'Plan estándar — uso típico por gerencia'),
    (gen_random_uuid(), 'Premium',  20, 100, 2, 'Plan premium con capacidad extendida');

-- ============================================================
-- SUPER ADMIN INICIAL
-- ============================================================
-- Nota: reemplazar password_hash con BCrypt real en deployment
INSERT INTO usuario (id, tenant_id, nombre, correo, password_hash, rol, estado, requiere_cambio_pwd)
VALUES (
    gen_random_uuid(), NULL,
    'Super Administrador', 'superadmin@pegol.app',
    '$2a$12$PLACEHOLDER_HASH_REPLACE_ON_DEPLOY',
    'SuperAdmin', 'Activo', TRUE
);
```

---

## 5. Vistas Útiles

```sql
-- Vista: progreso consolidado por área
CREATE VIEW v_progreso_area AS
SELECT
    a.id AS area_id,
    a.nombre AS area,
    a.ciclo_id,
    COUNT(DISTINCT ocg.id)       AS total_objetivos_cg,
    AVG(ocg.progreso)            AS progreso_promedio_cg,
    COUNT(DISTINCT ap.id)        AS total_acciones,
    COUNT(DISTINCT ap.id) FILTER (WHERE ap.status = 'Atrasado')   AS acciones_atrasadas,
    COUNT(DISTINCT ap.id) FILTER (WHERE ap.status = 'Terminado')  AS acciones_terminadas,
    COUNT(DISTINCT o.id)         AS total_okrs,
    AVG(o.puntuacion_final)      AS promedio_okrs
FROM area a
LEFT JOIN objetivo_cg ocg ON ocg.area_id = a.id
LEFT JOIN accion_plan ap  ON ap.area_id = a.id
LEFT JOIN okr o           ON o.area_id = a.id
GROUP BY a.id, a.nombre, a.ciclo_id;

-- Vista: resumen CAPEX por área
CREATE VIEW v_resumen_capex AS
SELECT
    pc.area_id,
    pc.ciclo_id,
    COUNT(pc.id)              AS total_proyectos,
    SUM(pc.presupuesto_aprobado) AS total_presupuesto_aprobado,
    SUM(pc.total_planeado)    AS total_planeado,
    SUM(pc.total_real)        AS total_real,
    SUM(pc.variacion)         AS variacion_total
FROM proyecto_capex pc
GROUP BY pc.area_id, pc.ciclo_id;

-- Vista: resumen OPEX por área (totales por cuenta)
CREATE VIEW v_resumen_opex AS
SELECT
    co.area_id,
    co.ciclo_id,
    co.id AS cuenta_id,
    co.nombre AS cuenta,
    SUM(po.monto_presupuestado) AS total_presupuestado,
    SUM(po.monto_real)          AS total_real,
    SUM(po.monto_real) - SUM(po.monto_presupuestado) AS variacion
FROM cuenta_opex co
JOIN subcuenta_opex so ON so.cuenta_opex_id = co.id
LEFT JOIN presupuesto_opex po ON po.subcuenta_opex_id = so.id
GROUP BY co.area_id, co.ciclo_id, co.id, co.nombre;
```

---

## 6. Resumen del Modelo

| Dominio | Tablas | Registros clave |
|---------|--------|----------------|
| SaaS | plan, tenant, usuario, refresh_token, log_auditoria | 5 |
| Ciclo | ciclo, umbral_semaforo | 2 |
| Estrategia | filosofia, pilar, area, objetivo_cg, okr, key_result, valor_mensual_kr | 7 |
| Plan Operativo | accion_plan, entregable_adjunto, historial_progreso | 3 |
| Presupuesto | proyecto_capex, desembolso_capex, cuenta_opex, subcuenta_opex, presupuesto_opex, rubro_material | 6 |
| Comunicación | notificacion, alerta_enviada | 2 |
| **Total** | **25 tablas** | **3 vistas** |

---

*Documento generado el 13/09/2026 · Fase 2 — Modelo de Datos.*
*Compatible con Supabase PostgreSQL 15 · RLS habilitado en todas las tablas de negocio.*

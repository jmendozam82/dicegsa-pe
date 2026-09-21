-- ============================================================
-- V003__unico_ciclo_activo.sql
-- ADR-006 (Aceptado 2026-09-14): garantizar RC-01 "solo un
-- ciclo Activo por tenant" a nivel de base de datos (CA #3 HU-007).
-- Autor: @Arquitecto · Fecha: 2026-09-14
-- Migración versionada conforme a DB-02 · EJECUTADA y verificada en
-- Supabase Cloud (2026-09-21, pg_indexes).
-- ============================================================

-- Índice único parcial: indexa solo las filas con estado='Activo' y
-- garantiza que no existan dos ciclos Activos del mismo tenant (RC-01).
-- Los estados Borrador/Cerrado no participan del índice (comportamiento
-- deseado: RC-01 aplica únicamente al estado Activo).
-- SQLSTATE 23505 (unique_violation) lo captura CicloService.ActivarAsync
-- y lo traduce a ValidacionException → HTTP 422, con rollback explícito
-- de la transacción (ADR-006, nota de implementación; patrón ADR-001/002).
CREATE UNIQUE INDEX uq_ciclo_unico_activo
    ON ciclo (tenant_id)
    WHERE estado = 'Activo';
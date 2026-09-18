-- ============================================================
-- V004__unico_responsable_area.sql
-- ADR-007 (Aceptado 2026-09-17): garantizar RN-012 "un
-- responsable por área por ciclo" a nivel de base de datos
-- (CA #3 HU-009).
-- Autor: Jorge (Dicegsa) / @Arquitecto · Fecha: 2026-09-17
-- Migración versionada conforme a DB-02 · NO EJECUTADA: requiere
-- Supabase; pendiente de aplicar en deploy junto a V001/V002/V003.
-- ============================================================

-- Índice único parcial: indexa solo las filas con responsable asignado
-- y área activa; garantiza que un responsable no sea asignado a dos
-- áreas activas del mismo ciclo (RN-012).
-- Las áreas sin responsable (responsable_id IS NULL) y las inactivas
-- (activa = FALSE) no participan del índice (comportamiento deseado:
-- desactivar libera al responsable para reasignación — Flag #7/D-G).
-- SQLSTATE 23505 (unique_violation) lo captura AreaService.CrearAsync /
-- AreaService.ActualizarAsync y lo traduce a ValidacionException → HTTP 422,
-- con rollback explícito de la transacción (ADR-007, nota de implementación;
-- patrón ADR-001/002/006).
CREATE UNIQUE INDEX uq_area_responsable_unico
    ON area (ciclo_id, responsable_id)
    WHERE responsable_id IS NOT NULL AND activa = TRUE;
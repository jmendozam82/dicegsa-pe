-- ============================================================
-- V002__unique_plan_nombre.sql
-- ADR-002: Garantizar unicidad case-insensitive de plan.nombre
-- a nivel de base de datos (CA #1 HU-002).
-- Autor: Jorge (Dicegsa) / @Arquitecto · Fecha: 2026-09-13
-- Creada por: @BackendDev (HU-002) · Migración versionada conforme a DB-02
-- NO EJECUTADA: requiere Supabase; pendiente de aplicar en deploy.
-- ============================================================

-- Índice único funcional: colisionan nombres que difieren solo
-- por mayúsculas/minúsculas (comportamiento deseado, ver spec DAL-P4).
-- SQLSTATE 23505 (unique_violation) lo captura PlanService.CrearAsync /
-- PlanService.ActualizarAsync y lo traduce a ValidacionException → HTTP 422,
-- con rollback explícito de la transacción (ADR-002, nota de implementación).
CREATE UNIQUE INDEX uq_plan_nombre
    ON plan (LOWER(nombre));
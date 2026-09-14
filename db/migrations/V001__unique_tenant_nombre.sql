-- ============================================================
-- V001__unique_tenant_nombre.sql
-- ADR-001: Garantizar unicidad case-insensitive de tenant.nombre
-- a nivel de base de datos (CA #4 HU-001).
-- Autor: Jorge (Dicegsa) / @Arquitecto · Fecha: 2026-09-13
-- Aplicado por: @BackendDev (HU-001) · Migración versionada conforme a DB-02
-- ============================================================

-- Índice único funcional: colisionan nombres que difieren solo
-- por mayúsculas/minúsculas (comportamiento deseado, ver spec DAL-8).
-- SQLSTATE 23505 (unique_violation) lo captura TenantService.CrearAsync /
-- TenantService.ActualizarAsync y lo traduce a ValidacionException → HTTP 422,
-- con rollback explícito de la transacción (ADR-001, nota de implementación).
CREATE UNIQUE INDEX uq_tenant_nombre
    ON tenant (LOWER(nombre));
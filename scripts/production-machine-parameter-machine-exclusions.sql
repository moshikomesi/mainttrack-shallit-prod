-- =============================================================================
-- MaintTrack — Production schema: Machine Parameter Photos machine exclusions
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the schema introduced by EF Core migration
--   `20261108191918_AddMachineParameterMachineExclusions`, so it can be run
--   against production WITHOUT `dotnet ef database update`.
--
--   Creates one tenant-scoped table:
--     • machine_parameter_machine_visibility
--
--   The table is an exclusion list for the Machine Parameters feature only:
--     • No row for a machine  → visible in Machine Parameters.
--     • A row for a machine   → hidden from Machine Parameters.
--   Other modules (shared hierarchy, Morning Round, Maintenance, Treatments,
--   Clearance) never read this table. The shared `machines` table is not
--   altered.
--
-- CRITICAL — run this BEFORE deploying the backend build that maps this
-- table, otherwise Machine Parameters requests will fail.
--
-- Safe properties:
--   • Idempotent — IF NOT EXISTS guards; safe to re-run.
--   • Non-destructive — no DROP, no DELETE, no data loss.
--   • No seed data — every machine remains visible until a row is added.
--
-- Usage:
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-machine-parameter-machine-exclusions.sql
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

CREATE TABLE IF NOT EXISTS machine_parameter_machine_visibility
(
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    machine_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_machine_parameter_machine_visibility" PRIMARY KEY (id)
);

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_machine_parameter_machine_visibility_machines_machine_id'
          AND conrelid = 'machine_parameter_machine_visibility'::regclass
    ) THEN
        ALTER TABLE machine_parameter_machine_visibility
            ADD CONSTRAINT "FK_machine_parameter_machine_visibility_machines_machine_id"
            FOREIGN KEY (machine_id)
            REFERENCES machines (id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_machine_parameter_machine_visibility_tenant_id_machine_id"
    ON machine_parameter_machine_visibility (tenant_id, machine_id);

CREATE INDEX IF NOT EXISTS
    "IX_machine_parameter_machine_visibility_machine_id"
    ON machine_parameter_machine_visibility (machine_id);

COMMIT;

-- =============================================================================
-- POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name = 'machine_parameter_machine_visibility';

SELECT conname, conrelid::regclass AS table_name
FROM pg_constraint
WHERE conrelid = 'machine_parameter_machine_visibility'::regclass
ORDER BY conname;

SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'machine_parameter_machine_visibility'
ORDER BY indexname;

-- =============================================================================
-- OPTIONAL — hide / restore a machine in Machine Parameters (manual, later)
-- =============================================================================
-- Do NOT run this as part of the general deploy. Replace the placeholders.
-- The tenant_id written must be the machine's own tenant; the SELECT below
-- takes it from the machines row so a cross-tenant row cannot be created.
-- =============================================================================
--
-- Hide:
-- BEGIN;
--   INSERT INTO machine_parameter_machine_visibility (id, tenant_id, machine_id, created_at)
--   SELECT gen_random_uuid(), m.tenant_id, m.id, NOW()
--   FROM machines m
--   WHERE m.id = '<MACHINE_ID>'
--     AND m.tenant_id = '<TENANT_ID>'
--   ON CONFLICT (tenant_id, machine_id) DO NOTHING;
-- COMMIT;
--
-- Restore:
-- BEGIN;
--   DELETE FROM machine_parameter_machine_visibility
--   WHERE tenant_id = '<TENANT_ID>'
--     AND machine_id = '<MACHINE_ID>';
-- COMMIT;
--
-- SELECT v.id, v.tenant_id, v.machine_id, m.name, v.created_at
-- FROM machine_parameter_machine_visibility v
-- JOIN machines m ON m.id = v.machine_id
-- WHERE v.tenant_id = '<TENANT_ID>'
-- ORDER BY m.name;
--
-- =============================================================================
-- ROLLBACK (manual, only if the feature build is reverted)
-- =============================================================================
-- BEGIN;
--   DROP TABLE IF EXISTS machine_parameter_machine_visibility;
-- COMMIT;

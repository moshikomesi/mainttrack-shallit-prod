-- =============================================================================
-- MaintTrack — Production data: Air Compressor / Cooling maintenance types
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same data changes introduced by the EF
--   Core migration `20260613200000_AddAircompressorAndCoolingMaintenanceTypes`,
--   so it can be run directly against production WITHOUT `dotnet ef database
--   update`.
--
--   Adds two `maintenance_types` catalog rows (per tenant) used by the
--   Water Cooling / Rooms arrays: 'aircompressor' and 'cooling'.
--
-- Prerequisites:
--   • `maintenance_types` and `tenants` tables must already exist (created by
--     an earlier release). Guarded below — safe no-op if either is missing.
--
-- Safe properties:
--   • Idempotent — NOT EXISTS guard per tenant/code, safe to re-run.
--   • Applies to ALL tenants automatically (`FROM tenants t` / `CROSS JOIN`),
--     no tenant UUID needs to be supplied.
--   • Non-destructive — INSERT only, no UPDATE/DELETE of existing rows.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-maintenance-types.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-maintenance-types.sql
-- =============================================================================

BEGIN;

INSERT INTO maintenance_types (id, tenant_id, code, is_active, created_at)
SELECT gen_random_uuid(), t.id, v.code, true, NOW()
FROM tenants t
CROSS JOIN (VALUES
  ('aircompressor'),
  ('cooling')
) AS v(code)
WHERE EXISTS (
    SELECT 1 FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
)
AND EXISTS (
    SELECT 1 FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relname = 'tenants' AND c.relkind = 'r' AND n.nspname = 'public'
)
AND NOT EXISTS (
  SELECT 1 FROM maintenance_types mt
  WHERE mt.tenant_id = t.id AND mt.code = v.code
);

COMMIT;

-- =============================================================================
-- SECTION: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT t.id AS tenant_id, mt.code, mt.is_active
FROM tenants t
JOIN maintenance_types mt ON mt.tenant_id = t.id
WHERE mt.code IN ('aircompressor', 'cooling')
ORDER BY t.id, mt.code;

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   DELETE FROM maintenance_types WHERE code IN ('aircompressor', 'cooling');
-- COMMIT;
-- NOTE: only safe if no existing maintenance_entries/treatments rows reference
-- these maintenance_type rows yet. Check references before deleting.

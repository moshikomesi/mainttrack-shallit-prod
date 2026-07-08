-- =============================================================================
-- MaintTrack — Production schema: Forklift inspection columns (no EF migration)
-- =============================================================================
-- Purpose:
--   Adds three nullable columns to `forklifts` that the current EF Core model
--   (`Forklift` domain entity + `MaintTrackDbContext.ConfigureForklift`)
--   already requires:
--     - last_inspection_date    (timestamp with time zone, nullable)
--     - inspection_expiry_date  (timestamp with time zone, nullable)
--     - inspection_updated_at   (timestamp with time zone, nullable)
--
--   IMPORTANT — this is NOT a normal "migration not yet applied to production"
--   gap. There is NO EF Core migration anywhere in this repository whose Up()
--   method actually creates these columns:
--     - `20260304200000_AddForklifts` creates the base `forklifts` table
--       WITHOUT these columns.
--     - `20260308185800_AddForkliftInspectionFields` — despite its name —
--       only alters `treatments.treatment_type` / `treatments.equipment_type`
--       column types. It does not touch `forklifts` at all.
--     - The columns exist only in the EF model snapshot / Designer files
--       (auto-generated "current model state"), never in an executable
--       migration body.
--   This means `dotnet ef database update` alone would NEVER have created
--   these columns even if it were run against production. This script is a
--   permanent, hand-written replacement for the missing migration body — it
--   is not superseded by any future `dotnet ef database update`.
--
--   These columns are actively read/written, unconditionally (no feature
--   flag), by:
--     - ForkliftService.GetAsync / GetByIdAsync (every GET /api/forklifts* call)
--     - ForkliftReportService (sets them when an inspection report is created)
--     - ForkliftReportsQueryService.GetExpiringInspectionsAsync (the
--       "expiring inspections" report)
--   Without this script, those endpoints fail at the database level with
--   "column does not exist" as soon as they touch a real Forklifts query.
--
-- Prerequisites:
--   • `forklifts` table must already exist (created by `AddForklifts`,
--     already applied — confirmed present in production).
--   (Guarded with an existence check below — safe no-op if missing.)
--
-- Safe properties:
--   • Idempotent — `ADD COLUMN IF NOT EXISTS`, safe to re-run any number of
--     times.
--   • Non-destructive — no DROP, no data deletion. New columns are nullable
--     with no default, so every existing `forklifts` row keeps all its data
--     and simply gets NULL for the three new columns (matches application
--     behavior for forklifts that have never had an inspection recorded).
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-forklift-inspection-columns.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-forklift-inspection-columns.sql
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: SCHEMA — new nullable columns on forklifts
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'forklifts' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE forklifts ADD COLUMN IF NOT EXISTS last_inspection_date timestamp with time zone NULL;
        ALTER TABLE forklifts ADD COLUMN IF NOT EXISTS inspection_expiry_date timestamp with time zone NULL;
        ALTER TABLE forklifts ADD COLUMN IF NOT EXISTS inspection_updated_at timestamp with time zone NULL;
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- SECTION 2: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'forklifts'
  AND column_name IN ('last_inspection_date', 'inspection_expiry_date', 'inspection_updated_at')
ORDER BY column_name;

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   ALTER TABLE forklifts DROP COLUMN IF EXISTS last_inspection_date;
--   ALTER TABLE forklifts DROP COLUMN IF EXISTS inspection_expiry_date;
--   ALTER TABLE forklifts DROP COLUMN IF EXISTS inspection_updated_at;
-- COMMIT;

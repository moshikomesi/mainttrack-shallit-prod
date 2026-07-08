-- =============================================================================
-- MaintTrack — Production schema: per-user feature flags
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same schema changes introduced by the EF
--   Core migration `20260708000000_AddUserFeatureFlags`, so it can be run
--   directly against production WITHOUT `dotnet ef database update`.
--
--   Adds two boolean columns to `users`, both defaulting to false:
--     • enable_new_morning_round
--     • enable_new_maintenance_log
--
--   These flags let the frontend route a given user to either the legacy or
--   the new (V2) Morning Round / Maintenance Log screen. See
--   backend/MaintTrack.Api/Endpoints/AuthEndpoints.cs (login + GET
--   /api/v1/auth/me) and frontend/src/App.tsx for how they are consumed.
--
-- CRITICAL — run this BEFORE deploying the new backend build.
--   The backend's EF Core `User` entity mapping expects both columns to
--   exist. If the backend is deployed before this script runs, every
--   authenticated request that touches the `users` table (i.e. almost all of
--   them) will fail with a SQL error ("column does not exist").
--
-- Safe properties:
--   • Idempotent — IF NOT EXISTS guard per column, safe to re-run.
--   • Defaults both columns to `false` for ALL existing users — 100% legacy
--     behavior is preserved for every user until a flag is explicitly
--     enabled (see the OPTIONAL section below).
--   • Non-destructive — no DROP, no DELETE, no data loss.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-user-feature-flags.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-user-feature-flags.sql
-- =============================================================================

BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'enable_new_morning_round'
    ) THEN
        ALTER TABLE users ADD COLUMN enable_new_morning_round boolean NOT NULL DEFAULT false;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'enable_new_maintenance_log'
    ) THEN
        ALTER TABLE users ADD COLUMN enable_new_maintenance_log boolean NOT NULL DEFAULT false;
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'users'
  AND column_name IN ('enable_new_morning_round', 'enable_new_maintenance_log')
ORDER BY column_name;

-- Sanity check: every user should default to false immediately after this
-- script runs (nobody is opted into V2 screens automatically).
SELECT COUNT(*) AS users_with_any_flag_enabled
FROM users
WHERE enable_new_morning_round = true OR enable_new_maintenance_log = true;

-- =============================================================================
-- OPTIONAL — enable flags for a specific pilot user (manual, separate step)
-- =============================================================================
-- Do NOT run this as part of the general deploy. This is a deliberate,
-- one-off action to be taken later, only when you're ready to pilot a real
-- production user on the new screens. Replace the placeholders below.
-- =============================================================================
--
-- BEGIN;
--   UPDATE users
--   SET enable_new_morning_round = true,
--       enable_new_maintenance_log = true
--   WHERE username = '<REAL_PRODUCTION_USERNAME>'
--     AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'; -- Shallit tenant
-- COMMIT;
--
-- SELECT id, username, tenant_id, enable_new_morning_round, enable_new_maintenance_log
-- FROM users
-- WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
-- ORDER BY username;

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   ALTER TABLE users DROP COLUMN IF EXISTS enable_new_morning_round;
--   ALTER TABLE users DROP COLUMN IF EXISTS enable_new_maintenance_log;
-- COMMIT;
-- NOTE: only drop these columns as part of a full rollback of the backend
-- deploy too — a backend build that expects these columns will error if they
-- are removed while that build is still running.

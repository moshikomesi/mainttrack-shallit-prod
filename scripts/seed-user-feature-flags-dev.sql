-- =============================================================================
-- Dev/test only — seed per-user feature flags (Morning Round V2 / new
-- Maintenance Log) for local verification.
-- =============================================================================
-- Enables both flags ONLY for the local "admin" user (Pilot Factory tenant).
-- Every other user keeps both flags at their default (false).
--
-- Usage: ./scripts/seed-user-feature-flags-dev.sh
-- =============================================================================

BEGIN;

-- Defensive reset: ensure only the intended user ever has these flags on
-- (safe to re-run; keeps local state predictable across seed re-runs).
UPDATE users
SET enable_new_morning_round = false,
    enable_new_maintenance_log = false
WHERE NOT (username = 'admin' AND tenant_id = '11111111-1111-1111-1111-111111111111');

UPDATE users
SET enable_new_morning_round = true,
    enable_new_maintenance_log = true
WHERE username = 'admin'
  AND tenant_id = '11111111-1111-1111-1111-111111111111';

COMMIT;

-- Verification (read-only)
SELECT id, username, tenant_id, enable_new_morning_round, enable_new_maintenance_log
FROM users
ORDER BY username;

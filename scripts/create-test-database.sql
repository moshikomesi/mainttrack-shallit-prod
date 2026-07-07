-- =============================================================================
-- MaintTrack — Create TEST database and role
-- =============================================================================
-- Purpose:
--   Provision an isolated `mainttrack_test` database + dedicated role on the
--   SAME PostgreSQL server as Production, without touching the Production
--   database (`mainttrack`) in any way.
--
-- Safe properties:
--   • Does NOT reference, read, or modify the `mainttrack` (production) database
--   • Idempotent: safe to re-run (CREATE DATABASE/ROLE only happen if missing)
--   • Schema/tables are created afterwards via EF migrations (see setup guide),
--     not by this script
--
-- Usage (run as a superuser, e.g. `postgres`):
--   psql -h HOST -U postgres -d postgres -f scripts/create-test-database.sql
--
-- Before running, replace CHANGE_ME_TEST_PASSWORD with a real generated
-- password and keep it only in the server-side secrets file
-- (/etc/mainttrack-test/secrets.env) — never commit real passwords to git.
-- =============================================================================

-- Dedicated role for the TEST environment (separate from Production's role).
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'mainttrack_test_user') THEN
        CREATE ROLE mainttrack_test_user WITH LOGIN PASSWORD 'CHANGE_ME_TEST_PASSWORD';
    END IF;
END
$$;

-- Conditional CREATE DATABASE (CREATE DATABASE cannot run inside DO blocks /
-- transactions, so this uses psql's \gexec idiom instead).
SELECT 'CREATE DATABASE mainttrack_test OWNER mainttrack_test_user'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'mainttrack_test')
\gexec

GRANT ALL PRIVILEGES ON DATABASE mainttrack_test TO mainttrack_test_user;

-- Verification (read-only)
SELECT datname, pg_get_userbyid(datdba) AS owner
FROM pg_database
WHERE datname = 'mainttrack_test';

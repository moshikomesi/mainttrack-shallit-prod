-- =============================================================================
-- MaintTrack — Production schema: Clearance foundation (Phase 1)
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the schema introduced by EF Core migrations
--   `20261019172521_AddClearanceFoundation` and
--   `20261104190405_AddClearanceViewersAndGrantTenantKeys`, so it can be run against
--   production WITHOUT `dotnet ef database update`.
--
--   Creates three tenant-scoped tables:
--     • clearance_items      — one row per report in the Clearance workflow
--                              (pending / approved / rejected): one per
--                              Maintenance Log entry, and one per Morning
--                              Round V2 submission with abnormal findings
--     • clearance_approvers  — explicit per-user approver assignments
--                              (approve/reject; approvers also view)
--     • clearance_viewers    — explicit per-user view-only assignments
--
--   No role grants Clearance access: without an approver or viewer row, a
--   user (including SuperAdmin) cannot see the queue or any Clearance status.
--
--   Tenant integrity is enforced by the database: approver and viewer rows
--   reference users (id, tenant_id), so a grant can never name a user of
--   another tenant. This adds one unique key to the existing users table:
--   "AK_users_id_tenant_id" UNIQUE (id, tenant_id).
--
--   Clearance is independent of "MaintenanceEntries"."IsSafeToOperate";
--   that column is not read or changed here.
--
-- CRITICAL — run this BEFORE deploying the backend build that maps these
-- tables. The backend reads and writes them on every Maintenance Log and
-- Morning Round V2 submission (automatic Clearance item creation), in the
-- Clearance queue and approve/reject endpoints (/api/v1/clearance).
-- Submitted maintenance reports are immutable and are never edited through
-- Clearance. Without these tables, report submission fails.
--
-- Safe properties:
--   • Idempotent — IF NOT EXISTS / pg_constraint guards; safe to re-run.
--   • Non-destructive — no DELETE or UPDATE of any data. The only DROPs are
--     IF EXISTS drops of the superseded single-column user_id foreign keys
--     and indexes on the two grant tables (left by an earlier version of
--     this script); no table or row is dropped.
--   • No seed data — no approver rows and no clearance rows are created.
--   • Every FK is ON DELETE RESTRICT — deleting a source row never silently
--     deletes approval history.
--   • Preflight checks abort with a clear message before any change.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-clearance-schema.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-clearance-schema.sql
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- =============================================================================
-- PREFLIGHT — referenced tables and key columns must exist
-- =============================================================================
-- Each FK target column must be covered by a primary key or unique constraint.
-- Note the quoted PascalCase naming of "MaintenanceEntries"."Id".

DO $$
DECLARE
    target record;
BEGIN
    FOR target IN
        SELECT *
        FROM (VALUES
            ('users', 'id'),
            ('MaintenanceEntries', 'Id'),
            ('morning_round_v2_submissions', 'id')
        ) AS t(table_name, column_name)
    LOOP
        IF to_regclass(format('public.%I', target.table_name)) IS NULL THEN
            RAISE EXCEPTION
                'Clearance preflight failed: required table public.% does not exist. Apply earlier production schema scripts first.',
                quote_ident(target.table_name);
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint c
            JOIN pg_attribute a
              ON a.attrelid = c.conrelid
             AND a.attnum = c.conkey[1]
            WHERE c.conrelid = format('public.%I', target.table_name)::regclass
              AND c.contype IN ('p', 'u')
              AND array_length(c.conkey, 1) = 1
              AND a.attname = target.column_name
        ) THEN
            RAISE EXCEPTION
                'Clearance preflight failed: public.%.% is not a primary key or unique column; cannot be referenced by a foreign key.',
                quote_ident(target.table_name), quote_ident(target.column_name);
        END IF;
    END LOOP;
END
$$;

-- =============================================================================
-- TABLES
-- =============================================================================

CREATE TABLE IF NOT EXISTS clearance_items
(
    id uuid NOT NULL,
    source_type character varying(50) NOT NULL,
    maintenance_entry_id uuid NULL,
    morning_round_v2_submission_id uuid NULL,
    status character varying(20) NOT NULL,
    submitted_by_user_id uuid NOT NULL,
    submitted_at timestamp with time zone NOT NULL,
    decided_by_user_id uuid NULL,
    decided_at timestamp with time zone NULL,
    decision_note character varying(2000) NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL,
    CONSTRAINT "PK_clearance_items" PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS clearance_approvers
(
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_clearance_approvers" PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS clearance_viewers
(
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_clearance_viewers" PRIMARY KEY (id)
);

-- =============================================================================
-- CHECK CONSTRAINTS (clearance_items)
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_clearance_items_source_type'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT ck_clearance_items_source_type
            CHECK (source_type IN ('maintenance_entry', 'morning_round_v2'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_clearance_items_status'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT ck_clearance_items_status
            CHECK (status IN ('pending', 'approved', 'rejected'));
    END IF;

    -- Exactly one source reference, matching source_type.
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_clearance_items_source_reference'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT ck_clearance_items_source_reference
            CHECK (
                (source_type = 'maintenance_entry'
                    AND maintenance_entry_id IS NOT NULL
                    AND morning_round_v2_submission_id IS NULL)
                OR (source_type = 'morning_round_v2'
                    AND maintenance_entry_id IS NULL
                    AND morning_round_v2_submission_id IS NOT NULL)
            );
    END IF;

    -- Decided items always carry the approver identity and decision time.
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_clearance_items_decision'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT ck_clearance_items_decision
            CHECK (
                (status = 'pending'
                    AND decided_by_user_id IS NULL
                    AND decided_at IS NULL)
                OR (status IN ('approved', 'rejected')
                    AND decided_by_user_id IS NOT NULL
                    AND decided_at IS NOT NULL)
            );
    END IF;
END
$$;

-- =============================================================================
-- FOREIGN KEYS (all ON DELETE RESTRICT)
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_clearance_items_MaintenanceEntries_maintenance_entry_id'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT "FK_clearance_items_MaintenanceEntries_maintenance_entry_id"
            FOREIGN KEY (maintenance_entry_id)
            REFERENCES "MaintenanceEntries" ("Id")
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_clearance_items_morning_round_v2_submissions_submission_id'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT "FK_clearance_items_morning_round_v2_submissions_submission_id"
            FOREIGN KEY (morning_round_v2_submission_id)
            REFERENCES morning_round_v2_submissions (id)
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_clearance_items_users_decided_by_user_id'
          AND conrelid = 'clearance_items'::regclass
    ) THEN
        ALTER TABLE clearance_items
            ADD CONSTRAINT "FK_clearance_items_users_decided_by_user_id"
            FOREIGN KEY (decided_by_user_id)
            REFERENCES users (id)
            ON DELETE RESTRICT;
    END IF;

END
$$;

-- =============================================================================
-- TENANT-SCOPED GRANT KEYS (clearance_approvers, clearance_viewers)
-- =============================================================================
-- A grant row must carry its user's tenant: (user_id, tenant_id) references
-- users (id, tenant_id), backed by the unique key AK_users_id_tenant_id.
-- Replaces the earlier single-column user_id foreign keys and indexes if a
-- previous version of this script created them.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'tenant_id'
          AND is_nullable <> 'NO'
    ) THEN
        RAISE EXCEPTION 'Clearance preflight failed: users.tenant_id must be NOT NULL.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM clearance_approvers a
        WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = a.user_id AND u.tenant_id = a.tenant_id)
    ) OR EXISTS (
        SELECT 1 FROM clearance_viewers v
        WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = v.user_id AND u.tenant_id = v.tenant_id)
    ) THEN
        RAISE EXCEPTION
            'Clearance preflight failed: a clearance_approvers or clearance_viewers row does not match its user''s tenant. Fix or remove it manually; this script never deletes grants.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'AK_users_id_tenant_id'
          AND conrelid = 'users'::regclass
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT "AK_users_id_tenant_id" UNIQUE (id, tenant_id);
    END IF;

    ALTER TABLE clearance_approvers DROP CONSTRAINT IF EXISTS "FK_clearance_approvers_users_user_id";
    ALTER TABLE clearance_viewers DROP CONSTRAINT IF EXISTS "FK_clearance_viewers_users_user_id";

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_clearance_approvers_users_user_id_tenant_id'
          AND conrelid = 'clearance_approvers'::regclass
    ) THEN
        ALTER TABLE clearance_approvers
            ADD CONSTRAINT "FK_clearance_approvers_users_user_id_tenant_id"
            FOREIGN KEY (user_id, tenant_id)
            REFERENCES users (id, tenant_id)
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_clearance_viewers_users_user_id_tenant_id'
          AND conrelid = 'clearance_viewers'::regclass
    ) THEN
        ALTER TABLE clearance_viewers
            ADD CONSTRAINT "FK_clearance_viewers_users_user_id_tenant_id"
            FOREIGN KEY (user_id, tenant_id)
            REFERENCES users (id, tenant_id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

-- =============================================================================
-- INDEXES
-- =============================================================================

-- Clearance queue: tenant-scoped filtering by status and submission time.
CREATE INDEX IF NOT EXISTS
    "IX_clearance_items_tenant_id_status_submitted_at"
    ON clearance_items (tenant_id, status, submitted_at);

-- At most one Clearance item per Maintenance Entry.
CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_clearance_items_maintenance_entry_id"
    ON clearance_items (maintenance_entry_id)
    WHERE maintenance_entry_id IS NOT NULL;

-- At most one Clearance item per Morning Round V2 submission.
CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_clearance_items_morning_round_v2_submission_id"
    ON clearance_items (morning_round_v2_submission_id)
    WHERE morning_round_v2_submission_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS
    "IX_clearance_items_decided_by_user_id"
    ON clearance_items (decided_by_user_id);

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_clearance_approvers_tenant_id_user_id"
    ON clearance_approvers (tenant_id, user_id);

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_clearance_viewers_tenant_id_user_id"
    ON clearance_viewers (tenant_id, user_id);

-- Back the composite (user_id, tenant_id) foreign keys; they supersede the
-- earlier single-column user_id indexes.
DROP INDEX IF EXISTS "IX_clearance_approvers_user_id";
DROP INDEX IF EXISTS "IX_clearance_viewers_user_id";

CREATE INDEX IF NOT EXISTS
    "IX_clearance_approvers_user_id_tenant_id"
    ON clearance_approvers (user_id, tenant_id);

CREATE INDEX IF NOT EXISTS
    "IX_clearance_viewers_user_id_tenant_id"
    ON clearance_viewers (user_id, tenant_id);

COMMIT;

-- =============================================================================
-- POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT table_name, column_name, data_type, character_maximum_length, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name IN ('clearance_items', 'clearance_approvers', 'clearance_viewers')
ORDER BY table_name, ordinal_position;

SELECT conname, contype, conrelid::regclass AS table_name
FROM pg_constraint
WHERE conrelid IN (
    'clearance_items'::regclass,
    'clearance_approvers'::regclass,
    'clearance_viewers'::regclass
)
ORDER BY table_name, conname;

-- Expected: 1 row (AK_users_id_tenant_id, contype u).
SELECT conname, contype
FROM pg_constraint
WHERE conrelid = 'users'::regclass
  AND conname = 'AK_users_id_tenant_id';

SELECT indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename IN ('clearance_items', 'clearance_approvers', 'clearance_viewers')
ORDER BY tablename, indexname;

-- Expected: 0 rows in all three tables right after this script.
SELECT
    (SELECT COUNT(*) FROM clearance_items) AS clearance_items_rows,
    (SELECT COUNT(*) FROM clearance_approvers) AS clearance_approvers_rows,
    (SELECT COUNT(*) FROM clearance_viewers) AS clearance_viewers_rows;

-- =============================================================================
-- OPTIONAL — assign Clearance approvers (manual, later; NOT part of deploy)
-- =============================================================================
-- Approvers are explicit assignments; no role grants approval automatically.
-- Replace the placeholders. Do not hardcode production user IDs here.
-- =============================================================================
--
-- BEGIN;
--   INSERT INTO clearance_approvers (id, tenant_id, user_id, created_at)
--   SELECT gen_random_uuid(), u.tenant_id, u.id, NOW()
--   FROM users u
--   WHERE u.username = '<DESIGNATED_USERNAME>'
--     AND u.tenant_id = '<TENANT_ID>'
--     AND u.is_active = true
--     AND NOT EXISTS (
--         SELECT 1
--         FROM clearance_approvers a
--         WHERE a.tenant_id = u.tenant_id
--           AND a.user_id = u.id
--     );
-- COMMIT;

-- =============================================================================
-- OPTIONAL — assign Clearance viewers (manual, later; NOT part of deploy)
-- =============================================================================
-- View-only access (queue, status, decision details) without approve/reject.
-- Approvers already view; do not add them here. Replace the placeholders.
-- =============================================================================
--
-- BEGIN;
--   INSERT INTO clearance_viewers (id, tenant_id, user_id, created_at)
--   SELECT gen_random_uuid(), u.tenant_id, u.id, NOW()
--   FROM users u
--   WHERE u.username = '<DESIGNATED_USERNAME>'
--     AND u.tenant_id = '<TENANT_ID>'
--     AND u.is_active = true
--     AND NOT EXISTS (
--         SELECT 1
--         FROM clearance_viewers v
--         WHERE v.tenant_id = u.tenant_id
--           AND v.user_id = u.id
--     );
-- COMMIT;

-- =============================================================================
-- ROLLBACK (reference only — NOT executed; run manually if ever needed)
-- =============================================================================
-- Drops the three tables created above (their constraints and indexes go
-- with them), then the unique key this script added to users. Drop the key
-- only after the tables: their foreign keys depend on it.
--
-- WARNING: dropping clearance_items permanently deletes all approval
-- decisions recorded in it. Export it first if it contains any rows:
--   \copy clearance_items TO 'clearance_items_backup.csv' CSV HEADER
--   \copy clearance_approvers TO 'clearance_approvers_backup.csv' CSV HEADER
--   \copy clearance_viewers TO 'clearance_viewers_backup.csv' CSV HEADER
--
-- BEGIN;
--   DROP TABLE IF EXISTS clearance_items;
--   DROP TABLE IF EXISTS clearance_approvers;
--   DROP TABLE IF EXISTS clearance_viewers;
--   ALTER TABLE users DROP CONSTRAINT IF EXISTS "AK_users_id_tenant_id";
-- COMMIT;

-- =============================================================================
-- MaintTrack — Production schema: Machine Parameter Photos (Phase 1)
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the schema introduced by EF Core migration
--   `20260923150459_AddMachineParameterPhotos`, so it can be run against
--   production WITHOUT `dotnet ef database update`.
--
--   Creates three tenant-scoped tables:
--     • machine_parameter_photos
--     • machine_parameter_photo_managers
--     • array_feature_visibility
--
-- CRITICAL — run this BEFORE deploying the backend build that maps these
-- tables, otherwise reads/writes against the new entities will fail.
--
-- Safe properties:
--   • Idempotent — IF NOT EXISTS guards; safe to re-run.
--   • Non-destructive — no DROP, no DELETE, no data loss.
--   • No seed data — no manager rows and no Array visibility rows.
--     All Arrays remain visible for this feature until a hide row is added.
--
-- Usage:
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-machine-parameter-photos.sql
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

CREATE TABLE IF NOT EXISTS machine_parameter_photos
(
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    machine_id uuid NOT NULL,
    image_url text NOT NULL,
    sort_order integer NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_machine_parameter_photos" PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS machine_parameter_photo_managers
(
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_machine_parameter_photo_managers" PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS array_feature_visibility
(
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    array_id uuid NOT NULL,
    feature_key text NOT NULL,
    is_visible boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_array_feature_visibility" PRIMARY KEY (id)
);

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_machine_parameter_photos_machines_machine_id'
          AND conrelid = 'machine_parameter_photos'::regclass
    ) THEN
        ALTER TABLE machine_parameter_photos
            ADD CONSTRAINT "FK_machine_parameter_photos_machines_machine_id"
            FOREIGN KEY (machine_id)
            REFERENCES machines (id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_machine_parameter_photo_managers_users_user_id'
          AND conrelid = 'machine_parameter_photo_managers'::regclass
    ) THEN
        ALTER TABLE machine_parameter_photo_managers
            ADD CONSTRAINT "FK_machine_parameter_photo_managers_users_user_id"
            FOREIGN KEY (user_id)
            REFERENCES users (id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_array_feature_visibility_arrays_array_id'
          AND conrelid = 'array_feature_visibility'::regclass
    ) THEN
        ALTER TABLE array_feature_visibility
            ADD CONSTRAINT "FK_array_feature_visibility_arrays_array_id"
            FOREIGN KEY (array_id)
            REFERENCES arrays (id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_machine_parameter_photos_machine_id_sort_order"
    ON machine_parameter_photos (machine_id, sort_order);

CREATE INDEX IF NOT EXISTS
    "IX_machine_parameter_photos_tenant_id_machine_id"
    ON machine_parameter_photos (tenant_id, machine_id);

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_machine_parameter_photo_managers_tenant_id_user_id"
    ON machine_parameter_photo_managers (tenant_id, user_id);

CREATE INDEX IF NOT EXISTS
    "IX_machine_parameter_photo_managers_user_id"
    ON machine_parameter_photo_managers (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_array_feature_visibility_tenant_id_array_id_feature_key"
    ON array_feature_visibility (tenant_id, array_id, feature_key);

CREATE INDEX IF NOT EXISTS
    "IX_array_feature_visibility_array_id"
    ON array_feature_visibility (array_id);

COMMIT;

-- =============================================================================
-- POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name IN (
      'machine_parameter_photos',
      'machine_parameter_photo_managers',
      'array_feature_visibility'
  )
ORDER BY table_name;

SELECT conname, conrelid::regclass AS table_name
FROM pg_constraint
WHERE conrelid IN (
    'machine_parameter_photos'::regclass,
    'machine_parameter_photo_managers'::regclass,
    'array_feature_visibility'::regclass
)
ORDER BY table_name, conname;

-- =============================================================================
-- OPTIONAL — grant Machine Parameter Photos manage access (manual, later)
-- =============================================================================
-- Do NOT run this as part of the general deploy. Replace the placeholders
-- with the two designated users. Do not hardcode production user IDs here.
-- Feature key for Array hide rows (when needed later): machine_parameter_photos
-- =============================================================================
--
-- BEGIN;
--   INSERT INTO machine_parameter_photo_managers (id, tenant_id, user_id, created_at)
--   SELECT gen_random_uuid(), u.tenant_id, u.id, NOW()
--   FROM users u
--   WHERE u.username = '<DESIGNATED_USERNAME_1>'
--     AND u.tenant_id = '<TENANT_ID>'
--     AND NOT EXISTS (
--         SELECT 1
--         FROM machine_parameter_photo_managers m
--         WHERE m.tenant_id = u.tenant_id
--           AND m.user_id = u.id
--     );
--
--   INSERT INTO machine_parameter_photo_managers (id, tenant_id, user_id, created_at)
--   SELECT gen_random_uuid(), u.tenant_id, u.id, NOW()
--   FROM users u
--   WHERE u.username = '<DESIGNATED_USERNAME_2>'
--     AND u.tenant_id = '<TENANT_ID>'
--     AND NOT EXISTS (
--         SELECT 1
--         FROM machine_parameter_photo_managers m
--         WHERE m.tenant_id = u.tenant_id
--           AND m.user_id = u.id
--     );
-- COMMIT;
--
-- SELECT m.id, u.username, m.tenant_id, m.created_at
-- FROM machine_parameter_photo_managers m
-- JOIN users u ON u.id = m.user_id
-- WHERE m.tenant_id = '<TENANT_ID>'
-- ORDER BY u.username;

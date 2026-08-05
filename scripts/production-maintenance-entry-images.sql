-- =============================================================================
-- MaintTrack — Production schema: maintenance entry additional images
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the schema introduced by EF Core migration
--   `20260804210000_AddMaintenanceEntryImages`, so it can be run against
--   production WITHOUT `dotnet ef database update`.
--
--   Creates `maintenance_entry_images` for up to two optional images per
--   Maintenance Log entry. Existing `MaintenanceEntries.ImageUrl` remains the
--   primary image and is unchanged.
--
-- CRITICAL — run this BEFORE deploying the new backend build that maps this
-- table, otherwise reads/writes that include AdditionalImages will fail.
--
-- Safe properties:
--   • Idempotent — IF NOT EXISTS guards; safe to re-run.
--   • Non-destructive — no DROP, no DELETE, no data loss.
--   • No backfill required — existing rows continue with primary ImageUrl only.
--
-- Usage:
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-maintenance-entry-images.sql
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

CREATE TABLE IF NOT EXISTS maintenance_entry_images
(
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    maintenance_entry_id uuid NOT NULL,
    image_url text NOT NULL,
    sort_order integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL,
    CONSTRAINT "PK_maintenance_entry_images" PRIMARY KEY (id)
);

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_maintenance_entry_images_MaintenanceEntries_maintenance_entry_id'
          AND conrelid = 'maintenance_entry_images'::regclass
    ) THEN
        ALTER TABLE maintenance_entry_images
            ADD CONSTRAINT "FK_maintenance_entry_images_MaintenanceEntries_maintenance_entry_id"
            FOREIGN KEY (maintenance_entry_id)
            REFERENCES "MaintenanceEntries" ("Id")
            ON DELETE CASCADE;
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_maintenance_entry_images_sort_order'
          AND conrelid = 'maintenance_entry_images'::regclass
    ) THEN
        ALTER TABLE maintenance_entry_images
            ADD CONSTRAINT ck_maintenance_entry_images_sort_order
            CHECK (sort_order >= 1 AND sort_order <= 2);
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS
    "IX_maintenance_entry_images_maintenance_entry_id_sort_order"
    ON maintenance_entry_images (maintenance_entry_id, sort_order);

CREATE INDEX IF NOT EXISTS
    "IX_maintenance_entry_images_tenant_id_maintenance_entry_id"
    ON maintenance_entry_images (tenant_id, maintenance_entry_id);

COMMIT;

-- =============================================================================
-- POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name = 'maintenance_entry_images';

SELECT conname
FROM pg_constraint
WHERE conrelid = 'maintenance_entry_images'::regclass
ORDER BY conname;

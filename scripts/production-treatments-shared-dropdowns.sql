-- =============================================================================
-- MaintTrack — Production schema: Treatments shared dropdowns (no EF migrations)
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same schema changes introduced by the EF
--   Core migration `20260613190000_UpdateTreatmentsSharedDropdowns`, so it can
--   be run directly against the production database WITHOUT
--   `dotnet ef database update`.
--
--   Adds three nullable columns to `treatments` so Treatments entries can
--   optionally reference a machine, a shared maintenance type, and the user
--   who created the entry (shared dropdowns with the Maintenance Log).
--
-- Prerequisites:
--   • `treatments` table must already exist (created by an earlier release).
--   • `machines`, `maintenance_types`, `users` tables must already exist.
--   (All guarded with existence checks below — safe no-op if any is missing.)
--
-- Safe properties:
--   • Idempotent — every ALTER/INDEX/CONSTRAINT uses IF NOT EXISTS / DO $$
--     guards, safe to re-run.
--   • Non-destructive — no DROP, no data deletion. New columns are nullable,
--     so existing `treatments` rows are unaffected (value stays NULL).
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-treatments-shared-dropdowns.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-treatments-shared-dropdowns.sql
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: SCHEMA — new nullable columns on treatments
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'treatments' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS machine_id uuid NULL;
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS maintenance_type_id uuid NULL;
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS created_by_user_id uuid NULL;
    END IF;
END $$;

-- =============================================================================
-- SECTION 2: INDEXES
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'created_by_user_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_created_by_user_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_created_by_user_id" ON treatments (created_by_user_id);
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_machine_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_machine_id" ON treatments (machine_id);
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_maintenance_type_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_maintenance_type_id" ON treatments (maintenance_type_id);
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'tenant_id'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_machine_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_tenant_id_machine_id" ON treatments (tenant_id, machine_id);
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'tenant_id'
    )
    AND EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_maintenance_type_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_tenant_id_maintenance_type_id" ON treatments (tenant_id, maintenance_type_id);
    END IF;
END $$;

-- =============================================================================
-- SECTION 3: FOREIGN KEYS (guarded — skipped if referenced table is missing)
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'machines' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_machines_machine_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT "FK_treatments_machines_machine_id"
        FOREIGN KEY (machine_id) REFERENCES machines (id) ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'maintenance_type_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'maintenance_types' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_maintenance_types_maintenance_type_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT "FK_treatments_maintenance_types_maintenance_type_id"
        FOREIGN KEY (maintenance_type_id) REFERENCES maintenance_types (id) ON DELETE SET NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'created_by_user_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'users' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_users_created_by_user_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT "FK_treatments_users_created_by_user_id"
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL;
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- SECTION 4: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'treatments'
  AND column_name IN ('machine_id', 'maintenance_type_id', 'created_by_user_id')
ORDER BY column_name;

SELECT conname
FROM pg_constraint
WHERE conname IN (
    'FK_treatments_machines_machine_id',
    'FK_treatments_maintenance_types_maintenance_type_id',
    'FK_treatments_users_created_by_user_id'
);

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   ALTER TABLE treatments DROP CONSTRAINT IF EXISTS "FK_treatments_machines_machine_id";
--   ALTER TABLE treatments DROP CONSTRAINT IF EXISTS "FK_treatments_maintenance_types_maintenance_type_id";
--   ALTER TABLE treatments DROP CONSTRAINT IF EXISTS "FK_treatments_users_created_by_user_id";
--   DROP INDEX IF EXISTS "IX_treatments_created_by_user_id";
--   DROP INDEX IF EXISTS "IX_treatments_machine_id";
--   DROP INDEX IF EXISTS "IX_treatments_maintenance_type_id";
--   DROP INDEX IF EXISTS "IX_treatments_tenant_id_machine_id";
--   DROP INDEX IF EXISTS "IX_treatments_tenant_id_maintenance_type_id";
--   ALTER TABLE treatments DROP COLUMN IF EXISTS machine_id;
--   ALTER TABLE treatments DROP COLUMN IF EXISTS maintenance_type_id;
--   ALTER TABLE treatments DROP COLUMN IF EXISTS created_by_user_id;
-- COMMIT;

-- =============================================================================
-- MaintTrack — Production schema: Treatments machine_component_id (no EF migrations)
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same schema changes introduced by the EF
--   Core migration `20260808100000_AddTreatmentMachineComponentId`, so it can
--   be run directly against the production database WITHOUT
--   `dotnet ef database update`.
--
--   Adds nullable `machine_component_id` to `treatments` so new Treatments can
--   reference a machine-specific component (UI label: "Treatment Type").
--   Does NOT remove or modify `maintenance_type_id` (kept for legacy reads).
--   Does NOT backfill existing rows.
--
-- Prerequisites:
--   • `treatments` table must already exist.
--   • `machine_components` table must already exist.
--   (All guarded with existence checks below — safe no-op if any is missing.)
--
-- Safe properties:
--   • Idempotent — every ALTER/INDEX/CONSTRAINT uses IF NOT EXISTS / DO $$
--     guards, safe to re-run.
--   • Non-destructive — no DROP of existing columns, no data deletion.
--     New column is nullable, so existing `treatments` rows are unaffected.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-treatments-machine-component.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-treatments-machine-component.sql
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: SCHEMA — new nullable column on treatments
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'treatments' AND c.relkind = 'r' AND n.nspname = 'public'
    ) THEN
        ALTER TABLE treatments ADD COLUMN IF NOT EXISTS machine_component_id uuid NULL;
    END IF;
END $$;

-- =============================================================================
-- SECTION 2: INDEXES
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_machine_component_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_machine_component_id" ON treatments (machine_component_id);
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
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_class WHERE relname = 'IX_treatments_tenant_id_machine_component_id' AND relkind = 'i'
    ) THEN
        CREATE INDEX "IX_treatments_tenant_id_machine_component_id" ON treatments (tenant_id, machine_component_id);
    END IF;
END $$;

-- =============================================================================
-- SECTION 3: FOREIGN KEY (guarded — skipped if machine_components is missing)
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'treatments' AND column_name = 'machine_component_id'
    )
    AND EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'machine_components' AND c.relkind = 'r' AND n.nspname = 'public'
    )
    AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_treatments_machine_components_machine_component_id'
    ) THEN
        ALTER TABLE treatments
        ADD CONSTRAINT "FK_treatments_machine_components_machine_component_id"
        FOREIGN KEY (machine_component_id) REFERENCES machine_components (id) ON DELETE SET NULL;
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- SECTION 4: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'treatments'
  AND column_name = 'machine_component_id'
ORDER BY column_name;

SELECT indexname
FROM pg_indexes
WHERE tablename = 'treatments'
  AND indexname IN (
    'IX_treatments_machine_component_id',
    'IX_treatments_tenant_id_machine_component_id'
  )
ORDER BY indexname;

SELECT conname
FROM pg_constraint
WHERE conname = 'FK_treatments_machine_components_machine_component_id';

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   ALTER TABLE treatments DROP CONSTRAINT IF EXISTS "FK_treatments_machine_components_machine_component_id";
--   DROP INDEX IF EXISTS "IX_treatments_machine_component_id";
--   DROP INDEX IF EXISTS "IX_treatments_tenant_id_machine_component_id";
--   ALTER TABLE treatments DROP COLUMN IF EXISTS machine_component_id;
-- COMMIT;

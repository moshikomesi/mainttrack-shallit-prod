-- =============================================================================
-- MaintTrack — Production manual schema (Morning Round v2 + Arrays)
-- =============================================================================
-- Purpose:
--   Apply all schema required for Morning Round v2 submission/reporting and the
--   Arrays → Machines hierarchy WITHOUT using EF migrations.
--
-- Safe properties:
--   • Idempotent (safe to run multiple times)
--   • Non-destructive (no DROP TABLE / no data deletion)
--   • Does NOT modify v1 morning_round_* tables
--   • Compatible with PostgreSQL 14+
--
-- Usage:
--   psql -h HOST -U USER -d DATABASE -f scripts/production-manual-schema-morning-round-v2.sql
--
-- After running:
--   • Populate arrays + machine.array_id for each production tenant (see OPTIONAL SEED)
--   • Set is_morning_round_enabled = true on arrays used in Morning Round v2
--   • Restart API if needed
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: SCHEMA
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1.1 Arrays (work groups for machine hierarchy)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS arrays (
    id                          uuid                        NOT NULL,
    tenant_id                   uuid                        NOT NULL,
    name_key                    text                        NOT NULL,
    sort_order                  integer                     NOT NULL DEFAULT 0,
    is_active                   boolean                     NOT NULL DEFAULT true,
    is_morning_round_enabled    boolean                     NOT NULL DEFAULT false,
    created_at                  timestamp with time zone    NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_arrays PRIMARY KEY (id)
);

-- Add columns that may be missing on partially-applied databases
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'arrays' AND column_name = 'sort_order'
    ) THEN
        ALTER TABLE arrays ADD COLUMN sort_order integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'arrays' AND column_name = 'is_active'
    ) THEN
        ALTER TABLE arrays ADD COLUMN is_active boolean NOT NULL DEFAULT true;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'arrays' AND column_name = 'is_morning_round_enabled'
    ) THEN
        ALTER TABLE arrays ADD COLUMN is_morning_round_enabled boolean NOT NULL DEFAULT false;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'arrays' AND column_name = 'created_at'
    ) THEN
        ALTER TABLE arrays ADD COLUMN created_at timestamp with time zone NOT NULL DEFAULT NOW();
    END IF;
END $$;

-- Ensure sensible defaults on existing columns (no-op if already set)
ALTER TABLE arrays ALTER COLUMN sort_order SET DEFAULT 0;
ALTER TABLE arrays ALTER COLUMN is_active SET DEFAULT true;
ALTER TABLE arrays ALTER COLUMN is_morning_round_enabled SET DEFAULT false;
ALTER TABLE arrays ALTER COLUMN created_at SET DEFAULT NOW();

-- -----------------------------------------------------------------------------
-- 1.2 Machines — array_id link (does NOT alter v1 machine columns)
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'machines' AND c.relkind = 'r' AND n.nspname = 'public'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'machines' AND column_name = 'array_id'
    ) THEN
        ALTER TABLE machines ADD COLUMN array_id uuid NULL;
    END IF;
END $$;

-- -----------------------------------------------------------------------------
-- 1.3 Morning Round v2 submissions
--     Payload is stored in items_json (jsonb array of { machineId, status, notes })
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS morning_round_v2_submissions (
    id                      uuid                        NOT NULL,
    tenant_id               uuid                        NOT NULL,
    report_date             date                        NOT NULL,
    submitted_at            timestamp with time zone    NOT NULL,
    submitted_by_user_id    uuid                        NOT NULL,
    items_json              jsonb                       NOT NULL DEFAULT '[]'::jsonb,
    created_at              timestamp with time zone    NOT NULL DEFAULT NOW(),
    updated_at              timestamp with time zone    NULL,
    CONSTRAINT pk_morning_round_v2_submissions PRIMARY KEY (id)
);

-- Add columns that may be missing on partially-created tables
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'morning_round_v2_submissions'
          AND column_name = 'items_json'
    ) THEN
        ALTER TABLE morning_round_v2_submissions
            ADD COLUMN items_json jsonb NOT NULL DEFAULT '[]'::jsonb;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'morning_round_v2_submissions'
          AND column_name = 'created_at'
    ) THEN
        ALTER TABLE morning_round_v2_submissions
            ADD COLUMN created_at timestamp with time zone NOT NULL DEFAULT NOW();
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'morning_round_v2_submissions'
          AND column_name = 'updated_at'
    ) THEN
        ALTER TABLE morning_round_v2_submissions
            ADD COLUMN updated_at timestamp with time zone NULL;
    END IF;
END $$;

ALTER TABLE morning_round_v2_submissions ALTER COLUMN items_json SET DEFAULT '[]'::jsonb;
ALTER TABLE morning_round_v2_submissions ALTER COLUMN created_at SET DEFAULT NOW();

-- =============================================================================
-- SECTION 2: INDEXES
-- =============================================================================

CREATE INDEX IF NOT EXISTS ix_arrays_tenant_id
    ON arrays (tenant_id);

CREATE INDEX IF NOT EXISTS ix_arrays_tenant_id_sort_order
    ON arrays (tenant_id, sort_order);

CREATE INDEX IF NOT EXISTS ix_machines_array_id
    ON machines (array_id)
    WHERE array_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_machines_tenant_id_array_id
    ON machines (tenant_id, array_id)
    WHERE array_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_morning_round_v2_submissions_tenant_id
    ON morning_round_v2_submissions (tenant_id);

CREATE INDEX IF NOT EXISTS ix_morning_round_v2_submissions_tenant_submitted_at
    ON morning_round_v2_submissions (tenant_id, submitted_at DESC);

-- =============================================================================
-- SECTION 3: CONSTRAINTS
-- =============================================================================

-- One submission per tenant per calendar day (upsert semantics in application)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_morning_round_v2_submissions_tenant_report_date'
    ) THEN
        ALTER TABLE morning_round_v2_submissions
            ADD CONSTRAINT uq_morning_round_v2_submissions_tenant_report_date
            UNIQUE (tenant_id, report_date);
    END IF;
END $$;

-- Fallback unique index if neither constraint nor index exists (partial prior runs)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_morning_round_v2_submissions_tenant_report_date'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'public'
          AND tablename = 'morning_round_v2_submissions'
          AND indexdef LIKE '%UNIQUE%'
          AND indexdef LIKE '%tenant_id%'
          AND indexdef LIKE '%report_date%'
    ) THEN
        CREATE UNIQUE INDEX ix_morning_round_v2_submissions_tenant_id_report_date
            ON morning_round_v2_submissions (tenant_id, report_date);
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- SECTION 4: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT 'arrays' AS object, COUNT(*) AS column_count
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'arrays';

SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'arrays'
ORDER BY ordinal_position;

SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'machines' AND column_name = 'array_id'
) AS machines_array_id_exists;

SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'morning_round_v2_submissions'
ORDER BY ordinal_position;

SELECT indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename IN ('arrays', 'machines', 'morning_round_v2_submissions')
ORDER BY tablename, indexname;

-- =============================================================================
-- SECTION 5: OPTIONAL DEV SEED ONLY — DO NOT RUN ON PRODUCTION AS-IS
-- =============================================================================
-- Uncomment and replace @TENANT_ID@ with your production tenant UUID before use.
-- This seeds translation-key arrays and sample machines for Pilot Factory layout.
-- Production tenants should seed arrays/machines via your operational runbook.
-- =============================================================================

/*
BEGIN;

-- Replace with your tenant UUID:
--   11111111-1111-1111-1111-111111111111  (Pilot Factory dev example)

INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
VALUES
  ('a1000001-1111-1111-1111-111111111101', '11111111-1111-1111-1111-111111111111', 'array.washing_system',       1, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111102', '11111111-1111-1111-1111-111111111111', 'array.onion_system',         2, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111103', '11111111-1111-1111-1111-111111111111', 'array.water_cooling_system', 3, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111104', '11111111-1111-1111-1111-111111111111', 'array.rooms',                4, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111105', '11111111-1111-1111-1111-111111111111', 'array.ginoshar',             5, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111106', '11111111-1111-1111-1111-111111111111', 'array.packing_house',        6, true, true, NOW())
ON CONFLICT (id) DO UPDATE SET
  name_key = EXCLUDED.name_key,
  sort_order = EXCLUDED.sort_order,
  is_active = EXCLUDED.is_active,
  is_morning_round_enabled = EXCLUDED.is_morning_round_enabled;

-- Map existing machines by translation key (name column) to arrays — safe updates only.
UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111101'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name IN (
    'machine.elevatorToDestoner', 'machine.destoner', 'machine.soakingPoolPump',
    'machine.washingDrumPump', 'machine.organicDrum', 'machine.roundPitPump',
    'machine.organicDrumAbovePool', 'machine.pushPumps2', 'machine.washing.conveyors',
    'machine.smallHopper', 'machine.largeHopper', 'machine.dryCleaningProcess',
    'machine.soakingTank', 'machine.internalWashDrum',
    'machine.polisher1', 'machine.polisher2', 'machine.polisher3', 'machine.wearBroken2'
  );

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111102'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name LIKE 'machine.onion.%';

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111103'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name LIKE 'machine.waterCooling.%';

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111104'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND (name LIKE 'machine.rooms.%' OR name IN ('machine.coolingDoor', 'machine.lubrication', 'machine.chlorineSystem'));

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111105'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name LIKE 'machine.ginoshar.%';

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111106'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND (name LIKE 'machine.packing.%' OR name IN (
    'machine.sorter1', 'machine.sorter2', 'machine.sorter3',
    'machine.scale1', 'machine.scale2', 'machine.scale3', 'machine.scale4', 'machine.scale5', 'machine.scale6',
    'machine.packing1', 'machine.packing2', 'machine.packing3', 'machine.packing4',
    'machine.packing5a', 'machine.packing5b', 'machine.packing6a', 'machine.packing6b',
    'machine.sorterLine5', 'machine.sorterLine6', 'machine.masters', 'machine.viscose'
  ));

COMMIT;

SELECT a.name_key, COUNT(m.id) AS active_machine_count
FROM arrays a
LEFT JOIN machines m ON m.array_id = a.id AND m.is_active = true
WHERE a.tenant_id = '11111111-1111-1111-1111-111111111111'
GROUP BY a.sort_order, a.name_key
ORDER BY a.sort_order;
*/

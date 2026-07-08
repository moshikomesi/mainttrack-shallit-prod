-- =============================================================================
-- MaintTrack — Production schema: Machine Components infrastructure
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same SCHEMA changes introduced by the EF
--   Core migration `20260705230000_AddMachineComponentsInfrastructure`, so it
--   can be run directly against production WITHOUT `dotnet ef database
--   update`.
--
--   Creates the two tables that back the Maintenance Log V2 hierarchical
--   picker (Array → Machine → Component):
--     • machine_components          (per-tenant component catalog)
--     • machine_component_mappings  (which components apply to which machine)
--
-- IMPORTANT — this script is SCHEMA ONLY. It does not insert any rows.
--   After running this script, apply the DATA scripts in this order:
--     1. scripts/machine_components_seed.sql        (base catalog + mappings)
--     2. scripts/production-hierarchy-map-alignment.sql
--          (adds RUBBER_STARS/BEARINGS/OVERHAUL + remaining mappings)
--   Both of those scripts INSERT INTO machine_components /
--   machine_component_mappings and will fail with "relation does not exist"
--   if this schema script has not been run first.
--
-- Safe properties:
--   • Idempotent (safe to run multiple times) — CREATE TABLE IF NOT EXISTS,
--     guarded indexes/constraints.
--   • Non-destructive (no DROP TABLE / no data deletion).
--   • Compatible with PostgreSQL 14+.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-machine-components-schema.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-machine-components-schema.sql
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: SCHEMA
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1.1 machine_components — per-tenant catalog of reusable component types
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS machine_components (
    id          uuid                        NOT NULL,
    tenant_id   uuid                        NOT NULL,
    code        character varying(100)      NOT NULL,
    name_key    character varying(200)      NOT NULL,
    sort_order  integer                     NOT NULL DEFAULT 0,
    is_active   boolean                     NOT NULL DEFAULT true,
    created_at  timestamp with time zone    NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_machine_components PRIMARY KEY (id)
);

-- -----------------------------------------------------------------------------
-- 1.2 machine_component_mappings — which components apply to which machine
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS machine_component_mappings (
    id            uuid                        NOT NULL,
    tenant_id     uuid                        NOT NULL,
    machine_id    uuid                        NOT NULL,
    component_id  uuid                        NOT NULL,
    sort_order    integer                     NOT NULL DEFAULT 0,
    is_active     boolean                     NOT NULL DEFAULT true,
    created_at    timestamp with time zone    NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_machine_component_mappings PRIMARY KEY (id)
);

-- =============================================================================
-- SECTION 2: INDEXES
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_class
        WHERE relname = 'ix_machine_components_tenant_id_code'
          AND relkind = 'i'
    ) THEN
        CREATE UNIQUE INDEX ix_machine_components_tenant_id_code
            ON machine_components (tenant_id, code);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_class
        WHERE relname = 'ix_machine_component_mappings_machine_id_component_id'
          AND relkind = 'i'
    ) THEN
        CREATE UNIQUE INDEX ix_machine_component_mappings_machine_id_component_id
            ON machine_component_mappings (machine_id, component_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_class
        WHERE relname = 'ix_machine_component_mappings_tenant_id_machine_id'
          AND relkind = 'i'
    ) THEN
        CREATE INDEX ix_machine_component_mappings_tenant_id_machine_id
            ON machine_component_mappings (tenant_id, machine_id);
    END IF;
END $$;

-- =============================================================================
-- SECTION 3: FOREIGN KEYS
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_machine_component_mappings_machines_machine_id'
    ) THEN
        ALTER TABLE machine_component_mappings
            ADD CONSTRAINT fk_machine_component_mappings_machines_machine_id
            FOREIGN KEY (machine_id) REFERENCES machines (id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_machine_component_mappings_machine_components_component_id'
    ) THEN
        ALTER TABLE machine_component_mappings
            ADD CONSTRAINT fk_machine_component_mappings_machine_components_component_id
            FOREIGN KEY (component_id) REFERENCES machine_components (id) ON DELETE RESTRICT;
    END IF;
END $$;

COMMIT;

-- =============================================================================
-- SECTION 4: POST-RUN VERIFICATION (read-only)
-- =============================================================================

SELECT 'machine_components' AS table_name, COUNT(*) AS column_count
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'machine_components'
UNION ALL
SELECT 'machine_component_mappings', COUNT(*)
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'machine_component_mappings';

SELECT indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename IN ('machine_components', 'machine_component_mappings')
ORDER BY tablename, indexname;

SELECT conname, conrelid::regclass AS table_name
FROM pg_constraint
WHERE conname IN (
    'fk_machine_component_mappings_machines_machine_id',
    'fk_machine_component_mappings_machine_components_component_id'
);

-- NEXT STEPS (run after this script succeeds):
--   psql ... -f scripts/machine_components_seed.sql
--   psql ... -f scripts/production-hierarchy-map-alignment.sql

-- =============================================================================
-- ROLLBACK (reference only — run manually if ever needed)
-- =============================================================================
-- BEGIN;
--   DROP TABLE IF EXISTS machine_component_mappings;
--   DROP TABLE IF EXISTS machine_components;
-- COMMIT;
-- NOTE: dropping these tables destroys all Maintenance Log V2 hierarchy data
-- (component catalog + machine/component mappings). Only do this as part of a
-- full, deliberate rollback of the entire Maintenance Log V2 feature.

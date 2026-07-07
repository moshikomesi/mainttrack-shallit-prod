-- =============================================================================
-- MaintTrack — Production Hierarchy Map Alignment (no EF migrations)
-- =============================================================================
-- Purpose:
--   Applies, via plain SQL, the exact same data/schema changes introduced by
--   the following 4 EF Core migrations (in order), so they can be run
--   directly against the production database WITHOUT `dotnet ef database
--   update` and WITHOUT deploying/registering EF migration assemblies:
--
--     1. 20260707220000_UpdateOnionMixerComponents
--     2. 20260707230000_HierarchyMapAlignment
--     3. 20260707233000_AddMachineSortOrder
--     4. 20260707234500_DeactivatePackingHouseExtras
--
--   See HIERARCHY_MAP_ALIGNMENT_REPORT.md at the repo root for the full
--   human-readable summary of what changed and why.
--
-- Safe properties:
--   • Idempotent — every INSERT uses NOT EXISTS guards, every UPDATE is
--     scoped by name/code so re-running has no additional effect.
--   • Applies to ALL tenants automatically (uses `FROM tenants t` /
--     `CROSS JOIN`), no tenant UUID needs to be supplied.
--   • Non-destructive — no DROP TABLE, no DELETE of historical data.
--     "Removed" machines are soft-deactivated (is_active = false), never
--     physically deleted, so historical reports referencing them still work.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -f scripts/production-hierarchy-map-alignment.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   ./scripts/run-production-hierarchy-map-alignment.sh
--
-- After running:
--   • Deploy the updated frontend build (frontend/src/i18n/translations.ts
--     label changes ship with the normal frontend deploy — no DB action
--     needed for those).
--   • Restart the API service so EF's model/migration-history checks (if any)
--     stay consistent; no code changes are required for this script alone.
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1 — Onion Mixer (מקנבת) component list update
--   (equivalent to migration 20260707220000_UpdateOnionMixerComponents)
-- =============================================================================

-- 1.1 New catalog components: RUBBER_STARS (גומיות/כוכבים), BEARINGS (מיסבים)
INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
SELECT v.id, t.id, v.code, v.name_key, v.sort_order, true, NOW()
FROM tenants t
CROSS JOIN (VALUES
    ('c3000001-0000-0000-0000-000000000023'::uuid, 'RUBBER_STARS', 'maintenanceComponent.rubberStars', 23),
    ('c3000001-0000-0000-0000-000000000024'::uuid, 'BEARINGS',     'maintenanceComponent.bearings',    24)
) AS v(id, code, name_key, sort_order)
WHERE NOT EXISTS (
    SELECT 1 FROM machine_components mc
    WHERE mc.tenant_id = t.id AND mc.code = v.code
);

-- 1.2 Deactivate the shared RUBBER mapping specifically for machine.onion.mixer
--     (the RUBBER catalog entry, and its use by other machines, is untouched)
UPDATE machine_component_mappings mcm
SET is_active = false
FROM machines m, machine_components mc
WHERE mcm.machine_id = m.id
  AND mcm.component_id = mc.id
  AND mcm.is_active = true
  AND m.name = 'machine.onion.mixer'
  AND m.is_active = true
  AND mc.code = 'RUBBER';

-- 1.3 Add RUBBER_STARS + BEARINGS mappings to machine.onion.mixer
INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
FROM (VALUES
    ('machine.onion.mixer', 'RUBBER_STARS', 1),
    ('machine.onion.mixer', 'BEARINGS',     4)
) AS spec(machine_name, component_code, sort_order)
JOIN machines m
    ON m.name = spec.machine_name
   AND m.is_active = true
JOIN machine_components mc
    ON mc.tenant_id = m.tenant_id
   AND mc.code = spec.component_code
WHERE NOT EXISTS (
    SELECT 1 FROM machine_component_mappings existing
    WHERE existing.machine_id = m.id AND existing.component_id = mc.id
);

-- =============================================================================
-- SECTION 2 — Hierarchy map alignment
--   (equivalent to migration 20260707230000_HierarchyMapAlignment)
-- =============================================================================

-- 2.1 New generic component: OVERHAUL (שיפוץ)
INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
SELECT gen_random_uuid(), t.id, 'OVERHAUL', 'maintenanceComponent.overhaul', 25, true, NOW()
FROM tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM machine_components mc WHERE mc.tenant_id = t.id AND mc.code = 'OVERHAUL'
);

-- 2.2 New top-level array: array.conveyors (מסועים) — excluded from Morning Round V2
INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
SELECT gen_random_uuid(), t.id, 'array.conveyors', 8, true, false, NOW()
FROM tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM arrays a WHERE a.tenant_id = t.id AND a.name_key = 'array.conveyors'
);

-- 2.3 New machine within the Conveyors array
INSERT INTO machines (id, tenant_id, name, code, description, is_active, array_id, created_at)
SELECT gen_random_uuid(), t.id, 'machine.conveyors.general', 'conveyors.general', NULL, true, a.id, NOW()
FROM tenants t
JOIN arrays a ON a.tenant_id = t.id AND a.name_key = 'array.conveyors'
WHERE NOT EXISTS (
    SELECT 1 FROM machines m WHERE m.tenant_id = t.id AND m.name = 'machine.conveyors.general'
);

-- 2.4 Component mappings: Conveyors machine, Elevator to Destoner, Water Cooling compressors
INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
FROM (VALUES
    ('machine.conveyors.general',          'MOTOR',        1),
    ('machine.conveyors.general',          'DRIVE_SHAFT',  2),
    ('machine.conveyors.general',          'DRIVEN_SHAFT', 3),
    ('machine.conveyors.general',          'CONVEYOR_BELT',4),
    ('machine.conveyors.general',          'BEARING',      5),
    ('machine.elevatorToDestoner',         'SHAFT',        1),
    ('machine.elevatorToDestoner',         'CONVEYOR',     2),
    ('machine.elevatorToDestoner',         'VOLTA_BELT',   3),
    ('machine.elevatorToDestoner',         'MOTOR',        4),
    ('machine.waterCooling.compressor1',   'OVERHAUL',     1),
    ('machine.waterCooling.compressor1',   'LUBRICATION',  2),
    ('machine.waterCooling.compressor2',   'OVERHAUL',     1),
    ('machine.waterCooling.compressor2',   'LUBRICATION',  2),
    ('machine.waterCooling.compressor3',   'OVERHAUL',     1),
    ('machine.waterCooling.compressor3',   'LUBRICATION',  2),
    ('machine.waterCooling.compressor4',   'OVERHAUL',     1),
    ('machine.waterCooling.compressor4',   'LUBRICATION',  2)
) AS spec(machine_name, component_code, sort_order)
JOIN machines m
    ON m.name = spec.machine_name
   AND m.is_active = true
JOIN machine_components mc
    ON mc.tenant_id = m.tenant_id
   AND mc.code = spec.component_code
WHERE NOT EXISTS (
    SELECT 1 FROM machine_component_mappings existing
    WHERE existing.machine_id = m.id AND existing.component_id = mc.id
);

-- 2.5 Assign the previously-unassigned Accumulator Outside machine to Packing House
UPDATE machines m
SET array_id = a.id
FROM arrays a
WHERE m.tenant_id = a.tenant_id
  AND a.name_key = 'array.packing_house'
  AND m.name = 'machine.accumulatorOutside'
  AND m.is_active = true
  AND m.array_id IS NULL;

-- 2.6 Deactivate machines not present in the reference hierarchy map
UPDATE machines
SET is_active = false, array_id = NULL
WHERE name IN (
    'machine.lubrication',
    'machine.chlorineSystem',
    'machine.coolingDoor',
    'machine.packing.visionSystem'
)
AND is_active = true;

-- =============================================================================
-- SECTION 3 — Machine ordering (sort_order column)
--   (equivalent to migration 20260707233000_AddMachineSortOrder)
-- =============================================================================

-- 3.1 Add sort_order column if missing
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'machines'
          AND column_name = 'sort_order'
    ) THEN
        ALTER TABLE machines ADD COLUMN sort_order integer NOT NULL DEFAULT 0;
    END IF;
END $$;

-- 3.2 Populate sort_order per the reference map (all tenants; matched by name key)
UPDATE machines m
SET sort_order = spec.sort_order
FROM (VALUES
    -- Washing Array (מערך שטיפה)
    ('machine.largeHopper',                1),
    ('machine.smallHopper',                2),
    ('machine.dryCleaningProcess',         3),
    ('machine.elevatorToDestoner',         4),
    ('machine.destoner',                   5),
    ('machine.soakingTank',                6),
    ('machine.soakingPoolPump',            7),
    ('machine.internalWashDrum',           8),
    ('machine.washingDrumPump',            9),
    ('machine.wearBroken2',                10),
    ('machine.polisher1',                  11),
    ('machine.polisher2',                  12),
    ('machine.polisher3',                  13),
    ('machine.organicDrum',                14),
    ('machine.roundPitPump',                15),
    ('machine.organicDrumAbovePool',       16),
    ('machine.pushPumps2',                 17),
    ('machine.washing.conveyors',          18),
    -- Onion Array (מערך בצל)
    ('machine.onion.hopper',                1),
    ('machine.onion.elevator',              2),
    ('machine.onion.washingDrum',           3),
    ('machine.onion.mixer',                 4),
    ('machine.onion.sorter',                5),
    ('machine.onion.conveyors',             6),
    ('machine.onion.tyingMachine',          7),
    ('machine.onion.dryingRoom',            8),
    -- Water Cooling Array (מערך קירור מים)
    ('machine.waterCooling.compressor1',    1),
    ('machine.waterCooling.compressor2',    2),
    ('machine.waterCooling.compressor3',    3),
    ('machine.waterCooling.compressor4',    4),
    ('machine.waterCooling.condensers',     5),
    ('machine.waterCooling.coolingPlate',   6),
    ('machine.waterCooling.fans',           7),
    ('machine.waterCooling.waterPump1',     8),
    ('machine.waterCooling.waterPump2',     9),
    ('machine.waterCooling.waterPump3',     10),
    ('machine.waterCooling.automaticWaterFilling', 11),
    -- Rooms (חדרים)
    ('machine.rooms.compressor1',           1),
    ('machine.rooms.compressor2',           2),
    ('machine.rooms.compressor3',           3),
    ('machine.rooms.compressor4',           4),
    ('machine.rooms.roomDoors',             5),
    ('machine.rooms.fastDoor',               6),
    ('machine.rooms.condensers',            7),
    -- Ginoshar (גן שומרון)
    ('machine.ginoshar.netPackingMachine',   1),
    ('machine.ginoshar.sensorPackingMachine',2),
    ('machine.ginoshar.airCompressor',       3),
    -- Packing House (בית אריזה)
    ('machine.sorter1',                      1),
    ('machine.sorter2',                      2),
    ('machine.sorter3',                      3),
    ('machine.packing.poolElevators',        4),
    ('machine.packing.conveyorSystem',       5),
    ('machine.packing.fastVerbrocken',       6),
    ('machine.packing.verbrocken',           7),
    ('machine.packing.bulkFiller',           8),
    ('machine.packing.metalDetector',        9),
    ('machine.scale1',                       10),
    ('machine.packing1',                     11),
    ('machine.scale2',                       12),
    ('machine.packing2',                     13),
    ('machine.scale3',                       14),
    ('machine.packing3',                     15),
    ('machine.scale4',                       16),
    ('machine.packing4',                     17),
    ('machine.scale5',                       18),
    ('machine.packing5a',                    19),
    ('machine.packing5b',                    20),
    ('machine.sorterLine5',                  21),
    ('machine.scale6',                       22),
    ('machine.packing6a',                    23),
    ('machine.packing6b',                    24),
    ('machine.sorterLine6',                  25),
    ('machine.masters',                      26),
    ('machine.accumulatorOutside',           27),
    ('machine.viscose',                      28),
    -- Conveyors (מסועים)
    ('machine.conveyors.general',            1)
) AS spec(machine_name, sort_order)
WHERE m.name = spec.machine_name;

-- =============================================================================
-- SECTION 4 — Remove Packing House extras not present in the reference map
--   (equivalent to migration 20260707234500_DeactivatePackingHouseExtras)
-- =============================================================================

UPDATE machines
SET is_active = false, array_id = NULL
WHERE name IN ('machine.packing.stackers', 'machine.packing.externalBuckets')
  AND is_active = true;

COMMIT;

-- =============================================================================
-- SECTION 5 — POST-RUN VERIFICATION (read-only)
-- =============================================================================

-- New/updated components present
SELECT code, name_key, sort_order, is_active
FROM machine_components
WHERE code IN ('RUBBER_STARS', 'BEARINGS', 'OVERHAUL')
ORDER BY code;

-- Conveyors array + machine created
SELECT a.name_key AS array_key, a.is_morning_round_enabled, m.name AS machine_key
FROM arrays a
LEFT JOIN machines m ON m.array_id = a.id AND m.is_active = true
WHERE a.name_key = 'array.conveyors';

-- Machines deactivated by this script
SELECT name, is_active, array_id
FROM machines
WHERE name IN (
    'machine.lubrication', 'machine.chlorineSystem', 'machine.coolingDoor',
    'machine.packing.visionSystem', 'machine.packing.stackers', 'machine.packing.externalBuckets'
)
ORDER BY name;

-- sort_order populated for Packing House, in map order
SELECT a.name_key AS array_key, m.name AS machine_key, m.sort_order, m.is_active
FROM machines m
JOIN arrays a ON a.id = m.array_id
WHERE a.name_key = 'array.packing_house'
ORDER BY m.sort_order, m.name;

-- Accumulator Outside now assigned
SELECT m.name, a.name_key AS array_key
FROM machines m
LEFT JOIN arrays a ON a.id = m.array_id
WHERE m.name = 'machine.accumulatorOutside';

-- =============================================================================
-- ROLLBACK (reference only — run manually and selectively if ever needed)
-- =============================================================================
-- BEGIN;
--   UPDATE machines m SET is_active = true, array_id = a.id
--     FROM arrays a WHERE a.tenant_id = m.tenant_id AND a.name_key = 'array.rooms'
--     AND m.name IN ('machine.lubrication', 'machine.chlorineSystem', 'machine.coolingDoor');
--   UPDATE machines m SET is_active = true, array_id = a.id
--     FROM arrays a WHERE a.tenant_id = m.tenant_id AND a.name_key = 'array.packing_house'
--     AND m.name IN ('machine.packing.visionSystem', 'machine.packing.stackers', 'machine.packing.externalBuckets');
--   UPDATE machines SET array_id = NULL WHERE name = 'machine.accumulatorOutside';
--   DELETE FROM machine_component_mappings mcm USING machines m
--     WHERE mcm.machine_id = m.id AND m.name IN (
--       'machine.conveyors.general', 'machine.elevatorToDestoner',
--       'machine.waterCooling.compressor1', 'machine.waterCooling.compressor2',
--       'machine.waterCooling.compressor3', 'machine.waterCooling.compressor4');
--   DELETE FROM machines WHERE name = 'machine.conveyors.general';
--   DELETE FROM arrays WHERE name_key = 'array.conveyors';
--   DELETE FROM machine_components WHERE code IN ('OVERHAUL', 'RUBBER_STARS', 'BEARINGS');
--   UPDATE machine_component_mappings mcm SET is_active = true
--     FROM machines m, machine_components mc
--     WHERE mcm.machine_id = m.id AND mcm.component_id = mc.id
--     AND m.name = 'machine.onion.mixer' AND mc.code = 'RUBBER';
--   -- sort_order column is left in place intentionally (harmless to keep;
--   -- dropping it would require: ALTER TABLE machines DROP COLUMN sort_order;)
-- COMMIT;

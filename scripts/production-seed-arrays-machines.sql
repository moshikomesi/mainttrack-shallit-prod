-- =============================================================================
-- MaintTrack — Production seed: Arrays + Machines hierarchy (CANONICAL)
-- =============================================================================
-- ★ This is the CANONICAL, current arrays/machines production seed script. ★
-- It implements the 6-array hierarchy (washing_system, onion_system,
-- water_cooling_system, rooms, ginoshar, packing_house — plus `conveyors`,
-- added later by production-hierarchy-map-alignment.sql) that all of this
-- sprint's hierarchy work (sort_order, machine_component_mappings,
-- translations) depends on.
--
-- Do not use scripts/obsolete/arrays_seed.sql, machines_seed.sql,
-- machines_mapping.sql, production-seed-shallit-arrays-insert.sql, or
-- production-seed-shallit-array-mapping.sql — they are superseded/obsolete.
-- See scripts/README.md ("Arrays production seed — which script to use") for
-- the full explanation.
--
-- Purpose:
--   Insert work-group arrays and link machines for Morning Round v2 / hierarchy.
--
-- BEFORE RUNNING:
--   1. Apply schema: scripts/production-manual-schema-morning-round-v2.sql
--   2. You MUST pass the target tenant UUID as a psql variable — this script
--      does not default it (a hardcoded default previously in this file
--      silently overrode any -v tenant_id=... passed on the command line;
--      fixed 2026-07-08). For the Shallit production tenant:
--        psql ... -v tenant_id='beed1fc4-ffbb-4ea1-b7c8-d84584506842' \
--             -f scripts/production-seed-arrays-machines.sql
--      Prefer scripts/run-production-seed-arrays-machines.sh, which sets
--      this for you (docker mode defaults to a placeholder tenant; remote
--      mode defaults PROD_TENANT_ID to the Shallit tenant above).
--   3. Ensure the tenant row exists in `tenants`
--
-- Safety:
--   • Idempotent (safe to run multiple times)
--   • INSERT ... ON CONFLICT DO NOTHING for new rows
--   • Machine inserts skipped when name already exists for tenant
--   • UPDATE only sets array_id (no deletes, no deactivations)
--   • Does NOT modify v1 tables (morning_round_reports, etc.)
--   • Fails fast (via \if below) if tenant_id was not supplied, instead of
--     silently seeding a placeholder tenant
--
-- Usage:
--   psql -h HOST -U USER -d DATABASE -v tenant_id='<tenant-uuid>' \
--        -f scripts/production-seed-arrays-machines.sql
-- =============================================================================

\if :{?tenant_id}
\else
  \echo 'ERROR: tenant_id psql variable not set.'
  \echo 'Run with: psql ... -v tenant_id=<tenant-uuid> -f production-seed-arrays-machines.sql'
  \quit
\endif

BEGIN;

-- =============================================================================
-- SECTION 1: INSERT ARRAYS
-- =============================================================================
-- Fixed array UUIDs (prefix a2000001...) — do not change after first production run.

INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
VALUES
  ('a2000001-0000-0000-0000-000000000001', :'tenant_id', 'array.washing_system',       1, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000002', :'tenant_id', 'array.onion_system',         2, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000003', :'tenant_id', 'array.water_cooling_system', 3, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000004', :'tenant_id', 'array.rooms',                4, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000005', :'tenant_id', 'array.ginoshar',             5, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000006', :'tenant_id', 'array.packing_house',        6, true, true, NOW())
ON CONFLICT (id) DO NOTHING;

-- =============================================================================
-- SECTION 2: INSERT MACHINES (only when missing for tenant + translation key)
-- =============================================================================
-- Fixed machine UUIDs (prefix b2000001...). Skips insert if `name` already exists.

INSERT INTO machines (id, tenant_id, name, code, description, is_active, array_id, created_at)
SELECT v.id, v.tenant_id, v.name, v.code, NULL, true, v.array_id, NOW()
FROM (VALUES
  -- Washing System (unified conveyors — single machine.washing.conveyors entry)
  ('b2000001-0000-0000-0000-000000000001'::uuid, :'tenant_id'::uuid, 'machine.elevatorToDestoner',     'washing.elevator_to_destoner',     'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000002'::uuid, :'tenant_id'::uuid, 'machine.destoner',               'washing.destoner',               'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000003'::uuid, :'tenant_id'::uuid, 'machine.soakingPoolPump',        'washing.soaking_pool_pump',        'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000004'::uuid, :'tenant_id'::uuid, 'machine.washingDrumPump',        'washing.washing_drum_pump',        'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000005'::uuid, :'tenant_id'::uuid, 'machine.organicDrum',            'washing.organic_drum',            'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000006'::uuid, :'tenant_id'::uuid, 'machine.roundPitPump',           'washing.round_pit_pump',           'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000007'::uuid, :'tenant_id'::uuid, 'machine.organicDrumAbovePool',   'washing.organic_drum_above_pool',   'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000008'::uuid, :'tenant_id'::uuid, 'machine.pushPumps2',             'washing.push_pumps_2',             'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000009'::uuid, :'tenant_id'::uuid, 'machine.washing.conveyors',      'washing.conveyors',                'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000010'::uuid, :'tenant_id'::uuid, 'machine.smallHopper',            'washing.small_hopper',             'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000011'::uuid, :'tenant_id'::uuid, 'machine.largeHopper',            'washing.large_hopper',             'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000012'::uuid, :'tenant_id'::uuid, 'machine.dryCleaningProcess',     'washing.dry_cleaning_process',     'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000013'::uuid, :'tenant_id'::uuid, 'machine.soakingTank',            'washing.soaking_tank',             'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000014'::uuid, :'tenant_id'::uuid, 'machine.internalWashDrum',       'washing.internal_wash_drum',       'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000015'::uuid, :'tenant_id'::uuid, 'machine.polisher1',              'washing.polisher_1',              'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000016'::uuid, :'tenant_id'::uuid, 'machine.polisher2',              'washing.polisher_2',              'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000017'::uuid, :'tenant_id'::uuid, 'machine.polisher3',              'washing.polisher_3',              'a2000001-0000-0000-0000-000000000001'::uuid),
  ('b2000001-0000-0000-0000-000000000018'::uuid, :'tenant_id'::uuid, 'machine.wearBroken2',            'washing.wear_broken_2',            'a2000001-0000-0000-0000-000000000001'::uuid),

  -- Onion System
  ('b2000001-0000-0000-0000-000000000101'::uuid, :'tenant_id'::uuid, 'machine.onion.hopper',           'onion.hopper',           'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000102'::uuid, :'tenant_id'::uuid, 'machine.onion.elevator',         'onion.elevator',         'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000103'::uuid, :'tenant_id'::uuid, 'machine.onion.washingDrum',      'onion.washing_drum',      'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000104'::uuid, :'tenant_id'::uuid, 'machine.onion.sorter',           'onion.sorter',           'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000105'::uuid, :'tenant_id'::uuid, 'machine.onion.conveyors',        'onion.conveyors',        'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000106'::uuid, :'tenant_id'::uuid, 'machine.onion.mixer',            'onion.mixer',            'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000107'::uuid, :'tenant_id'::uuid, 'machine.onion.tyingMachine',     'onion.tying_machine',     'a2000001-0000-0000-0000-000000000002'::uuid),
  ('b2000001-0000-0000-0000-000000000108'::uuid, :'tenant_id'::uuid, 'machine.onion.dryingRoom',       'onion.drying_room',       'a2000001-0000-0000-0000-000000000002'::uuid),

  -- Water Cooling System
  ('b2000001-0000-0000-0000-000000000201'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.compressor1',          'water_cooling.compressor_1',          'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000202'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.compressor2',          'water_cooling.compressor_2',          'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000203'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.compressor3',          'water_cooling.compressor_3',          'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000204'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.compressor4',          'water_cooling.compressor_4',          'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000205'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.condensers',           'water_cooling.condensers',           'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000206'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.coolingPlate',         'water_cooling.cooling_plate',         'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000207'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.fans',                 'water_cooling.fans',                 'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000208'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.waterPump1',           'water_cooling.water_pump_1',           'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000209'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.waterPump2',           'water_cooling.water_pump_2',           'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000210'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.waterPump3',           'water_cooling.water_pump_3',           'a2000001-0000-0000-0000-000000000003'::uuid),
  ('b2000001-0000-0000-0000-000000000211'::uuid, :'tenant_id'::uuid, 'machine.waterCooling.automaticWaterFilling', 'water_cooling.automatic_water_filling', 'a2000001-0000-0000-0000-000000000003'::uuid),

  -- Rooms Cooling System
  ('b2000001-0000-0000-0000-000000000301'::uuid, :'tenant_id'::uuid, 'machine.rooms.compressor1',  'rooms.compressor_1',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000302'::uuid, :'tenant_id'::uuid, 'machine.rooms.compressor2',  'rooms.compressor_2',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000303'::uuid, :'tenant_id'::uuid, 'machine.rooms.compressor3',  'rooms.compressor_3',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000304'::uuid, :'tenant_id'::uuid, 'machine.rooms.compressor4',  'rooms.compressor_4',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000305'::uuid, :'tenant_id'::uuid, 'machine.rooms.roomDoors',    'rooms.room_doors',    'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000306'::uuid, :'tenant_id'::uuid, 'machine.rooms.fastDoor',     'rooms.fast_door',     'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000307'::uuid, :'tenant_id'::uuid, 'machine.rooms.condensers',   'rooms.condensers',   'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000308'::uuid, :'tenant_id'::uuid, 'machine.coolingDoor',        'rooms.cooling_door',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000309'::uuid, :'tenant_id'::uuid, 'machine.lubrication',        'rooms.lubrication',  'a2000001-0000-0000-0000-000000000004'::uuid),
  ('b2000001-0000-0000-0000-000000000310'::uuid, :'tenant_id'::uuid, 'machine.chlorineSystem',     'rooms.chlorine_system', 'a2000001-0000-0000-0000-000000000004'::uuid),

  -- Ginoshar System
  ('b2000001-0000-0000-0000-000000000401'::uuid, :'tenant_id'::uuid, 'machine.ginoshar.netPackingMachine',    'ginoshar.net_packing_machine',    'a2000001-0000-0000-0000-000000000005'::uuid),
  ('b2000001-0000-0000-0000-000000000402'::uuid, :'tenant_id'::uuid, 'machine.ginoshar.sensorPackingMachine', 'ginoshar.sensor_packing_machine', 'a2000001-0000-0000-0000-000000000005'::uuid),
  ('b2000001-0000-0000-0000-000000000403'::uuid, :'tenant_id'::uuid, 'machine.ginoshar.airCompressor',        'ginoshar.air_compressor',        'a2000001-0000-0000-0000-000000000005'::uuid),

  -- Packing House System
  ('b2000001-0000-0000-0000-000000000501'::uuid, :'tenant_id'::uuid, 'machine.packing.poolElevators',    'packing.pool_elevators',    'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000502'::uuid, :'tenant_id'::uuid, 'machine.packing.conveyorSystem',   'packing.conveyor_system',   'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000503'::uuid, :'tenant_id'::uuid, 'machine.packing.fastVerbrocken',   'packing.fast_verbrocken',   'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000504'::uuid, :'tenant_id'::uuid, 'machine.packing.verbrocken',       'packing.verbrocken',       'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000505'::uuid, :'tenant_id'::uuid, 'machine.packing.bulkFiller',       'packing.bulk_filler',       'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000506'::uuid, :'tenant_id'::uuid, 'machine.packing.metalDetector',    'packing.metal_detector',    'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000507'::uuid, :'tenant_id'::uuid, 'machine.packing.stackers',         'packing.stackers',         'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000508'::uuid, :'tenant_id'::uuid, 'machine.packing.externalBuckets',  'packing.external_buckets',  'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000509'::uuid, :'tenant_id'::uuid, 'machine.packing.visionSystem',     'packing.vision_system',     'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000510'::uuid, :'tenant_id'::uuid, 'machine.sorter1',                  'packing.sorter_1',          'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000511'::uuid, :'tenant_id'::uuid, 'machine.sorter2',                  'packing.sorter_2',          'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000512'::uuid, :'tenant_id'::uuid, 'machine.sorter3',                  'packing.sorter_3',          'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000513'::uuid, :'tenant_id'::uuid, 'machine.scale1',                   'packing.scale_1',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000514'::uuid, :'tenant_id'::uuid, 'machine.scale2',                   'packing.scale_2',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000515'::uuid, :'tenant_id'::uuid, 'machine.scale3',                   'packing.scale_3',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000516'::uuid, :'tenant_id'::uuid, 'machine.scale4',                   'packing.scale_4',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000517'::uuid, :'tenant_id'::uuid, 'machine.scale5',                   'packing.scale_5',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000518'::uuid, :'tenant_id'::uuid, 'machine.scale6',                   'packing.scale_6',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000519'::uuid, :'tenant_id'::uuid, 'machine.packing1',                 'packing.packing_1',         'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000520'::uuid, :'tenant_id'::uuid, 'machine.packing2',                 'packing.packing_2',         'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000521'::uuid, :'tenant_id'::uuid, 'machine.packing3',                 'packing.packing_3',         'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000522'::uuid, :'tenant_id'::uuid, 'machine.packing4',                 'packing.packing_4',         'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000523'::uuid, :'tenant_id'::uuid, 'machine.packing5a',                'packing.packing_5a',        'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000524'::uuid, :'tenant_id'::uuid, 'machine.packing5b',                'packing.packing_5b',        'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000525'::uuid, :'tenant_id'::uuid, 'machine.packing6a',                'packing.packing_6a',        'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000526'::uuid, :'tenant_id'::uuid, 'machine.packing6b',                'packing.packing_6b',        'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000527'::uuid, :'tenant_id'::uuid, 'machine.sorterLine5',              'packing.sorter_line_5',     'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000528'::uuid, :'tenant_id'::uuid, 'machine.sorterLine6',              'packing.sorter_line_6',     'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000529'::uuid, :'tenant_id'::uuid, 'machine.masters',                  'packing.masters',           'a2000001-0000-0000-0000-000000000006'::uuid),
  ('b2000001-0000-0000-0000-000000000530'::uuid, :'tenant_id'::uuid, 'machine.viscose',                  'packing.viscose',           'a2000001-0000-0000-0000-000000000006'::uuid)
) AS v(id, tenant_id, name, code, array_id)
WHERE NOT EXISTS (
  SELECT 1 FROM machines m
  WHERE m.tenant_id = v.tenant_id AND m.name = v.name
)
ON CONFLICT (id) DO NOTHING;

-- =============================================================================
-- SECTION 3: UPDATE array_id ON EXISTING MACHINES (by translation key / name)
-- =============================================================================
-- Idempotent: sets array_id for known machine.name values under this tenant.
-- Does NOT change is_active or other columns.

-- Washing System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000001'
WHERE tenant_id = :'tenant_id'
  AND name IN (
    'machine.elevatorToDestoner', 'machine.destoner', 'machine.soakingPoolPump',
    'machine.washingDrumPump', 'machine.organicDrum', 'machine.roundPitPump',
    'machine.organicDrumAbovePool', 'machine.pushPumps2', 'machine.washing.conveyors',
    'machine.smallHopper', 'machine.largeHopper', 'machine.dryCleaningProcess',
    'machine.soakingTank', 'machine.internalWashDrum',
    'machine.polisher1', 'machine.polisher2', 'machine.polisher3', 'machine.wearBroken2'
  );

-- Onion System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000002'
WHERE tenant_id = :'tenant_id'
  AND name LIKE 'machine.onion.%';

-- Water Cooling System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000003'
WHERE tenant_id = :'tenant_id'
  AND name LIKE 'machine.waterCooling.%';

-- Rooms Cooling System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000004'
WHERE tenant_id = :'tenant_id'
  AND (
    name LIKE 'machine.rooms.%'
    OR name IN ('machine.coolingDoor', 'machine.lubrication', 'machine.chlorineSystem')
  );

-- Ginoshar System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000005'
WHERE tenant_id = :'tenant_id'
  AND name LIKE 'machine.ginoshar.%';

-- Packing House System
UPDATE machines
SET array_id = 'a2000001-0000-0000-0000-000000000006'
WHERE tenant_id = :'tenant_id'
  AND (
    name LIKE 'machine.packing.%'
    OR name IN (
      'machine.sorter1', 'machine.sorter2', 'machine.sorter3',
      'machine.scale1', 'machine.scale2', 'machine.scale3', 'machine.scale4', 'machine.scale5', 'machine.scale6',
      'machine.packing1', 'machine.packing2', 'machine.packing3', 'machine.packing4',
      'machine.packing5a', 'machine.packing5b', 'machine.packing6a', 'machine.packing6b',
      'machine.sorterLine5', 'machine.sorterLine6', 'machine.masters', 'machine.viscose'
    )
  );

COMMIT;

-- =============================================================================
-- SECTION 4: VERIFICATION (read-only)
-- =============================================================================

SELECT id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled
FROM arrays
WHERE tenant_id = :'tenant_id'
ORDER BY sort_order;

SELECT a.name_key AS array_name, COUNT(m.id) AS active_machines
FROM arrays a
LEFT JOIN machines m ON m.array_id = a.id AND m.is_active = true
WHERE a.tenant_id = :'tenant_id'
GROUP BY a.sort_order, a.name_key
ORDER BY a.sort_order;

SELECT m.name, m.code, a.name_key AS array_name
FROM machines m
LEFT JOIN arrays a ON a.id = m.array_id
WHERE m.tenant_id = :'tenant_id'
  AND m.array_id IS NOT NULL
  AND m.is_active = true
ORDER BY a.sort_order, m.name;

SELECT COUNT(*) AS unassigned_active_machines
FROM machines
WHERE tenant_id = :'tenant_id'
  AND is_active = true
  AND array_id IS NULL;

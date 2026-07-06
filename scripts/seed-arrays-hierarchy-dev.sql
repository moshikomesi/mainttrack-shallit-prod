-- Dev/test only: seed Arrays → Machines hierarchy for Morning Round v2.
-- Safe to re-run (idempotent). Does NOT modify schema.

BEGIN;

INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
VALUES
  ('a1000001-1111-1111-1111-111111111101', '11111111-1111-1111-1111-111111111111', 'array.washing_system',       1, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111102', '11111111-1111-1111-1111-111111111111', 'array.onion_system',         2, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111103', '11111111-1111-1111-1111-111111111111', 'array.water_cooling_system', 3, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111104', '11111111-1111-1111-1111-111111111111', 'array.rooms',                4, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111105', '11111111-1111-1111-1111-111111111111', 'array.ginoshar',             5, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111106', '11111111-1111-1111-1111-111111111111', 'array.packing_house',        6, true, true, NOW()),
  ('a1000001-1111-1111-1111-111111111107', '11111111-1111-1111-1111-111111111111', 'array.thai_dorms',           7, true, true, NOW())
ON CONFLICT (id) DO UPDATE SET
  name_key = EXCLUDED.name_key,
  sort_order = EXCLUDED.sort_order,
  is_active = EXCLUDED.is_active,
  is_morning_round_enabled = EXCLUDED.is_morning_round_enabled;

INSERT INTO machines (id, tenant_id, name, code, description, is_active, array_id, created_at)
SELECT v.id, v.tenant_id, v.name, v.code, NULL, true, v.array_id, NOW()
FROM (VALUES
  ('b1000001-1111-1111-1111-111111111001'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.elevatorToDestoner', 'washing.elevator_to_destoner', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111002'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.destoner', 'washing.destoner', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111003'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.soakingPoolPump', 'washing.soaking_pool_pump', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111004'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.washingDrumPump', 'washing.washing_drum_pump', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111005'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.organicDrum', 'washing.organic_drum', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111006'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.roundPitPump', 'washing.round_pit_pump', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111007'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.organicDrumAbovePool', 'washing.organic_drum_above_pool', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111008'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.pushPumps2', 'washing.push_pumps_2', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111009'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.washing.conveyors', 'washing.conveyors', 'a1000001-1111-1111-1111-111111111101'::uuid),
  ('b1000001-1111-1111-1111-111111111101'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.hopper', 'onion.hopper', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111102'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.elevator', 'onion.elevator', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111103'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.washingDrum', 'onion.washing_drum', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111104'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.mixer', 'onion.mixer', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111105'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.sorter', 'onion.sorter', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111106'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.conveyors', 'onion.conveyors', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111107'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.tyingMachine', 'onion.tying_machine', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111108'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.onion.dryingRoom', 'onion.drying_room', 'a1000001-1111-1111-1111-111111111102'::uuid),
  ('b1000001-1111-1111-1111-111111111201'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.compressor1', 'water_cooling.compressor_1', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111202'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.compressor2', 'water_cooling.compressor_2', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111203'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.compressor3', 'water_cooling.compressor_3', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111204'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.compressor4', 'water_cooling.compressor_4', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111205'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.condensers', 'water_cooling.condensers', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111206'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.coolingPlate', 'water_cooling.cooling_plate', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111207'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.fans', 'water_cooling.fans', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111208'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.waterPump1', 'water_cooling.water_pump_1', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111209'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.waterPump2', 'water_cooling.water_pump_2', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111210'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.waterPump3', 'water_cooling.water_pump_3', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111211'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.waterCooling.automaticWaterFilling', 'water_cooling.automatic_water_filling', 'a1000001-1111-1111-1111-111111111103'::uuid),
  ('b1000001-1111-1111-1111-111111111301'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.compressor1', 'rooms.compressor_1', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111302'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.compressor2', 'rooms.compressor_2', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111303'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.compressor3', 'rooms.compressor_3', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111304'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.compressor4', 'rooms.compressor_4', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111305'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.roomDoors', 'rooms.room_doors', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111306'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.fastDoor', 'rooms.fast_door', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111307'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.rooms.condensers', 'rooms.condensers', 'a1000001-1111-1111-1111-111111111104'::uuid),
  ('b1000001-1111-1111-1111-111111111401'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.ginoshar.netPackingMachine', 'ginoshar.net_packing_machine', 'a1000001-1111-1111-1111-111111111105'::uuid),
  ('b1000001-1111-1111-1111-111111111402'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.ginoshar.sensorPackingMachine', 'ginoshar.sensor_packing_machine', 'a1000001-1111-1111-1111-111111111105'::uuid),
  ('b1000001-1111-1111-1111-111111111403'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.ginoshar.airCompressor', 'ginoshar.air_compressor', 'a1000001-1111-1111-1111-111111111105'::uuid),
  ('b1000001-1111-1111-1111-111111111501'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.poolElevators', 'packing.pool_elevators', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111502'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.conveyorSystem', 'packing.conveyor_system', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111503'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.fastVerbrocken', 'packing.fast_verbrocken', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111504'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.verbrocken', 'packing.verbrocken', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111505'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.bulkFiller', 'packing.bulk_filler', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111506'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.metalDetector', 'packing.metal_detector', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111507'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.stackers', 'packing.stackers', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111508'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.externalBuckets', 'packing.external_buckets', 'a1000001-1111-1111-1111-111111111106'::uuid),
  ('b1000001-1111-1111-1111-111111111509'::uuid, '11111111-1111-1111-1111-111111111111'::uuid, 'machine.packing.visionSystem', 'packing.vision_system', 'a1000001-1111-1111-1111-111111111106'::uuid)
) AS v(id, tenant_id, name, code, array_id)
WHERE NOT EXISTS (
  SELECT 1 FROM machines m WHERE m.tenant_id = v.tenant_id AND m.name = v.name
);

UPDATE machines SET array_id = 'a1000001-1111-1111-1111-111111111101'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name IN (
    'machine.smallHopper', 'machine.largeHopper', 'machine.dryCleaningProcess',
    'machine.soakingTank', 'machine.internalWashDrum',
    'machine.polisher1', 'machine.polisher2', 'machine.polisher3',
    'machine.wearBroken2', 'machine.washing.conveyors',
    'machine.elevatorToDestoner', 'machine.destoner', 'machine.soakingPoolPump', 'machine.washingDrumPump',
    'machine.organicDrum', 'machine.roundPitPump', 'machine.organicDrumAbovePool', 'machine.pushPumps2'
  );

-- Washing System: one unified conveyors machine (retire belt/motor/shaft sub-components).
UPDATE machines
SET is_active = false, array_id = NULL
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name IN ('machine.conveyorMotor', 'machine.conveyorBelt', 'machine.conveyorShaft');

UPDATE machines
SET is_active = true, array_id = 'a1000001-1111-1111-1111-111111111101'
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND name = 'machine.washing.conveyors';

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

SELECT a.name_key, COUNT(m.id) AS machine_count
FROM arrays a
LEFT JOIN machines m ON m.array_id = a.id AND m.is_active = true
WHERE a.tenant_id = '11111111-1111-1111-1111-111111111111'
GROUP BY a.sort_order, a.name_key
ORDER BY a.sort_order;

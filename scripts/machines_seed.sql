BEGIN;

-- Insert machines missing from production (same catalog as local dev)

INSERT INTO machines (id, tenant_id, name, code, description, is_active, array_id, created_at)
SELECT v.id, v.tenant_id, v.name, v.code, NULL, true, NULL, NOW()
FROM (VALUES
  ('b2000001-0000-0000-0000-000000000002'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.destoner', 'washing.destoner'),
  ('b2000001-0000-0000-0000-000000000001'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.elevatorToDestoner', 'washing.elevator_to_destoner'),
  ('b2000001-0000-0000-0000-000000000403'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.ginoshar.airCompressor', 'ginoshar.air_compressor'),
  ('b2000001-0000-0000-0000-000000000401'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.ginoshar.netPackingMachine', 'ginoshar.net_packing_machine'),
  ('b2000001-0000-0000-0000-000000000402'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.ginoshar.sensorPackingMachine', 'ginoshar.sensor_packing_machine'),
  ('b2000001-0000-0000-0000-000000000105'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.conveyors', 'onion.conveyors'),
  ('b2000001-0000-0000-0000-000000000108'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.dryingRoom', 'onion.drying_room'),
  ('b2000001-0000-0000-0000-000000000102'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.elevator', 'onion.elevator'),
  ('b2000001-0000-0000-0000-000000000101'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.hopper', 'onion.hopper'),
  ('b2000001-0000-0000-0000-000000000106'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.mixer', 'onion.mixer'),
  ('b2000001-0000-0000-0000-000000000104'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.sorter', 'onion.sorter'),
  ('b2000001-0000-0000-0000-000000000107'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.tyingMachine', 'onion.tying_machine'),
  ('b2000001-0000-0000-0000-000000000103'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.onion.washingDrum', 'onion.washing_drum'),
  ('b2000001-0000-0000-0000-000000000005'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.organicDrum', 'washing.organic_drum'),
  ('b2000001-0000-0000-0000-000000000007'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.organicDrumAbovePool', 'washing.organic_drum_above_pool'),
  ('b2000001-0000-0000-0000-000000000505'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.bulkFiller', 'packing.bulk_filler'),
  ('b2000001-0000-0000-0000-000000000502'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.conveyorSystem', 'packing.conveyor_system'),
  ('b2000001-0000-0000-0000-000000000508'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.externalBuckets', 'packing.external_buckets'),
  ('b2000001-0000-0000-0000-000000000503'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.fastVerbrocken', 'packing.fast_verbrocken'),
  ('b2000001-0000-0000-0000-000000000506'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.metalDetector', 'packing.metal_detector'),
  ('b2000001-0000-0000-0000-000000000501'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.poolElevators', 'packing.pool_elevators'),
  ('b2000001-0000-0000-0000-000000000507'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.stackers', 'packing.stackers'),
  ('b2000001-0000-0000-0000-000000000504'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.verbrocken', 'packing.verbrocken'),
  ('b2000001-0000-0000-0000-000000000509'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.packing.visionSystem', 'packing.vision_system'),
  ('b2000001-0000-0000-0000-000000000008'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.pushPumps2', 'washing.push_pumps_2'),
  ('b2000001-0000-0000-0000-000000000301'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.compressor1', 'rooms.compressor_1'),
  ('b2000001-0000-0000-0000-000000000302'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.compressor2', 'rooms.compressor_2'),
  ('b2000001-0000-0000-0000-000000000303'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.compressor3', 'rooms.compressor_3'),
  ('b2000001-0000-0000-0000-000000000304'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.compressor4', 'rooms.compressor_4'),
  ('b2000001-0000-0000-0000-000000000307'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.condensers', 'rooms.condensers'),
  ('b2000001-0000-0000-0000-000000000306'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.fastDoor', 'rooms.fast_door'),
  ('b2000001-0000-0000-0000-000000000305'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.rooms.roomDoors', 'rooms.room_doors'),
  ('b2000001-0000-0000-0000-000000000006'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.roundPitPump', 'washing.round_pit_pump'),
  ('b2000001-0000-0000-0000-000000000003'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.soakingPoolPump', 'washing.soaking_pool_pump'),
  ('b2000001-0000-0000-0000-000000000009'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.washing.conveyors', 'washing.conveyors'),
  ('b2000001-0000-0000-0000-000000000004'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.washingDrumPump', 'washing.washing_drum_pump'),
  ('b2000001-0000-0000-0000-000000000211'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.automaticWaterFilling', 'water_cooling.automatic_water_filling'),
  ('b2000001-0000-0000-0000-000000000201'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.compressor1', 'water_cooling.compressor_1'),
  ('b2000001-0000-0000-0000-000000000202'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.compressor2', 'water_cooling.compressor_2'),
  ('b2000001-0000-0000-0000-000000000203'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.compressor3', 'water_cooling.compressor_3'),
  ('b2000001-0000-0000-0000-000000000204'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.compressor4', 'water_cooling.compressor_4'),
  ('b2000001-0000-0000-0000-000000000205'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.condensers', 'water_cooling.condensers'),
  ('b2000001-0000-0000-0000-000000000206'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.coolingPlate', 'water_cooling.cooling_plate'),
  ('b2000001-0000-0000-0000-000000000207'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.fans', 'water_cooling.fans'),
  ('b2000001-0000-0000-0000-000000000208'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.waterPump1', 'water_cooling.water_pump_1'),
  ('b2000001-0000-0000-0000-000000000209'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.waterPump2', 'water_cooling.water_pump_2'),
  ('b2000001-0000-0000-0000-000000000210'::uuid, 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid, 'machine.waterCooling.waterPump3', 'water_cooling.water_pump_3')
) AS v(id, tenant_id, name, code)
WHERE NOT EXISTS (
  SELECT 1 FROM machines m WHERE m.tenant_id = v.tenant_id AND m.name = v.name
)
ON CONFLICT (id) DO NOTHING;

-- Deactivate legacy machines not in local catalog
UPDATE machines SET is_active = false, array_id = NULL WHERE id = 'c43eafdf-992f-4368-96b0-20487c4d2918' AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';
UPDATE machines SET is_active = false, array_id = NULL WHERE id = '3d2eedd0-6317-45f3-8298-9af78a1b7dd2' AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';
UPDATE machines SET is_active = false, array_id = NULL WHERE id = '8abc786e-bfe0-4183-b95d-f9b6fc36f156' AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

COMMIT;

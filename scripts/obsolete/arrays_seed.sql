INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
VALUES
  ('a2000001-0000-0000-0000-000000000001', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.washing_system',       1, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000002', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.onion_system',         2, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000003', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.water_cooling_system', 3, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000004', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.rooms',                4, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000005', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.ginoshar',             5, true, true, NOW()),
  ('a2000001-0000-0000-0000-000000000006', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.packing_house',        6, true, true, NOW())
ON CONFLICT (id) DO NOTHING;

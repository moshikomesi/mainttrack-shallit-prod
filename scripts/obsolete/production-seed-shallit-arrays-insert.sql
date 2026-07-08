-- =============================================================================
-- MaintTrack — Production seed: create arrays for Shallit tenant (run BEFORE mapping)
-- =============================================================================
-- Tenant: beed1fc4-ffbb-4ea1-b7c8-d84584506842
-- Idempotent: ON CONFLICT (id) DO NOTHING
-- =============================================================================

INSERT INTO arrays (id, tenant_id, name_key, sort_order, is_active, is_morning_round_enabled, created_at)
VALUES
  ('c1beed1f-0001-4ea1-b7c8-d84584506801', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.production.washing',            1, true, true, NOW()),
  ('c1beed1f-0006-4ea1-b7c8-d84584506806', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.production.sorting_processing', 2, true, true, NOW()),
  ('c1beed1f-0007-4ea1-b7c8-d84584506807', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.production.cooling_fluid',      3, true, true, NOW()),
  ('c1beed1f-0008-4ea1-b7c8-d84584506808', 'beed1fc4-ffbb-4ea1-b7c8-d84584506842', 'array.production.wear_maintenance',   4, true, true, NOW())
ON CONFLICT (id) DO NOTHING;

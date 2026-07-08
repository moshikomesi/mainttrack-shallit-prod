-- =============================================================================
-- ⚠️  OBSOLETE — DO NOT USE FOR PRODUCTION ⚠️
-- =============================================================================
-- Superseded on 2026-07-08. This script creates a 4-array coarse hierarchy
-- (`array.production.washing`, `sorting_processing`, `cooling_fluid`,
-- `wear_maintenance`) that is INCOMPATIBLE with every hierarchy change made
-- this sprint (machine sort_order, machine_component_mappings, the full
-- Arrays→Machines→Components alignment in
-- scripts/production-hierarchy-map-alignment.sql — all of that work assumes
-- the 6-array model: array.washing_system / onion_system /
-- water_cooling_system / rooms / ginoshar / packing_house [+ conveyors]).
--
-- Use instead: scripts/production-seed-arrays-machines.sql
--
-- Kept here for historical reference only. See scripts/README.md
-- ("Arrays production seed — which script to use") for the full explanation.
-- =============================================================================

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

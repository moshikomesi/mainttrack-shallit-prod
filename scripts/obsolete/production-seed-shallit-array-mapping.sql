-- =============================================================================
-- MaintTrack — Production seed: map REAL machines → arrays (Shallit tenant)
-- =============================================================================
-- Tenant: beed1fc4-ffbb-4ea1-b7c8-d84584506842
--
-- Source: production machine UUIDs (machines.id) — UUID-based mapping ONLY.
-- Updates ONLY machines.array_id. No machine inserts.
--
-- Arrays (must exist before run — create separately if missing):
--   Washing           c1beed1f-0001-4ea1-b7c8-d84584506801
--   Sorting/Processing c1beed1f-0006-4ea1-b7c8-d84584506806
--   Cooling/Fluid     c1beed1f-0007-4ea1-b7c8-d84584506807
--   Wear/Maintenance  c1beed1f-0008-4ea1-b7c8-d84584506808
--
-- Idempotent: safe re-run; partial-safe (0-row UPDATE if id missing).
-- =============================================================================

BEGIN;

-- Washing Array → c1beed1f-0001-4ea1-b7c8-d84584506801
-- machine.soakingTank
UPDATE machines
SET array_id = 'c1beed1f-0001-4ea1-b7c8-d84584506801'
WHERE id = '0546f4ac-e5aa-4be0-bd39-74d72ab7ecb3'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- Sorting / Processing Array → c1beed1f-0006-4ea1-b7c8-d84584506806
-- machine.sorter1
UPDATE machines
SET array_id = 'c1beed1f-0006-4ea1-b7c8-d84584506806'
WHERE id = '15d493da-6c3b-46f1-b020-ad55fc152f82'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.sorter2
UPDATE machines
SET array_id = 'c1beed1f-0006-4ea1-b7c8-d84584506806'
WHERE id = '8227f147-134b-4f03-be38-646a3475afa3'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.sorter3
UPDATE machines
SET array_id = 'c1beed1f-0006-4ea1-b7c8-d84584506806'
WHERE id = 'ec1e53d9-85c2-468d-ae68-9189f1472602'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.sorterLine5
UPDATE machines
SET array_id = 'c1beed1f-0006-4ea1-b7c8-d84584506806'
WHERE id = '3d835a79-8b31-4487-bb09-a8aae2b6739c'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.sorterLine6
UPDATE machines
SET array_id = 'c1beed1f-0006-4ea1-b7c8-d84584506806'
WHERE id = 'cfef13d9-70da-4ff8-9aec-b50fb0bb7c23'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- Cooling / Fluid Array → c1beed1f-0007-4ea1-b7c8-d84584506807
-- machine.waterPump
UPDATE machines
SET array_id = 'c1beed1f-0007-4ea1-b7c8-d84584506807'
WHERE id = '39d7f011-1b50-4fc2-bf10-dfb8ac57e8b0'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.viscose
UPDATE machines
SET array_id = 'c1beed1f-0007-4ea1-b7c8-d84584506807'
WHERE id = 'c4367223-cd9b-4898-83f8-e2f64c1391c9'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- Wear / Maintenance Array → c1beed1f-0008-4ea1-b7c8-d84584506808
-- machine.wearBroken2
UPDATE machines
SET array_id = 'c1beed1f-0008-4ea1-b7c8-d84584506808'
WHERE id = '7e275298-d1e6-4368-bf11-3faa67e3ff5d'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

-- machine.wearBroken3
UPDATE machines
SET array_id = 'c1beed1f-0008-4ea1-b7c8-d84584506808'
WHERE id = '79976665-4625-4c55-bffc-788be4f18954'
  AND tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';

COMMIT;

-- Verification: machines per array (tenant-scoped)
SELECT array_id, COUNT(*) AS machine_count
FROM machines
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
GROUP BY array_id
ORDER BY array_id NULLS FIRST;

-- Unassigned machines (tenant-scoped)
SELECT id, name, code, array_id
FROM machines
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
  AND array_id IS NULL
ORDER BY name;

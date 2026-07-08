# Production SQL scripts

This folder contains SQL scripts that apply database changes **without running
`dotnet ef database update`**, since production does not run EF migrations
(`db.Database.Migrate()` is intentionally commented out in `Program.cs`).

Every script here is idempotent — safe to run multiple times — and
non-destructive (no `DROP TABLE`, no data deletion of existing rows) unless
explicitly noted otherwise. For the full step-by-step deployment runbook, see
[`docs/production-deployment-checklist.md`](../docs/production-deployment-checklist.md).

## Canonical production scripts, in execution order

| # | Script | Covers EF migration(s) | Type |
|---|---|---|---|
| 1 | `production-manual-schema-morning-round-v2.sql` | `AddArraysAndMachineArrayId`, `AddMorningRoundV2` | Schema |
| 2 | `production-machine-components-schema.sql` | `AddMachineComponentsInfrastructure` (schema half) | Schema |
| 3 | `production-treatments-shared-dropdowns.sql` | `UpdateTreatmentsSharedDropdowns` | Schema |
| 4 | `production-maintenance-types.sql` | `AddAircompressorAndCoolingMaintenanceTypes` | Data |
| 5 | `production-user-feature-flags.sql` | `AddUserFeatureFlags` | Schema |
| 6 | `production-seed-arrays-machines.sql` | `AddArraysAndMachineArrayId` (data) | Data |
| 7 | `machine_components_seed.sql` | `AddMachineComponentsInfrastructure` (data half) | Data |
| 8 | `production-hierarchy-map-alignment.sql` | `UpdateOnionMixerComponents`, `HierarchyMapAlignment`, `AddMachineSortOrder`, `DeactivatePackingHouseExtras` | Schema + Data |
| 9 | `production-forklift-inspection-columns.sql` | **None — see below** | Schema |
| 10 | `production-machine-components-complete.sql` | **None — data completion, see below** | Data |

Run in this exact order — later scripts depend on tables/columns created by
earlier ones (each script also states its own prerequisites in its header).

## `production-machine-components-complete.sql` — why there is no EF migration

Like the forklift script above, this one has no corresponding EF migration —
it is a hand-authored data completion pass for the Machine → Component
hierarchy (the canonical model used by Morning Round V2, Maintenance Log V2,
Maintenance Reports and Treatments). It adds the component catalog rows and
machine mappings for 9 previously-unmapped machine groups (Hopper, Dry
Cleaning, Scale, Line Sorter, Packing, Polisher, Accumulators, Sorters, Viscon
line) using the exact Hebrew component names supplied by the maintenance
team, with no normalization/merging of similar-looking names. New component
rows store the raw Hebrew text directly in `name_key` (instead of a
`maintenanceComponent.xxx` translation key) — the frontend's `t(key)` lookup
falls back to returning the key unchanged when no translation exists, so the
UI renders correctly with zero frontend/translation code changes. After
running it, 26 of the 76 active production machines still have no mapped
components (full list of machines and array names in the script's header
comment) — those require a follow-up script once their exact component names
are supplied by the maintenance team; see the hierarchy audit report for the
full breakdown.

## `production-forklift-inspection-columns.sql` — why there is no EF migration

This script is different from the other 8: it does not correspond to any
sprint migration at all. It was discovered during a repository-only audit
that the current EF model requires three columns on `forklifts`
(`last_inspection_date`, `inspection_expiry_date`, `inspection_updated_at`)
that are read/written unconditionally by `ForkliftService`,
`ForkliftReportService`, and `ForkliftReportsQueryService.GetExpiringInspectionsAsync`
— but **no migration in the repository's history ever actually creates them**:

- `20260304200000_AddForklifts` creates `forklifts` without these columns.
- `20260308185800_AddForkliftInspectionFields` — despite its name — only
  changes `treatments.treatment_type` / `treatments.equipment_type` column
  types; it never touches `forklifts`.
- The three properties exist only in the EF model snapshot / `.Designer.cs`
  files (which always reflect the *current* model shape), never in an
  executable migration `Up()` body.

This is a pre-existing gap that predates this sprint (both migrations above
are from the `Initial production snapshot` commit), not new sprint work.
Because no migration can be relied upon to produce these columns — now or in
any future `dotnet ef database update` — `production-forklift-inspection-columns.sql`
is a **permanent, hand-written replacement**, not a temporary stand-in.

## Arrays production seed — which script to use

Two competing approaches to seeding the Arrays → Machines hierarchy existed in
this repo. **This ambiguity is now resolved:**

**✅ Use: `production-seed-arrays-machines.sql`** (+ its runner
`run-production-seed-arrays-machines.sh`)

This implements the **6-array hierarchy** — `array.washing_system`,
`array.onion_system`, `array.water_cooling_system`, `array.rooms`,
`array.ginoshar`, `array.packing_house` (plus `array.conveyors`, added later
by `production-hierarchy-map-alignment.sql`). This is the **only** model
consistent with every other piece of hierarchy work in this sprint: machine
`sort_order` values, `machine_component_mappings`, and the
`translations.ts` labels used throughout the UI. It is parameterized by
`tenant_id` (pass `-v tenant_id='beed1fc4-ffbb-4ea1-b7c8-d84584506842'` for
the Shallit production tenant), matches existing machines by translation-key
`name` (not hardcoded UUIDs), and is safe to run against any environment.

**❌ Do not use — `scripts/obsolete/`:**

| File | Why it's obsolete |
|---|---|
| `arrays_seed.sql`, `machines_seed.sql`, `machines_mapping.sql` | Same 6-array data as the canonical script above, but split across 3 files with the tenant ID and machine UUIDs hardcoded from a single point-in-time snapshot. Fully superseded — kept for historical reference only. |
| `production-seed-shallit-arrays-insert.sql`, `production-seed-shallit-array-mapping.sql`, `run-production-seed-shallit-array-mapping.sh` | A different, coarser **4-array model** (`array.production.washing/sorting_processing/cooling_fluid/wear_maintenance`) that predates and is incompatible with this sprint's hierarchy work. Only maps 11 of ~70 machines, using names (`machine.waterPump`, `machine.wearBroken3`) that don't exist in the current catalog. **Must not be run against production** — if it already was, see the cleanup note below. |

If you are not certain whether the 4-array scripts were ever run against the
real production database, check first (read-only):

```sql
SELECT name_key FROM arrays WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842';
```

If you see `array.production.*` rows, the obsolete 4-array data was applied.
Clean it up before running the canonical script:

```sql
BEGIN;
-- Unassign any machines pointing at the obsolete 4-array rows
UPDATE machines SET array_id = NULL
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
  AND array_id IN (SELECT id FROM arrays WHERE name_key LIKE 'array.production.%');
-- Remove the obsolete 4-array rows themselves
DELETE FROM arrays
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
  AND name_key LIKE 'array.production.%';
COMMIT;
```

Then run `production-seed-arrays-machines.sql` as documented above.

## Feature-flag pilot rollout

`production-user-feature-flags.sql` only adds the two columns (both default
`false` for every user — no behavior change). Enabling a real pilot user is a
**separate, deliberate, manual step** — see the commented-out `OPTIONAL`
section at the bottom of that script. Never run
`seed-user-feature-flags-dev.sql` against production; it is hardcoded to the
local dev "Pilot Factory" tenant and is a no-op against real data.

## Dev-only scripts (do not use in production)

- `seed-arrays-hierarchy-dev.sql` / `.sh`
- `seed-user-feature-flags-dev.sql` / `.sh`
- `create-test-database.sql` / `.sh`

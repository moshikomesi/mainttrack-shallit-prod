# Hierarchy Map Alignment — Change Report

This report summarizes everything changed while aligning the system's
Arrays → Machines → Components hierarchy with the factory's reference table
map (onion mixer rename, bug fixes, missing components/mappings, array
reshuffling, and machine ordering), and explains how to apply the
database-side changes to a server **without running `dotnet ef database
update`**.

## 1. What changed

### 1.1 Frontend-only changes (translations, no DB impact)

All in `frontend/src/i18n/translations.ts`. These ship with a normal
frontend build/deploy — **no database action required**.

| Key | Before | After | Reason |
|---|---|---|---|
| `maintenanceComponent.impeller` | משבמת | מאיץ | Typo fix |
| `machine.polisher3` | פולישร 3 (stray Thai char) | פולישר 3 | Typo fix |
| `machine.largeHopper` | הופר גדול | הופר ראשון | Match map naming |
| `machine.smallHopper` | הופר קטן | הופר שני | Match map naming |
| `machine.wearBroken2` | ורברוכן 2 (מפריד שבורים) | מפריד שבורים | Use map's name only |
| `machine.onion.dryingRoom` | חדר ייבוש (מכולת ייבוש) | מכולת ייבוש | Use map's name only |
| `machine.packing.bulkFiller` | ממלאת תפזורת (ממלא צוברים) | ממלא צוברים | Use map's name only |
| `machine.packing.conveyorSystem` | מערכת מסועים (מסועים זחיח) | מסועים זחיח | Use map's name only |
| `machine.packing.poolElevators` | מעליות בריכה | מעליות של בריכות | Cosmetic |
| `machine.ginoshar.sensorPackingMachine` | מכונת אריזת חיישן | מכונת אריזת חיישן (גששים) | Kept both (system + map) |
| `machine.destoner` / `machine.elevatorToDestoner` | — | — | **Unchanged** — map spelling was a typo |
| new: `maintenanceComponent.overhaul` | — | שיפוץ | New generic component label |
| new: `array.conveyors` | — | מסועים | New array label |
| new: `machine.conveyors.general` | — | מסועים | New machine label |
| new: `maintenanceComponent.rubberStars` | — | גומיות/כוכבים | Onion mixer component |
| new: `maintenanceComponent.bearings` | — | מיסבים | Onion mixer component |

### 1.2 Database changes

Grouped by the 4 EF Core migrations that introduced them (all under
`backend/MaintTrack.Infrastructure/Migrations/`):

**`20260707220000_UpdateOnionMixerComponents`** — `machine.onion.mixer` (מקנבת)
- Added catalog components `RUBBER_STARS` (גומיות/כוכבים) and `BEARINGS` (מיסבים).
- Deactivated the shared `RUBBER` mapping *only* for this machine (other machines using `RUBBER`, e.g. `machine.dryCleaningProcess`, are untouched).
- Mapped `RUBBER_STARS` and `BEARINGS` onto this machine.

**`20260707230000_HierarchyMapAlignment`** — bulk hierarchy fixes
- Added a new generic component `OVERHAUL` (שיפוץ).
- Added component mappings for `machine.elevatorToDestoner` (ציר/מסוע/סרט וולטה/מנוע) — previously had none.
- Added component mappings (שיפוץ + שימון) for all 4 `machine.waterCooling.compressorX` — previously had none.
- Created new top-level array `array.conveyors` (מסועים), with `is_morning_round_enabled = false` (maintenance hierarchy only, not part of the Morning Round V2 checklist).
- Created `machine.conveyors.general` under that array with 5 components (מנוע/ציר מניע/ציר מונע/סרט/מיסבים).
- Assigned the previously-unassigned `machine.accumulatorOutside` (צוברים חוץ) to `array.packing_house`.
- Deactivated 4 machines not present in the reference map: `machine.lubrication`, `machine.chlorineSystem`, `machine.coolingDoor` (from Rooms), `machine.packing.visionSystem` (from Packing House).

**`20260707233000_AddMachineSortOrder`** — explicit ordering
- Added `sort_order` column (`integer NOT NULL DEFAULT 0`) to `machines`.
- Populated `sort_order` for every machine across all 6 arrays to match the exact order in the reference map.
- Updated `HierarchyService`, `MorningRoundV2Service`, and `MachineService` to `ORDER BY sort_order, name` instead of alphabetically by name.

**`20260707234500_DeactivatePackingHouseExtras`** — final cleanup
- Deactivated `machine.packing.stackers` (מערמים) and `machine.packing.externalBuckets` (דליים חיצוניים) — not present in the reference map for Packing House.

> **Note on "deactivate", not "delete":** every removed machine is soft-deactivated
> (`is_active = false`, `array_id = NULL`), never physically deleted. Historical
> Morning Round / maintenance reports that reference these machines keep working.

## 2. How to apply the DB changes without EF migrations

Your production deploy (`backend/deploy-backend.sh`) only publishes the API
binaries and restarts the `mainttrack` systemd service — it never runs
`dotnet ef database update` against the server. Consistent with the existing
`scripts/production-*.sql` files in this repo, all of the database changes
above have been consolidated into a single, idempotent, transaction-wrapped
SQL script you can run directly against Postgres:

- **`scripts/production-hierarchy-map-alignment.sql`** — the SQL itself (safe to run more than once).
- **`scripts/run-production-hierarchy-map-alignment.sh`** — a small runner wrapping it for two connection modes.

### Option A — direct connection to the production Postgres host

```bash
MODE=remote \
PROD_PGHOST=<production-db-host> \
PROD_PGUSER=<db-user> \
PROD_DB=mainttrack \
PROD_PGPASSWORD=<db-password> \
./scripts/run-production-hierarchy-map-alignment.sh
```

This prompts for a typed `apply` confirmation before touching the database
(mirrors the safety pattern already used in `scripts/refresh-test-from-production.sh`).

### Option B — via psql directly (no wrapper script)

```bash
psql "host=<host> user=<user> dbname=mainttrack sslmode=require" \
     -v ON_ERROR_STOP=1 \
     -f scripts/production-hierarchy-map-alignment.sql
```

### Option C — dockerized Postgres (TEST / local environments)

```bash
./scripts/run-production-hierarchy-map-alignment.sh
# or override the container/db name:
MAINTTRACK_PG_CONTAINER=mainttrack-postgres MAINTTRACK_DB_NAME=mainttrack_dev \
./scripts/run-production-hierarchy-map-alignment.sh
```

### After running the SQL

1. Deploy the frontend as usual (picks up the `translations.ts` label changes — no DB action needed for those).
2. Restart the API (`sudo systemctl restart mainttrack`) so the running process's EF model matches the new `machines.sort_order` column (the app already contains the `SortOrder` mapping and ordering logic in this codebase; only the column/data needed to reach the server).
3. Run the verification queries at the bottom of the SQL file (or re-run the script — it's idempotent) to confirm: new components exist, the Conveyors array/machine exists, the deactivated machines are inactive, Packing House `sort_order` matches the map, and `machine.accumulatorOutside` is assigned.

### Why this works without EF migration history bookkeeping

The SQL script only touches application data tables (`arrays`, `machines`,
`machine_components`, `machine_component_mappings`) and adds one plain
column (`machines.sort_order`). It does not depend on, and does not need to
register anything in, `__EFMigrationsHistory`. If this server's database is
later brought under normal EF migration management, running
`dotnet ef database update` at that point will simply see these 4 migrations
already reflected in the schema/data — to avoid EF trying to re-run them,
insert matching rows into `__EFMigrationsHistory` first (or use
`dotnet ef migrations script --idempotent` and diff), the same way the other
`scripts/production-manual-schema-*.sql` files in this repo have been used to
pre-seed schema ahead of migration adoption.

## 3. Files touched in this scope

- `frontend/src/i18n/translations.ts`
- `backend/MaintTrack.Domain/Machines/Machine.cs` (added `SortOrder`)
- `backend/MaintTrack.Infrastructure/Persistence/MaintTrackDbContext.cs` (mapped `SortOrder`)
- `backend/MaintTrack.Infrastructure/Hierarchy/HierarchyService.cs` (order by `SortOrder`)
- `backend/MaintTrack.Infrastructure/MorningRoundV2/MorningRoundV2Service.cs` (order by `SortOrder`)
- `backend/MaintTrack.Infrastructure/Machines/MachineService.cs` (order by `SortOrder`)
- `backend/MaintTrack.Infrastructure/Migrations/20260707220000_UpdateOnionMixerComponents.cs`
- `backend/MaintTrack.Infrastructure/Migrations/20260707230000_HierarchyMapAlignment.cs`
- `backend/MaintTrack.Infrastructure/Migrations/20260707233000_AddMachineSortOrder.cs`
- `backend/MaintTrack.Infrastructure/Migrations/20260707234500_DeactivatePackingHouseExtras.cs`
- `backend/MaintTrack.Infrastructure/Migrations/MaintTrackDbContextModelSnapshot.cs`
- `scripts/production-hierarchy-map-alignment.sql` (new — consolidated, migration-free equivalent)
- `scripts/run-production-hierarchy-map-alignment.sh` (new — runner for the above)

# Production Deployment Checklist — Sprint: Morning Round V2 / Maintenance Log V2 / Hierarchy Alignment / Feature Flags

Follow this document **top to bottom**. It is written to be executable — every
step has an exact command. Steps marked `[MANUAL]` require a human decision
or a value only you know (server host, credentials); everything else can be
copy-pasted.

Scope: this checklist covers everything introduced since the last production
release (commit `d4e9fde` — "Production Release: Maintenance Log + Maintenance
Tasks full integration with upload system refactor"), i.e. 10 EF migrations,
none of which are applied via `dotnet ef database update` in production, **plus
one pre-existing, non-sprint schema gap discovered during a repository audit**
(the `forklifts` inspection columns — see step 3.9 and the coverage table at
the end of this document for why it has no corresponding EF migration).

---

## 0. Before you start — set your environment variables `[MANUAL]`

```bash
export PROD_PGHOST="<production-db-host>"
export PROD_PGUSER="<production-db-user>"
export PROD_PGPASSWORD="<production-db-password>"
export PROD_DB="mainttrack"
export PROD_TENANT_ID="beed1fc4-ffbb-4ea1-b7c8-d84584506842"   # Shallit tenant
export DEPLOY_HOST="<production-server-host>"
export DEPLOY_USER="<ssh-user>"
export DEPLOY_KEY="<path-to-ssh-private-key>"
```

Confirm you can connect before doing anything else:

```bash
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -c "SELECT NOW();"
```

---

## 1. Pre-flight — read-only inspection `[MANUAL — review output]`

Confirm the current state of production before changing anything. **Do not
proceed if `array.production.*` rows exist** — see step 2a below first.

```bash
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -c "\dt arrays"
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -c "SELECT name_key FROM arrays WHERE tenant_id = '$PROD_TENANT_ID';" 2>/dev/null || echo "arrays table does not exist yet (expected on first run)"
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -c "\dt machine_components"
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -c "\d forklifts" | grep -i inspection || echo "forklift inspection columns missing (expected before step 3.9)"
```

## 2. Backup

```bash
mkdir -p ~/mainttrack-backups
PGPASSWORD="$PROD_PGPASSWORD" pg_dump "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
  -F c -f ~/mainttrack-backups/mainttrack_prod_backup_$(date +%Y%m%d_%H%M%S).dump
```

Verify the backup file was created and is non-trivial in size:

```bash
ls -lh ~/mainttrack-backups/ | tail -1
```

### 2a. `[MANUAL, only if step 1 found `array.production.*` rows]` Clean up obsolete 4-array data

```bash
PGPASSWORD="$PROD_PGPASSWORD" psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
UPDATE machines SET array_id = NULL
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
  AND array_id IN (SELECT id FROM arrays WHERE name_key LIKE 'array.production.%');
DELETE FROM arrays
WHERE tenant_id = 'beed1fc4-ffbb-4ea1-b7c8-d84584506842'
  AND name_key LIKE 'array.production.%';
COMMIT;
SQL
```

---

## 3. Apply database SQL (in this exact order)

```bash
cd /path/to/mainttrack-shallit-prod

CONN="host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require"

# 3.1 Arrays + machines.array_id + morning_round_v2_submissions (schema)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-manual-schema-morning-round-v2.sql

# 3.2 machine_components + machine_component_mappings (schema)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-machine-components-schema.sql

# 3.3 treatments.machine_id / maintenance_type_id / created_by_user_id (schema)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-treatments-shared-dropdowns.sql

# 3.4 aircompressor/cooling maintenance types (data)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-maintenance-types.sql

# 3.5 users.enable_new_morning_round / enable_new_maintenance_log (schema)
#     CRITICAL: must run before step 5 (backend deploy) — see script header.
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-user-feature-flags.sql

# 3.6 Arrays + machines hierarchy data (canonical 6-array model)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -v tenant_id="$PROD_TENANT_ID" -f scripts/production-seed-arrays-machines.sql

# 3.7 Machine components catalog + base machine↔component mappings (data)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/machine_components_seed.sql

# 3.8 Hierarchy map alignment: onion mixer components, OVERHAUL component,
#     conveyors array, sort_order, packing house cleanup (schema + data)
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-hierarchy-map-alignment.sql

# 3.9 forklifts.last_inspection_date / inspection_expiry_date / inspection_updated_at (schema)
#     NOT a sprint migration — see scripts/README.md for why no EF migration
#     exists for this. Required unconditionally by ForkliftService /
#     ForkliftReportService / the expiring-inspections report.
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -v ON_ERROR_STOP=1 -f scripts/production-forklift-inspection-columns.sql
```

Each script prints its own read-only verification queries at the end —
review the output of each before moving to the next.

### 3.10 Verify full hierarchy end-state

```bash
PGPASSWORD="$PROD_PGPASSWORD" psql "$CONN" -c "
SELECT a.name_key AS array_name, COUNT(m.id) AS active_machines
FROM arrays a
LEFT JOIN machines m ON m.array_id = a.id AND m.is_active = true
WHERE a.tenant_id = '$PROD_TENANT_ID'
GROUP BY a.sort_order, a.name_key
ORDER BY a.sort_order;"
```

You should see exactly 7 arrays: `array.washing_system`, `array.onion_system`,
`array.water_cooling_system`, `array.rooms`, `array.ginoshar`,
`array.packing_house`, `array.conveyors` — no `array.production.*` rows.

---

## 4. Deploy backend

```bash
./deploy/build.sh
DEPLOY_HOST="$DEPLOY_HOST" DEPLOY_USER="$DEPLOY_USER" DEPLOY_KEY="$DEPLOY_KEY" ./deploy/deploy.sh
```

`[MANUAL]` If your production server actually uses the layout in
`backend/deploy-backend.sh` (`/opt/mainttrack/api`, service `mainttrack`)
instead of `deploy/deploy.sh`'s layout (`/var/www/mainttrack/api`, service
`mainttrack-api`), use that script instead — confirm which matches your real
server first (`ssh ... "systemctl status mainttrack 2>/dev/null || systemctl status mainttrack-api"`).

## 5. Restart backend service (if not already done by the deploy script)

```bash
ssh -i "$DEPLOY_KEY" "$DEPLOY_USER@$DEPLOY_HOST" "sudo systemctl restart mainttrack-api"
ssh -i "$DEPLOY_KEY" "$DEPLOY_USER@$DEPLOY_HOST" "systemctl status mainttrack-api --no-pager | head -n 20"
```

## 6. Backend smoke tests

```bash
curl -sf https://mt.shallit.co.il/api/health && echo " -> health OK"

# Login as a known production user and confirm the response includes the new
# feature-flag fields (both should be false for every user right after this
# deploy, since no pilot user has been enabled yet):
curl -s -X POST https://mt.shallit.co.il/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"<REAL_USERNAME>","password":"<REAL_PASSWORD>"}' | python3 -m json.tool
```

Confirm the JSON response includes `"enableNewMorningRound": false` and
`"enableNewMaintenanceLog": false`.

---

## 7. Deploy frontend

```bash
VITE_API_URL=https://mt.shallit.co.il/api ./deploy/build.sh
# (deploy/build.sh builds both frontend and backend; if backend was already
#  deployed in step 4, you only need to re-upload frontend/build/*)
```

If `deploy/deploy.sh` was already run in step 4, the frontend was uploaded in
the same pass. Otherwise, re-run:

```bash
scp -i "$DEPLOY_KEY" -r frontend/build/* "$DEPLOY_USER@$DEPLOY_HOST:/var/www/mainttrack/web/"
```

No nginx restart is required for a static file swap, but reload if your
config caches file lists:

```bash
ssh -i "$DEPLOY_KEY" "$DEPLOY_USER@$DEPLOY_HOST" "sudo systemctl reload nginx"
```

---

## 8. Full smoke tests `[MANUAL — browser]`

Log in as an existing production user (flags default `false`) and verify:

- [ ] Login succeeds
- [ ] Home screen shows **legacy** "Morning Round" and "Maintenance Log" menu items (not the V2 labels)
- [ ] Legacy Morning Round (`/morning-round`) loads and a submission can be saved
- [ ] Legacy Maintenance Log — reachable via the home menu (now served at `/maintenance-v1`) — loads and a submission can be saved
- [ ] Direct navigation to `/maintenance` redirects to `/home` (expected — flag is off). **If this is not acceptable for launch day, hold this deploy and revisit routing before proceeding.**
- [ ] Reports list and report details load correctly for existing data
- [ ] Maintenance Tasks report loads
- [ ] Treatments screen loads and can create/view an entry
- [ ] Forklift report screen loads and can create/view an entry
- [ ] No new errors in backend logs: `ssh ... "journalctl -u mainttrack-api -n 100 --no-pager"`

Only after all of the above pass should you consider enabling the feature
flags for a real pilot user (separate, deliberate step — see
`scripts/production-user-feature-flags.sql`, OPTIONAL section).

---

## 9. Rollback

### 9a. Frontend-only rollback (fastest, if frontend smoke tests fail)

```bash
# Re-deploy the previous frontend build (keep the prior frontend/build/ artifact
# around before overwriting, or rebuild from the previous git commit)
git stash # if you have local changes
git checkout <previous-release-commit> -- frontend/
VITE_API_URL=https://mt.shallit.co.il/api ./deploy/build.sh
scp -i "$DEPLOY_KEY" -r frontend/build/* "$DEPLOY_USER@$DEPLOY_HOST:/var/www/mainttrack/web/"
git checkout HEAD -- frontend/
```

The new backend is backward compatible with the old frontend (all changes are
additive), so this is safe on its own.

### 9b. Backend rollback

```bash
git checkout <previous-release-commit> -- backend/
dotnet publish backend/MaintTrack.Api/MaintTrack.Api.csproj -c Release -o /tmp/rollback-publish
scp -i "$DEPLOY_KEY" -r /tmp/rollback-publish/* "$DEPLOY_USER@$DEPLOY_HOST:/var/www/mainttrack/api/"
ssh -i "$DEPLOY_KEY" "$DEPLOY_USER@$DEPLOY_HOST" "sudo systemctl restart mainttrack-api"
git checkout HEAD -- backend/
```

This is also safe on its own — none of this sprint's new/changed database
columns are required by the previous backend build (it simply never queries
them).

### 9c. Database rollback (only if the above are insufficient — last resort)

The forward-only SQL scripts are intentionally non-destructive (no dropped
columns/tables, only additive schema + soft-deactivation of a few machines),
so a database rollback should rarely be necessary. If required, restore from
the backup taken in step 2:

```bash
PGPASSWORD="$PROD_PGPASSWORD" pg_restore \
  -h "$PROD_PGHOST" -U "$PROD_PGUSER" -d "$PROD_DB" \
  --clean --if-exists \
  ~/mainttrack-backups/mainttrack_prod_backup_<timestamp>.dump
```

Alternatively, each new/modified script in `scripts/` has a commented-out
`ROLLBACK` section at the bottom with the exact reverse SQL, if you need a
more surgical rollback of a single change instead of a full restore.

---

## 10. Migration → SQL coverage (100% required before running this checklist)

| Migration | Covered by SQL | SQL File |
|---|---|---|
| `20260613190000_UpdateTreatmentsSharedDropdowns` | ✅ | `scripts/production-treatments-shared-dropdowns.sql` |
| `20260613200000_AddAircompressorAndCoolingMaintenanceTypes` | ✅ | `scripts/production-maintenance-types.sql` |
| `20260613210000_AddArraysAndMachineArrayId` | ✅ | `scripts/production-manual-schema-morning-round-v2.sql` (schema) + `scripts/production-seed-arrays-machines.sql` (data) |
| `20260613220000_AddMorningRoundV2` | ✅ | `scripts/production-manual-schema-morning-round-v2.sql` |
| `20260705230000_AddMachineComponentsInfrastructure` | ✅ | `scripts/production-machine-components-schema.sql` (schema) + `scripts/machine_components_seed.sql` (data) |
| `20260707220000_UpdateOnionMixerComponents` | ✅ | `scripts/production-hierarchy-map-alignment.sql` |
| `20260707230000_HierarchyMapAlignment` | ✅ | `scripts/production-hierarchy-map-alignment.sql` |
| `20260707233000_AddMachineSortOrder` | ✅ | `scripts/production-hierarchy-map-alignment.sql` |
| `20260707234500_DeactivatePackingHouseExtras` | ✅ | `scripts/production-hierarchy-map-alignment.sql` |
| `20260708000000_AddUserFeatureFlags` | ✅ | `scripts/production-user-feature-flags.sql` |

**Coverage: 10 / 10 sprint migrations (100%).**

### 10a. Additional non-migration schema gap (discovered by repository audit, not a sprint migration)

| Schema object | EF migration | Covered by SQL | SQL File |
|---|---|---|---|
| `forklifts.last_inspection_date` | **None exists** — see `scripts/README.md` for why | ✅ | `scripts/production-forklift-inspection-columns.sql` |
| `forklifts.inspection_expiry_date` | **None exists** | ✅ | `scripts/production-forklift-inspection-columns.sql` |
| `forklifts.inspection_updated_at` | **None exists** | ✅ | `scripts/production-forklift-inspection-columns.sql` |

These three columns are required unconditionally by the current backend
(`ForkliftService`, `ForkliftReportService`,
`ForkliftReportsQueryService.GetExpiringInspectionsAsync`) and current
frontend (`ForkliftReportScreen.tsx`), but no EF Core migration in the
repository's history ever creates them — `20260308185800_AddForkliftInspectionFields`
only alters unrelated `treatments` columns despite its name. This predates
this sprint and is being included here because it must be applied for the
Forklift Reports feature (and its list/detail endpoints) to function at all
in production, and it is not something a future `dotnet ef database update`
would ever fix. `scripts/production-forklift-inspection-columns.sql` is the
permanent, hand-written source of truth for this schema going forward.

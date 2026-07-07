# MaintTrack — TEST Environment Setup

This guide provisions a **fully isolated TEST environment** alongside the existing
Production deployment, on the same server. Every step below only ever creates,
reads, or restarts TEST-specific resources. **Production is never stopped,
restarted, or modified by any step in this guide.**

| | Production | TEST |
|---|---|---|
| Backend path | `/opt/mainttrack/api` | `/opt/mainttrack-test/api` |
| Service | `mainttrack.service` | `mainttrack-test.service` |
| Port (upstream) | `5062` | `5063` |
| Frontend path | `/var/www/mainttrack/web` | `/var/www/mainttrack-test` |
| Domain | `mt.shallit.co.il` | `mt-test.shallit.co.il` |
| Database | `mainttrack` | `mainttrack_test` |
| DB role | `mainttrack_user` | `mainttrack_test_user` |
| Secrets file | `/etc/mainttrack/secrets.env` | `/etc/mainttrack-test/secrets.env` |
| Upload storage | `/mnt/app` | `/mnt/app-test` |

> **Domain note:** `mt-test.shallit.co.il` is already referenced elsewhere in this
> repo (`frontend/.env.test`, `frontend/.env.staging`, and the README's "Staging"
> row) — this guide reuses that existing hostname as the TEST domain rather than
> introducing a new one. If you intend TEST and the existing "Staging" reference
> to be two *different* environments, tell me and I'll generate a distinct
> hostname/config instead.

## Files generated for review

| File | Purpose |
|---|---|
| `backend/deploy-test.sh` | Publishes + deploys the backend to TEST (gitignored, like `deploy-backend.sh` — contains a personal key path) |
| `frontend/deploy-test.sh` | Builds + deploys the frontend to TEST (gitignored, same reason) |
| `backend/MaintTrack.Api/appsettings.Test.json` | ASP.NET config for the `Test` environment |
| `deploy/systemd/mainttrack-test.service` | systemd unit for the TEST backend (see assumptions noted inside the file) |
| `nginx/mt-test.shallit.co.il.conf` | nginx site config for TEST |
| `scripts/create-test-database.sql` / `.sh` | Creates `mainttrack_test` DB + role |
| `scripts/refresh-test-from-production.sh` | Optional: one-way Production → TEST data refresh |
| `frontend/.env.test` | Already existed — `VITE_API_URL=https://mt-test.shallit.co.il/api` |

Nothing has been executed. Nothing has connected to any server. Review each file,
then follow the steps below manually (or hand them to whoever has server access).

---

## 1. Folder creation (on the server)

Run as a user with sudo access. This only creates new, empty TEST directories.

```bash
sudo mkdir -p /opt/mainttrack-test/api
sudo mkdir -p /var/www/mainttrack-test
sudo mkdir -p /mnt/app-test
sudo mkdir -p /etc/mainttrack-test

# Match ownership to whatever user runs Production (adjust if different)
sudo chown -R azureuser:azureuser /opt/mainttrack-test
sudo chown -R azureuser:azureuser /mnt/app-test
```

Verify Production paths are untouched:

```bash
ls -la /opt/mainttrack        # should be unchanged
systemctl status mainttrack   # should still be active
```

---

## 2. Database creation

Create the isolated `mainttrack_test` database + dedicated role. This step
never reads from or writes to the `mainttrack` (production) database.

```bash
# From your machine or the server, against the Postgres host:
PGHOST=<postgres-host> ./scripts/create-test-database.sh
```

This runs `scripts/create-test-database.sql`, which:
- creates role `mainttrack_test_user` (only if missing)
- creates database `mainttrack_test` (only if missing)
- grants the new role full privileges on `mainttrack_test` only

**Before running:** edit `scripts/create-test-database.sql` and replace
`CHANGE_ME_TEST_PASSWORD` with a real generated password. Store that same
password only in `/etc/mainttrack-test/secrets.env` (step 4) — never commit it.

### Optional: seed TEST from Production data

If you want TEST to start with a realistic dataset instead of an empty schema:

```bash
PROD_PGHOST=<host> PROD_PGUSER=<user> \
TEST_PGHOST=<host> TEST_PGUSER=postgres \
./scripts/refresh-test-from-production.sh
```

This is **read-only against Production** (`pg_dump` only) and only ever
drops/recreates `mainttrack_test`. The script hard-fails if `TARGET_DB` is
anything other than `mainttrack_test`, and refuses to run if source/target
resolve to the same database, as a guard against accidental misconfiguration.

---

## 3. Secrets file for TEST

Create `/etc/mainttrack-test/secrets.env` on the server (never commit this file):

```bash
sudo tee /etc/mainttrack-test/secrets.env > /dev/null <<'EOF'
ASPNETCORE_ENVIRONMENT=Test
MAINTTRACK_DB_CONNECTION=Host=localhost;Port=5432;Database=mainttrack_test;Username=mainttrack_test_user;Password=<same password as step 2>
Jwt__Key=<generate a separate 32+ char secret for TEST — do not reuse Production's>
EOF

sudo chmod 600 /etc/mainttrack-test/secrets.env
sudo chown azureuser:azureuser /etc/mainttrack-test/secrets.env
```

Using a **different** `Jwt__Key` than Production means TEST-issued tokens can
never be replayed against Production and vice versa.

---

## 4. Backend deployment

From your local machine, review `backend/deploy-test.sh` first, then:

```bash
./backend/deploy-test.sh
```

This:
1. `dotnet publish`es the API in Release mode to a local `publish-test/` folder
2. `rsync`s it to `/opt/mainttrack-test/api/` on the server only
3. Restarts `mainttrack-test` (not `mainttrack`)

It never touches `/opt/mainttrack/api` or runs `systemctl restart mainttrack`.

### Applying EF migrations to TEST

Run once after the first deploy (and after any future migration is added):

```bash
cd backend/MaintTrack.Api
MAINTTRACK_DB_CONNECTION="Host=localhost;Port=5432;Database=mainttrack_test;Username=mainttrack_test_user;Password=<...>" \
dotnet ef database update --project ../MaintTrack.Infrastructure/MaintTrack.Infrastructure.csproj
```

(Run this from a machine with `dotnet ef` tooling and network access to the
Postgres host — typically the server itself, or via an SSH tunnel.)

---

## 5. Service installation

```bash
# Copy the unit file to the server first, then:
sudo cp deploy/systemd/mainttrack-test.service /etc/systemd/system/mainttrack-test.service

# IMPORTANT: read the header comment inside the file first — it documents
# assumptions made without server access (dotnet path, run-as user, etc.)
# and tells you what to diff against the real mainttrack.service.

sudo systemctl daemon-reload
sudo systemctl enable mainttrack-test
sudo systemctl start mainttrack-test
sudo systemctl status mainttrack-test --no-pager
```

Confirm Production's service is unaffected:

```bash
systemctl status mainttrack --no-pager
```

---

## 6. Frontend deployment

```bash
./frontend/deploy-test.sh
```

This builds with `frontend/.env.test` (`VITE_API_URL=https://mt-test.shallit.co.il/api`)
and `rsync`s the output to `/var/www/mainttrack-test` only.

---

## 7. Nginx configuration

```bash
sudo cp nginx/mt-test.shallit.co.il.conf /etc/nginx/sites-available/mt-test.shallit.co.il
sudo ln -sf /etc/nginx/sites-available/mt-test.shallit.co.il /etc/nginx/sites-enabled/

# Always test config before reloading — a syntax error here could affect
# nginx globally (including Production sites served by the same nginx).
sudo nginx -t

sudo systemctl reload nginx
```

Do **not** run `systemctl restart nginx` (a full restart drops in-flight
connections across all sites, including Production). Use `reload`, and only
after `nginx -t` passes.

---

## 8. SSL setup (Let's Encrypt)

Requires DNS for `mt-test.shallit.co.il` to already point at the server.

```bash
sudo certbot --nginx -d mt-test.shallit.co.il
```

Certbot will edit the TEST site file in place to add the certificate paths and
an HTTP→HTTPS redirect. It does not touch the Production certificate or the
Production site file (`mt.shallit.co.il`).

Verify Production's certificate is unaffected:

```bash
sudo certbot certificates
```

---

## 9. Verification

```bash
# Backend health (TEST, local to server)
curl -sf http://127.0.0.1:5063/health && echo OK

# Backend health (TEST, via nginx/public domain)
curl -sf https://mt-test.shallit.co.il/health && echo OK

# Frontend loads
curl -sfI https://mt-test.shallit.co.il/ | head -n 1

# Service status
systemctl status mainttrack-test --no-pager

# Confirm Production is still healthy and untouched
curl -sf https://mt.shallit.co.il/health && echo OK
systemctl status mainttrack --no-pager
```

Also do a manual smoke test in the browser: log in at
`https://mt-test.shallit.co.il`, confirm it hits the TEST API (check Network
tab for `mt-test.shallit.co.il/api/...` calls), and that data is isolated from
Production (e.g. a forklift created in TEST does not appear in Production).

---

## 10. Rollback instructions

Rolling back TEST never requires touching Production. Choose the scope that
matches what went wrong:

**Roll back backend only** (previous publish folder):

```bash
# If you keep timestamped releases, re-point and restart:
sudo systemctl stop mainttrack-test
sudo rsync -av --delete /opt/mainttrack-test/api-previous/ /opt/mainttrack-test/api/
sudo systemctl start mainttrack-test
```
(Recommend keeping a `-previous` copy or a release-versioned folder structure
before your first real deploy, so a rollback target exists.)

**Roll back frontend only:**

```bash
# Re-deploy the last known-good build (re-run frontend/deploy-test.sh from the
# corresponding earlier git commit), or restore from a backup of
# /var/www/mainttrack-test if you keep one before each deploy.
```

**Roll back database:**

```bash
# If refresh-test-from-production.sh was used, simply re-run it to reset TEST
# to the latest Production snapshot. If a migration broke TEST, restore from
# a pg_dump backup taken before the migration:
pg_restore -h <host> -U mainttrack_test_user -d mainttrack_test --clean <backup-file>
```

**Full teardown** (if TEST needs to be decommissioned):

```bash
sudo systemctl stop mainttrack-test
sudo systemctl disable mainttrack-test
sudo rm /etc/systemd/system/mainttrack-test.service
sudo systemctl daemon-reload

sudo rm -f /etc/nginx/sites-enabled/mt-test.shallit.co.il
sudo nginx -t && sudo systemctl reload nginx

# Data removal — double-check the database name before running:
psql -h <host> -U postgres -c "DROP DATABASE IF EXISTS mainttrack_test;"

sudo rm -rf /opt/mainttrack-test /var/www/mainttrack-test /mnt/app-test /etc/mainttrack-test
```

None of the above reference `/opt/mainttrack`, `mainttrack.service`,
`/var/www/mainttrack/web`, `mainttrack` (the database), or
`mt.shallit.co.il` — Production is unreachable by any rollback/teardown step
in this section.

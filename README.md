# MaintTrack — ShallIT

Official development repository for the ShallIT MaintTrack deployment.

| Environment | URL |
|-------------|-----|
| Production | https://mt.shallit.co.il |
| Staging | https://mt-test.shallit.co.il |

This is a **standalone client project**. All ShallIT-specific development happens here — not in the MaintTrack core repository.

## Stack

- **Backend:** ASP.NET 8 minimal API (`backend/`)
- **Frontend:** Vite + React (`frontend/`)
- **Database:** PostgreSQL
- **Reverse proxy:** nginx (`nginx/`)
- **Deploy:** `deploy/build.sh`, `deploy/deploy.sh`

## API routing

The SPA uses `VITE_API_URL=https://mt.shallit.co.il/api`. Client paths are relative to that base (e.g. `/v1/auth/login` → `/api/v1/auth/login` on the server).

Versioned routes: `/api/v1/*`  
Other routes: `/api/morning-round`, `/api/maintenance`, `/api/maintenance-tasks`

## Local development

### PostgreSQL (local)

This project expects **one** Postgres on port **5432**. If both Homebrew and Docker Postgres are running, `localhost:5432` may hit the wrong instance (empty/partial `mainttrack_dev` on brew vs full data in Docker).

**Use Docker Postgres** (`mainttrack-postgres` container):

```bash
# Stop brew Postgres so Docker owns port 5432
brew services stop postgresql@14

# Ensure container is running
docker start mainttrack-postgres

# Verify you hit Docker (server addr is Docker network, not 127.0.0.1)
PGPASSWORD=postgres psql -h 127.0.0.1 -U postgres -d mainttrack_dev \
  -c "SELECT inet_server_addr(), current_database();"
```

Connection string (already in `appsettings.Development.json`):

`Host=localhost;Port=5432;Database=mainttrack_dev;Username=postgres;Password=postgres`

After switching DB, **restart the API** so EF opens a fresh connection.

Apply pending migrations:

```bash
cd backend/MaintTrack.Api
dotnet ef database update --project ../MaintTrack.Infrastructure/MaintTrack.Infrastructure.csproj
```

Local dev login: `admin` / `admin` (see `frontend/src/dev/devAuthBootstrap.ts`).

**Arrays → Machines hierarchy (Morning Round v2 dev seed):**

```bash
./scripts/seed-arrays-hierarchy-dev.sh
```

Idempotent SQL seed for the Pilot Factory tenant (`11111111-1111-1111-1111-111111111111`). Populates 7 arrays with machines using translation keys only. Safe to re-run. Verify via `GET /api/v2/morning-round` or `/morning-round-v2` in the UI.

### Backend

```bash
cd backend/MaintTrack.Api
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --urls http://localhost:5062
```

API: http://localhost:5062

### Frontend

```bash
cd frontend
cp .env.example .env.local   # optional; vite proxies /api in dev
npm ci
npm run dev
```

SPA: http://localhost:3000

### Build for production

```bash
./deploy/build.sh
```

## Configuration

| File | Purpose |
|------|---------|
| `frontend/.env.production` | Production API base URL |
| `frontend/.env.staging` | Staging API base URL |
| `frontend/.env.example` | Template for local overrides |
| `backend/MaintTrack.Api/appsettings.Production.json` | Production defaults |
| `config/secrets.example.env` | Server secrets template (never commit real values) |
| `nginx/mt.shallit.co.il.conf` | nginx site config |

Secrets (DB connection, JWT key) must be supplied via environment variables or a server-side secrets file — never committed to git.

## Deploy

See `deploy/deploy.sh` and `.github/workflows/deploy-production.yml`.

Default server paths:

- API: `/var/www/mainttrack/api`
- Web: `/var/www/mainttrack/web`
- Service: `mainttrack-api`

## Repository layout

```
backend/     .NET solution
frontend/    React SPA
nginx/       Site configuration
deploy/      Build and deploy scripts
config/      Environment templates
docs/        Additional documentation (if added)
```

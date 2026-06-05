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
Other routes: `/api/morning-round`, `/api/maintenance`, `/api/uploads`

## Local development

### Backend

```bash
cd backend/MaintTrack.Api
export ASPNETCORE_ENVIRONMENT=Development
# Set MAINTTRACK_DB_CONNECTION or edit appsettings.Development.json
dotnet run
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

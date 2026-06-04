# MaintTrack — ShallIT Production

Production-ready snapshot of MaintTrack for **https://mt.shallit.co.il**.

This repository contains the deployable application source (ASP.NET 8 API + Vite React SPA), configuration templates, nginx site template, and deployment scripts. It does not contain secrets.

## Architecture

| Component | Technology | Production URL |
|-----------|------------|----------------|
| SPA | Vite + React | `https://mt.shallit.co.il` |
| API | ASP.NET 8 minimal API | `https://mt.shallit.co.il/api/...` |
| Auth | JWT in HttpOnly cookie | Cookie scoped to site host |
| Database | PostgreSQL | Configure via env |
| Files | Local storage (default) or S3 | `Storage:Provider` in config |

### API routes

Versioned endpoints use the `/api/v1/` prefix (e.g. `/api/v1/auth/login`, `/api/v1/machines`).

Additional routes (no `v1` segment):

- `/api/morning-round`
- `/api/maintenance`
- `/api/uploads`

The frontend uses `VITE_API_URL=https://mt.shallit.co.il/api` so client paths like `/v1/auth/login` resolve to `/api/v1/auth/login`.

### CORS

Production allows origin `https://mt.shallit.co.il` only. Staging origin `https://mt-test.shallit.co.il` is configured for `ASPNETCORE_ENVIRONMENT=Staging`.

## Repository layout

```
backend/           .NET solution (Api, Application, Domain, Infrastructure)
frontend/          Vite React SPA
nginx/             Nginx site template
deploy/            build.sh, deploy.sh
config/            secrets.example.env
scripts/           export-production.sh (for re-exporting from dev repo)
.github/workflows/ CI build (and optional SSH deploy)
```

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- PostgreSQL
- nginx (production)

## Configuration

1. Copy `config/secrets.example.env` to the server and set real values.
2. Set environment variables (or systemd `EnvironmentFile`):

   - `ASPNETCORE_ENVIRONMENT=Production`
   - `MAINTTRACK_DB_CONNECTION` — PostgreSQL connection string
   - `Jwt__Key` — secure signing key (min 32 characters)

3. Optional: override `Storage:Provider` to `S3` and set AWS credentials.

Base templates:

- `backend/MaintTrack.Api/appsettings.json` — structure only (no secrets)
- `backend/MaintTrack.Api/appsettings.Production.json` — production defaults
- `frontend/.env.production` — `VITE_API_URL=https://mt.shallit.co.il/api`
- `frontend/.env.example` — documentation for local builds

## Build

```bash
chmod +x deploy/build.sh
./deploy/build.sh
```

Outputs:

- `frontend/build/` — static SPA
- `publish/api/` — published API

## Deploy

1. Install nginx using `nginx/mt.shallit.co.il.conf` (adjust TLS paths and paths).
2. Create systemd unit for the API with `ASPNETCORE_ENVIRONMENT=Production`.
3. Run:

```bash
export DEPLOY_HOST=your-server
export DEPLOY_USER=ubuntu
export DEPLOY_KEY=~/.ssh/your_key
chmod +x deploy/deploy.sh
./deploy/deploy.sh
```

Default remote paths (from CI workflow):

- API: `/var/www/mainttrack/api`
- Web: `/var/www/mainttrack/web`
- Service: `mainttrack-api`

## nginx requirements

- Proxy `/api/` and `/health` to Kestrel (default port `5062`)
- Set `X-Forwarded-Proto` for HTTPS (required for secure cookies; API uses `UseForwardedHeaders()`)
- Serve SPA from `root` with `try_files` fallback to `index.html`

## Re-export from development repo

If you maintain a separate development repository, refresh this snapshot with:

```bash
/path/to/MaintTrack/scripts/export-production.sh \
  /path/to/MaintTrack \
  /path/to/mainttrack-shallit-prod
```

Then re-apply production-specific config (CORS, appsettings templates, README) or run finalize steps documented in your process.

## Security

- Never commit `.env`, real `appsettings` secrets, or SSH keys.
- Rotate JWT keys and database passwords independently.
- Use TLS in production.

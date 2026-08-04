# AGENTS.md

## Cursor Cloud specific instructions

This section captures non-obvious, durable setup/run details for this repo. Standard commands live in `README.md`; only the caveats below are cloud-specific.

### Services (local dev)

| Service | How to run | Port | Notes |
|---|---|---|---|
| PostgreSQL 16 | `sudo pg_ctlcluster 16 main start` | 5432 | Not auto-started on VM boot — start it before the API. Dev DB `mainttrack_dev`, user/pass `postgres`/`postgres`. |
| Backend API (ASP.NET 8) | `cd backend/MaintTrack.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run` | 5062 | Uses `appsettings.Development.json` (points at `mainttrack_dev`). Serves `/api/*` and `/health`. |
| Frontend (Vite + React) | `cd frontend && npm run dev` | 3000 | Vite proxies `/api` and `/health` → `http://localhost:5062`. Requires `frontend/.env` (see below). |

The update script already runs `dotnet restore` and `npm ci`, so dependencies are refreshed on startup. Postgres and the API/frontend are NOT started by the update script — start them manually as above.

### Frontend requires `frontend/.env` with `VITE_API_URL=/api` (non-obvious)

The API client (`frontend/src/api/apiClient.ts`) prepends `VITE_API_URL` to paths in `frontend/src/api/endpoints.ts`, which are written WITHOUT the `/api` prefix (e.g. `/v1/auth/login`). The Vite dev proxy only forwards `/api` and `/health`. So local dev only works when `VITE_API_URL=/api` (client → `/api/v1/...` → proxy → backend `/api/v1/...`).

- The committed `.env.example` does NOT provide a working local value (its active line is the production URL), so copying it is not enough.
- `frontend/.env` (gitignored) must contain exactly: `VITE_API_URL=/api`. If it is missing, create it, then restart `npm run dev`.

### Database schema is created from the EF model, NOT from `dotnet ef database update` (important)

The committed EF migration chain is STALE: `Migrations/20260318100000_AddRolesAndUserRoleId.cs` and `20260328220000_MaintenanceTypesWithCodes.cs` have no `.Designer.cs` companions, so EF skips them. Running `dotnet ef database update` therefore builds an OUT-OF-DATE schema (e.g. `users.role` string column and no `roles` table) that does NOT match the runtime DbContext model (which uses `users.role_id` + a `roles` table). The running API will fail against a migration-built schema.

The correct schema matches the current model / `MaintTrackDbContextModelSnapshot.cs` — the same schema `DbContext.Database.EnsureCreated()` produces (this is what the test suite uses). The dev DB in this environment was created this way and seeded with:
- roles `1=Worker, 2=Manager, 3=SuperAdmin`
- tenant `00000000-0000-0000-0000-000000000001` ("Dev Tenant")
- user `admin` / password `admin` (SuperAdmin) — the frontend dev auto-login (`src/dev/devAuthBootstrap.ts`) logs in as `admin`/`admin`
- 5 morning-round checklist template items for the dev tenant

If you ever need to recreate the schema from scratch, do it from the model via `EnsureCreated()` (e.g. a small throwaway program referencing `MaintTrack.Infrastructure` that constructs `MaintTrackDbContext` and calls `EnsureCreated()`), then re-insert the seed rows above — do NOT use `dotnet ef database update`.

### Tests

Run with `dotnet test backend/MaintTrack.Tests/MaintTrack.Tests.csproj`. The tests spin up their own `mainttrack_test` database via the `mainttrack`/`change_me` Postgres role (already created) using `EnsureCreated`/`EnsureDeleted`.

- Gotcha: the API resolves its connection string from `MAINTTRACK_DB_CONNECTION` FIRST (over `appsettings`). If that env var is exported in your shell (e.g. pointing at `mainttrack_dev`), it overrides the tests' in-memory config and the tests will run against—and corrupt/collide with—the dev DB. `unset MAINTTRACK_DB_CONNECTION` before running tests.

### Lint / build

- Frontend has no ESLint/TypeScript tooling configured (Vite/SWC transpiles without type-checking); the build check is `npm run build` (outputs to `frontend/build`).
- Backend build/lint: `dotnet build backend/MaintTrack.sln` (a few nullable-reference warnings are expected and pre-existing).

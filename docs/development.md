# Development guide

Prerequisites: [.NET SDK 10](https://dotnet.microsoft.com/download) (pinned in `global.json`), Node.js ≥ 24.15 (Angular CLI requirement), Docker.

## Repository layout

```
src/Roomy.Api/        ASP.NET Core API — feature folders (vertical slices), see technical design §2
  Common/Tenancy/     Tenant resolution middleware, ITenantContext, slug rules
  Common/Persistence/ RoomyDbContext (EF Core + Npgsql)
  Tenants/            First feature slice
tests/Roomy.Api.Tests/  xUnit tests, mirrors the feature folder structure
frontend/             Angular workspace — three apps under projects/
  projects/member/    Booking experience for Members
  projects/admin/     Tenant Admin + Facility Manager console
  projects/kiosk/     Room door display
docs/                 PRD and technical design
```

## Run the whole stack in Docker

```bash
docker compose up --build
```

Brings up PostgreSQL, the API, and nginx serving all three Angular apps:

- http://localhost:8081/ — member app (admin at `/admin/`, kiosk at `/kiosk/`)
- http://localhost:8081/api/... and `/swagger` — proxied to the API container
- http://localhost:5023 — API exposed directly
- `localhost:5432` — PostgreSQL

For day-to-day development you'll usually want only the database in Docker (faster feedback via `dotnet run` / `ng serve`):

```bash
docker compose up -d postgres
```

## Backend

```bash
docker compose up -d postgres   # local PostgreSQL 16 (roomy/roomy_dev, db "roomy")
dotnet build Roomy.sln
dotnet test Roomy.sln
dotnet run --project src/Roomy.Api
```

The API listens per `src/Roomy.Api/Properties/launchSettings.json`; Swagger UI is at `/swagger` in Development. Health endpoints: `/healthz` (liveness), `/readyz` (DB reachability).

In Development the API migrates the database and seeds a demo tenant on startup:

| | |
|---|---|
| Tenant slug | `demo` |
| Tenant Admin | `admin@demo.test` / `RoomyDemo123!` |
| Member | `member@demo.test` / `RoomyDemo123!` |

Tenant resolution (technical design §3): requests to `/api/*` (except `/api/v1/platform/*`) must carry a tenant — subdomain in deployed environments, or the `X-Roomy-Tenant: <slug>` header during local development.

Auth: `POST /api/v1/auth/login` returns a 15-minute JWT (send as `Authorization: Bearer …`) and sets a rotating refresh-token cookie scoped to `/api/v1/auth`. Platform endpoints (`/api/v1/platform/*`) use the interim `X-Platform-Key` header instead — the key is `Platform:ApiKey` in configuration (`dev-platform-key` in Development).

Example — provision a tenant and log in:

```bash
curl -X POST localhost:5023/api/v1/platform/tenants \
  -H "Content-Type: application/json" -H "X-Platform-Key: dev-platform-key" \
  -d '{"name":"Acme","slug":"acme","adminEmail":"admin@acme.test","adminName":"Admin","adminPassword":"ChangeMe-12345"}'

curl -X POST localhost:5023/api/v1/auth/login \
  -H "Content-Type: application/json" -H "X-Roomy-Tenant: acme" \
  -d '{"email":"admin@acme.test","password":"ChangeMe-12345"}'
```

EF Core migrations:

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Name> --project src/Roomy.Api
dotnet ef database update --project src/Roomy.Api
```

**Convention (technical design §4):** every new tenant-owned table gets a `tenant_id` column, an EF global query filter, and a row-level-security policy added in its migration. CI will grow a schema test that fails if a table is missing RLS.

## Frontend

```bash
cd frontend
npm ci
npm start -- member        # or admin / kiosk; ng serve <app> works too
npm run build:all
npm run test:ci            # Vitest, headless
```

`ng serve` proxies `/api/*` to `http://localhost:5023` (see `frontend/proxy.conf.json`), so run the API alongside it. Sign in on the member app with the seeded credentials above (organization `demo`).

## CI

`.github/workflows/ci.yml` runs backend build + tests and frontend build + tests on every PR.

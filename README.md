# AMS Backend — Amanat Management System API

.NET 10 Web API for a small collateral-lending business: money is lent
against an item (gold chain, phone, vehicle papers…), top-up lends fold
under the same collateral group, partial returns come back early, and a
release closes the group when the money comes back. Every rupee moving
over the counter is also recorded in a **CashLedger** table, and admin
users get an **activity trail** of every change.

Stack: ASP.NET Core Web API (.NET 10) · EF Core 10 · SQL Server · JWT auth.

- Layers: `Controllers → Services → Repositories` (see Structure below)
- Source of truth for scope/data model/API: `../notes/requirements-plan.md`
- Frontend: `../lenders-crm-ui/frontend` (dev server on http://localhost:5173)

---

## Run the backend

```powershell
cd "D:\saish\Lenders CRM\lenders-crm-backend"
dotnet run --project Api
```

(or `dotnet run` from inside `Api/` — the `http` launch profile in
`Api/Properties/launchSettings.json` sets everything.)

- **Base URL:** http://localhost:5246
- **Swagger UI:** http://localhost:5246/swagger (Development only)
- CORS is already open for the Vite dev server (5173) and its preview
  port (4173) — start the frontend and sign in.

### One-time setup (fresh machine)

1. **.NET 10 SDK** — `dotnet --version` should report 10.x.
2. **SQL Server Express** — the default connection string targets the
   instance visible in SSMS on this machine (`HP_WORKSTATION\SQLEXPRESS`,
   database `AmsDb`). Change it in `Api/appsettings.json` under
   `ConnectionStrings:Default`; nothing else moves.
3. **Create/migrate the database** (run from `Api/`; migrations live in
   the `Data` project, the EF design package sits in `Api`):

   ```powershell
   dotnet ef database update --project ..\Data --startup-project .
   ```

   The migrations include the **admin seed** (`Data/AdminSeed.cs`), so the
   first login works right away:

   - **admin@lenders.in** / **admin123** (Admin role)

4. **Optional demo data:** `seed-year-data.sql` replaces all business data
   with a deterministic year of counter activity (1,000 customers, ~1,220
   lends, released collaterals, partials, ~2,000 cash-ledger rows). Run it
   in SSMS / `sqlcmd` — it wipes and reseeds business tables; users survive.
5. **NuGet:** a repo-local `nuget.config` sits at the backend root — keep
   commands running from inside `lenders-crm-backend\` (or its subfolders)
   so restore finds it; a machine-level feed elsewhere can 401 on restore.

### Ports

| What      | Port | Where                      |
| --------- | ---- | -------------------------- |
| API       | 5246 | `Api/launchSettings.json`  |
| Frontend  | 5173 | Vite dev server            |
| Database  | —    | `AmsDb` on SQLEXPRESS      |

---

## Structure

```
Api/
  Controllers/     thin HTTP layer — routing + authorization only;
                   they never touch the DbContext and never try/catch
  Services/        ALL business rules (interest, FIFO allocation,
                   release, insights, reports); rule violations throw
                   ApiException (400/404/409/401) and the
                   ApiExceptionHandler renders { message } to the SPA
  Repositories/    EF Core data access — the ONLY layer that touches
                   AmsDbContext
  Contracts.cs     request/response records (DTOs) for every endpoint
  Program.cs       DI wiring, JWT, CORS, Swagger, exception handler
Data/
  Entities/        User, Customer, Entry, PartialReturn, CashLedger,
                   ActivityLog
  AppDbContext.cs  model + indexes
  AdminSeed.cs     seed admin (hash pinned static — dynamic BCrypt in
                   HasData breaks `dotnet ef database update`)
  Migrations/      EF migrations
```

**Rules the layers follow** (from `../CLAUDE.md` and the plan):

- A controller never touches the DbContext; a repository never contains
  business rules. New feature → controller method + service method
  (+ repository method only for new data access).
- Routes and response shapes must match `notes/requirements-plan.md` §6 —
  the frontend's `src/api/client.ts` was written against them; if scope
  changes, update the plan in the same change.
- Money is `decimal` in C#, `DECIMAL(12,2)` in SQL — never float/double.
- Entry/lend dates are DATE (no time-of-day); audit timestamps are UTC.
- Customer outstanding balance is always SUM-computed, never stored.
- Release is one transactional endpoint —
  `POST /api/groups/{id}/release` — which 409s when the group is not
  Active.
- Don't compute money figures in the UI: every headline number
  (insights tiles, reports, cash total) is backend-computed by design.

---

## Auth

- **JWT Bearer.** `POST /api/auth/login` → `{ token, user }`; the token
  must ride as `Authorization: Bearer …` on every other endpoint.
- **Roles:** Admin, Lender, ViewOnly.
  - Every write endpoint is `[Authorize(Policy = "Writer")]`
    (Admin or Lender) — ViewOnly users browse but cannot lend, edit,
    record returns, release or move cash.
  - `/api/users*` additionally requires **Admin**.

---

## API surface

| Area | Endpoint(s) |
| ---- | ----------- |
| Auth | `POST /api/auth/login` |
| Customers | `GET /api/customers` · `POST /api/customers` · `POST /api/customers/find-or-create` · `PUT /api/customers/{id}` |
| Entries (lends + sub-lends) | `GET /api/entries` · `POST /api/entries` · `PUT /api/entries/{id}` |
| Partial returns | `GET /api/payments` · `PUT /api/payments/{id}` · `POST /api/groups/{id}/payments` |
| Collateral groups | `GET /api/groups/{id}` (whole trail) · `PUT /api/groups/{id}/customer` · `POST /api/groups/{id}/release` |
| Cash ledger | `GET /api/cash-ledger?from=&to=` (DateOnly, inclusive) · `GET /api/cash-ledger/total` (whole-book SUM) · `POST /api/cash-ledger` (manual ±) · `DELETE /api/cash-ledger/{id}` (manual rows only) |
| Insights (dashboard tiles) | `GET /api/insights?tab=all\|active\|released` |
| Reports | `GET /api/reports/summary?from=&to=` · `…/customers?from=&to=` · `…/releases?from=&to=` |
| Global search | `GET /api/search?q=` |
| Activity trail (Admin) | `GET /api/activity?userId=&groupId=` |
| Users (Admin) | `GET /api/users` · `POST /api/users` · `PUT /api/users/{id}` |

`from`/`to` windows are inclusive ISO dates; empty = all time.

---

## CashLedger semantics (the "Total Cash Available" book)

- A lend books **one negative** row on the day money goes out.
- A release books **two** rows on release day (principal + interest).
- A partial return books one positive row.
- A manual movement (outside the business) books one signed row — the
  only kind that can be deleted.
- `SUM(Amount)` **is** Total Cash Available and can go negative; the
  dashboard's "in hand", the cash page's total and the insights tiles
  all read this table.

## Deployment

`deployment/database/` holds a plain-SQL mirror of the schema
(`01-create-tables.sql`, `02-create-indexes.sql`) for servers without the
EF tooling — keep it in sync when migrations change.

`Data.Pg/` is an unused leftover of an abandoned cloud-Postgres attempt;
**SQL Server is the only supported database.**

### Deploy the API to Render (free tier) with a hosted SQL Server

The API is stateless — everything lives in SQL Server — so it runs fine on
Render's free web service. It deploys as a **Docker** service from the
`Dockerfile` at the backend root. Requirements:

1. **The database must be reachable from the internet** with SQL auth
   (User Id/Password — `Trusted_Connection` cannot work from the cloud).
   A local SQLEXPRESS instance does NOT qualify; use Azure SQL Database or
   any hosted SQL Server that allows remote connections. If the provider
   IP-filters, add Render's egress IPs to its allowlist.
2. **The code must be on GitHub/GitLab** (Render builds from a git repo).

Steps:

1. **Create the schema** on the hosted SQL Server — either run the EF
   migrations against it from this machine (temporarily overriding the
   connection string; the admin seed rides along):

   ```powershell
   $env:ConnectionStrings__Default = "Server=<host>,<port>;Database=AmsDb;User Id=<user>;Password=<pw>;Encrypt=True;TrustServerCertificate=True"
   dotnet ef database update --project ..\Data --startup-project .
   ```

   …or run `deployment/database/01-create-tables.sql` +
   `02-create-indexes.sql` if the server has no EF tooling — but prefer the
   **migration route**: the admin seed (`Data/AdminSeed.cs`) is a HasData
   seed and only migrations insert it, so a table-only setup ships without
   a working login.

2. **Push to GitHub**, then on Render: New → Web Service → pick the repo →
   Runtime **Docker**, Root Directory `lenders-crm-backend`, Instance type
   Free.

3. **Environment variables** (Render dashboard → Environment):

   | Key | Value |
   | --- | ----- |
   | `ConnectionStrings__Default` | the hosted SQL Server connection string (SQL auth; keep `Encrypt=True;TrustServerCertificate=True`) |
   | `Jwt__Key` | a long random secret — never the dev key in appsettings |
   | `Cors__AllowedOrigins` | the frontend's deployed origin(s), comma-separated, no trailing slash (e.g. `https://ams-crm-demo.vercel.app`) |
   | `ASPNETCORE_HTTP_PORTS` | `10000` (Render's Docker port) |
   | `ASPNETCORE_ENVIRONMENT` | `Production` (Swagger off, detailed errors off) |

   .NET maps `Section__Key` env vars onto appsettings automatically, so
   no code change is needed per deploy.

4. The frontend then points its API at the Render URL
   (`VITE_API_BASE`) instead of mock mode.

Free-tier caveats: the service **sleeps after 15 idle minutes** and the
first request after that waits ~30–60 s for the startup; 512 MB RAM is
plenty for this API. Swagger is Development-only, so verify a deployed
deploy with a real login through the frontend or `curl -X POST …/api/auth/login`.

## Gotchas

- After any entity/migration change: `dotnet ef database update`, then
  mirror the schema change into `deployment/database/*.sql`.
- `UpdateEntryRequest.Rate` travels as a **string** over the wire
  (`""` clears the interest rate) — don't "clean it up" to a number.
- Sub-lends need an explicit `groupId`; the first lend of a new
  collateral must NOT pass one (it creates the group).
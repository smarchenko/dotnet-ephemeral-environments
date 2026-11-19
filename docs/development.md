# Development

How to change the code and see the change running: workflow, database (model, migrations, demo data), running the apps from the IDE, tests and code quality. The local environment itself (scripts, demo data, checks): [local-environment.md](local-environment.md). Design: [architecture.md](architecture.md).

## Development workflow

There is nothing to publish: no registry, no `dotnet publish` by hand. Every image is built by Docker **from the
source files in the repo** (`docker compose up --build`, which `up.ps1` runs). `dotnet build` on the host does not
change the containers: they never use your `bin/` folders. A code change reaches the containers only when you run
`up.ps1` (or `db-update.ps1` for the `migrator` image) again.

Two ways to work, pick one per session:

**A. Everything in Docker** (closest to a preview environment; no debugger):

```powershell
./deploy/local/up.ps1                  # first time: builds everything (a few minutes)
# ... change code ...
./deploy/local/up.ps1                  # compiles only what changed (layer cache), restarts the containers
```

Each Dockerfile copies only the projects its app builds from, so Docker compiles only the images whose sources
changed (everything else comes from the layer cache in seconds):

| Changed project | Rebuilt images |
|---|---|
| Web | `web` |
| Api, Application | `api` |
| Contracts | `api`, `web` |
| Domain, Data | `api`, `migrator` |
| DbTool (seed sets) | `migrator` |
| ServiceDefaults, `Directory.*.props` | all |

Compose still recreates every container on each `up` (the image ids change on every build even when nothing was
compiled), so the apps restart for a few seconds. The database volume survives; only new migrations and new seed sets
are applied.

**B. Apps from the IDE, database in Docker** (debugger, `dotnet watch` hot reload):

```powershell
./deploy/local/down.ps1                # if A was running: frees 5080/5081 (the Web's dev port is 5080 too)
./deploy/local/db-update.ps1           # starts only postgres (localhost:5432), applies migrations and seed
dotnet run --project src/EphemeralEnvs.Api        # terminal 1 → http://localhost:5136  (or F5 in VS / Rider)
dotnet run --project src/EphemeralEnvs.Web        # terminal 2 → http://localhost:5080
# dotnet watch --project src/EphemeralEnvs.Web    # instead of run: applies code changes without a restart
```

The `Development` settings point the API at `localhost:5432`. If Postgres runs on another port
(`db-update.ps1 -DbPort 5433`), set `$env:ConnectionStrings__Default` before `dotnet run` (see
[Run the API locally](#run-the-api-locally)).

What to run after a change:

| You changed | A (Docker) | B (IDE) |
|---|---|---|
| Web / API / Application code | `up.ps1` | restart the app (or nothing with `dotnet watch`) |
| Domain model → new migration (see [Change the model](#change-the-model-new-entity-column-index)) | `up.ps1` | `db-update.ps1`, then restart the API |
| New seed set (see [Add demo data](#add-demo-data)) | `up.ps1` | `db-update.ps1` |
| Data messed up while testing | `db-reset.ps1` (or `db-update.ps1 -Restore`) | same |
| Anything | `dotnet test` before you commit | same |

## Build

```powershell
dotnet tool restore
dotnet build
```

## Database

PostgreSQL, accessed through EF Core (`src/EphemeralEnvs.Data`). The schema is defined by **EF Core migrations** in
`src/EphemeralEnvs.Data/Migrations`, the demo data by **seed sets** in `src/EphemeralEnvs.DbTool/Seeding/Sets`.

**The applications never migrate or seed on startup** (not in `Development` either; no `EnsureCreated`). Both are
separate steps from the `migrator` image, the same locally (compose) and later in the deploy pipelines:

| Step | Command in the image | Configuration |
|---|---|---|
| migrate | `./efbundle --connection "<connection string>"` | — |
| seed | `dotnet EphemeralEnvs.DbTool.dll seed [--restore]` | `ConnectionStrings__Default`, `App__EnvironmentName` |

The compose connection string has `GSS Encryption Mode=Disable`: there is no Kerberos here, and without it Npgsql in the
Linux images logs `Cannot load library libgssapi_krb5.so.2`.

### Change the model (new entity, column, index…)

Entities live in `EphemeralEnvs.Domain`, their mapping in `EphemeralEnvs.Data/Configurations`. The database never
builds itself from the model on startup, but you do not need a migration to see the tables either:

1. **While the model is still changing: look at the SQL, no database and no migration needed.**

   ```powershell
   dotnet tool restore                                        # once
   dotnet ef dbcontext script --project src/EphemeralEnvs.Data
   # CREATE TABLE products (id uuid NOT NULL, name character varying(200) NOT NULL, ...);
   ```

   It prints `CREATE TABLE` for the whole current model: the same tables, columns, types, keys and indexes
   `EnsureCreated` would make. Change the model, run it again, repeat.

2. **When the schema looks right: create the migration and apply it.**

   ```powershell
   dotnet ef migrations add AddCustomers --project src/EphemeralEnvs.Data
   ./deploy/local/db-update.ps1          # rebuilds the migrator image and applies the new migration
   ```

   Check the tables in the real database (any SQL client on `localhost:<DbPort>`, `ephemeral_envs` / `ephemeral_envs`)
   and read the generated migration: this is what previews and production will run. To see its SQL:
   `dotnet ef migrations script --project src/EphemeralEnvs.Data` (all migrations; add `<From> <To>` for a range).

3. **Something is wrong after it was applied: undo it, fix the model, go back to step 2.**

   ```powershell
   dotnet ef migrations remove --force --project src/EphemeralEnvs.Data   # reverts it in the local database, deletes its files
   ```

   Rework a migration as often as you like **until it is committed**; after that, a fix is a new migration.
   `dotnet ef` talks to `Host=localhost;Port=5432;...`; for another port set
   `$env:EE_DB = 'Host=localhost;Port=5433;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs'`.
   Instead of `--force` you can also delete the files with a plain `migrations remove` and run `db-reset.ps1`.

Why check the migration and not a database created from the model: the migration is what ships. It can differ from the
model's intent, e.g. a renamed property may come out as "drop column + add column", which loses the data. Only a
migration applied to a database with data (step 2) shows that.

Forgot the migration? `dotnet test` fails (`MigrationTests.Model_HasNoChangesWithoutMigration` in the architecture
tests, no Docker needed) when the model has changes that no migration contains. The same check by hand:

```powershell
dotnet ef migrations has-pending-model-changes --project src/EphemeralEnvs.Data
# No changes have been made to the model since the last migration.
```

Migration files are generated code: style rules do not apply to `Migrations/` (see `.editorconfig`).
`dotnet ef database update --project src/EphemeralEnvs.Data` also works from the host (same `EE_DB` rule), but the
usual path is `db-update.ps1`.

### Add demo data

Add a new class to `src/EphemeralEnvs.DbTool/Seeding/Sets/`. For example, once there is a `Customer` entity:

```csharp
internal sealed class Seed0002Customers : ISeedSet
{
    public string Name => "0002-customers";              // unique; sets run in ordinal order of their names

    public IEnumerable<object> Build() =>
    [
        Customer.CreateSeeded(Guid.Parse("0196a000-0000-7000-8000-000000000101"), "Acme"),   // fixed ids
    ];
}
```

- Sets are found by reflection: nothing to register. Objects are built **through the domain** (its invariants apply),
  so a new entity type needs no change in the runner, only a factory with an explicit id (like `Product.CreateSeeded`).
- **Never edit or rename a set that was applied somewhere**: it will not run there again (journal). New or changed demo
  data goes into a new set.
- A set is applied in one transaction. An object that is not an entity, or two objects with the same key, fail the
  set before anything is written (exit 1).
- `./deploy/local/db-update.ps1` applies the new set to your database.

Run the seed from the host (e.g. under the debugger) against the compose Postgres:

```powershell
$env:ConnectionStrings__Default = 'Host=localhost;Port=5432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs'
$env:App__EnvironmentName = 'local'
dotnet run --project src/EphemeralEnvs.DbTool -- seed            # or: seed --restore
```

## Run the API locally

Needs a PostgreSQL with the migrations applied, e.g. the one from `./deploy/local/up.ps1` (see [Database](#database)). In `Development` the API uses
`appsettings.Development.json` → `Host=localhost;Port=5432;Database=ephemeral_envs;...`; override it with the
env var `ConnectionStrings__Default`.

```powershell
dotnet run --project src/EphemeralEnvs.Api        # http://localhost:5136
```

Endpoints (errors are returned as ProblemDetails, `application/problem+json`):

| Request | Result |
|---|---|
| `GET /products` | all products, newest first |
| `GET /products/{id}` | 200, or 404 if not found / not a GUID |
| `POST /products` `{"name":"Pen","price":1.50}` | 201 + `Location: /products/{id}`; 400 if `name` is empty/whitespace/longer than 200 or `price` is not in 0.01 … 9999999999999999.99 or has more than 2 decimal places (`1.005` → 400, `1.500` is fine) |

| `GET /health/live` | 200 `Healthy` while the process answers; never checks the DB (a restart would not fix a DB outage) |
| `GET /health/ready` | 200 `Healthy` when the DB is reachable, 503 `Unhealthy` otherwise (deploy waits for it) |
| `GET /swagger` | Swagger UI, only when `Swagger__Enabled=true` (otherwise 404); document at `/openapi/v1.json`, its description shows `Environment: <App__EnvironmentName>` |

```powershell
Invoke-RestMethod -Method Post http://localhost:5136/products -ContentType 'application/json' -Body '{"name":"Pen","price":1.50}'
Invoke-RestMethod http://localhost:5136/products
Invoke-RestMethod http://localhost:5136/health/ready
Start-Process http://localhost:5136/swagger
```

API configuration (env vars; `__` = section separator):

| Key | Default (`appsettings.json`) | `Development` | Meaning |
|---|---|---|---|
| `ConnectionStrings__Default` | — | `Host=localhost;Port=5432;Database=ephemeral_envs;…` | PostgreSQL |
| `Cors__AllowedOrigins` | empty (no cross-origin calls) | `http://localhost:5080` | comma-separated origins of the Web; `*` is rejected at startup |
| `Swagger__Enabled` | `false` | `true` | a flag, not tied to the ASP.NET Core environment: a preview env runs as `Production` and can still show Swagger |
| `App__EnvironmentName` | `unknown` | `local` | shown in the OpenAPI description (a preview env will use its branch slug) |

**Logs** (set up once for every service in `EphemeralEnvs.ServiceDefaults`) go to stdout as JSON, one object per line, with `TraceId`/`SpanId`/`ParentId` of the request (an incoming
W3C `traceparent` header is continued). EF Core SQL commands are logged only in `Development` (in other environments
`/health/ready` would log `SELECT 1` on every probe).

## Run the Web locally

Needs the API running (see above). In `Development` the Web listens on `http://localhost:5080` (the origin the API's
`Development` CORS allows) and calls the API at `http://localhost:5136`. Both projects have only an `http` launch
profile (Visual Studio / Rider use it too): TLS is terminated by the reverse proxy in deployed environments, and a Web
served from another origin (e.g. `https://localhost:7004`) would be refused by CORS, so the widget would show
"API is unavailable".

```powershell
dotnet run --project src/EphemeralEnvs.Api        # terminal 1
dotnet run --project src/EphemeralEnvs.Web        # terminal 2 → http://localhost:5080
```

| Page | What it shows |
|---|---|
| `/` (Server-side) | product list fetched **by the server** through a typed `HttpClient` (`Api__InternalBaseUrl`, timeout `Api__TimeoutSeconds`, 5 s). API down → the page still renders with "API is unavailable right now." and a warning in the log |
| `/Home/Widget` | the same list plus an "add product" form, rendered by `wwwroot/js/widget.js`, which calls the API **from the browser** (`Api__PublicBaseUrl`, CORS). Validation errors (ProblemDetails `title` + every field error) are shown inline; API down → "API is unavailable" |

The footer of every page shows `Environment: <App__EnvironmentName>`.

Web configuration (env vars):

| Key | Default (`appsettings.json`) | `Development` | Compose | Meaning |
|---|---|---|---|---|
| `Api__InternalBaseUrl` | — (required) | `http://localhost:5136` | `http://api:8080` | API address for server-side calls (inside the Docker network) |
| `Api__PublicBaseUrl` | — (required) | `http://localhost:5136` | `http://localhost:5081` | API address for the browser; written at runtime into `<meta name="api-base-url">`, so the image has no build-time config |
| `Api__TimeoutSeconds` | `5` | `5` | `5` | timeout of a server-side API call; short on purpose (the page waits for it); raise it for a slow environment |
| `App__EnvironmentName` | `unknown` | `local` | `local` | shown in the footer |

Both `Api__*` URLs must be absolute and the timeout positive; otherwise the Web stops at startup (`ValidateOnStart`),
not on the first request. Logs are the same JSON format as the API's (both call `builder.AddServiceDefaults()`; an architecture test
checks that every service does).

## Tests

Five test projects; `dotnet test` runs all of them:

- `EphemeralEnvs.Domain.Tests` and `EphemeralEnvs.ArchitectureTests`: fast, no Docker. The architecture tests also fail
when the EF model was changed without a migration.
- `tests/EphemeralEnvs.Api.Tests`: API integration tests. The API runs in memory (`WebApplicationFactory`)
against a real, throwaway PostgreSQL 17 container started by [Testcontainers](https://dotnet.testcontainers.org/)
(one container per test class, migrations applied by the fixture, removed after the run).
- `tests/EphemeralEnvs.DbTool.Tests`: seed runner, seed set catalog and the CLI (exit codes, guard, `--restore`).
One PostgreSQL container for the whole project (xUnit collection fixture), a fresh migrated database per test.
- `tests/EphemeralEnvs.Web.Tests`: the Web in memory (`WebApplicationFactory`), the API replaced by a stub
`HttpMessageHandler` (`WebFactory`); no Docker. The server-side form `/Home/Create`: antiforgery token required,
API call, field errors from the API's ValidationProblemDetails, API unavailable.

The Docker-based tests need Docker running.

```powershell
dotnet test
```

No Docker settings are needed. On Windows the fixtures (`PostgresFixture`, `PostgresDatabaseFixture`) always use Docker Desktop's
**Linux engine** (`npipe://./pipe/dockerDesktopLinuxEngine`) when it exists, even if Docker Desktop is switched
to Windows containers. Elsewhere (Linux CI, macOS) Testcontainers auto-detects Docker; an explicit `DOCKER_HOST`
always wins.

> Do not set `DOCKER_HOST` in your shell for the tests: the Docker CLI expects `npipe:////./pipe/...` (4 slashes),
> Testcontainers expects `npipe://./pipe/...` (2 slashes), so one value breaks the other. Use `DOCKER_CONTEXT`
> for the CLI (see [Prerequisites](../README.md#prerequisites)).

## Code quality

The rules live in `Directory.Build.props` and `.editorconfig` and are enforced **at build time**:

- **Any warning fails the build** (`TreatWarningsAsErrors`): compiler warnings, the .NET analyzers
  (`AnalysisLevel=latest-recommended`) and code-style rules from `.editorconfig` (`EnforceCodeStyleInBuild`).
  Examples: unnecessary `using` (IDE0005), missing braces, block-scoped namespaces, private fields not
  named `_camelCase`.
- **Vulnerable NuGet packages fail restore/build** (`NuGetAuditMode=all`, level `moderate`, including
  transitive packages).
- Package versions are managed centrally in `Directory.Packages.props`; `.csproj` files reference packages
  without a version. Transitive pinning is on, so a `PackageVersion` entry also pins indirect dependencies
  (used to keep all EF Core assemblies on one patch version). MSBuild warnings (e.g. MSB3277 version
  conflicts) also fail the build.
- Line endings are LF everywhere (`.editorconfig` + `.gitattributes`), because CI runs on Linux.

Check formatting the same way CI will:

```powershell
dotnet format --verify-no-changes   # exit code 0 = OK
dotnet format                       # auto-fix
```

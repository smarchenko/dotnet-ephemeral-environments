# dotnet-ephemeral-environments

Branch-based deployment PoC: ephemeral preview environments per pull request for a .NET app.

A proof of concept for **per-branch preview environments**: QA tests a feature directly from its
branch / pull request in its own environment (Web + API + its own PostgreSQL database), instead of
merging into shared dev/test/uat branches. The environment is created when a PR is opened, updated on
every push and destroyed when the PR is closed.

The PoC is built in small stages: see the design spec
[`docs/superpowers/specs/2026-11-11-branch-deployment-design.md`](docs/superpowers/specs/2026-11-11-branch-deployment-design.md)
and the current progress in [`docs/STATUS.md`](docs/STATUS.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`, newer 10.0.x feature bands roll forward)
- Docker Desktop with the **Linux engine**. If you also use Windows containers (e.g. for Sitecore), you do
  not need to switch modes: point the CLI at the Linux engine in the current shell:

  ```powershell
  $env:DOCKER_CONTEXT = 'desktop-linux'
  docker info --format '{{.OSType}}'   # must print: linux
  ```

- Local .NET tools (`dotnet-ef`): `dotnet tool restore`

## Repository layout

| Path | What |
|---|---|
| `src/EphemeralEnvs.Web` | ASP.NET Core MVC front end |
| `src/EphemeralEnvs.Api` | ASP.NET Core Web API |
| `src/EphemeralEnvs.Application` | CQRS: commands, queries and their handlers; read side `IReadDb` / `ReadDbContext` |
| `src/EphemeralEnvs.Domain` | Domain entities (no dependencies) |
| `src/EphemeralEnvs.Data` | EF Core `DbContext`, configurations and migrations (PostgreSQL) |
| `src/EphemeralEnvs.Contracts` | DTOs shared by Web and API |
| `tests/EphemeralEnvs.Api.Tests` | API integration tests (in-memory API + PostgreSQL in Docker) |
| `tests/EphemeralEnvs.Domain.Tests` | Domain unit tests (invariants), no Docker |
| `tests/EphemeralEnvs.ArchitectureTests` | Architecture rules (layers, CQRS, domain), no Docker |
| `docs/` | Status, spec, plans |

### Architecture: CQRS (one database, separate read and write models)

```
Api (controllers: HTTP only) ──► Application ──► write: Command handler → Domain → AppDbContext (tracking)  ─┐
                                              └─► read:  Query handler   → IReadDb (read-only)   → DTO ─┴─► PostgreSQL
```

- The API project references only `Application`, `Contracts` and `Domain` (the latter only to map
  `DomainValidationException` to 400). It cannot see a `DbContext`: transitive project references are disabled
  (`DisableTransitiveProjectReferences`), and the architecture tests check it.
- A command returns only the new id; the API reads the result back through a query.
- Queries project straight into DTOs (`.Select(p => new ProductDto(...))`): `Product` exists only inside the
  expression tree and is never materialized on the read side. This is the common .NET style (Clean Architecture
  template, Vertical Slice); a separate read model without the domain (e.g. Dapper SQL) is left for the optional C3 stage.
- The read side cannot write, at three levels (each covered by tests):

  | Level | How | Stops |
  |---|---|---|
  | Compile time | Query handlers get `IReadDb` (only `IQueryable` sets), not the `DbContext` (architecture test) | `Add`/`Remove`/`SaveChanges`/`Database` |
  | Context | `ReadDbContext`: `SaveChanges` throws, `NoTracking` for the whole context | saves through the context itself |
  | Database | Read connection opens a read-only session (`Options=-c default_transaction_read_only=on`) | everything else: `ExecuteDelete`/`ExecuteUpdate`, raw SQL (Postgres error `25006`) |

  `ReadDbContext` maps the same tables; migrations belong to `AppDbContext` in `EphemeralEnvs.Data`. The strongest
  option, a separate Postgres role with `SELECT` only, belongs to the deployment stages.
- No MediatR: plain `ICommandHandler<,>` / `IQueryHandler<,>` interfaces, handlers registered in
  `AddApplication()`.

**Naming convention** (checked by architecture tests):

| What | Name | Project | Example |
|---|---|---|---|
| API input (request body) | `<Action><Entity>Request` | Contracts | `CreateProductRequest` |
| API output (response body) | `<Entity>Dto` | Contracts | `ProductDto` |
| CQRS command / query | `<Action><Entity>Command` / `Get…Query` | Application | `CreateProductCommand`, `GetProductByIdQuery` |

The controller maps `Request` → `Command`/`Query`, so the HTTP contract and the command can change independently.
Validation attributes go only on `*Request` types: MVC validates only what it binds from the request, and output
data already passed the domain.

**Validation in two places:**

| Where | What | Why |
|---|---|---|
| API: attributes on `CreateProductRequest` (`[ProductName]`, `[Price]`) | input format | friendly 400 with all `errors` per field at once, limits in the OpenAPI schema |
| Domain: `Product` constructor | invariants (name not blank, ≤ 200; price 0.01 … 9999999999999999.99, at most 2 decimal places) | an invalid `Product` cannot exist, whoever sends the command |

A violated invariant throws `DomainValidationException(field)`; the API turns it into the same 400
ValidationProblemDetails. The limits are constants in `Product` (also used by the EF mapping) — the single source
of truth. `Contracts` cannot reference `Domain`, so the semantic attributes `[ProductName]` (required, ≤ 200) and
`[Price]` (range + ≤ 2 decimals) mirror them once; every request contract uses these attributes instead of repeating
the numbers. Contract tests guard this:
- the attributes' limits equal the `Product` constants;
- every `string` in a `*Request` has a length limit and every `decimal` has a range (a new `RenameProductRequest`
  with a bare `string NewName` fails the build's tests, whatever the property is called);
- controller actions take only `*Request` types from Contracts (so a new input contract can't dodge the rule above)
  and return only `*Dto` types (unwrapping `Task<>`, `ActionResult<>`, arrays and other generics).

**Architecture tests** (`tests/EphemeralEnvs.ArchitectureTests`, [NetArchTest](https://github.com/NeVeSpl/NetArchTest.eNhancedEdition)):
layer dependencies (Api ↛ Data/EF Core, Domain and Contracts depend on nothing, Data ↛ upper layers,
Application ↛ ASP.NET Core, Web → only Contracts); controllers don't use domain types and are sealed; CQRS (query
handlers ↛ `AppDbContext` and use `IReadDb` instead of `ReadDbContext`, command handlers ↛ read side, handler results are never domain types, handlers internal + sealed); domain entities have no
public setters; request contracts are bounded and in sync with the domain (see above). A failure lists the offending types.

## Build

```powershell
dotnet tool restore
dotnet build
```

## Database

PostgreSQL, accessed through EF Core (`src/EphemeralEnvs.Data`). The schema is defined by **EF Core migrations** in
`src/EphemeralEnvs.Data/Migrations`.

**The applications never run migrations on startup.** In stage 0 the schema is applied by hand; from
stage 1 on it is a separate deploy step (`migrator` container), also used by local `docker compose up`.

Apply all migrations to a database:

```powershell
dotnet tool restore
# Default target: Host=localhost;Port=5432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs
dotnet ef database update --project src/EphemeralEnvs.Data

# Another database / port: set EE_DB
$env:EE_DB = 'Host=localhost;Port=55432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs'
dotnet ef database update --project src/EphemeralEnvs.Data
```

Add a new migration after changing the model (entities in `EphemeralEnvs.Domain`, mapping in
`EphemeralEnvs.Data/Configurations`):

```powershell
dotnet ef migrations add <Name> --project src/EphemeralEnvs.Data
```

Migration files are generated code: style rules do not apply to `Migrations/` (see `.editorconfig`).

## Run the API locally

Needs a PostgreSQL with the migrations applied (see [Database](#database)). In `Development` the API uses
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

```powershell
Invoke-RestMethod -Method Post http://localhost:5136/products -ContentType 'application/json' -Body '{"name":"Pen","price":1.50}'
Invoke-RestMethod http://localhost:5136/products
```

## Tests

Three test projects; `dotnet test` runs all of them:

- `EphemeralEnvs.Domain.Tests` and `EphemeralEnvs.ArchitectureTests`: fast, no Docker.
- `tests/EphemeralEnvs.Api.Tests`: API integration tests. The API runs in memory (`WebApplicationFactory`)
against a real, throwaway PostgreSQL 17 container started by [Testcontainers](https://dotnet.testcontainers.org/)
(one container per test class, migrations applied by the fixture, removed after the run). Docker must be running.

```powershell
dotnet test
```

No Docker settings are needed. On Windows the fixture (`PostgresFixture`) always uses Docker Desktop's
**Linux engine** (`npipe://./pipe/dockerDesktopLinuxEngine`) when it exists, even if Docker Desktop is switched
to Windows containers. Elsewhere (Linux CI, macOS) Testcontainers auto-detects Docker; an explicit `DOCKER_HOST`
always wins.

> Do not set `DOCKER_HOST` in your shell for the tests: the Docker CLI expects `npipe:////./pipe/...` (4 slashes),
> Testcontainers expects `npipe://./pipe/...` (2 slashes), so one value breaks the other. Use `DOCKER_CONTEXT`
> for the CLI (see [Prerequisites](#prerequisites)).

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

# Architecture

Projects, layers and the rules the architecture tests enforce. The overall design and stage roadmap: [design spec](superpowers/specs/2026-11-11-branch-deployment-design.md).

## Repository layout

| Path | What |
|---|---|
| `src/EphemeralEnvs.Web` | ASP.NET Core MVC front end |
| `src/EphemeralEnvs.Api` | ASP.NET Core Web API |
| `src/EphemeralEnvs.Application` | CQRS: commands, queries and their handlers; read side `IReadDb` / `ReadDbContext` |
| `src/EphemeralEnvs.Domain` | Domain entities (no dependencies) |
| `src/EphemeralEnvs.Data` | EF Core `DbContext`, configurations and migrations (PostgreSQL) |
| `src/EphemeralEnvs.Contracts` | DTOs shared by Web and API |
| `src/EphemeralEnvs.DbTool` | Console run as the `seed` step (`seed [--restore]`), seed sets in `Seeding/Sets/`; its Dockerfile builds the `migrator` image (with `efbundle`) |
| `src/EphemeralEnvs.ServiceDefaults` | Setup shared by every service (`builder.AddServiceDefaults()`): JSON logs now, OpenTelemetry in stage O1 |
| `tests/EphemeralEnvs.Api.Tests` | API integration tests (in-memory API + PostgreSQL in Docker) |
| `tests/EphemeralEnvs.DbTool.Tests` | Seed runner, seed set catalog and CLI tests (PostgreSQL in Docker) |
| `tests/EphemeralEnvs.Domain.Tests` | Domain unit tests (invariants), no Docker |
| `tests/EphemeralEnvs.ArchitectureTests` | Architecture rules (layers, CQRS, domain, contracts), no Docker |
| `tests/EphemeralEnvs.Web.Tests` | Web tests (in-memory Web, API replaced by a stub), no Docker |
| `tests/deploy/` | Pester tests of the deploy module, no Docker |
| `src/*/Dockerfile` | Images of Api, Web and `migrator` (DbTool) (build context = repo root) |
| `deploy/local/` | Local environment: `docker-compose.yml` (postgres → migrate → seed → api → web), `up.ps1`, `down.ps1`, `db-update.ps1`, `db-reset.ps1` (shared helpers in `common.ps1`) |
| `deploy/*.ps1`, `deploy/lib/`, `deploy/platform/`, `deploy/env/` | Several envs on one machine (stage 2): scripts, their module (`EphemeralEnvs.Deploy.psm1`), platform compose (Traefik + shared Postgres), per-env compose. See [environments.md](environments.md) |
| `docs/` | Status, spec, plans |

## CQRS (one database, separate read and write models)

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
- validation attributes sit on properties, not on positional-record constructor parameters (MVC would validate
  those, but the OpenAPI generator ignores them and the schema would lose `maxLength`/`minimum`/`maximum`);
- controller actions take only `*Request` types from Contracts (so a new input contract can't dodge the rule above)
  and return only `*Dto` types (unwrapping `Task<>`, `ActionResult<>`, arrays and other generics).

**Architecture tests** (`tests/EphemeralEnvs.ArchitectureTests`, [NetArchTest](https://github.com/NeVeSpl/NetArchTest.eNhancedEdition)):
layer dependencies (Api ↛ Data/EF Core, Domain and Contracts depend on nothing, Data ↛ upper layers,
Application ↛ ASP.NET Core, Web → only Contracts); controllers don't use domain types and are sealed; CQRS (query
handlers ↛ `AppDbContext` and use `IReadDb` instead of `ReadDbContext`, command handlers ↛ read side, handler results are never domain types, handlers internal + sealed); domain entities have no
public setters; request contracts are bounded and in sync with the domain (see above). A failure lists the offending types.

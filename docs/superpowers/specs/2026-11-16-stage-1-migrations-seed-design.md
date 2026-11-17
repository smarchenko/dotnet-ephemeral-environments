# Stage 1 — Migrations as a separate step + seed

Status: **APPROVED by user 2026-11-16**. Parent spec: `2026-11-11-branch-deployment-design.md` (stage 1 row in §3, §5.6).

## 1. Intent

Stage 0 applies the schema from the host (`dotnet ef database update` inside `deploy/local/up.ps1`).
In a real deploy (and later in every preview env) the schema must be applied by a **deploy step of its
own**, run once before the apps roll out; if it fails, the apps are not rolled out and the old version
keeps running. Right after it, a **seed step** fills a fresh environment with known demo data so QA
(and Bruno in stage 3) can rely on fixed ids such as `GET /products/{id}`.

Local dev uses exactly the same path as a deploy: `docker compose up` = postgres → migrate → seed → api → web.

## 2. Goals / non-goals / anti-goals

Goals
- `migrator` image: EF Core **migration bundle** + our console tool **`EphemeralEnvs.DbTool`** (command `seed`).
- Deterministic, idempotent seed of demo products with **fixed ids**, created **through the domain**.
- Compose runs migrate and seed as one-off services; api waits for them to finish successfully.
- Scripts for running the app from the IDE against the compose database: `db-update.ps1`, `db-reset.ps1`.
- `up.ps1` no longer needs the .NET SDK / `dotnet ef` on the host.

Non-goals (later stages)
- Several environments, env naming, DB-per-env, Traefik → stage 2. Stage 1 only avoids hard-coding
  anything that stage 2 must change (connection string comes from env vars only).
- **Backfill / data migration job** for expand/contract → new stage **4c** (after 4a). `DbTool` is shaped
  so a `backfill` command can be added there (subcommand dispatch, same image), nothing more now.
- CI → stage 5a/6b.

Anti-goals (would make the stage a failure even if it "works")
- Any migrate-or-seed on app startup, `EnsureCreated`, or `UseSeeding/UseAsyncSeeding` hooked into `Migrate()`.
- Seed that overwrites data QA changed by hand on re-run.
- Seed that bypasses domain validation (raw SQL inserts).
- Demo data able to reach production silently.

## 3. Approaches considered

Seed mechanism (decided by the user 2026-11-16):
1. SQL file with fixed UUIDs + `ON CONFLICT DO NOTHING` — simplest, but bypasses domain rules and breaks only at deploy time.
2. **.NET seeder through the domain, separate deploy step (chosen)** — same rules as the API, schema changes break the build, not the deploy; fixed ids via a domain factory.
3. EF Core `UseAsyncSeeding` — official hook, but runs on every migrate incl. production and is not a separate step.
4. Seed via HTTP API — good for per-test data (Bruno, E2E), not for env baseline: no fixed ids, needs a running API/auth, may need seed-only endpoints.

Rule recorded for the project: **reference data needed in every env (incl. prod) goes into migrations;
demo/QA data goes into the non-prod `seed` step.** Products are demo data.

Migrations: EF migration bundle (decided in the parent spec), not a `migrate` command in DbTool.

## 4. Design

### 4.1 Domain
- New factory `Product.CreateSeeded(Guid id, string name, decimal price, DateTimeOffset createdAt)`
  (name: builder's call if a better one fits). Same validation as the constructor; `Guid.Empty` →
  `DomainValidationException(nameof(Id))`. Normal ctor unchanged (still `Guid.CreateVersion7()`).
- Domain unit tests for the factory (valid, empty id, invalid name/price reuse existing rules).

### 4.2 `src/EphemeralEnvs.DbTool` (new console project)
- References **Domain, Data, ServiceDefaults** only (not Application/Contracts/Api/Web). It is a separate
  composition root, like Api; it writes through `AppDbContext` directly because Application commands do
  not accept ids. Architecture tests: DbTool depends on none of Api/Web/Application/Contracts; Api still
  must not depend on Data (unchanged).
- Generic host (`Host.CreateApplicationBuilder`) + `AddServiceDefaults()` → same JSON logs as the services.
- Config: `ConnectionStrings__Default` (required, fail fast with a clear message), `App__EnvironmentName`.
- Command dispatch: first arg = command. `seed` now; unknown/missing command → usage text, exit code 2.
  Shape allows `backfill <name>` in stage 4c.
- `seed`:
  - **Seed sets** (decided by the user 2026-11-16: seed runs **once per database**; **C# classes instead of JSON**,
    decided by the user 2026-11-16 after the question "what happens when customers etc. are added?"):
    `internal interface ISeedSet { string Name { get; } IEnumerable<object> Build(); }`, one class per set in
    `src/EphemeralEnvs.DbTool/Seeding/Sets/` (first: `Seed0001Products`, name `0001-products`, 5 products,
    fixed UUIDs as public constants, fixed `createdAt`). `Build()` creates entities **through the domain**
    (`Product.CreateSeeded`, later `Customer.CreateSeeded`, …); relations use the other set's id constants.
    Sets are discovered by reflection (no registration) and run in ordinal name order (`NNNN-description`);
    blank/duplicate/too long names fail. New demo data later = a **new set class** (like a new migration);
    applied sets are never edited.
  - The runner knows **no entity type**: each object must be an entity of `AppDbContext` (else fail before
    writing); its key comes from the EF model; "exists" = `FindAsync(type, key)`; EF orders inserts by foreign
    keys. Adding a new entity type needs no runner change.
    Why not JSON (approaches considered): (A) one list per entity in the set record — runner and DTOs grow with
    every entity; (B) JSON sections + one seeder per entity — no runner change, but DTO + mapping per entity,
    relations as GUID strings, errors only at run time; (C, chosen) C# sets — no DTOs, domain API changes break
    the build instead of a branch deploy, typed relations; cost: demo data is edited by developers only.
  - **Journal table** `__db_tool_history` (`name` PK, `kind` = `seed` now / `backfill` in stage 4c,
    `applied_at`), mapped in `EphemeralEnvs.Data` as a persistence-only type (not a domain entity) and created
    by a **new EF migration**. Exact table/column naming follows the existing convention in Data: builder's call.
  - `seed`: applies, in name order, every set **not in the journal**: inserts products whose id is not in the
    DB (existing ids skipped, never updated), then writes the journal row; one transaction per set. Already
    applied sets are skipped entirely → products QA deleted or changed stay deleted/changed.
    Logs per set: `applied 0001-products: inserted N, already present M` or `Skipped 0001-products (already applied)`.
  - `seed --restore`: ignores the journal; for every set inserts the missing keys (brings deleted demo data
    back), never updates existing rows; adds missing journal rows. Same guard.
  - How QA gets data back: `seed --restore` (only deleted ones), `db-reset` (whole DB back to the
    just-deployed state); on previews stage 6b adds a manual redeploy input (e.g. `reset-db: true`).
  - Guard (Claude's call after builder check): refuses (exit 1, nothing written) when `App__EnvironmentName`
    is **missing/empty** or equals `production` / `prod` (case-insensitive). So every caller (compose,
    `db-update.ps1`, `db-reset.ps1`, tests, stage 2 scripts) must set the name. The real protection is that
    production pipelines never call `seed`; the guard is a safety net. Tests: missing name → exit 1.
  - Exit code 0 on success, non-zero on any failure (invalid seed data, DB unreachable, …).
- Requires the schema to exist; it does not migrate.

### 4.3 `migrator` image — `src/EphemeralEnvs.DbTool/Dockerfile` (build context = repo root)
- Build stage (`sdk:10.0`): `dotnet tool restore`, `dotnet ef migrations bundle` for
  `src/EphemeralEnvs.Data` (Linux, output `efbundle`), and `dotnet publish` of DbTool.
- Final stage: `mcr.microsoft.com/dotnet/runtime:10.0` (DbTool needs the runtime), non-root `USER $APP_UID`,
  contains `/app/efbundle` and DbTool. Bundle self-contained or framework-dependent: builder's call.
- No entrypoint magic: compose/CI pass the command. Two uses of one image:
  - migrate: `./efbundle --connection "<conn>"`
  - seed: `dotnet EphemeralEnvs.DbTool.dll seed`
- `.dockerignore` / restore-layer pattern as in the Api Dockerfile.

### 4.4 Compose (`deploy/local/docker-compose.yml`)
- Services `migrate` and `seed`, both `image: ephemeral-envs/migrator` built once from the DbTool Dockerfile
  (builder ensures it is built once, not twice).
- `migrate` depends on `postgres: service_healthy`; `seed` depends on `migrate: service_completed_successfully`;
  `api` depends on `seed: service_completed_successfully` (+ postgres healthy). `restart: "no"` for both.
- Both get the connection string to `postgres` through env vars (the bundle via `--connection`).
- Header comment updated (stage 1, no manual migration step).

### 4.5 Scripts (`deploy/local/`)
- `up.ps1`: drop the `dotnet tool restore` / `dotnet ef` steps; compose does migrate + seed. If
  `docker compose up --wait` does not cope with one-off services on compose v2.30, the builder finds a
  working form (e.g. `--wait` on long-running services only) and documents it. On failure, print the
  `migrate`/`seed` logs.
- `db-update.ps1`: `docker compose run --rm migrate` then `run --rm seed` (for running Api/Web from the IDE
  against the compose DB after adding a migration). Builds the image if needed. `-Restore` → `seed --restore`.
- `db-reset.ps1`: drop and recreate the `ephemeral_envs` database (via `psql` in the postgres container,
  `DROP DATABASE … WITH (FORCE)`), then migrate + seed. Asks no questions (local only).
- Why `deploy/local/`: these are **local-dev helpers** around the local compose project. Test/preview envs
  reuse the **`migrator` image and its commands** (`efbundle`, `seed`, `seed --restore`), not these scripts:
  stage 2 adds the shared env scripts in `deploy/` (`deploy`/`destroy -Env x`), and the server side
  (6b) calls the same image from `deploy/deploy.sh` on Linux. The contract that must stay stable is the
  image's commands + env vars (`ConnectionStrings__Default`, `App__EnvironmentName`).
- Seed **data** is not in `deploy/local/`: it lives in `src/EphemeralEnvs.DbTool/Seeding/Sets/` (compiled) and is baked into the
  `migrator` image, so every env (local, each branch) gets the same data from the same image.
- QA reset path (no scripts for QA): stage 1 local = `db-reset.ps1` / `db-update.ps1 -Restore`; stage 2 local
  branch envs = options of the shared env script (e.g. `deploy -Env x -ResetDb` / `-RestoreSeed`); stage 6b =
  GitHub "Run workflow" on the preview deploy with inputs `reset-db` / `restore-seed` (stage 8: same in ADO).
- Same conventions as stage 0 scripts: `DOCKER_CONTEXT=desktop-linux`, restore caller env, ports from
  `EE_*_PORT`, clear errors.

### 4.6 Tests
- New `tests/EphemeralEnvs.DbTool.Tests` (Testcontainers Postgres, migrations applied like `PostgresFixture`):
  seed into empty DB → all products with the exact ids + journal row; second run → set skipped, nothing
  duplicated; a product deleted by hand is **not** restored by `seed`, but is by `seed --restore`; a product
  changed by hand is never overwritten (both modes); a new set file is applied on the next run while the old
  one is skipped; guard: missing env name / `production` / `prod` → non-zero, nothing written;
  invalid seed data → failure, nothing inserted and no journal row (transaction).
- Domain tests for the factory; architecture tests for DbTool references.
- All existing tests stay green; build 0 warnings; `dotnet format` clean.

### 4.7 Docs
- README: "Stage 1" run section (what changed in `up.ps1`, `db-update`/`db-reset`, seed ids list, how
  to see migrate/seed logs, failure demo).
- Parent spec: stage 1 row points here; add stage **4c Backfill job** to the roadmap; §5.6 note updated.
- STATUS.md updated.

## 5. Definition of done (manual checks)
1. `./deploy/local/down.ps1 -RemoveData; ./deploy/local/up.ps1` → migrate and seed exit 0, api/web up, `/` shows the seed products.
2. `GET /products/{fixed id}` → 200.
3. Re-run `up.ps1` → no new migrations, seed `Skipped 0001-products (already applied)`.
4. Edit one seeded product and delete another via SQL → `db-update.ps1` → both changes kept;
   `db-update.ps1 -Restore` → deleted one back, edited one still edited.
5. `db-reset.ps1` → DB back to the seed state.
6. Break a migration (or stop postgres before migrate) → migrate fails, api is **not** started, `up.ps1` exits non-zero with the logs.
7. Host without `dotnet ef` on PATH is not needed for `up.ps1`.

## 6. Left to the builder
Factory name; bundle self-contained vs not; product names/prices; `--wait` handling;
whether DbTool test fixture shares code with `PostgresFixture` (copy is acceptable).

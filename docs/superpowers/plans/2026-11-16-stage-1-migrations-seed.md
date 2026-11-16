# Stage 1 — Migrations as a separate step + seed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the schema with an EF Core migration bundle and fill demo data with a domain-based, once-per-database seed, both from one `migrator` image that runs as separate steps before the apps.

**Architecture:** New console `EphemeralEnvs.DbTool` (refs Domain, Data, ServiceDefaults) with command `seed [--restore]`; seed sets are JSON files baked into the image; applied sets are recorded in a journal table `__db_tool_history` (new migration). The `migrator` image holds `efbundle` + DbTool; compose runs `migrate` → `seed` → `api` → `web`.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, `dotnet ef migrations bundle`, Docker Compose v2.30, xUnit + Testcontainers, NetArchTest, PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-11-16-stage-1-migrations-seed-design.md` (parent: `docs/superpowers/specs/2026-11-11-branch-deployment-design.md`).

## Global Constraints

- **Never commit.** Each task ends with "stop for user review"; the user commits (message style `Stage 1 / Task N: …`).
- Every Docker command uses `DOCKER_CONTEXT=desktop-linux`. Avoid host ports 80/443.
- Build: 0 warnings (`TreatWarningsAsErrors`), `dotnet format --verify-no-changes` clean, all tests green.
- Central package management: new packages only via `Directory.Packages.props` (no versions in csproj).
- No migrate/seed on app startup, no `EnsureCreated`, no `UseSeeding/UseAsyncSeeding`.
- Seed never updates existing rows; seed runs once per database per set (journal); `--restore` only re-inserts missing ids.
- Seed guard refuses when `App__EnvironmentName` is missing/blank or equals `production`/`prod` (case-insensitive, trimmed).
- Exit codes of DbTool: `0` success, `1` failure (guard, DB, invalid/missing seed data), `2` usage error.
- Image contract (must stay stable for stage 2/6b): `./efbundle --connection "<conn>"`, `dotnet EphemeralEnvs.DbTool.dll seed [--restore]`, env `ConnectionStrings__Default`, `App__EnvironmentName`.
- Naming in Data: snake_case tables/columns (as `products`).
- Report to the user in Ukrainian after each task: what was added, how it works, how to run/test.

## Review Focus

1. Duplicate id inside one seed set → fail before writing anything (exit 1), not a PK violation half-way. → Task 3 test `Seed_DuplicateIdInSet_FailsAndWritesNothing`.
2. Seed against a database without migrations (no tables) → exit 1 with a clear log, no unhandled crash. → Task 3 test `Cli_SchemaMissing_Returns1`.
3. Missing seed directory → exit 1; empty directory → exit 0 with "no seed sets" log. → Task 3 tests `Cli_SeedDirectoryMissing_Returns1`, `Seed_NoSets_DoesNothing`.
4. Missing `ConnectionStrings:Default` → exit 1 with message naming the variable. → Task 3 test `Cli_NoConnectionString_Returns1`.
5. Env name with spaces/case like `" Production "` → refused. → Task 3 guard theory includes it.

---

### Task 1: Domain factory with explicit id

**Files:**
- Modify: `src/EphemeralEnvs.Domain/Product.cs`
- Test: `tests/EphemeralEnvs.Domain.Tests/ProductTests.cs`

**Interfaces:**
- Produces: `public static Product Product.CreateSeeded(Guid id, string name, decimal price, DateTimeOffset createdAt)` — same name/price validation as the ctor; `Guid.Empty` → `DomainValidationException(nameof(Id), "Id is required.")`.

- [ ] **Step 1: Write failing tests** in `ProductTests.cs`:
  - `CreateSeeded_ValidValues_KeepsGivenId` — id `0196a000-0000-7000-8000-000000000001` → `product.Id` equals it; name/price/createdAt set.
  - `CreateSeeded_EmptyId_Throws` → `ex.Field == "Id"`.
  - `CreateSeeded_InvalidName_Throws` (blank) → `Field == "Name"`; `CreateSeeded_InvalidPrice_Throws` (`0m`) → `Field == "Price"`.
- [ ] **Step 2:** `dotnet test tests/EphemeralEnvs.Domain.Tests` → FAIL (method missing).
- [ ] **Step 3:** Implement. Refactor so ctor and factory share validation (e.g. private ctor taking id); public ctor still uses `Guid.CreateVersion7()`. XML doc: "for seed data with fixed ids; normal code uses the constructor".
- [ ] **Step 4:** `dotnet test` (whole solution) → PASS; `dotnet format --verify-no-changes` → clean.
- [ ] **Step 5:** Stop for user review/commit.

### Task 2: Journal table `__db_tool_history`

**Files:**
- Create: `src/EphemeralEnvs.Data/History/DbToolHistoryEntry.cs`, `src/EphemeralEnvs.Data/Configurations/DbToolHistoryEntryConfiguration.cs`
- Modify: `src/EphemeralEnvs.Data/AppDbContext.cs`
- Create (generated): `src/EphemeralEnvs.Data/Migrations/<ts>_AddDbToolHistory.cs` (+ Designer, snapshot update)

**Interfaces:**
- Produces: `public sealed class DbToolHistoryEntry(string name, string kind, DateTimeOffset appliedAt)` with get-only `Name`, `Kind`, `AppliedAt`; `public static class DbToolHistoryKinds { public const string Seed = "seed"; }` (stage 4c adds `Backfill`).
- Produces: `AppDbContext.DbToolHistory : DbSet<DbToolHistoryEntry>`.
- Table `__db_tool_history`: `name` varchar(200) PK, `kind` varchar(50) not null, `applied_at` timestamptz not null.

- [ ] **Step 1:** Add the type, configuration (snake_case as `ProductConfiguration`), DbSet. Comment: persistence-only bookkeeping, not a domain entity; shared with stage 4c backfill.
- [ ] **Step 2:** `dotnet tool restore; dotnet ef migrations add AddDbToolHistory --project src/EphemeralEnvs.Data` → migration creates only `__db_tool_history`.
- [ ] **Step 3:** `dotnet ef migrations has-pending-model-changes --project src/EphemeralEnvs.Data` → "No changes have been made to the model since the last migration."
- [ ] **Step 4:** `dotnet build` 0 warnings; `dotnet test` → all PASS (Api tests apply the new migration through `PostgresFixture`).
- [ ] **Step 5:** Stop for user review/commit.

### Task 3: `EphemeralEnvs.DbTool` — seed command + tests

**Files:**
- Create: `src/EphemeralEnvs.DbTool/EphemeralEnvs.DbTool.csproj` (Exe; refs Domain, Data, ServiceDefaults; `Seed/*.json` → `CopyToOutputDirectory=PreserveNewest`), `Program.cs`, `DbToolCli.cs`, `Seeding/SeedSet.cs`, `Seeding/SeedSetLoader.cs`, `Seeding/SeedRunner.cs`, `Seeding/SeedGuard.cs`, `Seed/products-v1.json`
- Create: `tests/EphemeralEnvs.DbTool.Tests/` (csproj like Api.Tests minus Mvc.Testing, refs DbTool + Data + Domain), `PostgresDatabaseFixture.cs`, `SeedRunnerTests.cs`, `DbToolCliTests.cs`, `SeedGuardTests.cs`
- Modify: `EphemeralEnvs.slnx` (both projects), `tests/EphemeralEnvs.ArchitectureTests/{Assemblies.cs,LayerTests.cs,EphemeralEnvs.ArchitectureTests.csproj}`
- Possibly modify: `Directory.Packages.props` (`Microsoft.Extensions.Hosting` if not already transitively available)

**Interfaces:**
- Consumes: `Product.CreateSeeded(...)` (Task 1); `AppDbContext.DbToolHistory`, `DbToolHistoryEntry`, `DbToolHistoryKinds.Seed` (Task 2).
- Produces:
  - `public sealed record SeedProduct(Guid Id, string Name, decimal Price, DateTimeOffset CreatedAt)`
  - `public sealed record SeedSet(string Name, IReadOnlyList<SeedProduct> Products)`
  - `public static class SeedSetLoader { public static IReadOnlyList<SeedSet> LoadFromDirectory(string directory); }` — `*.json` files, name = file name without extension, ordered by name (ordinal); file = JSON array of `{ "id", "name", "price", "createdAt" }`; missing directory → `DirectoryNotFoundException`; malformed JSON → `InvalidDataException` naming the file.
  - `public enum SeedMode { Normal, Restore }`
  - `public sealed record SeedSetResult(string Name, bool SkippedAsApplied, int Inserted, int AlreadyPresent)`
  - `public sealed class SeedRunner(AppDbContext db, TimeProvider time, ILogger<SeedRunner> logger) { Task<IReadOnlyList<SeedSetResult>> RunAsync(IReadOnlyList<SeedSet> sets, SeedMode mode, CancellationToken ct); }`
  - `public static class SeedGuard { public static bool IsAllowed(string? environmentName); }`
  - `public static class DbToolCli { public static Task<int> RunAsync(string[] args, IReadOnlyDictionary<string, string?>? configOverrides = null, CancellationToken ct = default); }` — config keys `ConnectionStrings:Default`, `App:EnvironmentName`, `Seed:Directory` (default `Path.Combine(AppContext.BaseDirectory, "Seed")`).
  - `Program.cs`: `return await DbToolCli.RunAsync(args);`

**Behaviour (`SeedRunner.RunAsync`):** for each set in order: validate all products first (build domain objects via `Product.CreateSeeded`; duplicate id within the set → `InvalidDataException`); Normal mode + set name in journal → result `SkippedAsApplied=true`, nothing written; otherwise, in one transaction per set: insert products whose id is not in `products` (never update), add journal row (`kind=seed`, `applied_at=time.GetUtcNow()`) if missing, commit. Log per set: `applied {Set}: inserted {N}, already present {M}` / `skipped {Set} (already applied)` / `restored {Set}: …`. No sets → log "no seed sets found", return empty.

**Behaviour (`DbToolCli`):** host via `Host.CreateApplicationBuilder` + `AddServiceDefaults()` (JSON logs) + overrides; args: `seed` or `seed --restore` else usage → 2; guard fails → log error naming `App__EnvironmentName` → 1; missing connection string → log error naming `ConnectionStrings__Default` → 1; any exception → log error → 1; success → 0.

**`Seed/products-v1.json`:** 5 products, ids `0196a000-0000-7000-8000-00000000000{1..5}`, createdAt `2026-01-01T09:0{0..4}:00Z`: Notebook A5 4.50, Ballpoint pen 1.20, Desk lamp 34.99, USB-C cable 1 m 9.90, Coffee mug 12.00.

- [ ] **Step 1:** Create both projects + slnx entries; `PostgresDatabaseFixture`: one Postgres 17 container per class (Linux-engine pipe logic copied from `PostgresFixture`), method `Task<string> CreateDatabaseAsync(bool migrate = true)` creating a uniquely named DB and (optionally) applying migrations.
- [ ] **Step 2: Write failing tests**
  - `SeedGuardTests` theory: `null`, `""`, `"  "`, `"production"`, `"PROD"`, `" Production "` → false; `"local"`, `"gh-feature-x"` → true.
  - `SeedRunnerTests` (each on a fresh DB; sets built in code):
    - `Seed_EmptyDb_InsertsAllWithFixedIdsAndJournal` → 5 products with exact ids; one `__db_tool_history` row `products-v1`/`seed`.
    - `Seed_SecondRun_SkipsAppliedSet` → result `SkippedAsApplied`, still 5 rows.
    - `Seed_DeletedProduct_IsNotRestored` (delete one → Normal run → 4 rows).
    - `Restore_DeletedProduct_IsRestored` (delete one → Restore → 5 rows).
    - `Seed_And_Restore_NeverOverwriteEditedProduct` (update name via SQL → both modes → name unchanged).
    - `Seed_NewSet_AppliedOldSkipped` (v1 applied, then [v1, v2] → v1 skipped, v2 inserted, 2 journal rows).
    - `Seed_InvalidProduct_FailsAndWritesNothing` (price `0.001m`) → throws `DomainValidationException`, 0 products, 0 journal rows.
    - `Seed_DuplicateIdInSet_FailsAndWritesNothing` → `InvalidDataException`, nothing written.
    - `Seed_NoSets_DoesNothing` → empty result.
  - `DbToolCliTests` (overrides; `Seed:Directory` → temp dir with files written by the test):
    - `Cli_Seed_Returns0_AndSeeds`; `Cli_UnknownCommand_Returns2`; `Cli_NoArgs_Returns2`;
    - `Cli_GuardRefuses_Returns1_AndWritesNothing` (`App:EnvironmentName=production`);
    - `Cli_NoConnectionString_Returns1`; `Cli_SchemaMissing_Returns1` (`CreateDatabaseAsync(migrate: false)`);
    - `Cli_SeedDirectoryMissing_Returns1`; `Cli_ProductsV1File_IsValid` (load the real `Seed/products-v1.json` from output → 5 products, ids as above).
  - Architecture: `Assemblies.DbTool = typeof(DbTool.DbToolCli).Assembly`; `DbTool_DependsOnlyOnDomainDataServiceDefaults` (ShouldNot depend on Api, Application, Contracts, Web, AspNetCore); add `DbTool` to `Services_UseServiceDefaults`; update the `LayerTests` summary comment.
- [ ] **Step 3:** `dotnet test tests/EphemeralEnvs.DbTool.Tests` → FAIL (types missing).
- [ ] **Step 4:** Implement the interfaces above.
- [ ] **Step 5:** `dotnet test` (solution) → all PASS; build 0 warnings; `dotnet format --verify-no-changes` clean.
- [ ] **Step 6:** Manual: `docker compose -f deploy/local/docker-compose.yml up -d postgres` (stage 0 DB with schema) → `dotnet ef database update --project src/EphemeralEnvs.Data` → `dotnet run --project src/EphemeralEnvs.DbTool -- seed` with `ConnectionStrings__Default` / `App__EnvironmentName=local` → exit 0, JSON log `applied products-v1: inserted 5, already present 0`; second run → `skipped products-v1 (already applied)`.
- [ ] **Step 7:** Stop for user review/commit.

### Task 4: `migrator` image, compose, local scripts

**Files:**
- Create: `src/EphemeralEnvs.DbTool/Dockerfile`, `deploy/local/common.ps1`, `deploy/local/db-update.ps1`, `deploy/local/db-reset.ps1`
- Modify: `deploy/local/docker-compose.yml`, `deploy/local/up.ps1`, `deploy/local/down.ps1` (use `common.ps1` if it fits)

**Interfaces:**
- Consumes: DbTool (Task 3), migrations (Task 2).
- Produces (image contract): `/app/efbundle --connection "<conn>"`; `dotnet /app/EphemeralEnvs.DbTool.dll seed [--restore]`; workdir `/app`; `USER $APP_UID`.
- Produces (scripts): `db-update.ps1 [-Restore] [-DbPort]`, `db-reset.ps1 [-DbPort]`; `common.ps1` exports `Invoke-Step`, `Use-LocalDockerEnv { scriptblock }` (sets `DOCKER_CONTEXT` + `EE_*` vars, restores caller env).

- [ ] **Step 1: Dockerfile.** Build stage `sdk:10.0`: same restore-layer pattern as `src/EphemeralEnvs.Api/Dockerfile` (csproj of DbTool, Data, Domain, ServiceDefaults + shared props + `dotnet-tools.json`); `dotnet tool restore`; `dotnet publish` DbTool `-c Release -o /app`; `dotnet ef migrations bundle --project src/EphemeralEnvs.Data --startup-project src/EphemeralEnvs.Data -r linux-x64 -o /app/efbundle --configuration Release` (if a class-library startup project is refused, use DbTool as startup project). Final `mcr.microsoft.com/dotnet/runtime:10.0`, `WORKDIR /app`, copy, `USER $APP_UID`, no ENTRYPOINT (commands come from compose/CI).
- [ ] **Step 2:** `docker build -f src/EphemeralEnvs.DbTool/Dockerfile -t ephemeral-envs/migrator .` → success; `docker run --rm ephemeral-envs/migrator ./efbundle --help` → bundle help; `docker run --rm ephemeral-envs/migrator dotnet EphemeralEnvs.DbTool.dll` → usage, exit 2.
- [ ] **Step 3: Compose.** Services `migrate` and `seed`: identical `build` (YAML anchor) + `image: ephemeral-envs-local/migrator`; `migrate`: `command: ["./efbundle", "--connection", "Host=postgres;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs"]`, depends on `postgres: service_healthy`; `seed`: `command: ["dotnet", "EphemeralEnvs.DbTool.dll", "seed"]`, env `ConnectionStrings__Default` (same), `App__EnvironmentName: local`, depends on `migrate: service_completed_successfully`; `api` depends on `seed: service_completed_successfully` and `postgres: service_healthy`; `restart: "no"`. Update header comment (stage 1).
- [ ] **Step 4: `up.ps1`.** Remove `dotnet tool restore` / `dotnet ef`; keep `up --build --detach --wait` if compose v2.30 accepts completed one-shot services, else `--wait` only for `api web` (document the choice in a comment). On failure print `docker compose logs migrate seed`. Synopsis updated. Uses `common.ps1`.
- [ ] **Step 5: `db-update.ps1`.** `compose build migrate` → `compose run --rm migrate` → `compose run --rm seed [--restore]` (`-Restore` switch). Needs running postgres (starts it with `up -d --wait postgres`).
- [ ] **Step 6: `db-reset.ps1`.** `compose exec -T postgres psql -U ephemeral_envs -d postgres -c "DROP DATABASE IF EXISTS ephemeral_envs WITH (FORCE);"` then `CREATE DATABASE ephemeral_envs OWNER ephemeral_envs;` → migrate → seed. Prints that a running api reconnects by itself.
- [ ] **Step 7: Manual DoD (spec §5)** — all with `-DbPort 5433` if 5432 is busy:
  1. `down.ps1 -RemoveData; up.ps1` → migrate/seed exit 0, `/` shows the 5 products.
  2. `GET http://localhost:5081/products/0196a000-0000-7000-8000-000000000001` → 200.
  3. `up.ps1` again → bundle "No migrations were applied", seed `skipped products-v1 (already applied)`.
  4. SQL: rename product …02, delete product …03 → `db-update.ps1` → both kept; `db-update.ps1 -Restore` → …03 back, …02 still renamed.
  5. `db-reset.ps1` → exactly the 5 seed products.
  6. Stop postgres before migrate (or point migrate to a wrong host) → `up.ps1` exits non-zero, prints migrate logs, `api` not started.
  7. `up.ps1` works with `dotnet` removed from PATH in that shell.
- [ ] **Step 8:** Stop for user review/commit.

### Task 5: Docs

**Files:**
- Modify: `README.md`, `docs/superpowers/specs/2026-11-11-branch-deployment-design.md`, `docs/STATUS.md`

- [ ] **Step 1: README** — "Stage 1: how to run": what `up.ps1` now does (postgres → migrate → seed → api → web), seed products table (ids, names), `db-update.ps1 [-Restore]`, `db-reset.ps1`, how to read `docker compose logs migrate seed`, how to add a migration (`dotnet ef migrations add … --project src/EphemeralEnvs.Data` → `db-update.ps1`), how to add demo data (new `Seed/products-vN.json`, never edit applied sets), failure demo, note that QA reset on previews comes in stage 2/6b. Remove/adjust the stage 0 manual-migration text.
- [ ] **Step 2: Parent spec** — stage 1 row: scripts in `deploy/local/`, link to stage-1 spec, seed once per DB; add row **4c Backfill job** (post-deploy data migration for expand/contract, idempotent batches, journal `__db_tool_history` kind `backfill`, DbTool command `backfill <name>`); §5.6 last bullet: migrations now via `migrator`.
- [ ] **Step 3: STATUS.md** — stage 1 done, next = stage 2 design refresh.
- [ ] **Step 4:** Final: `dotnet build` 0 warnings, `dotnet test` all PASS, `dotnet format --verify-no-changes` clean. Stop for user review/commit; report in Ukrainian.

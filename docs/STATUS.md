# Branch-based deployment PoC — STATUS

> Living file. Update after every decision / completed step. Read this first on session start.

## Working rules (from user, 2026-11-11)
- Claude **never commits**; the user reviews and commits.
- After each step Claude reports: what was added, how it works, step-by-step run & test guide.
- `README.md` must always contain current run instructions.
- Small stages; stop after each for the user to experiment.

## Goal
Understand & test a flow where QA tests a feature **directly from its branch** (ephemeral / preview
environment per branch/PR) instead of merging into integration branches (dev/test/uat).
PoC, but as close to a real project as reasonable.

Target flow: open PR from `feature/x` → env (Web + API + own DB) auto-created at unique URL →
push updates it → PR closed/merged → env destroyed.

## Decisions so far (2026-11-11)
- Phasing: **A** local first (Docker + reverse proxy, `*.localhost`), then **B** real CI + hosting
  reachable by QA (Hetzner VPS ~5-10$/mo OK, or Azure if cheap; Bicep desirable for Azure, can be separate).
- App: **generic .NET first** (ASP.NET Core Web + ASP.NET Core Web API + DB via EF Core migrations).
  **Umbraco** as a second experiment on the same infra later.
- DB: **PostgreSQL** (user never used it — interested; light, DB-per-branch is cheap).
- Sitecore per-branch envs considered too heavy; known pattern there = shared CM, per-branch rendering host only (maybe later).
- CI: compare **both GitHub Actions and Azure DevOps Pipelines**.
- Repo layout: **one repo, two remotes** (`github`, `azure`); shared `src/` and `deploy/` scripts,
  platform-specific `.github/workflows/` and `azure-pipelines/`. Env name prefix per platform
  (e.g. `gh-feature-x`, `ado-feature-x`). Deploy logic lives in scripts, pipelines just call them.
- Repo lives in its own folder `D:\Work\Personal\PetProjects\BranchBasedDeployment` (moved out of pdp, git init'ed).
  Claude memory is path-bound → CLAUDE.md + docs/STATUS.md are the source of truth.

- Lifecycle: env **created on PR open** (draft too), **updated on push** to PR branch, **destroyed on
  PR close/merge**; plus **manual re-run/redeploy** (workflow_dispatch / manual ADO run) and
  **nightly cleanup** (destroy envs whose PR is closed or not updated for N days).

- Addressing: **separate subdomains** per service (more realistic): Web `<env>.<domain>`, API
  `<env>-api.<domain>` (single wildcard level → one wildcard DNS record + one wildcard cert later).
  Locally `<domain>` = `localhost`. Implies per-env API URL config in Web + CORS on API.
- QA API testing tool: **Bruno** (collection in repo; env `baseUrl` per branch).

- Web app: **ASP.NET Core MVC** (target real project is MVC) calling API **server-side** (API URL via
  env var; may use internal Docker network) **+ a small JS widget calling API from the browser**
  (runtime config, e.g. `/config.json` / injected into layout; requires CORS for env's Web origin).
  Build once, deploy anywhere — no per-env values baked into images.
- API: **Swagger UI must be enabled in preview envs** (not only Development), at `<env>-api.<domain>/swagger`.

- DB: **one shared PostgreSQL server, separate database (+ user) per env**; created by deploy
  script, `DROP DATABASE` on destroy.
- Migrations: **separate deploy step, NOT on app startup** (likely EF Core migration bundle in its
  own image/one-off container, run before rolling out Web/API).

- Seed: **empty schema + deterministic seed** (seed step after migrations; data in repo).
- NEW requirement: **migration safety check** step — follow **expand/contract (cleanup)** approach,
  **block irreversible/destructive schema changes unless explicitly allowed**.
  Approach **C**: (1) blocking gate = .NET tool inspecting EF `MigrationBuilder.Operations` of new
  migrations vs `main`, classifies expand/contract; contract ops allowed only with
  `[ContractMigration("reason")]` (+ CODEOWNERS on `Migrations/`); (2) **Squawk** on generated SQL as
  warnings (locking issues etc.). Runs as PR check before preview deploy.
  Contract rule (default): contract migration must be in its own PR (no other migrations / app code).
- **WORKING STYLE: build the PoC in small stages** — implement one thing, user looks & experiments,
  then next. Never land everything at once (lots of new tech for the user). One plan per stage.

- Hetzner model: **one long-lived VPS** created once by Terraform (run from laptop, local state,
  gitignored); preview envs = containers + DB deployed onto it by CI via SSH. No server per PR.
- IaC for Hetzner: **Terraform** (`hcloud` provider) + **cloud-init** for server setup.
  Bicep = Azure-only; stage 9 may compare Terraform vs Bicep on Azure.

## Stage roadmap (APPROVED 2026-11-11; FF1/FF2 added 2026-11-13)
0. App skeleton: MVC Web + API (Swagger) + EF Core/Postgres + JS widget + CORS + runtime config; one env via docker compose
1. Migrations as separate step (EF migration bundle `migrator` image) + seed
2. Multi-env locally: Traefik, `<env>.localhost`/`<env>-api.localhost`, shared Postgres DB-per-env, `deploy`/`destroy -Env x` scripts
FF1. Feature flags via config: Microsoft.FeatureManagement, demo flag in API ([FeatureGate]) + Web widget + `/features`, tests on/off, `deploy -Flags`; PR-label flags later in 6b (added 2026-11-13)
O1. OpenTelemetry + ServiceDefaults + Aspire Dashboard + traceparent from widget + HTTP resilience in ServiceDefaults (AddStandardResilienceHandler: timeouts, retries for safe methods, circuit breaker; replaces Api__TimeoutSeconds; added 2026-11-13)
3. Bruno collection (per-env baseUrl)
4a. migration-safety check (EF ops expand/contract + `[ContractMigration]` + Squawk warnings)
4b. API contract safety (oasdiff vs main)
4c. Backfill job: DbTool `backfill <name>` in the migrator image, post-deploy data migration for expand/contract (added 2026-11-16)
5a. GitHub Actions CI: build/format/tests+coverage/migration-safety/oasdiff PR checks, PR template, CODEOWNERS, rulesets (no deploy)
5b. Security & supply chain: gitleaks, CodeQL, Dependabot, Trivy, hadolint, actionlint+zizmor, actions pinned by SHA
Q. Review comments → analyzer rules (user's PDP item)
AI. (optional) AI code review
X. (optional) Playwright E2E, SBOM, PR title convention, license check
6a. Hetzner VPS via Terraform + cloud-init (wildcard DNS, TLS) + terraform fmt/validate, tflint, trivy config
6b. Preview deploy from GitHub on PR open/push/close + manual re-run + post-deploy smoke (health + Bruno `bru run`)
FF2. (optional) Runtime flags: OpenFeature + flagd or Unleash/GrowthBook, toggle without redeploy, % rollout (added 2026-11-13)
7. Nightly cleanup
K1. Helm chart + local k3d (namespace per env, 2 replicas, probes) + helm lint, kubeconform
K2. k3s on Hetzner + GitHub preview deploy to k8s
O2. Grafana (otel-lgtm) on server, per-env dashboards
8. Azure DevOps pipelines (same scripts, `ado-` prefix, branch policies)
9. (optional) Azure hosting: Bicep vs Terraform
10. (optional) Umbraco on same infra
C3. (optional, LAST, low priority) Full CQRS with separate read store: outbox → broker → projector → read DB, eventual consistency

## Design (in progress)
- §1 Repo/projects APPROVED: .NET 10; `src/EphemeralEnvs.Web` (MVC + wwwroot/js/widget.js), `src/EphemeralEnvs.Api`
  (controllers + Swagger UI), `src/EphemeralEnvs.Domain` (entities, no deps), `src/EphemeralEnvs.Data` (DbContext, Fluent
  configs, Migrations, Npgsql), `src/EphemeralEnvs.Contracts` (DTOs); `tests/EphemeralEnvs.Api.Tests` (WebApplicationFactory
  + Testcontainers Postgres); `deploy/local/docker-compose.yml`; central package mgmt. Model: `Product`
  (Id, Name, Price, CreatedAt). Manual Domain→DTO mapping. No full Clean Architecture.

- §2 Config & flows APPROVED: build once, env vars at runtime. Web: `Api__InternalBaseUrl` (typed
  HttpClient, server-side), `Api__PublicBaseUrl` (rendered into `<meta name="api-base-url">` for
  widget.js); pages `/` (server-side list) and `/Home/Widget` (JS list + add). API:
  `ConnectionStrings__Default`, `Cors__AllowedOrigins` (no `*`), `Swagger__Enabled` flag (not
  IsDevelopment), endpoints `GET/POST /products`, `GET /products/{id}`. Probes: `/health/live` (no deps)
  + `/health/ready` (DB). `App__EnvironmentName` shown in Web footer + Swagger. Stage 0 compose:
  postgres, api :5081, web :5080; migrations applied manually (`dotnet ef database update`) until stage 1.
- Roadmap additions: stage 2 also tries `--scale api=2 web=2` behind Traefik (→ Data Protection keys
  issue for MVC). New stages after 7: **K1** Helm chart + local k3d (namespace per env, replicas,
  probes); **K2** k3s on Hetzner (likely 8 GB VPS) + GitHub preview deploy to k8s; scripts get
  `-Target compose|k8s`.

- §3 Errors/tests/DoD APPROVED (see spec §5). Observability stages O1 (after 2) / O2 (with Hetzner/K8s).
  Local Docker: use `DOCKER_CONTEXT=desktop-linux` (Linux engine runs alongside Windows engine for
  Sitecore), preflight check, avoid ports 80/443.

- Naming (2026-11-11): solution `EphemeralEnvs.slnx`, projects/namespaces `EphemeralEnvs.*` (was `Poc.*`),
  DbContext `AppDbContext`. Local DB/user/password `ephemeral_envs` (was `poc`); env vars `EE_DB`,
  `EE_WEB_PORT`, `EE_API_PORT`, `EE_DB_PORT` (were `POC_*`); compose project `ephemeral-envs-local`.
- CQRS (2026-11-13): user rejected DbContext in API and plain service (= repository in disguise).
  Chosen **level 2**: one DB, separate write model (commands → domain → AppDbContext) and read model
  (queries → read-only ReadDbContext → DTO projections); new project `EphemeralEnvs.Application`; no MediatR;
  Api has no reference to Data (architecture test). Level 3 → optional last stage C3 (user wants to see it, not priority).
- Domain invariants (2026-11-13): validation also in the **domain** (`Product` ctor: name not blank, ≤200; price
  0.01…9999999999999999.99, ≤ 2 decimal places) → `DomainValidationException(field)`; DTO: semantic attributes `[ProductName]`/`[Price]` in Contracts (decided 2026-11-13: domain = single source, attributes mirror it once); limits as constants in `Product`, used by EF config;
  API keeps DTO attributes (friendly 400) + maps DomainValidationException → 400 ProblemDetails; ContractTests (arch) keep
  attribute limits = domain constants, forbid unbounded string/decimal in `*Request`, controllers take only `*Request` and return only `*Dto` (naming convention: input `*Request`, output `*Dto`, CQRS `*Command`/`*Query` — in README); new `tests/EphemeralEnvs.Domain.Tests` (unit, no Docker).
  Api references Domain only for the exception (controllers must not use domain types — arch test).
- Architecture tests (user request 2026-11-13): `tests/EphemeralEnvs.ArchitectureTests` with NetArchTest.eNhancedEdition:
  layers, controllers, CQRS (query↛AppDbContext, query uses IReadDb not ReadDbContext, command↛read side, handler results never domain types), no public setters in domain.
- Future product updates (2026-11-13, not stage 0): `PUT /products/{id}` full replacement → `UpdateProductCommand` →
  `Product.Update(name, price)`; optimistic concurrency via `xmin` (ETag / If-Match → 412); 404 for unknown id; no PATCH
  for now (task-based endpoints remain an option). Details: spec §5.3.
- Repos: **GitHub public, personal account** (showcase), repo name **`dotnet-ephemeral-environments`**, remote name `github`. ADO org created closer to stage 8.
  Public-repo security rules are in spec §2 (no secrets/IP in repo, fork PRs never deploy, SSH key only…).

- Code quality: baseline in stage 0 (.editorconfig, Directory.Build.props, dotnet format, NuGetAudit); stage 5 adds
  format/gitleaks checks, PR template, CODEOWNERS, rulesets; **stage Q** (after 5) = user's PDP item:
  2–3 recurring review comments → analyzers/CI (**user will provide the comments later**);
  optional AI review stage. ADO branch policies in stage 8.

- Local dev DB (from stage 1): compose `migrator` service before api; scripts db-update/db-reset; no
  EnsureCreated / no migrate-on-startup even in Dev. Stage 0: manual `dotnet ef database update`.

Deferred topics (discuss at relevant stage): QA access/auth on previews, cost/limits, shared staging.

## Brainstorm topics to cover
1. Trigger & lifecycle (create on PR open, update on push, destroy on close, cleanup of stale envs)
2. Isolation (own Web/API/DB per branch)
3. Addressing (wildcard DNS + reverse proxy)
4. DB per branch (create, migrations, seed)
5. Config (Web→API URL, API→conn string)
6. QA access
7. Cost / limits
8. What stays from classic flow (shared staging for integration?)

## Brainstorming process stage
- [x] Intent understood, path = architectural (spec → plan → implementation)
- [x] Clarifying questions + roadmap approved
- [x] Design sections (overall + stage 0) approved
- [ ] Approaches 2-3 + recommendation
- [ ] Design sections approved
- [x] Spec written: docs/superpowers/specs/2026-11-11-branch-deployment-design.md (APPROVED by user 2026-11-11)
- [x] Stage 0 plan: docs/superpowers/plans/2026-11-11-stage-0-app-skeleton.md (APPROVED, execution: **Native** / executing-plans)
- [ ] Phase A implemented
- [ ] Phase B implemented
- [ ] Umbraco experiment

## Next step
Stage 0 in progress (native). **Tasks 1–3 done and committed** (bf2cedf: scaffold; Domain + Data + InitialCreate; Products API, CQRS level 2, domain invariants, architecture + contract tests, read side read-only at 3 levels).
**Task 4 done and committed** (d6bf336: health probes, CORS, Swagger flag, environment name, JSON logs).
**Task 5 done and committed** (c6f0403): Web: `/` server-side list via typed `ProductsApiClient` (`Api__InternalBaseUrl`, 5 s timeout, friendly "API is unavailable right now." + warning log on failure); `/Home/Widget` + `wwwroot/js/widget.js` (fetch from the browser via `<meta name="api-base-url">` = `Api__PublicBaseUrl`, ProblemDetails errors inline, "API is unavailable" on network error); footer `Environment: …`; `ApiOptions` validated on start (absolute URLs); JSON logs like the API; Web dev port 5080 (matches API dev CORS); `UseHttpsRedirection`/HSTS removed (TLS at the proxy). Checked by hand incl. Playwright (no Web tests in stage 0 per spec). Plus (user request 2026-11-13): new `src/EphemeralEnvs.ServiceDefaults` (`builder.AddServiceDefaults()` = JSON logs; O1 adds OpenTelemetry there), used by Api and Web; arch tests: ServiceDefaults depends on nothing of ours, Api/Web reference it; `LoggingTests` pins the API's log config. 74 tests. Web server-side timeout moved to config `Api__TimeoutSeconds` (default 5, validated > 0 on start).  `https` launch profiles removed from Api and Web (2026-11-13): Studio picked `https://localhost:7004`, an origin the dev CORS does not allow, so the widget failed; local runs are http-only like the containers.
**Task 6 done, awaiting user review/commit:** `src/EphemeralEnvs.Api/Dockerfile`, `src/EphemeralEnvs.Web/Dockerfile` (multi-stage sdk:10.0 → aspnet:10.0, context = repo root, restore layer from csproj + shared props, port 8080, `USER $APP_UID`), `.dockerignore`, `deploy/local/docker-compose.yml` (project `ephemeral-envs-local`; postgres:17 + healthcheck + volume `pgdata`; api `${EE_API_PORT:-5081}`, waits for healthy postgres; web `${EE_WEB_PORT:-5080}`; env per spec §5.3/§5.4 incl. `Api__TimeoutSeconds`). Spec §5.9 items 1–8 checked 2026-11-13 (with `EE_DB_PORT=5433`, since `ee-db` holds 5432): containers up, migration Done, `/` list + footer `local`, widget POST 201 via CORS to :5081 (Playwright), Swagger 200, live/ready 200, postgres stopped → ready 503 / live 200 / `/` friendly error, same TraceId in web and api logs; 74 tests green, build 0 warnings, format OK. README: new "Stage 0: how to run" section. Plus (user request 2026-11-13): `deploy/local/up.ps1` (compose up --build --wait + `dotnet ef database update` against `-DbPort`, prints URLs; params `-WebPort/-ApiPort/-DbPort`, default EE_*_PORT then 5080/5081/5432; sets DOCKER_CONTEXT and restores the shell env) and `down.ps1` (`-RemoveData` drops the volume); checked: fresh up, re-run (no migrations), busy port → fails. Stage 1 `migrator` will replace the ef step inside up.ps1. **Stage 0 implemented.**
Task 6 committed by user (cbd6a53). **Stage 0 done.**
Stage 1 brainstorm started 2026-11-16. Finding: `Product` ctor generates `Guid.CreateVersion7()` → deterministic seed needs fixed ids (factory/ctor with id, or SQL). Open question to user: seed mechanism (SQL file vs .NET seeder via domain vs EF `UseAsyncSeeding`).
User usually seeds via the API; asked for arguments. Claude's answer: env baseline → .NET seeder (fixed ids, no running API/auth needed, no seed-only endpoints, fast); test-specific data → via API in tests/Bruno. User accepts the seeder if it is standard.
User raised: expand/contract needs a **post-deploy data migration (backfill) job**. Claude proposes: separate stage after 4a (needs a real expand/contract change to demo); stage 1 only shapes the tool so `backfill` fits later. Awaiting user confirmation.
Clarification: user meant seed **in code through the domain, right after migrations, with fixed ids** (not HTTP). Claude's recommendation: reference data needed everywhere (incl. prod) → in migrations; demo/QA data → separate non-prod deploy step `seed` (console in migrator image, domain + fixed ids, insert-missing-by-id), never on app startup.
**User approved both (2026-11-16):** seed = DbTool `seed` (domain, fixed ids, non-prod step); backfill job = new stage **4c** after 4a.
Stage 1 design draft: docs/superpowers/specs/2026-11-16-stage-1-migrations-seed-design.md — builder check (subagent) running, then hand to user for approval → writing-plans.
Builder check done. Claude's call: seed guard refuses missing/empty env name and `production`/`prod`. **User chose (2026-11-16): seed once per DB** — journal table `__db_tool_history` (new migration, reused by 4c backfill), seed sets `products-v1.json`…, `seed --restore` (re-insert deleted, never overwrite), `db-reset` full reset, 6b manual `reset-db` input. Spec updated. Clarified to user: seed data lives in DbTool (baked into image), deploy/local scripts are only local wrappers; QA reset = stage 2 `deploy -Env x -ResetDb/-RestoreSeed`, 6b GitHub Run workflow inputs.
**Stage 1 spec APPROVED 2026-11-16.** Plan written: docs/superpowers/plans/2026-11-16-stage-1-migrations-seed.md (5 tasks: domain factory; journal table + migration; DbTool seed + tests; migrator image + compose + scripts; docs). Plan approved, execution **Native**. Ledger: .superpowers/sdd/2026-11-16-stage-1-migrations-seed/progress.md
**Stage 1 Task 1 done, awaiting user review/commit:** `Product.CreateSeeded(id, name, price, createdAt)` (shared private ctor, `Guid.Empty` → DomainValidationException("Id")), 4 domain tests; 78 tests green. User decision: factory is **internal**, `InternalsVisibleTo` DbTool + Domain.Tests (Domain csproj). Task 1 committed by user.
**Task 2 done, awaiting user review/commit:** `Data/History/DbToolHistoryEntry` (+ `DbToolHistoryKinds.Seed`), config → table `__db_tool_history` (name PK varchar(200), kind varchar(50), applied_at timestamptz), `AppDbContext.DbToolHistory`, migration `20261116084418_AddDbToolHistory`; 78 tests green. User decision: journal type/kinds/DbSet **internal**, `InternalsVisibleTo` DbTool + DbTool.Tests (Data csproj). Task 2 committed by user.
**Task 3 done, awaiting user review/commit:** `src/EphemeralEnvs.DbTool` (DbToolCli: `seed` / `seed --restore`, exit 0/1/2; SeedSetLoader, SeedRunner, SeedGuard; `Seed/products-v1.json` 5 products ids `0196a000-0000-7000-8000-00000000000{1..5}`), `tests/EphemeralEnvs.DbTool.Tests` (32, Testcontainers, DB per test), arch tests for DbTool; 112 tests green. Ruling: migrator image base will be `aspnet:10.0` (ServiceDefaults needs ASP.NET Core framework). Ledger: .superpowers/sdd/2026-11-11-stage-0-app-skeleton/progress.md

**Task 3 follow-up (user question about logging):** DbTool now registers `AppDbContext` (`AddDbContext` + `UseNpgsql`, as in the API), `TimeProvider` and `SeedRunner` in the host DI and resolves them from a scope, so EF Core logs go to the same JSON console output (before: DbContext was created with `new` and EF logs were lost). Tests 112/112, format clean.

**Task 3 revised (user decision 2026-11-16): seed sets are C# classes, not JSON.** `ISeedSet { Name; Build() }` in `src/EphemeralEnvs.DbTool/Seeding/Sets/` (first: `Seed0001Products`, name `0001-products`, ids as public constants), found by reflection, run in ordinal name order. `SeedRunner` is generic: entity type + key from the EF model, `FindAsync` → add missing, EF orders FK inserts — new entity types (customers…) need no runner change. Removed JSON loader/`Seed/` folder/`Seed:Directory`. Spec §4.2 and plan Task 3 updated (approaches A/B/C recorded). Tests 117/117. Local DB journal now also has the old `products-v1` row (harmless; `db-reset` in Task 4 clears it). **Awaiting user review/commit, then Task 4.**

**Stage 1 Task 4 done, awaiting user review/commit:** `migrator` image (`src/EphemeralEnvs.DbTool/Dockerfile`, aspnet:10.0, `/app/efbundle` + DbTool, no ENTRYPOINT, user app); compose `postgres → migrate → seed → api → web` (api waits for seed completed successfully); `deploy/local/common.ps1` (Invoke-Step, Invoke-Compose, Start-Postgres `--no-recreate`, Invoke-MigrateAndSeed, Use-LocalDockerEnv); `up.ps1` without dotnet ef (`--wait` works with one-off services on compose v2.30; prints migrate/seed logs on failure); `db-update.ps1 [-Restore]`, `db-reset.ps1` (DROP … WITH (FORCE), migrate, seed, restart api). Manual DoD 1–7 passed; tests 117/117. Decision (user): pre-existing log noise `libgssapi_krb5.so.2` (Npgsql GSS probe on Ubuntu 24.04 images) removed by `GSS Encryption Mode=Disable` in the compose connection string (`x-db-connection`); verified 0 occurrences in migrate/seed/api logs. Docker Desktop may be in Windows-containers mode: the Linux engine stays reachable via `desktop-linux`. Next: Task 5 (README stage 1, parent spec rows, STATUS), then final review.
Task 4 committed by user (9a22439).
**Stage 1 Task 5 done, awaiting user review/commit:** README "Stage 1: how to run" (compose order postgres → migrate → seed → api → web, scripts, demo data table, seed behaviour, checks incl. failure demo), "Database" rewritten (migrator contract, add a migration → db-update.ps1, add demo data = new `Seeding/Sets/SeedNNNN….cs`, run DbTool from host), layout + tests (4 projects); parent spec: stage 1 row (done, link, scripts), new row 4c Backfill job, §5.6. Next: final whole-stage review (fresh reviewer), then **stage 2 design refresh** (multi-env locally).
**Stage 1 final review done** (fresh reviewer): no Critical. Important I1 fixed: concurrent seed runs on one DB failed with 23505 → `pg_advisory_xact_lock` at the start of each set's transaction, journal check inside it; test `Seed_ConcurrentRuns_OneAppliesOthersSkip` RED→GREEN, suite 118/118. 7 minors deferred (see ledger), awaiting user decision. Next: user review/commit of Task 5 + fix, then **stage 2 design refresh**.
User approved (2026-11-18) review minors M1/M3/M7 + spec for M2: done. Dockerfiles (migrator, api, web) copy only their project closure; DbTool reports 42703 like 42P01 ("schema is missing or out of date"); CLI tests assert the log; README new section **Development workflow** (nothing to publish, images built from sources by `up.ps1`; mode A all-in-Docker vs mode B IDE + db-update; what to run after which change). Finding: every `up` gives new image ids → all containers recreated (no recompilation). Suite 119/119. Awaiting user review/commit. Next: **stage 2 design refresh**.
README: section 'Change the model' (dbcontext script → migrations add + db-update → migrations remove --force; has-pending-model-changes), user request 2026-11-18.
Test added (user request 2026-11-18): ArchitectureTests MigrationTests.Model_HasNoChangesWithoutMigration (Database.HasPendingModelChanges, no DB); mutation HasMaxLength+1 → fails with hint 'dotnet ef migrations add'. README updated.
Docs split (user request 2026-11-18): README.md is short (intro, prerequisites, quick start, links); details moved verbatim to docs/local-environment.md (containers, scripts, demo data, checks), docs/development.md (workflow, build, database/migrations/seed, run API/Web from IDE + config, tests, code quality), docs/architecture.md (layout, CQRS, naming, validation, arch tests). Every step that changes how to run/test updates README quick start + the matching docs/*.md.
**Stage 2 brainstorm (2026-11-18).** User: local multi-env is an intermediate stage; main mode `deploy -Branch x` (committed code via `git worktree`, like CI) + `-FromWorkingTree`. Approach **A** chosen: PowerShell scripts in `deploy/`, platform compose (Traefik :8080 + shared Postgres) + compose project per env `ee-<env>`, later the same scripts from CI with `DOCKER_HOST=ssh://`. User: add a server-side Web form with antiforgery to show the Data Protection problem with `--scale web=2` and its fix. Claude's calls: random DB password per deploy (stateless), DB access via `db-connect -Env x [-Psql]` (admin superuser / `docker exec psql`); DP keys on a per-env shared volume (Web has no DB access). Draft spec: docs/superpowers/specs/2026-11-18-stage-2-multi-env-local-design.md — builder check running, then to user for approval → writing-plans.
Builder check done; Claude's calls added to the stage 2 spec: password rotated only after migrate+seed succeed (failed deploy keeps old env serving), env compose file from the deployed commit, -Branch resolves local then any remote, one env = one branch (-Force to take over). Spec handed to user for approval.
**Stage 2 spec APPROVED 2026-11-18.** Next: plan (writing-plans).
Stage 2 plan written: docs/superpowers/plans/2026-11-18-stage-2-multi-env-local.md (7 tasks: Web form + antiforgery; DP keys + footer; deploy module + Pester; platform; env compose + deploy.ps1; destroy/list/db-*; docs). Awaiting user review + execution method.
Stage 2 Task 1 done, awaiting user review: Web /Home/Create server-side form (global AutoValidateAntiforgeryToken), ProductsApiClient.CreateProductAsync → CreateProductResult, nav 'Add (form)', new tests/EphemeralEnvs.Web.Tests (6 tests, WebFactory + StubApiHandler, no Docker). Suite 126/126. Checked in Docker (up.ps1 + Playwright). Docs: README URL, local-environment check 4, development Tests.
Fix (user report 2026-11-19): deploy/local/up.ps1 broke under Windows PowerShell 5.1 (non-ASCII '→' in a BOM-less UTF-8 file decodes to a quote char) → ASCII '->'; verified up.ps1 under powershell.exe 5.1. Plan rule: .ps1/.psm1 ASCII only, stage 2 scripts #Requires -Version 7.2.
Stage 2 Task 2 done, awaiting user review: Web DataProtectionSetup (DataProtection__KeysPath → PersistKeysToFileSystem + SetApplicationName(EphemeralEnvs.Web); unset → defaults), footer shows host name, Web Dockerfile creates /keys owned by app user (verified: key written to a named volume as uid 1654). 4 new Web tests (shared keys → accepted, separate → 400, keys written, footer). Suite 130.
Stage 2 Task 3 done, awaiting user review: deploy/lib/EphemeralEnvs.Deploy.psm1 (#Requires 7.2, ASCII): Get-EnvName (-Branch/-Env, 40-char cut + SHA-1, 'platform' reserved), ConvertTo-DbName, New-DbPassword, Get-RepoRoot, Resolve-Commit (local, <remote>/<name>, any remote; ambiguous/not found errors), Invoke-Native, Use-DockerEnv (desktop-linux unless DOCKER_HOST, linux preflight), Invoke-PlatformSql (psql -A -t in ee-platform postgres), Get-EnvContainers, Get-RunningApiPassword. tests/deploy/EnvName.Tests.ps1: 16 Pester tests green. Pester 6.2.0 installed (CurrentUser; -MinimumVersion 5.5 picks latest). .ee/ in .gitignore/.dockerignore. docs/development.md: 'Script tests (Pester)'.
Stage 2 Task 4 done, awaiting user review: deploy/platform/compose.yml (project ee-platform: Traefik v3.7.19 with ping healthcheck, docker provider exposedByDefault=false, :8080 envs, :8090 dashboard; Postgres 17 superuser postgres/${EE_PG_ADMIN_PASSWORD:-postgres}, :5442; network ee-platform, volume ee-platform-pgdata), deploy/platform.ps1 up|down [-RemoveData], module: Invoke-PlatformCompose, Start-Platform (up -d --wait --no-recreate). Verified: both healthy, dashboard 200, anything.localhost:8080 -> 404, re-run up keeps containers, down keeps volume, -RemoveData drops it. Docs for the new scripts come in Task 7 (docs/environments.md). Open check for Task 5/6: platform down while env containers are attached to network ee-platform.
Stage 2 Task 5 done, awaiting user review: deploy/env/compose.yml (project ee-<env>; migrate/seed profile tools on ee-platform; api alias <env>-api on default net; Traefik labels as list; ee.* labels; web dpkeys:/keys) + deploy/deploy.ps1 (-Branch | -FromWorkingTree, -Env, -Force, -Rebuild, -ApiReplicas, -WebReplicas, -NoSharedKeys) + module Invoke-EnvDeploy (owner check, worktree .ee/worktrees/<env>, images once per sha12, platform, role/db + REVOKE CONNECT, migrate+seed with current password, rotate after success, up --scale, wait via Traefik Host header). Verified on a scratch clone with committed test branches: DoD 1, 2, 3, 5, 6, 8, 9 + role isolation (part of 4). Scale experiment: -NoSharedKeys -> 12/12 POST 400 (Traefik round robin: one client's GET and POST always hit different replicas), shared keys -> 12/12 302. platform down with envs attached: compose keeps network ee-platform (in use), up reattaches, envs answer again. Test envs/images removed. Suite: dotnet 130, Pester 16, build 0 warnings.
**Decision (user, 2026-11-19): deploy default flipped.** `deploy.ps1` (no switch) = current branch from the local checkout (uncommitted included): clean tree -> tag <sha12> (images reused), dirty -> wt-<env> + label <sha>+dirty + warning. `-Committed` = HEAD via worktree (CI parity). `-Branch <b>` = another branch via worktree. `-FromWorkingTree` removed; env owner = branch only. New label ee.source (local|worktree) for db-* scripts. Spec 3/4.4/4.7/DoD 5 and plan Tasks 5-6 updated. Verified: dirty on real repo (wt-master, +dirty), -Committed (worktree, 6053c56), clean default on scratch clone (build once, then skip), dirty -> wt, -Committed reuses <sha12> images, -Branch. Fix: Update-EnvWorktree leaked git stdout into its return value. Env master (6053c56, -Committed) left running for the user; test envs/images removed.
**Decision (user, 2026-11-19): shorter env names.** Get-EnvName drops a leading feature/ bugfix/ hotfix/ fix/ story/ chore/ (case-insensitive); max 30 (was 40); longer -> 25 chars cut back to a word boundary + '-' + 4 hex SHA-1 (was 33 + 6 hex). Example: feature/14074-introduce-theme-specific-navigation -> 14074-introduce-theme-9dc9. Pester 25/25 (new cases: prefixes, word boundary, 30 exact, single long word, chore/platform reserved). Spec 4.3 + plan Global Constraints updated. Before: removed env feature-demo-deploy-from-feature-branch (containers, volume, db, role, worktree, images 692189658d19) at user request.
Stage 2 Task 6 done, awaiting user review: deploy/destroy.ps1, list.ps1, db-update.ps1 [-Restore], db-reset.ps1, db-connect.ps1 [-Psql]; module: Get-EnvState (tag/branch/commit/source/deployed-at/replicas/keys mode/password from api+web containers), Get-DeployedEnvs (+ databases without env as '(database only)', by env name), Invoke-EnvDbUpdate/DbReset/DbConnect/Destroy; shared Set-EnvDatabase/Test-DbRole now also used by deploy. Fix: ConvertFrom-Json turned ee.deployed-at into DateTime -> labels normalized back to ISO UTC. Verified: list (2 envs), db-connect strings (env role connects), delete demo row -> db-update -Restore brings it back (QA row kept), db-reset on web x2 -NoSharedKeys -> 5 products, new password, deployed-at kept, still web x2 and no shared keys; destroy -> containers/volume/db/role/worktree gone, master still 200; destroy twice -> ok; half-deployed (db+role+worktree, no containers) -> listed, db-update says 'deploy it first', destroy cleans. -Psql not run here (needs an interactive TTY). Pester 25/25.
Task 6 fix (user report 2026-11-23): 'docker image prune -a --filter reference=ee/*' fails (prune has no reference filter). New deploy/prune-images.ps1 + Invoke-EnvImagePrune/Select-UnusedEnvImages: removes ee/* images whose tag no deployed env (containers labelled ee.env) uses; keeps the migrator of a live env (no container, needed by db-update/db-reset), skips <none>. destroy hint/help point to it; spec 4.7 row updated. tests/deploy/ImagePrune.Tests.ps1 5 tests RED->GREEN, Pester 30/30. Real run: removed 12 stale images, kept ee/*:38f5afcf11fc of master, second run 'No unused', db-update -Env master ok.
Stage 2 Task 7 done, awaiting user review: new docs/environments.md (requirements, quick start, platform + ports, deploy modes/parameters/steps, env names, images + prune-images, list/destroy, database scripts, Data Protection scale experiment, two compose files + backward-compatible contract, troubleshooting incl. EF __EFMigrationsHistory log on first deploy, port 8080, *.localhost in PowerShell). README: PowerShell 7.2 prerequisite, multi-env quick start, docs row. CLAUDE.md docs list, development.md (Pester), architecture.md (layout rows Web.Tests, tests/deploy, deploy scripts), local-environment.md link, parent spec stage 2 Done + ee_<env>. Verified: all relative links/anchors resolve; quick start from fresh pwsh (deploy -Env qs-check -> web 200, api Healthy via curl.exe and Host header, list, destroy, prune). Next: final whole-stage review (fresh reviewer) after the user commits.
Stage 2 final review (opus, 2026-11-23): With fixes, 0 Critical. Fixed: env names ending in -api rejected (Web host would equal another env's API host), deploy re-sets the role to the running api's password (repairs drift that failed every deploy; RED reproduced), platform dashboard/Postgres bound to 127.0.0.1 (platform down+up applied). Not reproduced: Data Protection key race (7/7 one key). 6 minors deferred (listed in the final report). Pester 32/32. Awaiting user commit; then stage 2 is done, next stage per roadmap (FF1/O1/3).
**Stage 2 DONE (committed 2026-11-23).** Plan workspace .superpowers/sdd/2026-11-18-stage-2-multi-env-local deleted. Next stage per roadmap order: FF1 (feature flags via config, Microsoft.FeatureManagement) -> brainstorm first.
**Stage FF1 brainstorm (2026-11-23).** User: team does not use flags; goal = playground for situations A-F + H (G filters declined); each situation on its own flag where needed; H (life cycle) done once. Client flags: API GET /features with an allow-list (option 2, user); flags readable by the browser anyway -> allow-list + server enforcement. No -Flags = appsettings defaults of the deployed commit (user). Draft spec: docs/superpowers/specs/2026-11-23-stage-ff1-feature-flags-design.md (flags ProductStats B/D/E, NewBadge C, ProductCount H; -Flags with Api:/Web: prefix; env files from label ee.flags). Builder check running, then to user for approval.
FF1 builder check done; Claude's calls added to the spec: ProductCount life cycle = 3 tasks each ending with a user commit + H walkthrough; stats failure other than 404 keeps the products and shows 'unavailable right now' in the panel; -Flags validated per service (unprefixed -> services that declare it, Api:NewBadge refused). Spec handed to user for approval.
**Stage FF1 spec APPROVED 2026-11-23.** Plan written: docs/superpowers/plans/2026-11-23-stage-ff1-feature-flags.md (8 tasks: flags foundation + stats gate B; /features + widget D; Web NewBadge C + stats panel E; deploy -Flags A; ProductCount introduce/release/retire H as 3 tasks; docs). Awaiting user review + execution method.
FF1 Task 1 done, awaiting user review: Microsoft.FeatureManagement.AspNetCore 4.8.0 (Api), Contracts.FeatureFlags (ProductStats, All), ProductStatsDto, GetProductStatsQuery (one grouped query; empty -> 0/nulls), GET /products/stats [FeatureGate] -> 404 when off; Api appsettings FeatureManagement.ProductStats=false; ArchitectureTests.FeatureFlagTests (config keys known, every flag declared); CqrsTests handler count 3. Suite 135/135, format clean; deploy/local: stats 404.
FF1 Task 1 committed by user (2026-11-25). Decisions after review: CqrsTests handler check = NotEmpty, not an exact count (exact count breaks on every new handler / branch merge). Flags gate entry points only (not a security boundary; handlers stay callable from code) - a handler decorator only when a side-effect command gets a second entry point. Extra step (user): FeatureGateDocumentTransformer hides operations behind a closed [FeatureGate] from Swagger; SwaggerTests +2; suite 137/137; local doc lists /products, /products/{id} only. Next: FF1 Task 2 (/features + widget) after user commit.
FF1 Task 2 done, awaiting user review: GET /features (ClientFeatureFlags allow-list -> {"productStats":bool}, camelCase keys, Cache-Control no-store, CORS) + widget stats bar (fetch /features once in parallel with /products; flag on -> /products/stats line above the table; any failure -> no bar, no error box). FeaturesTests 5; suite 143/143. Checked in browser on deploy/local (api flag via temp compose override): off -> no bar; on -> '7 products · average 9.80 · newest ...', updates after Add; /features blocked or stats 500 -> no bar, no error, products listed.
FF1 Task 2 revised after user review (decision: a design is fixed, so layout must depend on flags, not on which data arrives first): widget waits for /features before the first render (2 s timeout -> flags off, late answer ignored), content invisible until then, stats place reserved ('Loading statistics...'), products + stats in parallel, stats failure keeps the place with 'Statistics are unavailable right now.'. Browser-checked with delayed/failed requests: table top never moves once visible.
FF1 extra (user, before Task 2 commit): live flags for deploy/local - optional config/flags.json in Api+Web (ServiceDefaults.AddFlagsFile, reloadOnChange, wins over env), local compose mounts deploy/local/config/{api,web} read-only + polling file watcher (Windows -> Linux container has no change events). flags.json git-ignored, flags.example.json committed. Change picked up in ~8 s without restart. Local only: multi-env flags stay deploy-time (label ee.flags must tell the truth). Docs: local-environment.md "Feature flags without a restart". Suite 143/143.
FF1 Task 3 done, awaiting user review: Web flags NewBadge (situation C, <feature> tag helper, products < 24 h) + stats panel (situation E): Web flag ProductStats on -> stats fetched in parallel with products; API 404 -> 'Statistics are not available (feature off in the API).', other failure/timeout -> 'Statistics are unavailable right now.', products always shown. FeatureFlagPageTests 8; suite 151/151. Live check via deploy/local flags.json (web/api combos) correct; one unexplained wrong result right after up.ps1 not reproduced (watch in Task 4).
- 2026-11-25: FF1 Task 3 extra: arch test `All_ListsEveryFlagConstant_ExactlyOnce` (every `FeatureFlags` constant is in `All`, no duplicates; previously a forgotten constant went unnoticed or failed with a misleading message).

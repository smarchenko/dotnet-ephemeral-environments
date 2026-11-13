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

## Stage roadmap (APPROVED 2026-11-11)
0. App skeleton: MVC Web + API (Swagger) + EF Core/Postgres + JS widget + CORS + runtime config; one env via docker compose
1. Migrations as separate step (EF migration bundle `migrator` image) + seed
2. Multi-env locally: Traefik, `<env>.localhost`/`<env>-api.localhost`, shared Postgres DB-per-env, `deploy`/`destroy -Env x` scripts
O1. OpenTelemetry + ServiceDefaults + Aspire Dashboard + traceparent from widget
3. Bruno collection (per-env baseUrl)
4a. migration-safety check (EF ops expand/contract + `[ContractMigration]` + Squawk warnings)
4b. API contract safety (oasdiff vs main)
5a. GitHub Actions CI: build/format/tests+coverage/migration-safety/oasdiff PR checks, PR template, CODEOWNERS, rulesets (no deploy)
5b. Security & supply chain: gitleaks, CodeQL, Dependabot, Trivy, hadolint, actionlint+zizmor, actions pinned by SHA
Q. Review comments → analyzer rules (user's PDP item)
AI. (optional) AI code review
X. (optional) Playwright E2E, SBOM, PR title convention, license check
6a. Hetzner VPS via Terraform + cloud-init (wildcard DNS, TLS) + terraform fmt/validate, tflint, trivy config
6b. Preview deploy from GitHub on PR open/push/close + manual re-run + post-deploy smoke (health + Bruno `bru run`)
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
Stage 0 in progress (native). **Tasks 1–3 done** (scaffold; Domain + Data + InitialCreate; Products API + ProblemDetails + Testcontainers tests; reworked to CQRS level 2 with `EphemeralEnvs.Application`; domain invariants; architecture tests (NetArchTest); price ≤ 2 decimals; read side read-only at 3 levels (IReadDb for handlers, ReadDbContext NoTracking + SaveChanges throws, DB session default_transaction_read_only — decided 2026-11-13); `[ProductName]`/`[Price]` + contract tests; queries project to DTO, results never domain types (decided 2026-11-13: standard .NET projection, no separate read model; Dapper-style read model → C3); 54 tests) — Task 3 awaiting user review/commit. Tests need no Docker settings: PostgresFixture uses the Linux engine pipe on Windows (DOCKER_HOST in shell breaks the docker CLI — 2 vs 4 slashes).
Next: Task 4 (health probes, CORS, Swagger flag, environment name, JSON logs). Ledger: .superpowers/sdd/2026-11-11-stage-0-app-skeleton/progress.md

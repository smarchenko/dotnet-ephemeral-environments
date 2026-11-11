# Branch-based deployment PoC — STATUS

> Living file. Update after every decision / completed step. Read this first on session start.

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
3. Bruno collection (per-env baseUrl)
4. migration-safety check (EF ops expand/contract + `[ContractMigration]` + Squawk warnings)
5. GitHub Actions CI: build + migration-safety PR check (no deploy)
6a. Hetzner VPS via Terraform + cloud-init (wildcard DNS, TLS)
6b. Preview deploy from GitHub on PR open/push/close + manual re-run
7. Nightly cleanup
K1. Helm chart + local k3d (namespace per env, 2 replicas, probes)
K2. k3s on Hetzner + GitHub preview deploy to k8s
8. Azure DevOps pipelines (same scripts, `ado-` prefix)
9. (optional) Azure hosting: Bicep vs Terraform
10. (optional) Umbraco on same infra
## Design (in progress)
- §1 Repo/projects APPROVED: .NET 10; `src/Poc.Web` (MVC + wwwroot/js/widget.js), `src/Poc.Api`
  (controllers + Swagger UI), `src/Poc.Domain` (entities, no deps), `src/Poc.Data` (DbContext, Fluent
  configs, Migrations, Npgsql), `src/Poc.Contracts` (DTOs); `tests/Poc.Api.Tests` (WebApplicationFactory
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
- [ ] Design sections (in progress: overall architecture + stage 0)
- [ ] Approaches 2-3 + recommendation
- [ ] Design sections approved
- [ ] Spec written: docs/superpowers/specs/YYYY-MM-DD-branch-deployment-design.md
- [ ] Implementation plan (writing-plans)
- [ ] Phase A implemented
- [ ] Phase B implemented
- [ ] Umbraco experiment

## Next step
1. ~~Move to own folder + git init~~ (done 2026-11-11).
2. Continue brainstorm: env lifecycle (create/update/destroy, stale cleanup) and addressing
   (`<env>.localhost` locally via reverse proxy; wildcard DNS later).

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
  **block irreversible/destructive schema changes unless explicitly allowed**. (approach being discussed)

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
- [ ] Clarifying questions (in progress — CI + repo + lifecycle + addressing + Web type + DB placement + migrations + seed decided; next: migration safety check approach)
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

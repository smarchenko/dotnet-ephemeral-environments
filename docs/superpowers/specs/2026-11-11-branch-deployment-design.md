# Branch-based deployment PoC — Design

- Date: 2026-11-11
- Status: approved by user (2026-11-11)
- Scope: overall architecture + decisions + stage roadmap; **stage 0 specified in detail**.
  Later stages get a short design refresh (and their own plan) when they start.

## 1. Goal

Try out a flow where QA tests a feature **directly from its branch** in an ephemeral preview
environment, instead of merging into shared integration branches (dev/test/uat).
It is a PoC, but kept as close to a real project as is reasonable.

Target flow: open PR from `feature/x` → env (Web + API + its own DB) is created automatically at a
unique URL → each push updates it → PR closed or merged → env is destroyed.

### Working style (hard constraint)

The PoC is built in **small stages**. Each stage can be run and tried on its own, and has its own
plan and a short "how to try it" section. After each stage we stop so the user can look and
experiment before the next stage starts. Never deliver several new technologies at once.

## 2. Decisions

| Area | Decision |
|---|---|
| Phasing | Local first (Docker + Traefik + `*.localhost`), then real CI + hosting reachable by QA |
| App | Generic .NET 10: ASP.NET Core MVC Web + ASP.NET Core Web API + EF Core. Umbraco is an optional later experiment |
| DB | PostgreSQL. **One shared server, a separate database (and user) per env**. `DROP DATABASE` on destroy |
| Migrations | **A separate deploy step, never on app startup**: an EF Core migration bundle in a `migrator` image, run as a one-off container before Web/API roll out. If it fails, the deploy stops and the old version keeps running |
| Seed | Empty schema + deterministic seed (data kept in the repo), run as a step after migrations |
| Migration safety | PR check `migration-safety`: a .NET tool inspects the EF `MigrationBuilder.Operations` of new migrations (compared with `main`) and classifies them as expand or contract. Contract ops (drop, rename, type change, NOT NULL without default…) are allowed only with `[ContractMigration("reason")]`, and such a migration must be in **its own PR** (no other migrations, no app code). **Squawk** on the generated SQL gives warnings only (locking etc.). CODEOWNERS on `Migrations/` |
| Lifecycle | Create on PR open (draft too), update on push, destroy on PR close/merge. Manual re-run/redeploy. **Nightly cleanup** of envs whose PR is closed or that were not updated for N days |
| Addressing | Separate subdomains: Web `<env>.<domain>`, API `<env>-api.<domain>` (one wildcard level, so one DNS record and one cert). Locally `<domain>` = `localhost` |
| Env naming | Platform prefix + sanitized branch: `gh-feature-x`, `ado-feature-x` |
| CI | Both GitHub Actions and Azure DevOps Pipelines. **One repo, two remotes** (`github`, `azure`). Deploy logic lives in shared `deploy/` scripts; pipelines only call them |
| Hosting | Hetzner: **one long-lived VPS** created by **Terraform** (`hcloud` provider) + **cloud-init**, run from the laptop, local state (gitignored). Preview envs are containers deployed onto it by CI over SSH. No server per PR. Azure (Bicep vs Terraform) is optional later |
| QA tools | Swagger UI enabled in preview envs (by flag, not `IsDevelopment()`). **Bruno** collection in the repo |
| Observability | Stage 0: `TraceId`/`SpanId` in JSON console logs (built-in). Later: OpenTelemetry + Aspire Dashboard (O1), Grafana `otel-lgtm` (O2) |
| Kubernetes | Optional stages after the Docker flow works: Helm + k3d locally (K1), k3s on Hetzner (K2) |
| Repos | **GitHub: public repo on the personal account** (showcase project; unlimited Actions, free secret scanning + push protection). Azure DevOps org + Azure Repos are created closer to stage 8 (request free hosted parallelism early, or use a self-hosted agent) |
| Security (public repo) | No secrets, server IP or keys in the repo: everything lives in GitHub Secrets/Variables. **Fork PRs never deploy**; `pull_request_target` with PR code checkout is never used; no self-hosted runners on the public repo. Server: SSH key only (no password login), Hetzner Cloud Firewall (22/80/443, SSH optionally limited to the owner's IP), Postgres not exposed, preview envs behind Traefik basic auth |
| Code quality | Stage 0: `.editorconfig`, `Directory.Build.props` (`Nullable`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`), `dotnet format --verify-no-changes` locally. Stage 0 also turns on built-in **NuGetAudit** (vulnerable packages fail the build). Stage 5a: format/build/coverage checks, PR template, CODEOWNERS, rulesets on `main`. Stage 5b: security and supply chain (see §3). Stage Q: 2–3 recurring review comments turned into analyzer rules (user's PDP goal). AI review optional |
| Local Docker | **Linux engine via `DOCKER_CONTEXT=desktop-linux`** (it runs next to the Windows-containers engine used for Sitecore, so no switching is needed). Scripts run a preflight check that `docker info` reports `linux`. PoC ports are configurable and **avoid 80/443** (Sitecore). Traefik later on `8080` |

## 3. Stage roadmap

| # | Stage | New things |
|---|---|---|
| 0 | App skeleton | MVC Web, API + Swagger, Domain/Data/Contracts, EF Core + Postgres, JS widget, CORS, runtime config, health probes; one env via docker compose |
| 1 | Migrations as a separate step + seed | EF migration bundle, `migrator` image, seed step. **Local dev uses the same path**: compose service `migrator` runs before `api` (`depends_on: condition: service_completed_successfully`), so `docker compose up` = DB → migrate → seed → apps. When running from the IDE: `scripts/db-update.ps1` (`docker compose run --rm migrator`) and `scripts/db-reset.ps1` (drop → migrate → seed). No `EnsureCreated`, no migrate-on-startup even in Dev |
| 2 | Multi-env locally | Traefik, `<env>.localhost:8080` / `<env>-api.localhost:8080`, shared Postgres with DB-per-env, `deploy`/`destroy -Env x` scripts; try `--scale api=2 web=2` (→ Data Protection keys) |
| O1 | Observability | `Poc.ServiceDefaults` + OpenTelemetry (ASP.NET Core, HttpClient, Npgsql), Aspire Dashboard, `traceparent` sent from the widget, `deployment.environment` tag |
| 3 | Bruno | Collection with a per-env `baseUrl` |
| 4a | Migration safety | Expand/contract tool, `[ContractMigration]`, Squawk warnings; demo rename via expand → switch → contract PRs |
| 4b | API contract safety | **oasdiff**: compare the PR's OpenAPI document with `main`'s; breaking changes (removed endpoint/field, new required param…) fail unless explicitly allowed. The API counterpart of migration-safety |
| 5a | GitHub Actions CI | PR checks (no deploy): build (warnings as errors), tests + **coverage report** (coverlet + ReportGenerator in the job summary, no hard threshold yet), `dotnet format --verify-no-changes`, `migration-safety`, `oasdiff`; PR template, CODEOWNERS, rulesets/branch protection on `main` |
| 5b | Security & supply chain | **gitleaks** (secrets), **CodeQL** (SAST for C#, JS and workflows; free on public repos), **Dependabot** (nuget, docker, github-actions; grouped weekly), **Trivy** (scan built images: fail on fixable HIGH/CRITICAL), **hadolint** (Dockerfiles), **actionlint + zizmor** (workflow correctness and security), all third-party actions **pinned by commit SHA** |
| Q | Review comments → rules | 2–3 recurring review comments from the user's real project → `.editorconfig` severity / existing analyzers (Roslynator, Meziantou, BannedApiAnalyzers) / custom Roslyn analyzer with tests; demo PR fails in CI |
| AI | (optional) AI code review | Claude Code GitHub Action or Copilot review; compare with the analyzers |
| X | (optional) Extra checks | Playwright E2E smoke of the Web on the preview env, SBOM (CycloneDX) attached to images, PR title convention (Conventional Commits), license check of dependencies |
| 6a | Hetzner VPS | Terraform + cloud-init: Docker, Traefik, Postgres, wildcard DNS, TLS. Checks: `terraform fmt -check`, `terraform validate`, **tflint**, Trivy config scan (IaC misconfig) |
| 6b | Preview deploy from GitHub | On PR open/push/close + manual re-run; link posted to the PR. **Post-deploy smoke**: wait for `/health/ready`, then run the Bruno collection (`bru run`) against the preview env; the deploy job fails if smoke fails |
| 7 | Nightly cleanup | Scheduled job that destroys orphaned/stale envs |
| K1 | Kubernetes locally | Helm chart, k3d, namespace per env, 2 replicas, probes, rolling update; scripts get `-Target compose\|k8s`. Checks: `helm lint`, **kubeconform** (manifest schema validation) |
| K2 | Kubernetes on Hetzner | k3s (probably an 8 GB VPS), GitHub preview deploy to k8s |
| O2 | Grafana | `otel-lgtm` on the server, per-env dashboards |
| 8 | Azure DevOps | Same scripts, `ado-` prefix, branch policies, compare with GitHub |
| 9 | (optional) Azure | Hosting with Bicep vs Terraform |
| 10 | (optional) Umbraco | Second app on the same infra |

Topics for later, discussed when their stage comes up: QA access/auth on previews (e.g. basic
auth), cost/limits (max concurrent envs), whether a shared staging env stays for integration
testing, remote Terraform state.

## 4. Architecture (target)

```
                 ┌──────────────── host (laptop / Hetzner VPS) ────────────────┐
 Browser ──────► │ Traefik  *.domain                                           │
                 │   <env>.domain      ──► web-<env>  ──(internal)──► api-<env> │
                 │   <env>-api.domain  ──► api-<env>  ──► postgres (shared)     │
                 │                                       └─ db: app_<env>      │
                 └──────────────────────────────────────────────────────────────┘
 CI (GitHub / ADO) ── ssh ──► deploy/deploy.sh <env>:  create DB → migrator → seed → up web/api
                              deploy/destroy.sh <env>: down → DROP DATABASE
```

Images are **built once** per commit. Everything env-specific comes from environment variables at
runtime.

## 5. Stage 0 — detailed design

### 5.1 Repository layout

```
/
├─ src/
│  ├─ Poc.Web/          ASP.NET Core MVC (+ wwwroot/js/widget.js), Dockerfile
│  ├─ Poc.Api/          ASP.NET Core Web API (controllers) + Swagger UI, Dockerfile
│  ├─ Poc.Domain/       entities, no dependencies
│  ├─ Poc.Data/         DbContext, IEntityTypeConfiguration<>, Migrations/, Npgsql
│  └─ Poc.Contracts/    DTOs shared by Web and Api
├─ tests/
│  └─ Poc.Api.Tests/    WebApplicationFactory + Testcontainers PostgreSQL
├─ deploy/local/docker-compose.yml
├─ docs/
├─ Directory.Build.props, Directory.Packages.props   (central package management)
├─ .editorconfig, .gitignore, .dockerignore, README.md
└─ Poc.sln
```

References: `Api → Domain, Data, Contracts`; `Data → Domain`; `Web → Contracts`.
No full Clean Architecture (no Application layer, no CQRS). Domain → DTO mapping is written by hand.

### 5.2 Domain

`Product { Guid Id; string Name; decimal Price; DateTimeOffset CreatedAt }`.
Fluent config: `Name` required, max 200; `Price` `numeric(18,2)`; table `products`.
The initial migration `InitialCreate` lives in `Poc.Data`.

### 5.3 API

- Endpoints (controllers): `GET /products`, `GET /products/{id}` (404 if missing),
  `POST /products` (validation: `Name` required, `Price > 0` → 400; on success 201 + Location).
- Errors are returned as `ProblemDetails` (RFC 9457) for 400/404/500.
- Health: `/health/live` (no dependencies, always 200 while the process runs) and `/health/ready`
  (checks the DB → 503 when the DB is down).
- Swagger UI at `/swagger`, enabled when `Swagger__Enabled=true`. The OpenAPI description shows
  `App__EnvironmentName`.
- CORS: an explicit list from `Cors__AllowedOrigins` (comma-separated). `*` is never used.

API configuration:

| Key | Stage 0 value (compose) |
|---|---|
| `ConnectionStrings__Default` | `Host=postgres;Database=poc;Username=poc;Password=poc` |
| `Cors__AllowedOrigins` | `http://localhost:5080` |
| `Swagger__Enabled` | `true` |
| `App__EnvironmentName` | `local` |

### 5.4 Web

- `/`: product list fetched **server-side** through a typed `HttpClient` (`Api__InternalBaseUrl`).
- `/Home/Widget`: the same list plus an "add product" form, rendered by `wwwroot/js/widget.js`,
  which calls the API **from the browser**. The browser-facing API URL comes from
  `Api__PublicBaseUrl`; the layout writes it into `<meta name="api-base-url">`, so the image has
  no build-time config.
- When the API is unreachable or returns an error, the Web page shows a friendly error message
  and logs a warning instead of crashing. The widget shows its errors inline.
- The footer shows `App__EnvironmentName`.

Web configuration:

| Key | Stage 0 value (compose) |
|---|---|
| `Api__InternalBaseUrl` | `http://api:8080` |
| `Api__PublicBaseUrl` | `http://localhost:5081` |
| `App__EnvironmentName` | `local` |

### 5.5 Logging

Console logging uses the JSON formatter with scopes enabled and `ActivityTrackingOptions`
(`TraceId`, `SpanId`), so one request can be followed through Web → API in `docker logs`.
No Serilog or OpenTelemetry in stage 0.

### 5.6 Containers

- Multi-stage Dockerfiles (`sdk:10.0` → `aspnet:10.0`), the app listens on 8080 and runs as a
  non-root user.
- `deploy/local/docker-compose.yml`: `postgres` (postgres:17, named volume, healthcheck),
  `api` (host `5081` → 8080, depends on healthy postgres), `web` (host `5080` → 8080).
  Host ports can be changed through compose variables (`POC_WEB_PORT`, `POC_API_PORT`,
  `POC_DB_PORT`, default 5432 → change it if it is taken).
- Every Docker command uses the Linux engine: `$env:DOCKER_CONTEXT='desktop-linux'`.
- Migrations in stage 0 are applied **by hand** from the host (`dotnet ef database update` against
  the exposed Postgres port). The app never migrates on startup. Stage 1 replaces this with the
  `migrator` step.

### 5.7 Code quality baseline

- `.editorconfig` with C# style and naming rules at `warning` severity.
- `Directory.Build.props`: `<Nullable>enable`, `<ImplicitUsings>enable`,
  `<AnalysisLevel>latest-recommended`, `<EnforceCodeStyleInBuild>true`, `<TreatWarningsAsErrors>true`,
  `<NuGetAuditMode>all</NuGetAuditMode>` + `<NuGetAuditLevel>moderate</NuGetAuditLevel>` (vulnerable
  direct or transitive packages fail `restore`/`build`).
- The README documents `dotnet format --verify-no-changes`. CI enforcement comes in stage 5.

### 5.8 Tests

`tests/Poc.Api.Tests` (xUnit, `WebApplicationFactory<Program>`, Testcontainers PostgreSQL; schema
applied via `Database.MigrateAsync()` in the test fixture only):

- create a product, then get it by id and in the list;
- invalid `POST` → 400 ProblemDetails; unknown id → 404;
- `/health/live` → 200; `/health/ready` → 200;
- CORS: an allowed origin gets `Access-Control-Allow-Origin`, an unknown origin does not.

Testcontainers must use the Linux engine: `DOCKER_HOST=npipe:////./pipe/dockerDesktopLinuxEngine`
(to be verified during implementation; document the working setting in the README).
No Web tests in stage 0; the Web is checked by hand with the list below.

### 5.9 Definition of done / how to try

1. `$env:DOCKER_CONTEXT='desktop-linux'; docker compose -f deploy/local/docker-compose.yml up --build -d`
2. `dotnet ef database update` (command documented in the README).
3. `http://localhost:5080`: the list is rendered server-side and the footer shows `local`.
4. `http://localhost:5080/Home/Widget`: the widget loads and adds a product through the API, and a
   CORS request is visible in DevTools.
5. `http://localhost:5081/swagger` works; `/health/live` and `/health/ready` return 200.
6. `docker compose stop postgres` → `/health/ready` returns 503 while `/health/live` stays 200;
   the Web shows a friendly error message (API returned 500 ProblemDetails), not a crash page.
7. Logs of web and api for one request share the same `TraceId`.
8. `dotnet test` is green; `dotnet build` has no warnings; `dotnet format --verify-no-changes` passes.
9. The README has a "Stage 0: how to run" section.

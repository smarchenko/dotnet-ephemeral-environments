# Stage 2 — Several environments locally Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `deploy.ps1 -Branch x` builds a branch's commit once and runs it as its own environment (own compose project, own database + role) behind Traefik at `http://<env>.localhost:8080`; `destroy`/`list`/`db-*` manage envs; a server-side Web form demonstrates Data Protection with 2 Web replicas.

**Architecture:** Long-lived platform compose project `ee-platform` (Traefik + shared Postgres, network `ee-platform`); one compose project `ee-<env>` per env from `deploy/env/compose.yml` of the deployed commit. PowerShell 7 scripts in `deploy/` with a shared module; state lives only in Docker (container labels) and git (worktrees in `.ee/worktrees/<env>`).

**Tech Stack:** PowerShell 7.2+, Pester 5, Docker Compose v2, Traefik v3, PostgreSQL 17, ASP.NET Core 10 MVC (antiforgery, Data Protection), xUnit + `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** `docs/superpowers/specs/2026-11-18-stage-2-multi-env-local-design.md`

## Global Constraints

- **Never commit** (CLAUDE.md): every "Commit" step = stop, leave changes in the working tree, report to the user (what was added, how it works, how to run/test it).
- Docker: Linux engine; scripts default `DOCKER_CONTEXT=desktop-linux` unless `DOCKER_HOST` is set, restore the caller's env afterwards; preflight `docker info` → `OSType=linux`. Never bind host 80/443.
- Ports: Traefik `${EE_PROXY_PORT:-8080}`, dashboard `${EE_DASHBOARD_PORT:-8090}`, platform Postgres `${EE_PLATFORM_DB_PORT:-5442}` (local only). Stage 1 (`deploy/local/`, 5080/5081/5432) stays unchanged.
- Names: compose project `ee-<env>`; database and role `ee_<env>` (`-` → `_`); hosts `<env>.${EE_DOMAIN}` / `<env>-api.${EE_DOMAIN}`, `EE_DOMAIN=localhost`; image repos `ee/api`, `ee/web`, `ee/migrator`, tags `<sha12>` or `wt-<env>`.
- Env name: lowercase, runs of non-`[a-z0-9]` → `-`, trim `-`; max 40 → first 33 (trailing `-` trimmed) + `-` + 6 hex of SHA-1 of the full sanitized name; empty or `platform` → error.
- Labels on every env service: `ee.env`, `ee.branch`, `ee.commit` (sha or `working-tree`), `ee.deployed-at` (UTC ISO 8601).
- No secrets written to files; DB password = 32 hex chars from a CSPRNG per deploy, passed via process env to compose. Local platform admin password defaults to `postgres` (`EE_PG_ADMIN_PASSWORD`).
- Connection strings keep `GSS Encryption Mode=Disable`.
- Build 0 warnings, `dotnet format --verify-no-changes` clean, all tests green.
- Docs: README short (quick start + links); new `docs/environments.md`; CLAUDE.md docs list; `docs/STATUS.md` after every step.

## Review Focus

1. Failed `migrate`/`seed` on re-deploy → old env keeps serving (password not rotated before success). Pinned by manual DoD 8 in Task 5; the order lives in one function `Invoke-EnvDeploy` so a reviewer can read it top-down.
2. `web` resolving `api` of another env on the shared network → `Api__InternalBaseUrl` must be `http://<env>-api:8080` with the alias only on the env's default network. Pinned by Task 5 DoD 2 (two envs with different code).
3. Named volume `dpkeys` not writable by the non-root user → keys fall back silently / exception at startup. Pinned by Task 2 Dockerfile step + Task 5 scale check.
4. Explicit `-Env` with uppercase or `feature/x` → must error with the normalized value, not silently normalize. Pinned by Pester case in Task 3.
5. `destroy` of a half-deployed env (no containers, db exists; or worktree missing) → completes without error. Pinned by Task 6 manual check.

---

### Task 1: Web — server-side "Add product" form with antiforgery

**Files:**
- Create: `tests/EphemeralEnvs.Web.Tests/EphemeralEnvs.Web.Tests.csproj`, `WebFactory.cs`, `StubApiHandler.cs`, `CreateProductFormTests.cs`
- Create: `src/EphemeralEnvs.Web/Views/Home/Create.cshtml`, `src/EphemeralEnvs.Web/Models/CreateProductViewModel.cs` (only if the view needs more than `CreateProductRequest` + error message; otherwise bind `CreateProductRequest` directly)
- Modify: `src/EphemeralEnvs.Web/Services/ProductsApiClient.cs`, `Controllers/HomeController.cs`, `Program.cs` (global `AutoValidateAntiforgeryTokenAttribute`, `public partial class Program;`), `Views/Shared/_Layout.cshtml` (nav link "Add (form)"), `EphemeralEnvs.slnx`

**Interfaces:**
- Produces: `ProductsApiClient.CreateProductAsync(CreateProductRequest request, CancellationToken ct) : Task<CreateProductResult>`; `public sealed record CreateProductResult(bool Created, IReadOnlyDictionary<string, string[]> Errors)` (in `Services/`). 201 → `Created=true`, empty errors; 400 with ValidationProblemDetails → `Created=false`, its `errors`; other statuses/network → `HttpRequestException` / `TaskCanceledException` (as `GetProductsAsync`).
- Produces: `WebFactory(IDictionary<string,string?>? settings = null, HttpMessageHandler? apiHandler = null) : WebApplicationFactory<Program>` — sets `Api:InternalBaseUrl=http://api.test/`, `Api:PublicBaseUrl=http://api.test/`, `App:EnvironmentName=test`, swaps the `ProductsApiClient` primary handler. Task 2 reuses it.
- Produces: test helper `static Task<(string Token, string Cookie)> GetFormTokenAsync(HttpClient client)` — GETs `/Home/Create`, returns the hidden `__RequestVerificationToken` value and the antiforgery `Set-Cookie` pair.

- [ ] **Step 1: Create the test project** (xUnit, `Microsoft.AspNetCore.Mvc.Testing`, references Web + Contracts; same csproj shape as `tests/EphemeralEnvs.Api.Tests`), add to `EphemeralEnvs.slnx`, add `public partial class Program;` to Web `Program.cs`.

- [ ] **Step 2: Write the failing tests** in `CreateProductFormTests.cs`:

```csharp
[Fact] Post_WithoutToken_Returns400()                       // POST form fields, no token → 400, stub API never called
[Fact] Post_WithToken_Valid_CallsApi_RedirectsToIndex()     // stub returns 201 → 302 Location "/", stub saw POST /products with {name, price}
[Fact] Post_ApiValidationError_ShowsFieldErrors()           // stub 400 {"errors":{"Name":["Name is required."]}} → 200, body contains "Name is required."
[Fact] Post_ApiUnavailable_ShowsFriendlyMessage()           // stub throws HttpRequestException → 200, body contains HomeController.ApiUnavailableMessage
[Fact] Get_Create_RendersFormWithToken()                    // 200, body contains "__RequestVerificationToken"
```
Client: `CreateClient(new() { AllowAutoRedirect = false })`.

- [ ] **Step 3: Run** `dotnet test tests/EphemeralEnvs.Web.Tests` — Expected: FAIL (404 on `/Home/Create`, `CreateProductAsync` missing → compile error first; add the method signature throwing `NotImplementedException` so the tests compile and fail on behavior).

- [ ] **Step 4: Implement** `CreateProductAsync` (POST `products` as JSON; read `HttpValidationProblemDetails` on 400), `HomeController.Create()` GET and `Create(CreateProductRequest)` POST (ModelState from local validation attributes first, then API errors via `ModelState.AddModelError(key, msg)`; 201 → `RedirectToAction(nameof(Index))`; API unavailable → `ViewData["Error"] = ApiUnavailableMessage`, same `when` filter as `Index`), the view (form tag helper, `asp-validation-for`), global `AutoValidateAntiforgeryTokenAttribute` in `AddControllersWithViews`, nav link.

- [ ] **Step 5: Run** `dotnet test` (whole suite) — Expected: all green (120 + 5). `dotnet build` 0 warnings, `dotnet format --verify-no-changes` clean.

- [ ] **Step 6: Manual** `./deploy/local/up.ps1` → `http://localhost:5080/Home/Create` adds a product, empty name shows the error, list shows the new product.

- [ ] **Step 7: Stop and report** (no commit). Update `docs/STATUS.md`, `docs/local-environment.md` (checks: the form), `docs/development.md` (tests: 5 projects).

### Task 2: Web — shared Data Protection keys + replica name in the footer

**Files:**
- Create: `src/EphemeralEnvs.Web/Options/DataProtectionSetup.cs` (or an extension in `Program.cs` region — one static method)
- Create: `tests/EphemeralEnvs.Web.Tests/DataProtectionTests.cs`
- Modify: `src/EphemeralEnvs.Web/Program.cs`, `Views/Shared/_Layout.cshtml`, `src/EphemeralEnvs.Web/Dockerfile`

**Interfaces:**
- Consumes: `WebFactory`, `GetFormTokenAsync` (Task 1).
- Produces: config key `DataProtection:KeysPath` (env var `DataProtection__KeysPath`); set and non-empty → `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(path)).SetApplicationName("EphemeralEnvs.Web")`; empty/unset → ASP.NET defaults. Image directory `/keys` owned by `$APP_UID`. Task 5's compose relies on both.

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact] TokenFromOneInstance_AcceptedByAnother_WhenKeysShared()
// two WebFactory instances, both DataProtection:KeysPath = same new temp dir; token+cookie from A; POST to B (HandleCookies=false, Cookie header set) → 302
[Fact] TokenFromOneInstance_RejectedByAnother_WhenKeysNotShared()
// different temp dirs → 400
[Fact] Footer_ShowsInstanceName()   // GET / body contains Environment.MachineName
```

- [ ] **Step 2: Run** `dotnet test tests/EphemeralEnvs.Web.Tests --filter DataProtectionTests` — Expected: first test FAIL (400: each factory has its own ephemeral key ring), second passes already (it pins the problem), footer FAIL.

- [ ] **Step 3: Implement** the keys setup and footer `Environment: <env> · <Environment.MachineName>`; Web Dockerfile final stage before `USER`: `RUN mkdir /keys && chown $APP_UID /keys`.

- [ ] **Step 4: Run** `dotnet test` — Expected: all green; build 0 warnings; format clean.

- [ ] **Step 5: Verify the image**: `docker build -f src/EphemeralEnvs.Web/Dockerfile -t ee-web-check .` then `docker run --rm -v ee-check-keys:/keys -e DataProtection__KeysPath=/keys -e Api__InternalBaseUrl=http://x/ -e Api__PublicBaseUrl=http://x/ ee-web-check` and request `/` once (or wait for startup log) → `docker run --rm -v ee-check-keys:/k alpine ls /k` shows `key-*.xml`. Remove the volume.

- [ ] **Step 6: Stop and report.** STATUS.

### Task 3: Deploy module — env name, preflight, docker/git helpers (Pester)

**Files:**
- Create: `deploy/lib/EphemeralEnvs.Deploy.psm1`, `tests/deploy/EnvName.Tests.ps1`
- Modify: `.gitignore`, `.dockerignore` (add `.ee/`)

**Interfaces:**
- Produces (exported from the module):
  - `Get-EnvName -Branch <string> | -Env <string>` → `[string]`; `-Env` not in normal form → throws `"Env name '<given>' is not valid; use '<normalized>'."`.
  - `ConvertTo-DbName -EnvName <string>` → `ee_<env with - → _>`.
  - `Use-DockerEnv -ScriptBlock <scriptblock>` → sets `DOCKER_CONTEXT=desktop-linux` unless `DOCKER_HOST` set, runs preflight (`docker info --format '{{.OSType}}'` = `linux`, else throw), restores env + location (repo root as cwd).
  - `Invoke-Native -Title <string> -ScriptBlock <scriptblock>` → like stage-1 `Invoke-Step` (throws on non-zero exit).
  - `Resolve-Commit -Branch <string>` → `[pscustomobject]@{ Sha; Sha12; BranchName }` (local ref, else unique `<remote>/<name>` over `git remote`; ambiguous → throw asking for `-Branch <remote>/<name>`; `BranchName` = without remote prefix; not found → throw with "git fetch" hint).
  - `New-DbPassword` → 32 lowercase hex from `[System.Security.Cryptography.RandomNumberGenerator]`.
  - `Invoke-PlatformSql -Sql <string> [-Database <string>]` → `docker exec -i <ee-platform postgres container> psql -U postgres -v ON_ERROR_STOP=1 -d <db>` with SQL on stdin; returns output.
  - `Get-EnvContainers -EnvName <string>` → containers of project `ee-<env>` with their `ee.*` labels and env (`docker ps -a --filter label=com.docker.compose.project=ee-<env>` + `docker inspect`).
  - `Get-RunningApiPassword -EnvName <string>` → password parsed from the running api container's `ConnectionStrings__Default`, or `$null`.
  - `Get-RepoRoot` → repo root path.

- [ ] **Step 1: Install Pester 5** if `Get-Module -ListAvailable Pester` shows only 3.x: `Install-Module Pester -MinimumVersion 5.5 -Scope CurrentUser -Force -SkipPublisherCheck`. Note it in `docs/development.md` (prerequisite for script tests).

- [ ] **Step 2: Write the failing Pester tests** (`tests/deploy/EnvName.Tests.ps1`, `BeforeAll { Import-Module $PSScriptRoot/../../deploy/lib/EphemeralEnvs.Deploy.psm1 -Force }`):

```powershell
Get-EnvName -Branch 'feature/Add-X_2'      | Should -Be 'feature-add-x-2'
Get-EnvName -Branch '--Fix//bug--'         | Should -Be 'fix-bug'
Get-EnvName -Branch 'master'               | Should -Be 'master'
# 41+ chars → 40 chars, 33-char head + '-' + 6 hex; same input → same output; two long names differing only at the end → different
(Get-EnvName -Branch ('a' * 50)).Length    | Should -Be 40
Get-EnvName -Branch ('a' * 50)             | Should -Match '^a{33}-[0-9a-f]{6}$'
{ Get-EnvName -Branch '///' }              | Should -Throw '*empty*'
{ Get-EnvName -Branch 'platform' }         | Should -Throw '*reserved*'
Get-EnvName -Env 'feature-x'               | Should -Be 'feature-x'
{ Get-EnvName -Env 'Feature/X' }           | Should -Throw "*use 'feature-x'*"
ConvertTo-DbName -EnvName 'feature-x'      | Should -Be 'ee_feature_x'
New-DbPassword                             | Should -Match '^[0-9a-f]{32}$'
```
Also: a 34th char that is `-` → head is trimmed (`('a'*32)+'-b'*10` → head without trailing `-`).

- [ ] **Step 3: Run** `pwsh -NoProfile -c "Invoke-Pester tests/deploy -Output Detailed"` — Expected: FAIL (module/functions missing).

- [ ] **Step 4: Implement** the module (all exported functions above; only the pure ones are unit-tested). Add `.ee/` to `.gitignore` and `.dockerignore`.

- [ ] **Step 5: Run** Pester — Expected: all pass. Smoke the impure helpers by hand: `Import-Module …; Use-DockerEnv { docker info --format '{{.OSType}}' }` → `linux`; `Resolve-Commit -Branch master`.

- [ ] **Step 6: Stop and report.** STATUS.

### Task 4: Platform — Traefik + shared Postgres

**Files:**
- Create: `deploy/platform/compose.yml`, `deploy/platform.ps1`
- Modify: `deploy/lib/EphemeralEnvs.Deploy.psm1` (add `Start-Platform`)

**Interfaces:**
- Consumes: `Use-DockerEnv`, `Invoke-Native` (Task 3).
- Produces: compose project `ee-platform`, network `ee-platform` (`name: ee-platform`), Postgres service `postgres` reachable as `postgres` on that network, superuser `postgres` / `${EE_PG_ADMIN_PASSWORD:-postgres}`, volume `ee-platform-pgdata`; `Start-Platform` → `docker compose -p ee-platform -f deploy/platform/compose.yml up -d --wait --no-recreate`. `platform.ps1 up | down [-RemoveData]`.

- [ ] **Step 1: Write** `compose.yml`: Traefik `v3.x` (pin an exact current tag), args `--providers.docker=true --providers.docker.exposedByDefault=false --providers.docker.network=ee-platform --entrypoints.web.address=:80 --api.insecure=true --api.dashboard=true`, ports `${EE_PROXY_PORT:-8080}:80`, `${EE_DASHBOARD_PORT:-8090}:8080`, socket `/var/run/docker.sock:/var/run/docker.sock:ro`; Postgres 17 with healthcheck `pg_isready -U postgres`, port `${EE_PLATFORM_DB_PORT:-5442}:5432`.

- [ ] **Step 2: Write** `platform.ps1` (`param([ValidateSet('up','down')] $Action, [switch] $RemoveData)`); `up` prints dashboard URL and DB port.

- [ ] **Step 3: Verify**: `./deploy/platform.ps1 up` → both healthy; `http://localhost:8090/dashboard/` opens; `curl.exe -s -o NUL -w "%{http_code}" http://anything.localhost:8080/` → `404` (Traefik, no router); re-run `up` → nothing recreated; `down` keeps volume; `down -RemoveData` removes it.

- [ ] **Step 4: Stop and report.** STATUS.

### Task 5: Env compose + `deploy.ps1`

**Files:**
- Create: `deploy/env/compose.yml`, `deploy/deploy.ps1`
- Modify: `deploy/lib/EphemeralEnvs.Deploy.psm1` (add `Invoke-EnvDeploy` and helpers it needs)

**Interfaces:**
- Consumes: everything from Tasks 3–4; config `DataProtection__KeysPath` + `/keys` (Task 2).
- Produces:
  - `deploy.ps1 (-Branch <b> | -FromWorkingTree) [-Env <e>] [-Force] [-Rebuild] [-ApiReplicas <int>=1] [-WebReplicas <int>=1] [-NoSharedKeys]` (parameter sets `Branch` / `WorkingTree`).
  - `Get-EnvComposeArgs -EnvName <e> -ComposeFile <path>` → `@('-p', "ee-$e", '-f', $path)`; worktree path `Get-WorktreePath -EnvName <e>` → `<repo>/.ee/worktrees/<e>`. Tasks 6 reuse both.
  - `Set-EnvComposeVariables -EnvName -Tag -Password -Branch -Commit -KeysPath` → sets process env `EE_ENV, EE_TAG, EE_DOMAIN, EE_PROXY_PORT, EE_DB_PASSWORD, EE_BRANCH, EE_COMMIT, EE_DEPLOYED_AT, EE_DP_KEYS_PATH` (restored by `Use-DockerEnv`).

- [ ] **Step 1: Write** `deploy/env/compose.yml` per spec §4.6: services `migrate`/`seed` (image `ee/migrator:${EE_TAG}`, network `ee-platform` only, `restart: "no"`, `profiles: ["tools"]` so `up` never starts them), `api`, `web` (networks `ee-platform` + `default` with alias `${EE_ENV}-api` on `default` for api only), env vars, Traefik labels (`traefik.enable=true`, routers `ee-${EE_ENV}-api` / `ee-${EE_ENV}-web`, `loadbalancer.server.port=8080`, api `loadbalancer.healthcheck.path=/health/ready`, `traefik.docker.network=ee-platform`), `ee.*` labels, volume `dpkeys:/keys` on web; networks: `ee-platform: external: true`.

- [ ] **Step 2: Implement** `Invoke-EnvDeploy`, in this order (spec §4.4, §4.5, §4.7):
  1. env name (`-Env` or from branch / current branch for `-FromWorkingTree`); owner check: running containers' `ee.branch` ≠ this branch and no `-Force` → throw `"Env '<e>' runs branch '<owner>'. Use -Force to take it over."`
  2. code: `Resolve-Commit` → worktree add/checkout `--detach`; `-FromWorkingTree` → repo root, tag `wt-<env>`, commit label `working-tree`; compose file from that tree, missing → throw.
  3. images: skip if all three `ee/*:<tag>` exist (unless `-Rebuild` or working tree); else `docker build -f src/<Project>/Dockerfile -t ee/<name>:<tag> <context>` for api, web, migrator (DbTool Dockerfile).
  4. `Start-Platform`.
  5. role/db (`Invoke-PlatformSql`, idempotent: create role if missing; create database owner role if missing; `REVOKE CONNECT ON DATABASE … FROM PUBLIC`). Password: `$current = Get-RunningApiPassword`; `$new = New-DbPassword`; if `$current` is null → `ALTER/CREATE ROLE … PASSWORD $new` now and use `$new` for migrate/seed; else use `$current`.
  6. `compose run --rm migrate`, then `compose run --rm seed` (with the chosen password in `EE_DB_PASSWORD`). Failure → throw (old env untouched).
  7. if `$current` was set: `ALTER ROLE … PASSWORD $new`.
  8. `compose up -d --wait --remove-orphans --scale api=$ApiReplicas --scale web=$WebReplicas api web` with `EE_DB_PASSWORD=$new`.
  9. health wait (60 s, 2 s poll): `Invoke-WebRequest http://localhost:$port/health/ready -Headers @{Host="<env>-api.$domain"}` → 200, then `/` with `Host=<env>.$domain` → 200.
  10. print Web/API/Swagger URLs, branch, commit, database, dashboard.

- [ ] **Step 3: Write** `deploy.ps1` (params → `Use-DockerEnv { Invoke-EnvDeploy … }`).

- [ ] **Step 4: Manual DoD** (spec §5): 1 (`-Branch master`), 2 (second branch `demo-x` with a footer change; both serve their own code; data separate; dashboard lists both), 3 (re-deploy master: "images exist, skipping build", data kept), 5 (`-FromWorkingTree`), 6 (scale: `-WebReplicas 2 -NoSharedKeys` → form fails ~half the time with 400; without → always works; footer shows two host names), 8 (commit a migration that fails, e.g. raw SQL `SELECT 1/0`, deploy → stops after migrate, previous version still answers `/health/ready` 200; delete the branch afterwards), 9 (`-Env master -Branch demo-x` refused, `-Force` takes over; redeploy master afterwards). Role isolation (part of 4): `docker exec … psql "host=localhost dbname=ee_master user=ee_demo_x password=<from db-connect>"` → `permission denied for database`.

- [ ] **Step 5: Run** `dotnet test`, Pester — Expected: green.

- [ ] **Step 6: Stop and report.** STATUS.

### Task 6: `destroy`, `list`, `db-update`, `db-reset`, `db-connect`

**Files:**
- Create: `deploy/destroy.ps1`, `deploy/list.ps1`, `deploy/db-update.ps1`, `deploy/db-reset.ps1`, `deploy/db-connect.ps1`
- Modify: `deploy/lib/EphemeralEnvs.Deploy.psm1`

**Interfaces:**
- Consumes: Task 3 + Task 5 helpers (`Get-EnvComposeArgs`, `Get-WorktreePath`, `Set-EnvComposeVariables`, `Get-EnvContainers`, `Get-RunningApiPassword`).
- Produces: scripts per spec §4.7. `db-update`/`db-reset` take tag/branch/commit from the api container labels (env not running → throw "deploy it first"); compose file from the worktree (`working-tree` commit → repo root).

- [ ] **Step 1: Implement** `destroy.ps1 -Env <e>` (each step tolerates "already gone": `compose down -v --remove-orphans` (compose file from worktree if present, else repo `deploy/env/compose.yml`), `DROP DATABASE IF EXISTS … WITH (FORCE)`, `DROP ROLE IF EXISTS`, `git worktree remove --force` if registered + `git worktree prune`, `docker image rm ee/{api,web,migrator}:wt-<e>` if present).

- [ ] **Step 2: Implement** `list.ps1` (env, branch, commit (12), deployed at, api/web replica counts, Web URL; then databases `ee_%` from `pg_database` without a running env as "orphan").

- [ ] **Step 3: Implement** `db-update.ps1 -Env <e> [-Restore]` (running password, migrate, seed / `seed --restore`; no rotation) and `db-reset.ps1 -Env <e>` (`DROP DATABASE … WITH (FORCE)`, recreate + revoke, new password set, migrate, seed, `up -d --wait --force-recreate` with the deployed tag and current replica counts and the deployed keys mode (read `DataProtection__KeysPath` from the running web)).

- [ ] **Step 4: Implement** `db-connect.ps1 -Env <e> [-Psql]` (print `Host=localhost;Port=<EE_PLATFORM_DB_PORT>;Database=ee_<e>;Username=ee_<e>;Password=<running>` and the admin string; `-Psql` → `docker exec -it <postgres> psql -U postgres -d ee_<e>`).

- [ ] **Step 5: Manual**: `list` with 2 envs; `db-connect -Env demo-x -Psql` → `select count(*) from products;`; delete a demo product → `db-update -Env demo-x -Restore` brings it back; `db-reset -Env demo-x` → 5 products, app works; `destroy -Env demo-x` → gone from `list`, `docker volume ls` and `\l`, worktree gone, master still works; `destroy -Env demo-x` again → no error; half-deployed: create db via deploy that fails at migrate on a new env name, then `destroy` → clean.

- [ ] **Step 6: Stop and report.** STATUS.

### Task 7: Docs

**Files:**
- Create: `docs/environments.md`
- Modify: `README.md`, `CLAUDE.md` (docs list), `docs/development.md` (Web.Tests, Pester), `docs/superpowers/specs/2026-11-11-branch-deployment-design.md` (stage 2 row → Done + link; §4 `app_<env>` → `ee_<env>`), `docs/STATUS.md`

- [ ] **Step 1: Write** `docs/environments.md`: what an env is; platform (`platform.ps1`, ports, dashboard); `deploy` (both modes, parameters, what each step does, re-deploy keeps data, one env = one branch, `-Force`); naming rules; images (build once, `docker image prune`); `destroy`/`list`/`db-*`; database access (`db-connect`, port 5442, admin `postgres/postgres` local only); scale experiment step by step; troubleshooting (port 8080 busy → `EE_PROXY_PORT`; `*.localhost` in PowerShell → `curl.exe` or Host header; Pester 5 install).

- [ ] **Step 2: Update** README quick start (`./deploy/deploy.ps1 -Branch master` → `http://master.localhost:8080`, `destroy`) + docs table row; CLAUDE.md list; development.md; parent spec; STATUS.

- [ ] **Step 3: Verify** all links resolve (`grep -o '](docs/[^)#]*' README.md` paths exist) and the quick start runs from a fresh shell.

- [ ] **Step 4: Stop and report.** Then final whole-stage review.

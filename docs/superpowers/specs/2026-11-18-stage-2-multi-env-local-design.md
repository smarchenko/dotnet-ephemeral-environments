# Stage 2 — Several environments locally (Traefik, DB per env, deploy/destroy)

Status: **APPROVED by user 2026-11-18**. Parent spec: `2026-11-11-branch-deployment-design.md`
(stage 2 row in §3, target architecture in §4).

## 1. Intent

A preview environment is "the code of one branch, running with its own database, at its own URL". Stage 2 builds
that on the developer's machine, with the **same scripts CI will run later** (6b): `deploy -Branch feature-x`
takes the committed code of the branch, builds the images once, gives the environment its own database and
publishes it at `http://feature-x.localhost:8080`. Two or more branches can run side by side; that is a side benefit,
the point is to have the deploy/destroy mechanics working and understood before a server and CI are added.

Local is an intermediate stage. Whatever is decided here must still be right when the same script runs from a CI
runner against a Hetzner VPS over `DOCKER_HOST=ssh://…` (the server only needs Docker).

The stage-1 setup (`deploy/local/`, one env on fixed ports, IDE workflow) stays as it is for daily development.

## 2. Goals / non-goals / anti-goals

Goals
- Long-lived **platform**: Traefik (`:8080`) + one shared PostgreSQL.
- `deploy` = the current branch **as it is on disk** (uncommitted changes included); `deploy -Committed` = only its
  committed code (like CI); `deploy -Branch <name>` = another branch's committed code, via `git worktree`, without
  touching the local checkout (revised by user 2026-11-19, see §3). Re-deploy of the same env keeps its database: only new
  migrations and new seed sets run, QA data stays.
- Every env: own compose project, own database + own Postgres role, URLs `<env>.localhost:8080` (Web) and
  `<env>-api.localhost:8080` (API).
- `destroy -Env x` removes everything of the env (containers, volumes, database, role, worktree).
- `list`, `db-update -Env x [-Restore]`, `db-reset -Env x`, `db-connect -Env x`.
- Images **built once per commit** (tag = commit sha), env-specific values only from environment variables.
- `--scale` experiment: Web gets a server-side form with an antiforgery token; with 2 Web replicas it fails
  without shared Data Protection keys and works with them.

Non-goals (later stages)
- CI, server, TLS, basic auth, wildcard DNS → 5a/6a/6b. Nightly cleanup of stale envs → 7.
- Bash versions of the scripts (6b decides whether the runner uses `pwsh`, which GitHub's Ubuntu runners have).
- Feature flags per env (`-Flags`) → FF1; the parameter list leaves room for it, nothing more.
- Kubernetes → K1.

Anti-goals (would make the stage a failure even if it works)
- Scripts that only work on this laptop: host paths baked into compose files, state files the script needs on the
  next run, anything that breaks when the Docker engine is remote.
- One env able to read or break another env's data (shared superuser in app connection strings).
- Secrets in the repo or in files the scripts write (the local admin password of the platform Postgres is the
  one documented exception, local only).
- Rebuilding images per env for the same commit.

## 3. Approaches considered

| | Approach | Trade-off | Decision |
|---|---|---|---|
| A | **PowerShell scripts in `deploy/`, run where the Docker CLI runs.** Platform = one compose project; each env = its own compose project attached to the platform network. Remote later via `DOCKER_HOST=ssh://`. | Same script locally and in CI; server needs only Docker; build happens on the caller's side (fine for a PoC, CI later builds on the runner and the engine is remote). | **Chosen** (user, 2026-11-18) |
| B | Bash scripts that live on the server; CI `ssh`es in and runs `deploy.sh <env>` (the sketch in parent §4) | Simple on the server, but local runs need WSL/Git Bash and a second copy of the logic; the server needs the repo/scripts deployed to it. | Rejected |
| C | A platform (Coolify/Dokploy, or k8s now) | Hides exactly the mechanics this PoC wants to show; k8s is planned separately (K1/K2). | Rejected |

Where the code comes from (user, 2026-11-18): **(b) `git worktree`** checkout of the branch's commit, like CI does,
plus `-FromWorkingTree` for uncommitted code. Rejected: (a) always the working tree (not what CI deploys),
(c) `git archive | docker build -` (stateless, but piping binary tar through PowerShell into a native command is
fragile on Windows).

**Revised 2026-11-19 (user):** locally one usually deploys the branch one is on and expects the local files to be
used, so the default is flipped. `deploy` (no switches) builds from the local checkout; if it has no uncommitted
changes it is identical to the commit and gets the commit's tag (`<sha12>`, images reused), otherwise `wt-<env>`
(always rebuilt) and commit label `<sha>+dirty`. `-Committed` deploys only the committed HEAD through a worktree
(CI parity check). `-Branch <name>` stays for another branch (worktree). `-FromWorkingTree` is gone; an env's owner
is simply its branch, whichever of the three ways deployed it.

## 4. Design

### 4.1 Layout

```
deploy/
  platform/compose.yml        Traefik + shared Postgres (project ee-platform)
  env/compose.yml             one env: migrate, seed, api, web (project ee-<env>)
  lib/EphemeralEnvs.Deploy.psm1   shared functions (env name, docker/psql helpers, image tags)
  platform.ps1                up | down [-RemoveData]
  deploy.ps1  destroy.ps1  list.ps1  db-update.ps1  db-reset.ps1  db-connect.ps1
  local/                      stage 1, unchanged
tests/deploy/*.Tests.ps1      Pester tests of the pure functions (env name)
.ee/worktrees/<env>/          git worktrees of deployed branches (git-ignored, docker-ignored)
```

All scripts: `#Requires -Version 7.2`, `$ErrorActionPreference = 'Stop'`, native exit codes checked (as
`Invoke-Step` in stage 1), preflight that `docker info` reports `OSType=linux`. Locally they default
`DOCKER_CONTEXT=desktop-linux` (and restore the caller's env afterwards, as stage 1); if `DOCKER_HOST` is set they
use it unchanged (the CI path).

Note for 6b: with a remote engine (`DOCKER_HOST=ssh://`) `docker build` runs on the remote daemon (the context is
uploaded); nothing in this stage depends on where the build runs.

### 4.2 Platform (`deploy/platform/compose.yml`, project `ee-platform`)

- Network **`ee-platform`** (fixed name, so env projects reference it as `external`).
- **Traefik v3** (exact tag pinned): docker provider, `exposedByDefault=false`, `network=ee-platform`,
  docker socket mounted read-only; entrypoint `web` → host `${EE_PROXY_PORT:-8080}`; dashboard (insecure API mode)
  on host `${EE_DASHBOARD_PORT:-8090}`, local only.
- **Postgres 17**, volume `ee-platform-pgdata`, superuser `postgres`, password `${EE_PG_ADMIN_PASSWORD}`; locally
  the scripts default it to `postgres` (documented, local only; on the server a secret, 6a). Host port
  `${EE_PLATFORM_DB_PORT:-5442}` published **locally only** (for SQL clients; 5432 belongs to stage 1).
  Healthcheck `pg_isready`.
- `platform.ps1 up` = `compose up -d --wait`; `down [-RemoveData]` (`-RemoveData` drops the volume = all env
  databases). `deploy.ps1` starts the platform if it is not running (`--no-recreate`, never restarts a running one).

### 4.3 Environment name

`Get-EnvName` (pure function, Pester-tested): from `-Env` if given, else from the branch name.
- A leading branch type prefix is dropped (case-insensitive): `feature/`, `bugfix/`, `hotfix/`, `fix/`, `story/`,
  `chore/` — most branches share it and it only makes URLs longer (revised by user 2026-11-19).
- Lowercase; every run of characters outside `[a-z0-9]` → `-`; trim `-` at both ends.
  `feature/Add-X_2` → `add-x-2`, `my/Add-X_2` → `my-add-x-2`.
- Max **30** chars (revised 2026-11-19, was 40); longer → first 25 chars cut back to a word boundary (a single long
  word is cut hard) + `-` + first 4 hex of SHA-1 of the full sanitized name:
  `feature/14074-introduce-theme-specific-navigation` → `14074-introduce-theme-9dc9`. Two long branches with the
  same start differ only in the hash; a clash of hashes is caught by the owner check (§4.4). Keeps `<env>-api`
  inside a 63-char DNS label and `ee_<env>` inside Postgres' 63-byte names.
- Empty result, or `platform` (clashes with `ee-platform`) → error.
- An explicit `-Env` must already be in normal form (same rules); otherwise error showing the normalized value.
- CI adds a prefix (`gh-`, `ado-`) in 6b/8; nothing local.

Derived names: compose project `ee-<env>`; database **`ee_<env>`** (`-` → `_`) and role of the same name;
hosts `<env>.${EE_DOMAIN}` / `<env>-api.${EE_DOMAIN}` with `EE_DOMAIN=localhost` locally. (Parent §4 said
`app_<env>`; this spec renames it to `ee_<env>` and updates the parent.)

### 4.4 Code and images

Three sources (revised 2026-11-19):
- **default** (no switch): the current branch from the local checkout. Build context = repo root. No uncommitted
  or untracked changes (`git status --porcelain` empty) → tag `<sha12>` of HEAD, images reused like a commit;
  otherwise tag **`wt-<env>`**, always rebuilt (layer cache keeps it fast), commit label `<sha>+dirty`, and the
  script warns that the env includes uncommitted changes. Detached HEAD → `-Env` required.
- **`-Committed`**: the current branch's HEAD commit only, through the worktree below (what CI will deploy).
- **`-Branch <name>`** (another branch, the local checkout is not touched): resolve with `git rev-parse --verify <ref>^{commit}`: the local branch `<name>`, else
  `<remote>/<name>` over all remotes (this repo has `github`; `azure` comes in stage 8). Several remotes with
  different commits → error asking for the full ref (`-Branch github/feature-x`); the env name is taken from the part
  after the remote. No automatic fetch (error hint: `git fetch`). Worktree `.ee/worktrees/<env>`: created with
  `git worktree add --detach`, or moved to the new commit with `git -C … checkout --detach <sha>` on re-deploy.
- Image tags: `ee/api`, `ee/web`, `ee/migrator` : **`<sha12>`** (first 12 chars of the commit). If all three
  tags exist, nothing is built (`-Rebuild` forces). Build context = the worktree root, Dockerfiles unchanged.
- `.dockerignore` and `.gitignore` get `.ee/`.
- Containers carry labels `ee.env`, `ee.branch`, `ee.commit` (sha, or `<sha>+dirty`), `ee.source` (`local` = repo
  root, `worktree` = `.ee/worktrees/<env>`), `ee.deployed-at` (UTC ISO).
  `list`, `db-update`, `db-reset` read the deployed tag and branch from these labels: the running env is the
  state, no state files.
- **The env compose file comes from the deployed commit** (`.ee/worktrees/<env>/deploy/env/compose.yml`; the
  local checkout's for a default deploy), so a branch that changes it is deployed the way CI will deploy it (CI checks
  out one commit for scripts, compose and code). The scripts themselves run from the current checkout. A commit
  without `deploy/env/compose.yml` (older than stage 2) → error. `db-update`/`db-reset` use the same worktree.
- **One env = one branch.** If the env already runs with a different `ee.branch` label (two branches normalise to the
  same name, or an explicit `-Env`), `deploy` refuses
  and names the current owner; `-Force` takes the env over (its database and QA data stay). Same branch → normal
  re-deploy.

### 4.5 Database per env and the password

Provisioning runs SQL as the platform superuser through `docker exec <platform postgres container> psql` (no
host port needed, so it works unchanged over `DOCKER_HOST=ssh://`):
1. Role `ee_<env>`: `CREATE ROLE … LOGIN PASSWORD …` if missing, else `ALTER ROLE … PASSWORD …`.
2. Database `ee_<env>` `OWNER ee_<env>` if missing; `REVOKE CONNECT ON DATABASE ee_<env> FROM PUBLIC` (an env's
   role cannot connect to another env's database; the owner and the superuser still can).
3. Migrations and seed run as the env role (owner of the database and of everything the migrations create).

**Password: a new random one on every deploy** (32 hex chars from a CSPRNG), handed to the containers through the
process environment of `docker compose` (never written to a file). The containers are recreated on every deploy
anyway, so nothing needs to remember it; that matters for CI, whose runner is gone after each run.

Order, so that a failed `migrate`/`seed` leaves the old version serving (the api's `/health/ready` checks the
database and Traefik drops replicas that fail it):
- env api **running** → `migrate`/`seed` use its current password (read with `docker inspect`); only after both
  succeed: `ALTER ROLE … PASSWORD <new>` → `up` recreates api/web with it. Between `ALTER ROLE` and the replacement
  the old api cannot open new connections (seconds; a re-deploy is not zero-downtime anyway).
- **nothing running** (first deploy, or after a failed first deploy) → the new password is set first.
`db-update` uses the running api's password and does not rotate; `db-reset` rotates (it recreates api/web).

**How a person runs queries** (user question 2026-11-18), `db-connect.ps1 -Env x`:
- prints the env's current connection string (read from the running api container with `docker inspect`; valid
  until the next deploy) and the admin one for `localhost:5442`, so any SQL client (DBeaver, Rider) can connect;
- `-Psql` opens `psql` on the env database inside the platform Postgres container (`docker exec -it`, as the
  superuser) — no port and no password needed, so it also works against the server later.
On the server (6a/6b) Postgres is not published; a GUI client goes through an SSH tunnel, documented then.

### 4.6 Env compose (`deploy/env/compose.yml`, project `ee-<env>`)

Variables from the script: `EE_ENV` (also `App__EnvironmentName` of every service), `EE_TAG`, `EE_DOMAIN`, `EE_PROXY_PORT`, `EE_DB_PASSWORD`, `EE_BRANCH`,
`EE_COMMIT`, `EE_DEPLOYED_AT`, `EE_DP_KEYS_PATH`. Connection string as in stage 1 (`GSS Encryption Mode=Disable`),
`Host=postgres;Database=ee_${EE_ENV};Username=ee_${EE_ENV};Password=${EE_DB_PASSWORD}`.

- `migrate`, `seed`: image `ee/migrator:${EE_TAG}`, same commands as stage 1, `App__EnvironmentName=${EE_ENV}`;
  network `ee-platform` only. Run by the script with `compose run --rm` before `up` (not via `depends_on`), so a
  failure stops the deploy before the apps change and the old version keeps running.
- `api`: image `ee/api:${EE_TAG}`; networks `ee-platform` + the env's own `default` with alias
  **`${EE_ENV}-api`**. `Cors__AllowedOrigins=http://${EE_ENV}.${EE_DOMAIN}:${EE_PROXY_PORT}`, `Swagger__Enabled=true`.
  Traefik labels: router rule ``Host(`${EE_ENV}-api.${EE_DOMAIN}`)``, service port 8080, load-balancer
  health check `/health/ready`, `traefik.docker.network=ee-platform`.
- `web`: image `ee/web:${EE_TAG}`; same two networks. `Api__InternalBaseUrl=http://${EE_ENV}-api:8080` — **not**
  `http://api:8080`: every env's api is also on the shared `ee-platform` network under the service name `api`, so
  that name could resolve to another env. `Api__PublicBaseUrl=http://${EE_ENV}-api.${EE_DOMAIN}:${EE_PROXY_PORT}`.
  Router ``Host(`${EE_ENV}.${EE_DOMAIN}`)``. Volume `dpkeys` → `/keys`, `DataProtection__KeysPath=${EE_DP_KEYS_PATH}`
  (`/keys` by default, empty with `-NoSharedKeys`).
- No host ports. Labels from §4.4 on every service.

### 4.7 Scripts

| Script | What it does |
|---|---|
| `deploy.ps1 [-Committed \| -Branch <b>] [-Env <e>] [-Force] [-Rebuild] [-ApiReplicas n] [-WebReplicas n] [-NoSharedKeys]` | preflight → env name → owner check (§4.4) → code (worktree) → images (skip if present) → platform up → role/db → `migrate` → `seed` → password (§4.5) → `up -d --wait --scale …` → wait for `/health/ready` through Traefik → print URLs, branch, commit, database |
| `destroy.ps1 -Env <e>` | `compose down -v --remove-orphans` → `DROP DATABASE … WITH (FORCE)` → `DROP ROLE` → `git worktree remove --force` → remove `wt-<env>` images. Every step tolerates "already gone", so a half-deployed env can be destroyed. Shared `<sha12>` images stay (`docker image prune` in docs) |
| `list.ps1` | table: env, branch, commit, deployed at, replicas, Web URL (from container labels; databases without containers listed as orphans) |
| `db-update.ps1 -Env <e> [-Restore]` | migrate + seed (`--restore`) with the env's deployed `migrator` tag |
| `db-reset.ps1 -Env <e>` | drop + recreate database (rotates the password), migrate, seed, recreate api/web |
| `db-connect.ps1 -Env <e> [-Psql]` | §4.5 |
| `platform.ps1 up \| down [-RemoveData]` | §4.2 |

Health wait: the script calls `http://localhost:${EE_PROXY_PORT}/health/ready` with header `Host: <env>-api.localhost`
(Windows does not resolve `*.localhost` for PowerShell; browsers and `curl.exe` do), timeout 60 s, then the same
for `/` of the Web. Re-deploying the same commit just recreates the containers (new password).

### 4.8 `--scale` and Data Protection

- Web gets **`/Home/Create`**: server-side MVC form (name, price) with the form tag helper's antiforgery token;
  antiforgery enforced globally (`AutoValidateAntiforgeryTokenAttribute`). POST → `ProductsApiClient.CreateProductAsync`
  → 201: redirect to `/`; 400 ProblemDetails: form again with the field errors; API down: form again with the
  stage-0 "API is unavailable right now." message. Link to it from the layout.
- `DataProtection:KeysPath` set → `PersistKeysToFileSystem(path)` + `SetApplicationName("EphemeralEnvs.Web")`;
  unset → ASP.NET defaults (keys inside the container, different per replica). The Web Dockerfile creates `/keys`
  owned by the app user so the named volume is writable by the non-root user. Keys are not encrypted at rest (the
  warning log is expected; on the server they live on a local volume, same trust as the database).
- Footer shows the container's host name next to the environment name, so the balancing is visible.
- Experiment (docs): `deploy -Branch x -WebReplicas 2 -NoSharedKeys` → submitting the form fails with 400 about
  half of the time; `deploy -Branch x -WebReplicas 2` → always works. API replicas: stateless, they just balance.
- Note for K1: a volume shared by replicas does not carry over to k8s (needs RWX storage, a DB or Redis); decided there.

### 4.9 Tests

- New `tests/EphemeralEnvs.Web.Tests` (WebApplicationFactory, the API stubbed with an `HttpMessageHandler`):
  POST `/Home/Create` without token → 400; with token → API called, redirect to `/`; API 400 ProblemDetails → form
  with the message; API unreachable → friendly message. **Data Protection:** two app instances with the same keys
  directory → a token issued by one is accepted by the other; with separate directories → rejected.
- Pester 5: `Get-EnvName` cases (§4.3: type prefixes, sanitizing, 30-char cut at a word boundary + hash, `platform`, empty, explicit `-Env`).
- Architecture tests: Web still references only Contracts + ServiceDefaults.
- Scripts beyond the pure function are checked by hand (§5); automated deploy tests come with CI (6b smoke).

### 4.10 Docs

- New **`docs/environments.md`**: platform, deploy/destroy/list, naming, code source (local checkout vs `-Committed` vs `-Branch`),
  database access, the scale experiment, troubleshooting (port 8080 busy, `*.localhost` in PowerShell).
- README: quick start gets the `deploy`/`destroy` lines + link; CLAUDE.md docs list gets `environments.md`.
- Parent spec: stage 2 row → link + "Done" when done; §4 `app_<env>` → `ee_<env>`. STATUS.

## 5. Definition of done (manual checks)

1. `deploy.ps1 -Branch master` from a clean shell: platform starts, env `master` up;
   `http://master.localhost:8080` lists 5 demo products, footer `master`; `http://master-api.localhost:8080/swagger`.
2. Second branch with a visible change (`git switch -c demo-x`, edit the Web footer, commit) →
   `deploy.ps1 -Branch demo-x`; both envs run, each shows its own code; adding a product in one does not show in
   the other. Traefik dashboard `:8090` lists both.
3. Re-deploy `master` (no code change): no image build, data added in step 2 still there, new migrations only.
4. `list.ps1` shows both; `db-connect.ps1 -Env demo-x -Psql` → `select count(*) from products`.
   The `demo-x` role cannot connect to `ee_master`.
5. `deploy` with an uncommitted change deploys it as `wt-<env>` (`+dirty`); after committing, `deploy` reuses or
   builds `<sha12>`; `-Committed` with an uncommitted change deploys without it.
6. Scale: §4.8 experiment, both halves.
7. `destroy.ps1 -Env demo-x`: containers, volume, database, role, worktree gone; `master` still works.
8. A broken migration on a branch → deploy stops after `migrate`, the env's previous version keeps running and
   serving (its password was not rotated).
9. Deploying another branch onto a taken env name is refused; `-Force` takes it over.
10. `dotnet test` (incl. Web.Tests), Pester, build 0 warnings, format clean.

## 6. Left to the builder

- Exact Traefik tag, the PowerShell module's function split, how `list` formats its table.
- The SQL for idempotent role/database creation (`DO` block or `\gexec`), quoting of identifiers.
- Whether the Web form shares a partial with the widget page (looks only).

# Ephemeral environments (stage 2: several envs on one machine)

An **environment** (env) is one branch of the app running on its own: Web + API containers and its own
PostgreSQL database, reachable at `http://<env>.localhost:8080` (Web) and `http://<env>-api.localhost:8080` (API).
Any number of envs run side by side on one Docker engine. All of them share a small **platform**: one Traefik
(routes by host name) and one PostgreSQL server (a database per env).

This is the local rehearsal of what CI will do later on a server: the same scripts, pointed at a remote engine.
The single-env setup from stages 0 and 1 (`deploy/local/`, [local-environment.md](local-environment.md)) stays
for day-to-day development; the two do not share ports or containers.

- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Platform](#platform)
- [Deploy](#deploy)
- [Env names](#env-names)
- [Images](#images)
- [List and destroy](#list-and-destroy)
- [Database](#database)
- [Scale experiment: Data Protection keys](#scale-experiment-data-protection-keys)
- [How it is built](#how-it-is-built)
- [Troubleshooting](#troubleshooting)

## Requirements

- Docker Desktop with the **Linux engine**. The scripts set `DOCKER_CONTEXT=desktop-linux` themselves (unless
  `DOCKER_HOST` is set), so Docker Desktop may stay in Windows-containers mode.
- **PowerShell 7.2+** (`pwsh`). Windows ships only Windows PowerShell 5.1: `winget install Microsoft.PowerShell`.
  The scripts refuse to start in 5.1 (`#Requires -Version 7.2`).
- Git. No .NET SDK: images are built inside Docker.

## Quick start

```powershell
./deploy/deploy.ps1                  # the current branch as it is on disk -> http://<branch>.localhost:8080
./deploy/list.ps1                    # what runs where
./deploy/destroy.ps1 -Env <env>      # remove it completely
```

`deploy.ps1` prints the URLs at the end. The first deploy builds three images and takes a few minutes; later
deploys of the same commit take seconds.

Browsers resolve any `*.localhost` to your machine, no hosts file needed. PowerShell's `Invoke-WebRequest` does
not: use `curl.exe` or a `Host` header (see [Troubleshooting](#troubleshooting)).

## Platform

```powershell
./deploy/platform.ps1 up             # start Traefik + Postgres (deploy.ps1 does this when they are not running)
./deploy/platform.ps1 down           # stop them; envs stay deployed but are unreachable until the next up
./deploy/platform.ps1 down -RemoveData   # also drop the Postgres volume = the databases of ALL envs
```

| Port | What | Override |
|---|---|---|
| 8080 | All envs: `<env>.localhost:8080`, `<env>-api.localhost:8080` | `EE_PROXY_PORT` |
| 8090 | Traefik dashboard <http://localhost:8090/dashboard/> (routes, services, replicas) | `EE_DASHBOARD_PORT` |
| 5442 | Shared Postgres for SQL clients (5432 belongs to `deploy/local`) | `EE_PLATFORM_DB_PORT` |

Postgres superuser: `postgres` / `postgres` (override with `EE_PG_ADMIN_PASSWORD`). The dashboard, the open
Postgres port and this password are **local only**; a server will not publish them.

Compose project `ee-platform`, network `ee-platform` (every env joins it), volume `ee-platform-pgdata`.
`platform.ps1 up` never recreates a running container, so it is safe to run at any time.

## Deploy

Three ways to choose the code:

| Command | Code | Images | Typical use |
|---|---|---|---|
| `./deploy/deploy.ps1` | Current branch **as it is on disk**, uncommitted changes included | Clean tree: `ee/*:<sha12>`, built once per commit. Uncommitted changes: `ee/*:wt-<env>`, rebuilt every time | "Show me what I am working on" |
| `./deploy/deploy.ps1 -Committed` | Current branch's **HEAD commit only**, checked out into `.ee/worktrees/<env>` | `ee/*:<sha12>` | Exactly what CI will deploy |
| `./deploy/deploy.ps1 -Branch <b>` | **Another branch's** commit, in `.ee/worktrees/<env>`; your checkout is not touched | `ee/*:<sha12>` | Look at a colleague's branch |

`-Branch` looks for a local branch first, then `<remote>/<b>` (also accepts `origin/<b>`). It never fetches:
run `git fetch` first if the branch is new. A dirty deploy is labelled `<sha>+dirty` and prints a yellow warning.

Parameters:

| Parameter | What |
|---|---|
| `-Env <name>` | Env name instead of the one derived from the branch. Must already be in normal form (see [Env names](#env-names)) |
| `-Force` | Take over an env that runs another branch |
| `-Rebuild` | Build the images even if `ee/*:<sha12>` exist |
| `-ApiReplicas <n>`, `-WebReplicas <n>` | Replicas, 1-10 (default 1) |
| `-NoSharedKeys` | Do not share Data Protection keys between Web replicas: for the [scale experiment](#scale-experiment-data-protection-keys) |

What a deploy does, in order:

1. Resolve the branch and commit, derive the env name. **One env = one branch**: deploying another branch onto
   a name that is taken stops with "Env 'x' runs branch 'y'. Use -Force to take it over."
2. Build the images, unless `ee/*:<sha12>` already exist.
3. Start the platform if it is not running.
4. Create the database and role `ee_<env>` if they do not exist (`REVOKE CONNECT ... FROM PUBLIC`: an env role
   cannot open another env's database).
5. Run `migrate`, then `seed` (stage 1 migrator image) with the **current** password.
6. Only after both succeeded: give the role a new random password.
7. Start (or update) `api` and `web` with the new password and replica counts, remove containers that should
   not be there.
8. Wait until `/health/ready` (API) and `/` (Web) answer through Traefik, then print the URLs.

Re-deploying an env **keeps its data**: migrations and new seed sets are applied, rows changed by hand stay.
If `migrate` or `seed` fails, steps 6-7 do not run, so the running version keeps serving with its old password.
To start from a clean database use `db-reset` (below).

## Env names

Derived from the branch, or given with `-Env`:

1. A leading `feature/`, `bugfix/`, `hotfix/`, `fix/`, `story/` or `chore/` is dropped (any case).
2. Lowercase; every run of characters other than `a-z0-9` becomes one `-`; no `-` at the start or the end.
3. At most **30** characters. A longer name is cut to 25 characters, back to the last `-` inside them, then
   gets `-` + 4 hex characters of the SHA-1 of the full name, so two long branches never collide.

| Branch | Env |
|---|---|
| `master` | `master` |
| `feature/Add-X_2` | `add-x-2` |
| `feature/14074-introduce-theme-specific-navigation` | `14074-introduce-theme-9dc9` |
| `my/feature/x` | `my-feature-x` |

`platform` is reserved. `-Env` must already be in normal form; otherwise the error suggests the right one.
The database and role are `ee_<env>` with `-` replaced by `_`.

## Images

- `ee/api`, `ee/web`, `ee/migrator`, tagged `<sha12>` (first 12 characters of the commit) or `wt-<env>` for a
  deploy with uncommitted changes.
- An image is built **once per commit** and reused by every env on that commit. Everything env-specific
  (name, database, URLs, password) comes from environment variables at start-up.
- `destroy` removes `wt-<env>` images but keeps `<sha12>` ones: another env may run the same commit.
  To free the space:

  ```powershell
  ./deploy/prune-images.ps1   # removes ee/* images whose tag no deployed env uses
  ```

  It keeps the migrator image of every deployed env (no container runs it, but `db-update` and `db-reset` need
  it) and never touches images outside `ee/*`. Plain `docker image prune` cannot filter by name.

## List and destroy

```powershell
./deploy/list.ps1
```

One row per env: branch, commit (`+dirty` = uncommitted changes were deployed), deploy time (UTC), running/total
API and Web replicas, Web URL. Everything is read from the containers' labels, there is no state file. A
database `ee_*` without containers (a deploy that failed half-way) is listed as `(database only)`.

```powershell
./deploy/destroy.ps1 -Env <env>
```

Removes containers, network and volumes of `ee-<env>`, the database and role, the worktree
`.ee/worktrees/<env>` and the `wt-<env>` images. Every step tolerates "already gone": it cleans up a half-deployed
env, and running it twice is fine. The platform and other envs are not touched.

## Database

```powershell
./deploy/db-connect.ps1 -Env <env>         # connection strings for localhost:5442
./deploy/db-connect.ps1 -Env <env> -Psql   # psql on ee_<env> inside the Postgres container (no port, no password)
./deploy/db-update.ps1 -Env <env>          # new migrations and new seed sets; apps keep running
./deploy/db-update.ps1 -Env <env> -Restore # also re-insert deleted demo rows
./deploy/db-reset.ps1 -Env <env>           # drop -> create -> migrate -> seed, new password, restart api + web
```

- `db-connect` prints two strings: as the env role `ee_<env>` (the password changes on every deploy and
  `db-reset`, so the string is valid until then) and as the superuser `postgres` (local only).
- `db-update` uses the env's deployed migrator image and current password; the password is not rotated. Seed
  only inserts rows whose key is missing, so data changed by hand is never overwritten. For new code, deploy.
- `db-reset` loses everything in `ee_<env>`. It brings the env back exactly as deployed: same images, replica
  counts, keys mode and deploy time. Use it between test rounds.
- These scripts need a deployed env; for a `(database only)` one they say "deploy it first".

## Scale experiment: Data Protection keys

The Web's server-side form (`/Home/Create`) carries an **antiforgery token**, encrypted with ASP.NET Core Data
Protection keys. Each Web replica creates its own keys unless they are shared, so a token issued by one replica
cannot be read by another one.

1. Deploy two Web replicas **without** shared keys:

   ```powershell
   ./deploy/deploy.ps1 -WebReplicas 2 -NoSharedKeys
   ```

2. Open `http://<env>.localhost:8080/Home/Create`, fill the form, submit. Result: **400 Bad Request**. The footer
   shows which replica (container host name) served the page.

   It fails every time, not "half of the time": Traefik balances round robin, so with one browser the GET (form)
   and the POST (submit) always go to different replicas. With many users it would look random.

3. Deploy again **with** shared keys (the default):

   ```powershell
   ./deploy/deploy.ps1 -WebReplicas 2
   ```

   Submit again: the product is added. Both replicas read the keys from one volume (`dpkeys`, mounted at
   `/keys`, `DataProtection__KeysPath`), so any replica can check any token. The footer still alternates.

The API keeps no such state: `-ApiReplicas 2` just works. On a server with several machines the keys will
need a shared store (database, blob storage), not a Docker volume.

## How it is built

```
deploy/
  deploy.ps1, destroy.ps1, list.ps1,          scripts: parse parameters, call the module
  db-update.ps1, db-reset.ps1, db-connect.ps1,
  platform.ps1, prune-images.ps1
  lib/EphemeralEnvs.Deploy.psm1               all logic (Pester tests: tests/deploy/)
  platform/compose.yml                         project ee-platform: Traefik + Postgres
  env/compose.yml                              project ee-<env>: migrate, seed, api, web
```

Two compose files because they have different lifecycles: the platform is started once and outlives every env;
an env is created and destroyed many times a day. Recreating Traefik or Postgres on every deploy would cut off
all other envs.

An env uses `env/compose.yml` **from the deployed commit** (from the worktree for `-Committed`/`-Branch`), so
older branches keep deploying with the file they were written for. That makes the platform an interface. Its
contract with the env file is:

- network `ee-platform`, Postgres reachable on it as `postgres:5432` with a superuser `postgres`;
- Traefik entrypoint `web`, docker provider with `traefik.enable=true` labels;
- variables `EE_ENV`, `EE_DB`, `EE_TAG`, `EE_DOMAIN`, `EE_PROXY_PORT`, `EE_DB_PASSWORD`, `EE_DP_KEYS_PATH` and
  the `ee.*` labels the scripts read (`ee.env`, `ee.branch`, `ee.commit`, `ee.source`, `ee.deployed-at`).

**Change the contract only backward-compatibly** (add, do not rename or remove), or every branch created
before the change stops deploying until it is rebased.

## Troubleshooting

**"Failed executing DbCommand ... `__EFMigrationsHistory`" on the first deploy.** Expected: the migration bundle
first asks for the migration history table, which a new database does not have yet; EF logs that query as
failed, then creates the table and applies the migrations. The deploy succeeds. It appears only on a fresh
database.

**Port 8080 is taken.** Choose another port for the session and restart the platform with it:

```powershell
$env:EE_PROXY_PORT = '8081'
./deploy/platform.ps1 down
./deploy/deploy.ps1      # starts the platform on 8081, prints http://<env>.localhost:8081
```

Keep the variable set for every later deploy in that shell (or set it as a user environment variable), and
re-deploy envs deployed before the change: the API's CORS origin and the public API URL include the port.

**`*.localhost` does not resolve in PowerShell.** Browsers and `curl.exe` resolve it, the Windows resolver
behind `Invoke-WebRequest` does not. Use either:

```powershell
curl.exe http://master-api.localhost:8080/health/ready
Invoke-WebRequest http://localhost:8080/health/ready -Headers @{ Host = 'master-api.localhost' }
```

**404 page not found (plain text).** That is Traefik: no env answers to this host name. Check the name with
`./deploy/list.ps1`; right after a deploy Traefik needs a second or two to pick up the containers.

**"Docker engine is 'windows', expected 'linux'".** `DOCKER_HOST` is set to the Windows engine; unset it (the
scripts use `DOCKER_CONTEXT=desktop-linux` themselves).

**A script stops with a `#requires` error about version 7.2.** You are in Windows PowerShell 5.1: run it from
`pwsh` (see [Requirements](#requirements)).

**Branch not found with `-Branch`.** The scripts never fetch: `git fetch`, then deploy again. A name found on
two remotes needs the remote: `-Branch origin/<b>`.

**Platform stopped while envs were running.** `platform.ps1 down` keeps the network `ee-platform` while env
containers are attached to it; after `platform.ps1 up` the envs answer again, nothing to redeploy.

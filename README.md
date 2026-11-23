# dotnet-ephemeral-environments

Branch-based deployment PoC: ephemeral preview environments per pull request for a .NET app.

A proof of concept for **per-branch preview environments**: QA tests a feature directly from its
branch / pull request in its own environment (Web + API + its own PostgreSQL database), instead of
merging into shared dev/test/uat branches. The environment is created when a PR is opened, updated on
every push and destroyed when the PR is closed.

The PoC is built in small stages: see the design spec
[`docs/superpowers/specs/2026-11-11-branch-deployment-design.md`](docs/superpowers/specs/2026-11-11-branch-deployment-design.md)
and the current progress in [`docs/STATUS.md`](docs/STATUS.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`, newer 10.0.x feature bands roll forward)
- Docker Desktop with the **Linux engine**. If you also use Windows containers (e.g. for Sitecore), you do
  not need to switch modes: point the CLI at the Linux engine in the current shell:

  ```powershell
  $env:DOCKER_CONTEXT = 'desktop-linux'
  docker info --format '{{.OSType}}'   # must print: linux
  ```

- Local .NET tools (`dotnet-ef`): `dotnet tool restore`
- For the multi-env scripts (`deploy/*.ps1`): **PowerShell 7.2+** (`winget install Microsoft.PowerShell`, run them
  from `pwsh`)

## Quick start

Only Docker is needed to run it (the .NET SDK is for writing code):

```powershell
./deploy/local/up.ps1                  # build + start: postgres → migrations → demo data → API → Web
./deploy/local/up.ps1 -DbPort 5433     # if 5432 is taken by another Postgres
./deploy/local/db-reset.ps1            # database back to the "just deployed" state (between test rounds)
./deploy/local/down.ps1                # stop (-RemoveData also drops the database)
```

| URL | What |
|---|---|
| <http://localhost:5080> | Web: product list (5 demo products) |
| <http://localhost:5080/Home/Widget> | JS widget calling the API from the browser |
| <http://localhost:5080/Home/Create> | Server-side form (antiforgery token) adding a product through the Web |
| <http://localhost:5081/swagger> | API (Swagger UI) |
| <http://localhost:5081/health/ready> | readiness probe (checks the database) |

Several environments side by side, one per branch (Traefik + shared Postgres), see
[docs/environments.md](docs/environments.md):

```powershell
./deploy/deploy.ps1                       # the current branch as on disk -> http://<branch>.localhost:8080
./deploy/deploy.ps1 -Branch <other>       # another branch, your checkout untouched
./deploy/list.ps1                         # what runs where
./deploy/destroy.ps1 -Env <env>           # remove an env with its database
```

## Documentation

| Document | What's inside |
|---|---|
| [docs/local-environment.md](docs/local-environment.md) | Containers and start order, scripts (`up`, `db-update [-Restore]`, `db-reset`, `down`), demo data, step-by-step checks |
| [docs/environments.md](docs/environments.md) | Several envs on one machine: platform, `deploy` modes, env names, images, `list`/`destroy`/`db-*`, the Data Protection scale experiment, troubleshooting |
| [docs/development.md](docs/development.md) | Development workflow (Docker vs IDE, nothing to publish), changing the model and migrations, adding demo data, running API/Web from the IDE and their configuration, tests, code quality |
| [docs/architecture.md](docs/architecture.md) | Repository layout, CQRS, naming and validation rules, architecture tests |
| [docs/STATUS.md](docs/STATUS.md) | Current stage, decisions, next step |
| [docs/superpowers/specs/](docs/superpowers/specs/) | Design specs (overall + per stage) |

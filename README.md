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

## Repository layout

| Path | What |
|---|---|
| `src/EphemeralEnvs.Web` | ASP.NET Core MVC front end |
| `src/EphemeralEnvs.Api` | ASP.NET Core Web API |
| `src/EphemeralEnvs.Domain` | Domain entities (no dependencies) |
| `src/EphemeralEnvs.Data` | EF Core `DbContext`, configurations and migrations (PostgreSQL) |
| `src/EphemeralEnvs.Contracts` | DTOs shared by Web and API |
| `tests/EphemeralEnvs.Api.Tests` | API integration tests |
| `docs/` | Status, spec, plans |

## Build

```powershell
dotnet tool restore
dotnet build
```

## Database

PostgreSQL, accessed through EF Core (`src/EphemeralEnvs.Data`). The schema is defined by **EF Core migrations** in
`src/EphemeralEnvs.Data/Migrations`.

**The applications never run migrations on startup.** In stage 0 the schema is applied by hand; from
stage 1 on it is a separate deploy step (`migrator` container), also used by local `docker compose up`.

Apply all migrations to a database:

```powershell
dotnet tool restore
# Default target: Host=localhost;Port=5432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs
dotnet ef database update --project src/EphemeralEnvs.Data

# Another database / port: set EE_DB
$env:EE_DB = 'Host=localhost;Port=55432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs'
dotnet ef database update --project src/EphemeralEnvs.Data
```

Add a new migration after changing the model (entities in `EphemeralEnvs.Domain`, mapping in
`EphemeralEnvs.Data/Configurations`):

```powershell
dotnet ef migrations add <Name> --project src/EphemeralEnvs.Data
```

Migration files are generated code: style rules do not apply to `Migrations/` (see `.editorconfig`).

## Code quality

The rules live in `Directory.Build.props` and `.editorconfig` and are enforced **at build time**:

- **Any warning fails the build** (`TreatWarningsAsErrors`): compiler warnings, the .NET analyzers
  (`AnalysisLevel=latest-recommended`) and code-style rules from `.editorconfig` (`EnforceCodeStyleInBuild`).
  Examples: unnecessary `using` (IDE0005), missing braces, block-scoped namespaces, private fields not
  named `_camelCase`.
- **Vulnerable NuGet packages fail restore/build** (`NuGetAuditMode=all`, level `moderate`, including
  transitive packages).
- Package versions are managed centrally in `Directory.Packages.props`; `.csproj` files reference packages
  without a version. Transitive pinning is on, so a `PackageVersion` entry also pins indirect dependencies
  (used to keep all EF Core assemblies on one patch version). MSBuild warnings (e.g. MSB3277 version
  conflicts) also fail the build.
- Line endings are LF everywhere (`.editorconfig` + `.gitattributes`), because CI runs on Linux.

Check formatting the same way CI will:

```powershell
dotnet format --verify-no-changes   # exit code 0 = OK
dotnet format                       # auto-fix
```

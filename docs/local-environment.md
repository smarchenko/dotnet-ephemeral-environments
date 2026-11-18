# Local environment

Run one environment (Web + API + PostgreSQL) on this machine, manage its demo data and check that it works.
Prerequisites: see the [README](../README.md#prerequisites). Writing code: [development.md](development.md).

One environment (Web + API + PostgreSQL) on this machine, from `deploy/local/docker-compose.yml`
(compose project `ephemeral-envs-local`). Only Docker is needed: no .NET SDK and no `dotnet ef` on the host.

`up.ps1` runs one `docker compose up --build --wait`, which starts the services in this order:

```
postgres (healthy) ──► migrate ──► seed ──► api (healthy) ──► web
                       efbundle    DbTool
```

| Container | Host port (override) | What |
|---|---|---|
| `postgres` | `5432` (`EE_DB_PORT`) | PostgreSQL 17, data in volume `pgdata` |
| `migrate` | — (one-off, exits) | applies the EF Core migrations (`./efbundle`); prints `No migrations were applied` when the schema is current |
| `seed` | — (one-off, exits) | adds the demo data once per database (`dotnet EphemeralEnvs.DbTool.dll seed`) |
| `api` | `5081` (`EE_API_PORT`) | Web API; starts only after `seed` exited 0; CORS allows `http://localhost:$EE_WEB_PORT` |
| `web` | `5080` (`EE_WEB_PORT`) | MVC Web; calls the API at `http://api:8080` inside the compose network |

`migrate` and `seed` run from one image, `migrator` (`src/EphemeralEnvs.DbTool/Dockerfile`: the migration bundle
`/app/efbundle` + the DbTool console). The apps never migrate or seed on startup. If a step fails, `api` and `web`
are not started, and `up.ps1` prints the step's logs and exits with code 1. Api and Web images:
`src/EphemeralEnvs.Api/Dockerfile`, `src/EphemeralEnvs.Web/Dockerfile` (multi-stage `sdk:10.0` → `aspnet:10.0`,
port 8080, non-root user).

## Scripts (`deploy/local/`)

```powershell
./deploy/local/up.ps1                        # build + start everything; safe to re-run
./deploy/local/up.ps1 -DbPort 5433           # if 5432 is taken by another Postgres (also -WebPort, -ApiPort or EE_*_PORT)
./deploy/local/db-update.ps1                 # after pulling new migrations / seed sets: migrate + seed; apps keep running
./deploy/local/db-update.ps1 -Restore        # also bring back demo rows that were deleted (changed rows stay changed)
./deploy/local/db-reset.ps1                  # drop + create the database, migrate, seed, restart api: "just deployed" state
./deploy/local/down.ps1                      # stop, keep the database volume
./deploy/local/down.ps1 -RemoveData          # stop and drop the database volume
```

The scripts set `DOCKER_CONTEXT=desktop-linux` themselves and restore your shell's variables afterwards.
`db-update.ps1` and `db-reset.ps1` start postgres if it is not running and never recreate a running one; they take
`-DbPort` (pass the same port as to `up.ps1`, or set `EE_DB_PORT` once). A busy port makes `up.ps1` stop with Docker's
"port is already allocated" error: pick another port.

## Demo data (seed)

Seed set `0001-products` (`src/EphemeralEnvs.DbTool/Seeding/Sets/Seed0001Products.cs`). The ids are fixed, so QA,
tests and later sets can refer to them:

| Id | Name | Price |
|---|---|---|
| `0196a000-0000-7000-8000-000000000001` | Notebook A5 | 4.50 |
| `0196a000-0000-7000-8000-000000000002` | Ballpoint pen | 1.20 |
| `0196a000-0000-7000-8000-000000000003` | Desk lamp | 34.99 |
| `0196a000-0000-7000-8000-000000000004` | USB-C cable 1 m | 9.90 |
| `0196a000-0000-7000-8000-000000000005` | Coffee mug | 12.00 |

How the seed behaves:

- A set runs **once per database**: applied sets are recorded in the journal table `__db_tool_history`
  (`name`, `kind` = `seed`, `applied_at`). Later runs log `Skipped 0001-products (already applied)`.
- It **never updates** existing rows. `seed --restore` (`db-update.ps1 -Restore`) runs every set again and inserts
  only the rows whose id is missing: deleted demo rows come back, rows QA changed stay changed.
- It refuses to run when `App__EnvironmentName` is missing, blank or `production`/`prod`. Exit codes: `0` success,
  `1` failure (guard, database, invalid seed data), `2` usage error.

| You want | Run | QA data |
|---|---|---|
| new migrations and seed sets after a pull | `db-update.ps1` | untouched |
| deleted demo rows back | `db-update.ps1 -Restore` | edited and added rows untouched |
| a clean start between test rounds of a feature | `db-reset.ps1` | all lost: only migrations + seed sets |

Preview environments get the same steps per environment (stages 2 and 6b).

## Check that it works

1. `./deploy/local/up.ps1` (add `-DbPort 5433` if needed). The first build takes a few minutes.
2. Logs of the database steps:

   ```powershell
   $env:DOCKER_CONTEXT = 'desktop-linux'
   docker compose -f deploy/local/docker-compose.yml logs --no-log-prefix migrate seed
   # first run:  Applying migration '..._InitialCreate' ...   applied 0001-products: inserted 5, already present 0
   # next runs:  No migrations were applied ...               Skipped 0001-products (already applied)
   ```

3. <http://localhost:5080>: the 5 demo products, rendered server-side; the footer shows `Environment: local`.
   `curl http://localhost:5081/products/0196a000-0000-7000-8000-000000000001` → 200.
4. <http://localhost:5080/Home/Widget>: add a product; DevTools → Network shows the cross-origin calls to `:5081`.
5. Seed never overwrites: change one demo product and delete another (any SQL client on `localhost:<DbPort>`,
   `ephemeral_envs` / `ephemeral_envs`, or
   `docker compose -f deploy/local/docker-compose.yml exec postgres psql -U ephemeral_envs`), then
   `db-update.ps1` → nothing changes; `db-update.ps1 -Restore` → the deleted row is back, the changed one stays changed
   (log: `restored 0001-products: inserted 1, already present 4`).
6. `db-reset.ps1` → exactly the 5 demo products again, your product from step 4 is gone; the API keeps working.
7. A failing step stops the deploy. Create a conflicting table, then start again:

   ```powershell
   ./deploy/local/down.ps1 -RemoveData
   docker compose -f deploy/local/docker-compose.yml up --detach --wait postgres
   docker compose -f deploy/local/docker-compose.yml exec postgres psql -U ephemeral_envs -c 'CREATE TABLE products (x int);'
   ./deploy/local/up.ps1     # migrate fails (42P07: relation "products" already exists), logs printed, exit 1
   docker compose -f deploy/local/docker-compose.yml ps -a   # api / web: Created, never started
   ./deploy/local/down.ps1 -RemoveData                       # clean up
   ```

   (Use the same `EE_DB_PORT` for these commands as for `up.ps1`.)
8. <http://localhost:5081/swagger> works; `curl http://localhost:5081/health/live` and `/health/ready` return 200.
9. Database down:

   ```powershell
   docker compose -f deploy/local/docker-compose.yml stop postgres
   curl -i http://localhost:5081/health/ready    # 503
   curl -i http://localhost:5081/health/live     # 200
   # http://localhost:5080 → "API is unavailable right now." (the API returned 500 ProblemDetails)
   docker compose -f deploy/local/docker-compose.yml start postgres
   ```

10. One request through both services: open <http://localhost:5080>, then

    ```powershell
    docker compose -f deploy/local/docker-compose.yml logs web api | Select-String '"TraceId":"<id>"'
    ```

    Take `<id>` from the web's `Request finished` line: the same `TraceId` appears in the API's lines (W3C `traceparent`).
11. Code checks: `dotnet test`, `dotnet build` (no warnings), `dotnet format --verify-no-changes`
    (see [development.md](development.md#code-quality)).

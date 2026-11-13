# Stage 0 — App Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A runnable .NET 10 MVC Web + Web API + PostgreSQL skeleton in one env via docker compose.
It includes Swagger, CORS, health probes, a JS widget, runtime config and a code-quality baseline.

**Architecture:** (amended 2026-11-13, CQRS level 2 — see spec §5.1) `EphemeralEnvs.Api` (controllers) calls command/query handlers in
`EphemeralEnvs.Application`, which uses `EphemeralEnvs.Data` (EF Core/Npgsql) over `EphemeralEnvs.Domain`; the Api never sees a DbContext. It
exposes DTOs from `EphemeralEnvs.Contracts`. `EphemeralEnvs.Web` (MVC) calls the API server-side through a typed
HttpClient. Its JS widget calls the API from the browser, using a URL rendered into a `<meta>` tag.
All env-specific values are runtime env vars, and images are built once.

**Tech Stack:** .NET 10, ASP.NET Core MVC + Web API, EF Core + Npgsql, PostgreSQL 17,
Microsoft.AspNetCore.OpenApi + Swashbuckle.AspNetCore.SwaggerUI, xUnit, WebApplicationFactory,
Testcontainers.PostgreSql, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-11-11-branch-deployment-design.md` (§2, §5)

## Global Constraints

- **Never `git commit`.** Each task ends with "Stop & report": tell the user (in Ukrainian) what was
  added, how it works, and give step-by-step run/test instructions. Then wait for the user's go.
- `README.md` must always reflect how to run and test what exists so far.
- Target `net10.0`. Use central package management (`Directory.Packages.props`) and the latest
  stable package versions (EF Core/Npgsql 10.x).
- Build must have **0 warnings** (`TreatWarningsAsErrors=true`); `dotnet format --verify-no-changes` must pass.
- Every Docker command uses the Linux engine: `$env:DOCKER_CONTEXT='desktop-linux'`.
  Tests: no setting needed — `PostgresFixture` targets `npipe://./pipe/dockerDesktopLinuxEngine` on Windows (Task 3 ruling).
- **No migrations on app startup — ever** (permanent rule). In stage 0 *only*, the schema is applied
  by hand (`dotnet ef database update`) or in the test fixture. From stage 1 on, migrations run as a
  separate deploy step (EF migration bundle in a `migrator` container), and the pipelines (stage 5+)
  call that step before rolling out Web/API.
- CORS origins come only from `Cors__AllowedOrigins`. Never `AllowAnyOrigin`.
- Swagger is enabled by `Swagger__Enabled`, never by `IsDevelopment()`.
- Read configuration lazily (options / `IServiceProvider`), so `WebApplicationFactory` overrides apply.
- Ports: web host `5080`, api host `5081`, postgres host `5432`; overridable via `EE_WEB_PORT`,
  `EE_API_PORT`, `EE_DB_PORT`. Containers listen on `8080`. Never use host ports 80/443.

## Review Focus

1. `Price` too large for `numeric(18,2)` (e.g. `1e17`) → expect 400, not a 500 DB overflow. Test in Task 3.
2. `Name` whitespace-only or 201 chars → expect 400. Test in Task 3.
3. `GET /products/not-a-guid` → expect 404, not 400/500 (route constraint `{id:guid}`). Test in Task 3.
4. Browser `POST` from the widget triggers a CORS **preflight** (`OPTIONS` + `Content-Type: application/json`)
   → an allowed origin must pass preflight. Test in Task 4.
5. `Cors__AllowedOrigins` with spaces/trailing slash (`" http://localhost:5080/ "`) → still matches
   `http://localhost:5080`. Test in Task 4.

## File Structure

```
global.json                         SDK 10.0.x, rollForward latestFeature
.editorconfig, .gitignore, .dockerignore
Directory.Build.props               shared build/quality settings
Directory.Packages.props            central package versions
.config/dotnet-tools.json           dotnet-ef local tool
EphemeralEnvs.slnx
README.md
src/EphemeralEnvs.Domain/Product.cs
src/EphemeralEnvs.Data/AppDbContext.cs
src/EphemeralEnvs.Data/Configurations/ProductConfiguration.cs
src/EphemeralEnvs.Data/DesignTimeDbContextFactory.cs
src/EphemeralEnvs.Data/Migrations/*          InitialCreate (generated)
src/EphemeralEnvs.Contracts/ProductDto.cs, CreateProductRequest.cs
src/EphemeralEnvs.Api/Program.cs
src/EphemeralEnvs.Api/Controllers/ProductsController.cs
src/EphemeralEnvs.Api/Infrastructure/CorsSetup.cs          origin parsing + policy
src/EphemeralEnvs.Api/Infrastructure/AppOptions.cs         App:EnvironmentName
src/EphemeralEnvs.Api/Dockerfile
src/EphemeralEnvs.Web/Program.cs
src/EphemeralEnvs.Web/Services/ProductsApiClient.cs
src/EphemeralEnvs.Web/Options/ApiOptions.cs                InternalBaseUrl, PublicBaseUrl
src/EphemeralEnvs.Web/Controllers/HomeController.cs
src/EphemeralEnvs.Web/Views/Home/Index.cshtml, Widget.cshtml
src/EphemeralEnvs.Web/Views/Shared/_Layout.cshtml
src/EphemeralEnvs.Web/wwwroot/js/widget.js
src/EphemeralEnvs.Web/Dockerfile
tests/EphemeralEnvs.Api.Tests/PostgresFixture.cs           Testcontainers + migrate
tests/EphemeralEnvs.Api.Tests/ApiFactory.cs                WebApplicationFactory<Program>
tests/EphemeralEnvs.Api.Tests/ProductsTests.cs
tests/EphemeralEnvs.Api.Tests/HealthTests.cs
tests/EphemeralEnvs.Api.Tests/CorsTests.cs
tests/EphemeralEnvs.Api.Tests/SwaggerTests.cs
deploy/local/docker-compose.yml
```

---

### Task 1: Solution scaffold + code-quality baseline

**Files:** Create `global.json`, `.editorconfig`, `.gitignore`, `Directory.Build.props`,
`Directory.Packages.props`, `EphemeralEnvs.slnx`, the 5 `src/*` projects, `tests/EphemeralEnvs.Api.Tests`, `README.md`.

**Interfaces:**
- Produces: projects `EphemeralEnvs.Domain` (classlib), `EphemeralEnvs.Data` (classlib), `EphemeralEnvs.Contracts` (classlib),
  `EphemeralEnvs.Api` (`dotnet new webapi --use-controllers`), `EphemeralEnvs.Web` (`dotnet new mvc`),
  `EphemeralEnvs.Api.Tests` (`dotnet new xunit`). References: Api → Domain, Data, Contracts; Data → Domain;
  Web → Contracts; Tests → Api, Data.

- [ ] **Step 1: Create the solution, projects and references** using the templates above. Remove the
  template sample code (WeatherForecast, Privacy page, `UnitTest1`).
- [ ] **Step 2: `global.json`**: `{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }`.
- [ ] **Step 3: `Directory.Build.props`** with exactly: `TargetFramework=net10.0`, `Nullable=enable`,
  `ImplicitUsings=enable`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`,
  `TreatWarningsAsErrors=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=moderate`. Remove the duplicated
  properties from the individual csproj files.
- [ ] **Step 4: `Directory.Packages.props`** with `ManagePackageVersionsCentrally=true`; move all
  package versions there (csproj files keep `<PackageReference Include=... />` without a Version).
- [ ] **Step 5: `.editorconfig`**: start from `dotnet new editorconfig`, then set at `warning`:
  `csharp_style_namespace_declarations = file_scoped`, `csharp_prefer_braces = true`,
  `dotnet_style_require_accessibility_modifiers = always`, `csharp_style_var_elsewhere = true`,
  `IDE0005` (unnecessary usings) = warning, private-field naming `_camelCase`.
  `.gitignore`: `dotnet new gitignore` plus `*.tfstate*`, `.env`.
- [ ] **Step 6: Add the dotnet-ef local tool**: `dotnet new tool-manifest; dotnet tool install dotnet-ef`.
- [ ] **Step 7: README skeleton**: project purpose (1 paragraph, link to spec), prerequisites
  (.NET 10 SDK, Docker Desktop with the Linux engine, `$env:DOCKER_CONTEXT='desktop-linux'`), section
  "Code quality" with `dotnet build` and `dotnet format --verify-no-changes`.
- [ ] **Step 8: Verify**
  Run: `dotnet build` → `Build succeeded. 0 Warning(s) 0 Error(s)`
  Run: `dotnet format --verify-no-changes` → exit code 0
- [ ] **Step 9: Stop & report** (what was added, why each quality setting exists, how to try:
  add `var x = 1;` unused in a file → build fails; revert).

---

### Task 2: Domain + Data + InitialCreate migration

**Files:** Create `src/EphemeralEnvs.Domain/Product.cs`, `src/EphemeralEnvs.Data/AppDbContext.cs`,
`src/EphemeralEnvs.Data/Configurations/ProductConfiguration.cs`, `src/EphemeralEnvs.Data/DesignTimeDbContextFactory.cs`,
`src/EphemeralEnvs.Data/Migrations/*` (generated).

**Interfaces:**
- Produces: `public sealed class Product { Guid Id; string Name; decimal Price; DateTimeOffset CreatedAt }`
  with a constructor `Product(string name, decimal price, DateTimeOffset createdAt)` (Id = `Guid.CreateVersion7()`),
  plus a private parameterless ctor for EF.
- Produces: `public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext` with
  `DbSet<Product> Products`; `OnModelCreating` applies configurations from the assembly.
- Produces: `ProductConfiguration : IEntityTypeConfiguration<Product>`: table `products`; snake_case
  columns `id`, `name`, `price`, `created_at`; `Name` required, max length 200; `Price` `numeric(18,2)`.
- Produces: `DesignTimeDbContextFactory`: connection string from env var `EE_DB` or default
  `Host=localhost;Port=5432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs`.

- [ ] **Step 1: Implement the four files** (packages: `Npgsql.EntityFrameworkCore.PostgreSQL`,
  `Microsoft.EntityFrameworkCore.Design` in Data, with `PrivateAssets=all`).
- [ ] **Step 2: Generate the migration**
  Run: `dotnet ef migrations add InitialCreate --project src/EphemeralEnvs.Data`
  Expected: `src/EphemeralEnvs.Data/Migrations/*_InitialCreate.cs` contains `CreateTable(name: "products"` with a
  `numeric(18,2)` price and `character varying(200)` name.
- [ ] **Step 3: Verify** `dotnet build` (0 warnings) and `dotnet format --verify-no-changes`.
  Generated migration files may trigger style warnings; if so, exclude `Migrations/` in `.editorconfig`
  with `generated_code = true`, not by lowering global severities.
- [ ] **Step 4: Update README**: section "Database" explaining that the schema is applied by hand in
  stage 0 (`dotnet ef database update --project src/EphemeralEnvs.Data`, `EE_DB` env var if the port differs).
- [ ] **Step 5: Stop & report.**

---

### Task 3: Products API + ProblemDetails + integration tests

**Files:** Create `src/EphemeralEnvs.Contracts/ProductDto.cs`, `src/EphemeralEnvs.Contracts/CreateProductRequest.cs`,
`src/EphemeralEnvs.Api/Controllers/ProductsController.cs`, `tests/EphemeralEnvs.Api.Tests/PostgresFixture.cs`,
`tests/EphemeralEnvs.Api.Tests/ApiFactory.cs`, `tests/EphemeralEnvs.Api.Tests/ProductsTests.cs`. Modify `src/EphemeralEnvs.Api/Program.cs`.

**Interfaces:**
- Produces: `public sealed record ProductDto(Guid Id, string Name, decimal Price, DateTimeOffset CreatedAt);`
- Produces: `public sealed record CreateProductRequest(string Name, decimal Price);` with
  `[Required(AllowEmptyStrings = false)][StringLength(200)]` on Name (whitespace-only rejected) and
  `[Range(typeof(decimal), "0.01", "9999999999999999.99")]` on Price.
- Produces routes: `GET /products` → `ProductDto[]` ordered by `CreatedAt` desc; `GET /products/{id:guid}`
  → 200/404; `POST /products` → 201 + `Location: /products/{id}` + body `ProductDto`.
- Produces: `Program` must be visible to tests (`public partial class Program;`).
- Produces: `PostgresFixture : IAsyncLifetime` with `string ConnectionString` (postgres:17 container,
  `Database.MigrateAsync()` once); `ApiFactory(string connectionString, IDictionary<string,string?>? extra = null)
  : WebApplicationFactory<Program>`, which sets `ConnectionStrings:Default`, `Cors:AllowedOrigins=http://localhost:5080`
  and `Swagger:Enabled=true`, plus `extra`, via `UseSetting`.
- API wiring in Program: `AddControllers()`, `AddProblemDetails()`, `UseExceptionHandler()`,
  `UseStatusCodePages()`; DbContext registered with
  `AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")))`.
  **Amended (CQRS level 2):** registration moved to `EphemeralEnvs.Application.AddApplication()`; controller uses handlers;
  `ReadDbContext` (read-only) for queries; `ArchitectureTests` + `DisableTransitiveProjectReferences` keep Data out of Api.

- [ ] **Step 1: Verify Testcontainers reaches the Linux engine.** Write the fixture and one trivial test
  that starts the container.
  Run: `$env:DOCKER_HOST='npipe:////./pipe/dockerDesktopLinuxEngine'; dotnet test`
  Expected: PASS. If it fails, find the working setting (e.g. `~/.testcontainers.properties`
  `docker.host=...`), and record the result in README and STATUS.
- [ ] **Step 2: Write failing tests in `ProductsTests`** (shared fixture via `IClassFixture<PostgresFixture>`):
  - `Post_ValidProduct_Returns201_AndIsRetrievableById_AndInList`: POST `{Name:"Pen",Price:1.50}` → 201,
    Location header set; GET Location → 200 with the same Name/Price; GET /products contains its Id.
  - `Post_EmptyName_Returns400ProblemDetails`: `{Name:"",Price:1}` → 400, content-type
    `application/problem+json`, `errors` contains `Name`.
  - `Post_WhitespaceName_Returns400`: `{Name:"   ",Price:1}` → 400.
  - `Post_NameOf201Chars_Returns400`.
  - `Post_ZeroPrice_Returns400`: `{Name:"Pen",Price:0}` → 400, `errors` contains `Price`.
  - `Post_PriceTooLarge_Returns400`: `{Name:"Pen",Price:1e17}` → 400 (not 500).
  - `Get_UnknownId_Returns404ProblemDetails`: random Guid → 404 `application/problem+json`.
  - `Get_NonGuidId_Returns404`: `/products/not-a-guid` → 404.
- [ ] **Step 3: Run** `dotnet test` → the new tests FAIL (404 / route missing).
- [ ] **Step 4: Implement the DTOs, the controller (manual Product ↔ DTO mapping) and the Program wiring.**
  `CreatedAt = TimeProvider.System.GetUtcNow()` (register `TimeProvider.System` as a singleton).
- [ ] **Step 5: Run** `dotnet test` → all PASS. `dotnet build` 0 warnings, format OK.
- [ ] **Step 6: Update README**: section "Tests" (the `DOCKER_HOST` setting, `dotnet test`).
- [ ] **Step 7: Stop & report.**

---

### Task 4: Health probes, CORS, Swagger flag, environment name, JSON logs

**Files:** Create `src/EphemeralEnvs.Api/Infrastructure/CorsSetup.cs`, `src/EphemeralEnvs.Api/Infrastructure/AppOptions.cs`,
`tests/EphemeralEnvs.Api.Tests/HealthTests.cs`, `CorsTests.cs`, `SwaggerTests.cs`. Modify `src/EphemeralEnvs.Api/Program.cs`,
`src/EphemeralEnvs.Api/appsettings.json`.

**Interfaces:**
- Consumes: `ApiFactory`, `PostgresFixture` (Task 3).
- Produces: `public static class CorsSetup { public const string PolicyName = "AppOrigins";
  public static string[] ParseOrigins(string? raw); }`. Split on `,`, trim whitespace and a trailing `/`,
  drop empties, and never return `*` (throw `InvalidOperationException` if present). The policy allows any
  header and the methods GET/POST/OPTIONS for the parsed origins.
- Produces: `AppOptions { string EnvironmentName = "unknown" }` bound to section `App`.
- Health: `/health/live` with `Predicate = _ => false` (no checks); `/health/ready` with a DB check
  tagged `ready` (`AddDbContextCheck<AppDbContext>(tags: ["ready"])`, package
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`), filtered by that tag.
  **Amended:** the Api cannot see `AppDbContext` — expose the check from Application
  (e.g. `IHealthChecksBuilder.AddDatabaseCheck()` extension), the Api only calls it.
- Swagger: `AddOpenApi()` with a document transformer that sets `Info.Description =
  $"Environment: {EnvironmentName}"`. When `Swagger:Enabled` is true, map `/openapi/v1.json` and
  `UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"))` at `/swagger`.
- Logging: `builder.Logging.AddJsonConsole(o => o.IncludeScopes = true)` and
  `builder.Logging.Configure(o => o.ActivityTrackingOptions = TraceId | SpanId | ParentId)`.
  Clear other providers.

- [ ] **Step 1: Write failing tests**:
  - `HealthTests.Live_Returns200` and `Ready_WithDb_Returns200`.
  - `HealthTests.Ready_WithUnreachableDb_Returns503_AndLiveStays200`: an `ApiFactory` with connection string
    `Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2` → ready 503, live 200.
  - `CorsTests.AllowedOrigin_GetsAllowOriginHeader`: GET /products with `Origin: http://localhost:5080` →
    header `Access-Control-Allow-Origin: http://localhost:5080`.
  - `CorsTests.UnknownOrigin_GetsNoAllowOriginHeader`: `Origin: http://evil.localhost` → header absent.
  - `CorsTests.Preflight_ForJsonPost_FromAllowedOrigin_Succeeds`: OPTIONS /products with `Origin`,
    `Access-Control-Request-Method: POST`, `Access-Control-Request-Headers: content-type` → 204 and
    allow-origin header present.
  - `CorsTests.ParseOrigins_TrimsSpacesAndTrailingSlash`: `" http://localhost:5080/ , http://a.localhost:8080"`
    → `["http://localhost:5080","http://a.localhost:8080"]`; `"*"` → throws.
  - `SwaggerTests.Enabled_ServesDocWithEnvironmentName`: `Swagger:Enabled=true`, `App:EnvironmentName=test-env`
    → `/openapi/v1.json` 200 and its body contains `Environment: test-env`; `/swagger/index.html` 200.
  - `SwaggerTests.Disabled_Returns404`: `Swagger:Enabled=false` → `/openapi/v1.json` 404, `/swagger/index.html` 404.
- [ ] **Step 2: Run** `dotnet test` → the new tests FAIL.
- [ ] **Step 3: Implement** CorsSetup, AppOptions and the Program wiring (`UseCors(PolicyName)` before
  `MapControllers`). In `appsettings.json` set defaults: `Swagger:Enabled=false`,
  `App:EnvironmentName="unknown"`, `Cors:AllowedOrigins=""`.
- [ ] **Step 4: Run** `dotnet test` → all PASS; build 0 warnings; format OK.
- [ ] **Step 5: Update README**: an API config table (the 4 keys from spec §5.3) and the health endpoints.
- [ ] **Step 6: Stop & report.**

---

### Task 5: Web (MVC): server-side list, JS widget, error handling

**Files:** Create `src/EphemeralEnvs.Web/Options/ApiOptions.cs`, `src/EphemeralEnvs.Web/Services/ProductsApiClient.cs`,
`src/EphemeralEnvs.Web/Views/Home/Widget.cshtml`, `src/EphemeralEnvs.Web/wwwroot/js/widget.js`. Modify
`src/EphemeralEnvs.Web/Program.cs`, `Controllers/HomeController.cs`, `Views/Home/Index.cshtml`,
`Views/Shared/_Layout.cshtml`, `appsettings.json`.

**Interfaces:**
- Consumes: `ProductDto` (Contracts); API routes from Task 3; CORS from Task 4.
- Produces: `ApiOptions { Uri InternalBaseUrl; Uri PublicBaseUrl }` bound to section `Api`,
  `ValidateOnStart` (both required, absolute). `AppOptions`-equivalent `App:EnvironmentName` read via
  `IConfiguration` in the layout.
- Produces: `public sealed class ProductsApiClient(HttpClient http)` with
  `Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken ct)`. Typed client, `BaseAddress =
  InternalBaseUrl`, `Timeout = 5s`.
- `HomeController.Index`: calls the client. On `HttpRequestException` / `TaskCanceledException` /
  non-success it logs a warning and renders the view with `ViewData["Error"] = "API is unavailable right now."`
  (a friendly message, HTTP 200 page). `HomeController.Widget`: just the view.
- Layout: `<meta name="api-base-url" content="@PublicBaseUrl">`, a nav with "Server-side" (`/`) and
  "Widget" (`/Home/Widget`), footer `Environment: @App:EnvironmentName`.
- `widget.js` (no framework, `fetch`): reads the meta tag, GETs `{base}/products` and renders a list;
  the form POSTs JSON `{name, price}`, then reloads the list. Non-2xx → show ProblemDetails `title` plus
  `errors` inline; network error → "API is unavailable". It reads its config only from the meta tag.
- Logging: the same JSON console + ActivityTrackingOptions setup as the API (Task 4).

- [ ] **Step 1: Implement** the files above. No Web tests in stage 0 (spec §5.8).
- [ ] **Step 2: Verify locally without Docker for the Web**: run the API (`dotnet run --project src/EphemeralEnvs.Api`)
  against Postgres from Task 2's manual setup, run the Web with `Api__InternalBaseUrl` and
  `Api__PublicBaseUrl` pointing at the API, and open `/` and `/Home/Widget`.
  Expected: both lists render, and adding a product via the widget works.
- [ ] **Step 3: Verify the failure path**: stop the API → `/` shows "API is unavailable right now."
  (no exception page); the widget shows "API is unavailable".
- [ ] **Step 4:** build 0 warnings; format OK.
- [ ] **Step 5: Update README**: a Web config table (3 keys from spec §5.4) and the two pages.
- [ ] **Step 6: Stop & report.**

---

### Task 6: Dockerfiles + docker compose + end-to-end check

**Files:** Create `src/EphemeralEnvs.Api/Dockerfile`, `src/EphemeralEnvs.Web/Dockerfile`, `.dockerignore`,
`deploy/local/docker-compose.yml`. Modify `README.md`.

**Interfaces:**
- Consumes: all config keys from Tasks 3–5.
- Dockerfiles: multi-stage `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`.
  Build context = repo root (so `Directory.*.props` and `global.json` are included). `ASPNETCORE_HTTP_PORTS=8080`,
  `USER $APP_UID` (the non-root user built into the image).
- Compose (project name `ephemeral-envs-local`):
  - `postgres`: `postgres:17`, env `POSTGRES_DB/USER/PASSWORD=ephemeral_envs`, volume `pgdata`, port `${EE_DB_PORT:-5432}:5432`,
    healthcheck `pg_isready -U ephemeral_envs -d ephemeral_envs`.
  - `api`: build `src/EphemeralEnvs.Api/Dockerfile`, port `${EE_API_PORT:-5081}:8080`, `depends_on: postgres: condition: service_healthy`,
    env exactly as the spec §5.3 table (Cors origin uses `${EE_WEB_PORT:-5080}`).
  - `web`: build `src/EphemeralEnvs.Web/Dockerfile`, port `${EE_WEB_PORT:-5080}:8080`, `depends_on: api`, env as spec §5.4
    (PublicBaseUrl uses `${EE_API_PORT:-5081}`).

- [ ] **Step 1: Implement** the Dockerfiles, `.dockerignore` (`bin/`, `obj/`, `.git/`, `tests/`, `docs/`) and compose.
- [ ] **Step 2: Run** `$env:DOCKER_CONTEXT='desktop-linux'; docker compose -f deploy/local/docker-compose.yml up --build -d`
  Expected: 3 containers running, postgres `healthy`.
- [ ] **Step 3: Apply the schema** with `dotnet ef database update --project src/EphemeralEnvs.Data` → `Done.`
- [ ] **Step 4: Walk through spec §5.9 items 3–7 by hand** and note the results:
  `/` lists products and shows the footer `Environment: local`; the widget adds a product and DevTools shows
  the CORS request to `:5081`; `:5081/swagger` works; `curl :5081/health/live` and `/health/ready` return 200;
  `docker compose stop postgres` → ready 503, live 200, `/` shows the friendly error;
  `docker compose logs api web` → one request's `TraceId` appears in both. Restart postgres afterwards.
- [ ] **Step 5: Verify** `dotnet test` green, `dotnet build` 0 warnings, `dotnet format --verify-no-changes` OK.
- [ ] **Step 6: README "Stage 0: how to run"**: the numbered steps from spec §5.9, including the
  port-override variables and how to tear down (`docker compose ... down -v`).
- [ ] **Step 7: Update `docs/STATUS.md`** (stage 0 implemented; next = stage 1 design refresh).
- [ ] **Step 8: Stop & report** (the full stage summary + the "how to try" checklist).

# Stage FF1: Feature Flags from Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A feature-flag playground: three flags (`ProductStats`, `NewBadge`, `ProductCount`) that show situations A–F and H of the spec, switchable per env with `deploy.ps1 -Flags`.

**Architecture:** `Microsoft.FeatureManagement.AspNetCore` in Api and Web, flag names as shared constants in Contracts, every flag declared `false` in each app's `appsettings.json`. The API gates `GET /products/stats` and serves an allow-listed `GET /features` for the JS widget; the Web renders server-side parts behind its own flags. `deploy.ps1 -Flags` writes per-service env files from the normalized label `ee.flags`.

**Tech Stack:** .NET 10, ASP.NET Core MVC + Web API, Microsoft.FeatureManagement.AspNetCore 4.x, xUnit, Testcontainers, PowerShell 7.2 + Pester, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-11-23-stage-ff1-feature-flags-design.md`

## Global Constraints

- **Never commit** (CLAUDE.md): every task ends with "Stop and report"; the user reviews and commits, then says to continue. The report: what was added, how it works, step-by-step run/test guide (Ukrainian).
- Flag names only through `EphemeralEnvs.Contracts.FeatureFlags` constants; no string literals of flag names elsewhere in C#.
- Each app declares every flag it uses under `FeatureManagement` in its `appsettings.json`, default `false` (until the H "release" task).
- Without `-Flags` a deploy has no overrides: flags = `appsettings.json` defaults of the deployed commit.
- The browser gets only the client allow-list (`ClientFeatureFlags`, today `ProductStats`); the server enforces (404), the UI only follows.
- Exact UI copy: `Statistics are not available (feature off in the API).` / `Statistics are unavailable right now.` / badge text `New` / heading `Products (<count>)`.
- "Recent" for the badge = `CreatedAt` within the last 24 hours (UTC).
- Build: `TreatWarningsAsErrors`, analyzers, central package management (`Directory.Packages.props`), `dotnet format --verify-no-changes` clean.
- Scripts: PowerShell 7.2+, ASCII only, logic in `deploy/lib/EphemeralEnvs.Deploy.psm1`, thin scripts. State lives in container labels; `.ee/` files are transient.
- Docs updated per CLAUDE.md (README short; `docs/environments.md`, `docs/development.md`, `docs/architecture.md`); `docs/STATUS.md` after every task.

## Review Focus

1. Flag name in another case (`-Flags productstats`) → accepted and normalized to the declared name (.NET config keys are case-insensitive). Pinned: Pester case in Task 4.
2. Re-deploy **without** `-Flags` after a deploy with flags → overrides gone (empty label, empty env files), defaults apply. Pinned: Pester case + manual step in Task 4.
3. `db-reset` of an env deployed with flags → recreated containers keep the same flags. Pinned: manual step in Task 4.
4. The widget against an API without `/features` (older branch) or with `/features` failing → products still render, no error box. Pinned: manual step in Task 2.
5. Stats call timing out (Web server-side) → products stay listed, panel says "unavailable right now". Pinned: Web test in Task 3.

---

### Task 1: Flags foundation + server-side gate (situation B)

**Files:**
- Modify: `Directory.Packages.props` (add `Microsoft.FeatureManagement.AspNetCore`, latest stable 4.x — check with `dotnet package search Microsoft.FeatureManagement.AspNetCore --exact-match`)
- Modify: `src/EphemeralEnvs.Api/EphemeralEnvs.Api.csproj`, `src/EphemeralEnvs.Api/Program.cs`, `src/EphemeralEnvs.Api/appsettings.json`, `src/EphemeralEnvs.Api/Controllers/ProductsController.cs`
- Create: `src/EphemeralEnvs.Contracts/FeatureFlags.cs`, `src/EphemeralEnvs.Contracts/ProductStatsDto.cs`, `src/EphemeralEnvs.Application/Products/GetProductStats.cs`
- Test: `tests/EphemeralEnvs.Api.Tests/ProductStatsTests.cs`, `tests/EphemeralEnvs.ArchitectureTests/FeatureFlagTests.cs`

**Interfaces:**
- Produces: `public static class FeatureFlags { public const string ProductStats = "ProductStats"; public static IReadOnlyList<string> All { get; } }` (Contracts; later tasks add `NewBadge`, `ProductCount` and extend `All`).
- Produces: `public sealed record ProductStatsDto(int Count, decimal? AveragePrice, DateTimeOffset? NewestCreatedAt);` (Contracts).
- Produces: `public sealed record GetProductStatsQuery;` + internal handler `IQueryHandler<GetProductStatsQuery, ProductStatsDto>` (Application, read side via `IReadDb`, registered like the other handlers).
- Produces: `GET /products/stats` → 200 `ProductStatsDto`, 404 when `ProductStats` is off.

- [ ] **Step 1: Write the failing API tests** in `ProductStatsTests` (own `IClassFixture<PostgresFixture>`, `ApiFactory` with `extra` settings):
  - `Stats_FlagOff_Returns404` — factory with `{"FeatureManagement:ProductStats": "false"}`; `GET /products/stats` → 404.
  - `Stats_FlagOn_MatchesProductList` — flag `"true"`; POST two products (1.00, 3.00); `GET /products` and `GET /products/stats`; assert `Count == list.Length`, `AveragePrice == list.Average(p => p.Price)`, `NewestCreatedAt == list.Max(p => p.CreatedAt)` (compare to the list, because the class's database is shared by its tests).
  - `Stats_FlagOn_EmptyDatabase_ReturnsZeroAndNulls` — in a **separate** test class `ProductStatsEmptyTests` with its own fixture: `Count == 0`, `AveragePrice == null`, `NewestCreatedAt == null`.
- [ ] **Step 2: Write the failing architecture tests** in `FeatureFlagTests` (read `src/EphemeralEnvs.Api/appsettings.json` and `src/EphemeralEnvs.Web/appsettings.json`, found by walking up from `AppContext.BaseDirectory` to the folder with `EphemeralEnvs.slnx`; a missing `FeatureManagement` section = no flags):
  - `AppSettings_DeclareOnlyKnownFlags` — every key under `FeatureManagement` in each file is in `FeatureFlags.All` (message names file and key).
  - `EveryFlag_IsDeclaredBySomeApp` — every name in `FeatureFlags.All` is a key in at least one file.
- [ ] **Step 3: Run** `dotnet test tests/EphemeralEnvs.Api.Tests --filter "FullyQualifiedName~ProductStats"` and `dotnet test tests/EphemeralEnvs.ArchitectureTests --filter FullyQualifiedName~FeatureFlagTests`.
  Expected: compile errors / FAIL (`FeatureFlags`, `ProductStatsDto` do not exist).
- [ ] **Step 4: Implement**: constants + DTO + query/handler (`Count`, `Average` of price as nullable, `Max` of `CreatedAt` as nullable, one query); `builder.Services.AddFeatureManagement()` in Api `Program.cs`; `"FeatureManagement": { "ProductStats": false }` in Api `appsettings.json`; action `[HttpGet("stats")] [FeatureGate(FeatureFlags.ProductStats)] public Task<ProductStatsDto> GetStats(...)` declared **before** `GetById` (route `stats` must not be taken as an id; `{id:guid}` already prevents it, keep the order anyway).
- [ ] **Step 5: Run** both commands again, then `dotnet test` and `dotnet format --verify-no-changes`.
  Expected: all PASS, build 0 warnings.
- [ ] **Step 6: Check in Docker** — `./deploy/local/up.ps1`; `curl.exe -i http://localhost:5081/products/stats` → 404. Stop the local env only if it was not running before.
- [ ] **Step 7: STATUS + stop and report.** Report how to see 404 vs 200 locally (`$env:FeatureManagement__ProductStats='true'; dotnet run --project src/EphemeralEnvs.Api` or the IDE). User commits.

### Task 2: Client flags `GET /features` + widget stats bar (situation D)

**Files:**
- Create: `src/EphemeralEnvs.Api/Controllers/FeaturesController.cs`, `src/EphemeralEnvs.Api/ClientFeatureFlags.cs`
- Modify: `src/EphemeralEnvs.Web/wwwroot/js/widget.js`, `src/EphemeralEnvs.Web/Views/Home/Widget.cshtml` (container for the bar)
- Test: `tests/EphemeralEnvs.Api.Tests/FeaturesTests.cs`

**Interfaces:**
- Consumes: `FeatureFlags.ProductStats`, `GET /products/stats` (Task 1).
- Produces: `internal static class ClientFeatureFlags { public static IReadOnlyList<string> Names { get; } = [FeatureFlags.ProductStats]; }` (Api).
- Produces: `GET /features` → `200 application/json`, object with one camelCase key per allow-listed flag (`{"productStats":true}`), header `Cache-Control: no-store`. Keys built with `JsonNamingPolicy.CamelCase.ConvertName` (dictionary keys are not camelCased by default).

- [ ] **Step 1: Write the failing tests** in `FeaturesTests`:
  - `Features_ReturnsAllowListedFlagState` — theory over `"true"`/`"false"` for `FeatureManagement:ProductStats`; body parsed as `Dictionary<string,bool>` equals exactly `{ "productStats": <value> }`.
  - `Features_NeverExposesNonClientFlags` — settings `FeatureManagement:SomeServerOnlyFlag=true`; body has no key other than `productStats`.
  - `Features_IsNotCached` — `Cache-Control` contains `no-store`.
  - `Features_AllowsCorsFromWeb` — preflight/GET with `Origin: http://localhost:5080` returns `Access-Control-Allow-Origin` (same as `CorsTests` does for `/products`).
- [ ] **Step 2: Run** `dotnet test tests/EphemeralEnvs.Api.Tests --filter FullyQualifiedName~FeaturesTests`. Expected: FAIL (404 for `/features`).
- [ ] **Step 3: Implement** `FeaturesController` (`[Route("features")]`, uses `IFeatureManager.IsEnabledAsync` for each of `ClientFeatureFlags.Names`).
- [ ] **Step 4: Run** the same command, then `dotnet test`. Expected: PASS.
- [ ] **Step 5: Widget.** In `widget.js`: fetch `/features` in parallel with `/products`; if `features.productStats === true`, fetch `/products/stats` and render one line above the table: `<count> products · average <avg, 2 decimals> · newest <yyyy-MM-dd HH:mm:ss UTC>` (text via `textContent`; empty list → `0 products`). Any failure of `/features` or stats (network, non-2xx, bad JSON) → no bar and **no** error box; the products and the add-form behave exactly as before.
- [ ] **Step 6: Check by hand** (`deploy/local`, flag via a temporary `FeatureManagement__ProductStats: "true"` on the api service, or the IDE): flag on → bar visible and updates after adding a product; flag off → no bar; `/features` failing (browser DevTools → Network → right-click the `/features` request → Block request URL, reload) → no bar, no error box, products render (Review Focus 4). Record what was checked in the report.
- [ ] **Step 7: STATUS + stop and report.** User commits.

### Task 3: Web server-side flags: `NewBadge` (C) and stats panel (E)

**Files:**
- Modify: `src/EphemeralEnvs.Web/EphemeralEnvs.Web.csproj`, `Program.cs`, `appsettings.json`, `Controllers/HomeController.cs`, `Services/ProductsApiClient.cs`, `Views/Home/Index.cshtml`, `Views/_ViewImports.cshtml` (feature tag helper, if used)
- Modify: `src/EphemeralEnvs.Contracts/FeatureFlags.cs` (add `NewBadge`)
- Create: `src/EphemeralEnvs.Web/Models/HomeIndexModel.cs`
- Test: `tests/EphemeralEnvs.Web.Tests/FeatureFlagPageTests.cs`

**Interfaces:**
- Consumes: `ProductStatsDto`, `FeatureFlags.ProductStats` (Task 1).
- Produces: `FeatureFlags.NewBadge = "NewBadge"` (in `All`).
- Produces: `ProductsApiClient.GetStatsAsync(CancellationToken ct) : Task<ProductStatsDto?>` — `null` on 404; throws `HttpRequestException` / `TaskCanceledException` on other failures (like `GetProductsAsync`).
- Produces: `public sealed record HomeIndexModel(IReadOnlyList<ProductDto> Products, bool ShowStats, ProductStatsDto? Stats, string? StatsMessage);` — the `Index` view's model.
- Web `appsettings.json`: `"FeatureManagement": { "ProductStats": false, "NewBadge": false }`.

- [ ] **Step 1: Write the failing Web tests** in `FeatureFlagPageTests` (`WebFactory` settings + `StubApiHandler`; the stub answers `GET /products` with one product created 1 hour ago named `Fresh` and one created 48 hours ago named `Old`):
  - `NewBadge_On_MarksOnlyRecentProducts` — badge `New` appears once, in the `Fresh` row.
  - `NewBadge_Off_NoBadge`.
  - `Stats_WebFlagOff_DoesNotCallStatsEndpoint` — `stub.Requests` has no `/products/stats`.
  - `Stats_On_RendersPanel` — stub returns stats `{count:2, averagePrice:2.50, newestCreatedAt:...}`; page contains `2` and `2.50`.
  - `Stats_On_Api404_ShowsNotAvailable_AndProducts` — page 200, contains `Statistics are not available (feature off in the API).` and both product names (situation E).
  - `Stats_On_Api500_ShowsUnavailable_AndProducts` — contains `Statistics are unavailable right now.` and both names.
  - `Stats_On_Timeout_ShowsUnavailable_AndProducts` — stub throws `TaskCanceledException` for `/products/stats` only (Review Focus 5).
  - `Products_Unreachable_StillShowsApiUnavailable` — existing behavior unchanged with the flag on (no stats call needed).
- [ ] **Step 2: Run** `dotnet test tests/EphemeralEnvs.Web.Tests --filter FullyQualifiedName~FeatureFlagPageTests`. Expected: FAIL.
- [ ] **Step 3: Implement**: `AddFeatureManagement()` in Web; `GetStatsAsync`; `Index` loads products (existing error path unchanged), then, only if `ProductStats` is on, stats in its own try/catch (404 → not-available message, other failures → unavailable message, request-aborted cancellation still propagates); `Index.cshtml` uses `HomeIndexModel`, renders panel when `ShowStats`, badge when `NewBadge` on and `CreatedAt >= UtcNow - 24h`.
- [ ] **Step 4: Run** the same command, then `dotnet test`, `dotnet format --verify-no-changes`. Expected: PASS, 0 warnings; the architecture flag tests still pass (`NewBadge` declared by Web).
- [ ] **Step 5: STATUS + stop and report** (how to try with `FeatureManagement__NewBadge` / `FeatureManagement__ProductStats` on the Web only). User commits.

### Task 4: `deploy.ps1 -Flags` (situation A, staging E)

**Files:**
- Modify: `deploy/lib/EphemeralEnvs.Deploy.psm1`, `deploy/deploy.ps1`, `deploy/list.ps1`, `deploy/env/compose.yml`
- Test: `tests/deploy/Flags.Tests.ps1`

**Interfaces:**
- Produces (pure, exported, Pester-tested):
  - `Get-DeclaredFlags -CodeRoot <string>` → `@{ Api = [string[]]; Web = [string[]] }` from `src/EphemeralEnvs.{Api,Web}/appsettings.json` `FeatureManagement` keys (missing section → empty).
  - `ConvertTo-FlagOverrides -Flags <string[]> -Declared <hashtable>` → `[ordered]@{ Api = [ordered]@{ <Name> = <bool> }; Web = [ordered]@{...} }`. Rules: `Name` = true; `Name=true|false` (case-insensitive value); prefix `Api:`/`Web:` (case-insensitive) limits to that service; no prefix → every service that declares the name; names matched case-insensitively and written with the declared casing. Errors (throw, message contains the quoted input): unknown flag (declared by neither) `"Unknown flag 'X'. Declared: ..."`, prefixed flag not declared by that service, unknown prefix, bad value, same flag twice for a service.
  - `ConvertTo-FlagsLabel -Overrides` → `"Api:ProductStats=true;Web:NewBadge=true;Web:ProductStats=true"` (always service-prefixed, sorted by service then name; `''` when none). *(Plan decision: the label is always prefixed so it round-trips without the declared lists; the spec's example label was unprefixed.)*
  - `ConvertFrom-FlagsLabel -Label` → overrides (inverse; `''`/`$null` → empty).
  - `Get-FlagsEnvFileContent -Overrides -Service <Api|Web>` → lines `FeatureManagement__<Name>=true|false`, `''` when none.
- Produces (side effects): `Write-EnvFlagFiles -EnvName -Overrides` → writes `.ee/envs/<env>/api.flags.env` and `web.flags.env`, returns their paths; `Set-EnvComposeVariables` gains `-FlagsLabel` and sets `EE_FLAGS`, `EE_API_FLAGS_FILE`, `EE_WEB_FLAGS_FILE`; `Get-EnvState` gains `Flags` (label); `Get-DeployedEnvs` rows gain `Flags`.
- Compose: `api` and `web` get `env_file: [{ path: "${EE_API_FLAGS_FILE:-no-flags.env}", required: false }]` (web: `EE_WEB_FLAGS_FILE`) and label `ee.flags=${EE_FLAGS:-}` on both. `required: false` keeps a checkout whose module does not set the variable working.

- [ ] **Step 1: Write the failing Pester tests** in `tests/deploy/Flags.Tests.ps1` (`$declared = @{ Api = @('ProductStats'); Web = @('ProductStats','NewBadge') }`):
  - bare name → both declaring services: `ProductStats` → Api.ProductStats=true, Web.ProductStats=true; `NewBadge` → only Web.
  - `ProductStats=false` → false for both; value `TRUE` accepted.
  - `Web:ProductStats` → only Web; `api:productstats` → Api with name `ProductStats` (Review Focus 1).
  - errors: `Nope` (`*Unknown flag 'Nope'*`), `Api:NewBadge` (`*Api*NewBadge*`), `Db:ProductStats` (`*prefix*`), `ProductStats=yes` (`*true or false*`), `ProductStats,Web:ProductStats=false` (`*twice*`).
  - empty `-Flags` → both services empty, label `''`, env file contents `''` (Review Focus 2).
  - label: overrides → `Api:ProductStats=true;Web:NewBadge=true;Web:ProductStats=true`; `ConvertFrom-FlagsLabel` of that equals the overrides; `ConvertFrom-FlagsLabel ''` → empty.
  - env file content for Web: `FeatureManagement__NewBadge=true` and `FeatureManagement__ProductStats=true` lines.
  - `Get-DeclaredFlags` on a temp folder with two small appsettings files (one without `FeatureManagement`).
- [ ] **Step 2: Run** `pwsh -NoProfile -c "Invoke-Pester tests/deploy -Output Detailed"`. Expected: the new tests FAIL (functions missing), the old 32 pass.
- [ ] **Step 3: Implement** the pure functions; run Pester. Expected: all PASS.
- [ ] **Step 4: Wire it in.** `deploy.ps1 -Flags <string[]>` (help + example). `Invoke-EnvDeploy`: declared flags from `$codeRoot`; overrides computed **before** building images (bad flags fail fast); flag files written and variables set before migrate; warn `"This commit's deploy/env/compose.yml does not read flags (no EE_API_FLAGS_FILE); -Flags ignored."` when `-Flags` given and the compose file lacks `EE_API_FLAGS_FILE`; summary line `Flags      <label or 'defaults (appsettings.json)'>`. `db-reset`/`db-update` regenerate the files from `Get-EnvState`'s `Flags` before compose runs. `destroy` removes `.ee/envs/<env>/`. `list.ps1` adds the `Flags` column.
- [ ] **Step 5: Check in Docker** (scratch env names, destroy them after):
  1. `./deploy/deploy.ps1 -Env ff-off` and `./deploy/deploy.ps1 -Env ff-on -Flags ProductStats,NewBadge` → same image tag; `ff-off`: no badge/panel/bar, `curl.exe -i http://ff-off-api.localhost:8080/products/stats` 404; `ff-on`: badge, panel, widget bar, stats 200 (A, B, C, D).
  2. `./deploy/deploy.ps1 -Env ff-e -Flags Web:ProductStats` → Index panel says "not available (feature off in the API)", products listed (E).
  3. `./deploy/list.ps1` → Flags column shows the labels.
  4. `./deploy/db-reset.ps1 -Env ff-on` → badge/panel still there (Review Focus 3).
  5. `./deploy/deploy.ps1 -Env ff-on` (no `-Flags`) → features gone, list Flags empty (Review Focus 2).
  6. `./deploy/deploy.ps1 -Env ff-x -Flags Nope` → fails before building, message lists declared flags.
  7. `./deploy/destroy.ps1` each; `.ee/envs/<env>/` gone; `./deploy/prune-images.ps1`.
- [ ] **Step 6: Docs:** `docs/environments.md` section "Feature flags" (syntax, defaults, list column, walkthroughs A–E from Step 5); README one line. STATUS.
- [ ] **Step 7: Stop and report.** User commits.

### Task 5: `ProductCount` — introduce (situation H, step 1 of 3)

**Files:**
- Modify: `src/EphemeralEnvs.Contracts/FeatureFlags.cs` (add `ProductCount`), `src/EphemeralEnvs.Web/appsettings.json` (`"ProductCount": false`), `HomeController.cs` / `Index.cshtml` (heading)
- Test: `tests/EphemeralEnvs.Web.Tests/FeatureFlagPageTests.cs`

- [ ] **Step 1: Failing tests:** `ProductCount_On_HeadingShowsCount` (stub with 2 products → `<h1>Products (2)</h1>`), `ProductCount_Off_HeadingWithoutCount` (`<h1>Products</h1>`).
- [ ] **Step 2: Run** `dotnet test tests/EphemeralEnvs.Web.Tests --filter "FullyQualifiedName~ProductCount"`. Expected: FAIL.
- [ ] **Step 3: Implement**; **Step 4: Run** it and `dotnet test`. Expected: PASS (architecture test sees `ProductCount` declared by Web).
- [ ] **Step 5: Check:** `deploy.ps1 -Env ff-h` → `Products`; `deploy.ps1 -Env ff-h -Flags ProductCount` → `Products (5)`. Keep `ff-h` for Tasks 6–7.
- [ ] **Step 6: STATUS + stop and report.** User commits ("introduce").

### Task 6: `ProductCount` — release (H, step 2 of 3)

- [ ] **Step 1: Failing test first:** rename `ProductCount_Off_HeadingWithoutCount` → keep it (explicit `"false"` setting) and add `ProductCount_Default_HeadingShowsCount` (no setting → `Products (2)`). Run → FAIL.
- [ ] **Step 2:** Web `appsettings.json`: `"ProductCount": true`. Nothing else changes. Run `dotnet test` → PASS.
- [ ] **Step 3: Check:** `deploy.ps1 -Env ff-h` → `Products (5)` without `-Flags`; `deploy.ps1 -Env ff-h -Flags ProductCount=false` → `Products`.
- [ ] **Step 4: STATUS + stop and report.** User commits ("release").

### Task 7: `ProductCount` — retire (H, step 3 of 3)

- [ ] **Step 1: Show the guard working (RED):** remove `ProductCount` from `FeatureFlags` and the flag check (heading always with the count), but leave `"ProductCount": true` in Web `appsettings.json`. Run `dotnet test tests/EphemeralEnvs.ArchitectureTests --filter FullyQualifiedName~FeatureFlagTests`. Expected: FAIL — `AppSettings_DeclareOnlyKnownFlags` names `ProductCount` in the Web file. Paste the message into the report.
- [ ] **Step 2:** Remove the config entry; delete `ProductCount_Off_HeadingWithoutCount`; keep the heading test as `Index_HeadingShowsCount` (no flag). Run `dotnet test` → PASS.
- [ ] **Step 3: Check:** `deploy.ps1 -Env ff-h` → `Products (5)`; `deploy.ps1 -Env ff-h -Flags ProductCount` → fails `Unknown flag 'ProductCount'`. `destroy.ps1 -Env ff-h`.
- [ ] **Step 4: STATUS + stop and report.** User commits ("retire").

### Task 8: Docs

**Files:** `docs/environments.md` (H walkthrough: the three commits and what each deploy shows), `docs/development.md` (add a flag: constant + `All`, `appsettings.json` of each app that uses it, tests on/off, `ClientFeatureFlags` if the browser needs it; run with a flag from the IDE; retire a flag: the Task 5–7 steps and the guard test), `docs/architecture.md` (flag rules from Global Constraints; layout rows for `FeaturesController`/`ClientFeatureFlags`), README (docs table text), parent spec FF1 row → **Done** + link to this stage's spec, `docs/STATUS.md`.

- [ ] **Step 1: Write** the docs.
- [ ] **Step 2: Verify** relative links/anchors resolve (same script as stage 2 Task 7) and the environments.md walkthroughs match Task 4 Step 5 output.
- [ ] **Step 3: Stop and report.** Then final whole-stage review.

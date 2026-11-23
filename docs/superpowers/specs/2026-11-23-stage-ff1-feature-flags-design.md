# Stage FF1: feature flags from configuration — design

Status: draft for the user's review (2026-11-23). Parent: [`2026-11-11-branch-deployment-design.md`](2026-11-11-branch-deployment-design.md)
(roadmap row FF1). Builds on stage 2 ([`2026-11-18-stage-2-multi-env-local-design.md`](2026-11-18-stage-2-multi-env-local-design.md),
[`docs/environments.md`](../../environments.md)).

## 1. Intent

The user's team does not use feature flags. This stage is a **playground**: see how flags behave in the situations
that come up in trunk-based development, each one small enough to try on its own:

- a feature is merged **disabled** and enabled only on a preview env;
- the same image behaves differently per env, chosen at deploy time;
- the server enforces a flag, the UI only follows it;
- Web and API can disagree about a flag, and the UI must survive that;
- a flag has a life cycle and is removed once the feature is released.

Success: for every situation the user can deploy two envs (or one env twice), open the pages and see the difference,
with a test pinning each behavior.

## 2. Goals, non-goals, anti-goals

Goals (situations chosen by the user; letters are used across this spec):

| # | Situation | Shown by |
|---|---|---|
| A | Merged disabled, enabled on a preview | All flags `false` in `appsettings.json`; `deploy.ps1 -Flags X` turns X on for one env |
| B | Server-side gate | `GET /products/stats` behind `[FeatureGate]`: 404 when off, 200 when on |
| C | Server-rendered UI | Web shows a "New" badge next to recent products only when `NewBadge` is on |
| D | Client-side JS | The widget asks the API `GET /features` and shows a stats bar only when `ProductStats` is on |
| E | Web and API disagree | Web (server-side) renders a stats panel when its `ProductStats` is on; if the API's flag is off (404), the panel says "not available" and the page still works |
| F | Tests in both states | Every flag has a test with the flag on and one with it off |
| H | Flag life cycle, **once** | Flag `ProductCount` is added, released (default `true`), then removed from code and config; a guard test and the deploy script catch leftovers |

Each situation has **its own flag** where sharing would blur it: `ProductStats` (B, D, E), `NewBadge` (C),
`ProductCount` (H). H is done once, for `ProductCount` only, not for every flag.

Non-goals:

- Feature filters (`Percentage`, `TimeWindow`, targeting) — situation G, declined by the user.
- Changing a flag without a redeploy (runtime flag service) — that is stage FF2.
- Flags from PR labels — stage 6b.
- Flags for the single-env setup `deploy/local/` beyond what `appsettings.json` / environment variables already give.

Anti-goals (failure even if it works):

- A flag used as a security boundary: hiding a button is UI; the server must refuse (B).
- The client receiving all flags: only an explicit allow-list reaches the browser (user's concern, 2026-11-23).
- Demos where several flags interact, so the user cannot tell which flag caused what.
- A deploy whose flags depend on hidden state: without `-Flags`, the flags are the `appsettings.json` defaults of
  the deployed commit (user's decision, 2026-11-23).

## 3. Approaches

**How the JS widget learns the flags (situation D)** — decided by the user: **option 2**.

1. Web renders the client flags into the page (`<meta>` / JSON script tag). No extra request, no flicker; but the
   widget would see the Web's flags while the feature lives in the API, so Web/API drift shows a button the API
   does not serve.
2. **Chosen:** the API serves `GET /features` with the allow-listed client flags. The service that owns the
   feature decides; the widget already talks to the API directly; the Web does not need to know API flags. Cost:
   one more request, made in parallel with loading the products.

Either way the browser can read what it receives (anything the browser needs, it can see); the protection is the
allow-list plus server-side enforcement, not the transport.

**How `-Flags` reaches the containers** — Claude's call: a generated env file per service (section 4.5). Rejected:
listing every flag as a variable in `deploy/env/compose.yml` (the env compose file would change with every new flag
and old branches would break the contract); one custom variable parsed by the app (non-standard configuration).

## 4. Design

### 4.1 Library and flag names

- `Microsoft.FeatureManagement.AspNetCore` (latest stable 4.x at plan time; central package management) in Api and
  Web. Flags read from the standard section `FeatureManagement` (`FeatureManagement__ProductStats=true` as an
  environment variable).
- Flag names are constants in one class shared by Api and Web: `EphemeralEnvs.Contracts.FeatureFlags`
  (`ProductStats`, `NewBadge`, `ProductCount`) plus `All` (every name). No string literals elsewhere.
- Each app declares **every flag it uses** in its own `appsettings.json` under `FeatureManagement`, all `false`
  (situation A). Api: `ProductStats`. Web: `ProductStats`, `NewBadge`, `ProductCount`.

### 4.2 API

- `GET /products/stats` → `ProductStatsDto(int Count, decimal? AveragePrice, DateTimeOffset? NewestCreatedAt)`
  (new DTO in Contracts, new query + handler in Application, read side like the other queries). Guarded by
  `[FeatureGate(FeatureFlags.ProductStats)]`: disabled → **404** (the library's default), so a disabled feature looks
  like a missing endpoint (B).
- `GET /features` → JSON object of the **client allow-list** only, camelCase names: `{ "productStats": true }`.
  The allow-list is a constant in the Api (`ClientFeatureFlags`), today just `ProductStats`. `Cache-Control: no-store`.
  Not behind a flag; CORS as the other endpoints (GET).
- Swagger shows both endpoints (the stats one is listed even when off — acceptable, noted in docs).

### 4.3 Web

- **C — `NewBadge`:** on the server-rendered product list (`Home/Index`), a "New" badge next to products created in
  the last 24 hours (UTC, by `CreatedAt`), only when the Web's `NewBadge` is on. Uses the library's `<feature>` tag
  helper or `IFeatureManager`.
- **E — `ProductStats` (Web side):** when the Web's `ProductStats` is on, `Home/Index` also calls the API
  `GET /products/stats` (new `ProductsApiClient.GetStatsAsync`) and renders a stats panel (count, average price,
  newest). API answers 404 → panel text "Statistics are not available (feature off in the API)."; any other stats
  failure (timeout, 5xx, unreachable) → panel text "Statistics are unavailable right now." The products that loaded
  stay on the page: a stats failure never replaces the list (Claude's call after the builder check: "the page still
  works"). If the products call itself fails, the existing "API is unavailable" behavior applies. When the Web's flag
  is off, the Web does **not** call the stats endpoint.
- **H — `ProductCount`:** when on, the `Home/Index` heading shows the number of products: `Products (5)`.
- **D — widget (`wwwroot/js/widget.js`):** loads `GET /features` in parallel with the products. If `productStats`
  is true, it loads `GET /products/stats` and shows a one-line stats bar above the table. `/features` failing or a
  404 from stats → no bar, no error box (the products still render). The widget never decides from Web state.

### 4.4 Life cycle of `ProductCount` (H, done once)

Three tasks in the plan, in this order, each ending with a stop for the user to commit (CLAUDE.md: Claude never
commits), so the history shows the cycle as three commits:

1. Introduce: flag `false` by default, feature behind it, tests for on and off.
2. Release: default `true` in Web `appsettings.json`; nothing else changes.
3. Retire: remove the flag check (feature always on), the constant, the config entry and the "off" test.

Guards that catch leftovers:

- **Architecture test** (no Docker): for Api and Web, every key under `FeatureManagement` in `appsettings.json` is in
  `FeatureFlags.All`, and every name in `FeatureFlags.All` is declared by at least one app. A retired flag left in
  config, or a constant nobody declares, fails the build's tests.
- **Deploy script:** `-Flags` names are checked against the `FeatureManagement` keys of the deployed code's Api and
  Web `appsettings.json` (section 4.5). After retirement `-Flags ProductCount` fails with "unknown flag".

Walkthrough after each step (in the task report and in `docs/environments.md`): after "introduce", `deploy.ps1`
shows no count and `deploy.ps1 -Flags ProductCount` shows it; after "release", the count shows without `-Flags`, and
`-Flags ProductCount=false` hides it; after "retire", the count always shows and `-Flags ProductCount` is refused.

### 4.5 Deploy

- `deploy.ps1 -Flags <string[]>`, e.g. `-Flags ProductStats,NewBadge` or `-Flags ProductStats=true,Web:NewBadge=false`.
  - `Name` = `Name=true`. `Name=true|false` sets a value (`false` turns off a flag whose default is `true`).
  - No prefix: the flag goes to **every service that declares it** in its `appsettings.json` (`ProductStats` → Api
    and Web, `NewBadge` → Web only). Prefix `Api:` or `Web:` limits it to that service (used to stage situation E).
  - Errors: unknown flag (declared by neither service), a prefixed flag the service does not declare
    (`Api:NewBadge`), unknown prefix, value other than `true`/`false`, the same flag given twice for a service.
    (Per-service check: Claude's call after the builder check.)
  - Without `-Flags`: no overrides; every flag has its `appsettings.json` default from the deployed commit.
- Parsing and validation are pure module functions (Pester-tested), e.g. `ConvertTo-FlagOverrides -Flags -KnownFlags`
  → per-service map.
- Delivery: the module writes `.ee/envs/<env>/api.flags.env` and `web.flags.env` (lines
  `FeatureManagement__<Name>=true|false`; empty file when no overrides) on every deploy, and
  `deploy/env/compose.yml` adds `env_file:` entries pointing at them through `EE_API_FLAGS_FILE` /
  `EE_WEB_FLAGS_FILE`. `.ee/` is already git-ignored.
- **State stays in labels**, as in stage 2: label `ee.flags` holds the normalized overrides
  (`ProductStats=true;Web:NewBadge=false`, empty when none). `db-reset` and other scripts that recreate containers
  regenerate the env files from the label, so the files are a transient artifact, not state. `destroy` removes
  `.ee/envs/<env>/`.
- Compose contract: an older branch's `env/compose.yml` without the `env_file` entries simply ignores the
  overrides (it has no flags). The scripts warn when `-Flags` is given and the deployed compose file has no
  `EE_API_FLAGS_FILE` reference. The platform contract does not change.
- Output: `deploy.ps1` prints a `Flags` line (overrides per service, or "defaults from appsettings.json");
  `list.ps1` gets a `Flags` column from `ee.flags`.

### 4.6 Tests (F)

- Api tests (existing `WebApplicationFactory` + Testcontainers fixture, flags set through configuration
  overrides): stats off → 404; stats on → 200 with correct count/average/newest (empty table → count 0, nulls);
  `/features` → exactly the allow-listed flags with their state, a non-listed flag never appears.
- Web tests (existing `WebFactory` + stub API): `NewBadge` on/off (recent product badged, old one not); `ProductCount`
  on/off; `ProductStats` on with stub stats → panel; on with stub 404 → "not available", page 200 (E); on with stub
  500 or timeout → "unavailable right now" and the products still listed; off → the stub sees no stats call.
- Architecture test from 4.4.
- Pester: `-Flags` parsing and validation cases from 4.5; env file content; `ee.flags` round trip
  (label → overrides → label).
- The widget (D) has no JS test harness in this repo: checked by hand with the steps in the docs.

### 4.7 Docs

- `docs/environments.md`: section "Feature flags" — `-Flags` syntax, defaults, `list` column, and one short
  walkthrough per situation A–E and H (which envs to deploy, which page to open, what to expect).
- `docs/development.md`: adding a flag (constant, `appsettings.json` of each app that uses it, tests on/off, client
  allow-list if the browser needs it), running with a flag from the IDE (`appsettings.Development.json` or an
  environment variable), retiring a flag (H steps).
- `docs/architecture.md`: flag rules (shared constants, declared defaults, server enforces, client allow-list).
- README: one line + link. Parent spec: FF1 row → Done + link.

## 5. Constraints

- .NET 10, central package management, `TreatWarningsAsErrors`, analyzers on: the new code builds without warnings.
- Scripts: PowerShell 7.2+, ASCII only, logic in `deploy/lib/EphemeralEnvs.Deploy.psm1`.
- Images stay env-agnostic: flags come only from environment variables at start-up.
- Small stages: the plan stops after each task for the user to try it.

## 6. Left to the builder

- Exact markup of the badge, panel and stats bar (Bootstrap classes already in use).
- Whether `<feature>` tag helper or `IFeatureManager` in the controller for C/H.
- Rounding of the average price (2 decimals, invariant culture, as elsewhere).
- Task split, as long as `ProductCount`'s three life-cycle steps are separate commits in order (4.4).

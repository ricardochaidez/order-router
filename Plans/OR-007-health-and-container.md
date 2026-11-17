# Plan: OR-007: Containerize the API and add the health endpoint

**Ticket:** [Docs/Tickets/OR-007-health-and-container.md](../Docs/Tickets/OR-007-health-and-container.md)
**Depends on:** OR-004 (done, see [OR-004-database-schema.md](OR-004-database-schema.md)); uses the DI extension from OR-006 (done, see [OR-006-reference-data-read-path.md](OR-006-reference-data-read-path.md))
**References:** [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Health); Technical Requirements FR-17, FR-19, NFR-06, NFR-07, NFR-11, SC-22, SC-23, SC-24, SC-32, SC-33; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-008 (dev key and auth; finishes AC7), OR-025 (full README)

> **Save location:** Phase 0 copies this plan to `Plans/OR-007-health-and-container.md`. Update that file as each phase is done.

## Context

The schema (OR-004), the seeder (OR-005) and the read ports (OR-006) exist, but the API host doesn't use the database yet, and nothing runs it in a container. OR-007 does three things:

1. It wires the database into `Program.cs`. The API **never** migrates or seeds (SC-22, SC-33).
2. It adds an anonymous `GET /health` that only checks that PostgreSQL is reachable. An empty or unmigrated database is still healthy (SC-23, NFR-06, ADR-0002).
3. It adds a multi-stage, non-root Dockerfile and puts the API in `docker-compose.yml`, running in `Development` (NFR-07, FR-17, SC-32).

Out of scope: authentication and the dev-key handler (OR-008), the full README (OR-025), any migration or seed step in the API or in compose (SC-22, SC-33), and reading reference data at startup.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| AC7 (dev key) | **Split.** OR-007 checks that `ASPNETCORE_ENVIRONMENT=Development` in the container. It also adds a dev-only placeholder key to `.env.example`, and compose passes it to the API. The part that says "a request with the dev key passes authentication" is a **follow-up for OR-008**, which owns the dev-key handler. |
| Connection string | **Commit the dev default in `appsettings.Development.json`.** It is the same localhost value as `OrderRouterDbContextFactory.DefaultConnectionString`, which already contains the dev-only password, so no new secret is committed. Compose overrides it with the `ConnectionStrings__OrderRouter` environment variable. In any other environment there is no default: if the setting is missing, startup throws `InvalidOperationException` naming `ConnectionStrings:OrderRouter`, without using `!`. |
| `/health` shape | **Minimal API + `HealthCheckService`.** `app.MapGet("/health", ...)` runs the checks tagged as dependency checks, then returns `200` with `Healthy` or `503` with `Unhealthy` as plain text. It is `.AllowAnonymous()` and is documented in OpenAPI with both status codes. It still uses ASP.NET Core health checks (`AddDbContextCheck<OrderRouterDbContext>`), as ADR-0002 says. |
| Startup | **Fail fast if PostgreSQL is unreachable.** A hosted service runs the **same** dependency health checks when the host starts. If any check is not `Healthy`, it logs the problem and throws, and the process exits. It only opens a connection (`CanConnectAsync`). It never queries tables, so an empty or unmigrated database starts normally (AC6). Compose uses `depends_on: condition: service_healthy` so the API waits for Postgres. |

### Other design choices (from the ticket, ADRs, rules and existing code; no question needed)

| Topic | Choice and reason |
|---|---|
| Health check registration | Add the `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` package (10.0.x) to **Infrastructure**. Add a new extension, `AddOrderRouterDatabaseHealthCheck(this IHealthChecksBuilder)`, in `PersistenceServiceCollectionExtensions`. It registers `AddDbContextCheck<OrderRouterDbContext>(name: "postgresql", tags: [HealthTags.Dependency], customTestQuery: CanConnectAsync)` with a short timeout (5 s). This keeps EF types out of the Api project. The default `AddDbContextCheck` already uses `CanConnectAsync`, which needs no schema (SC-33). |
| Status mapping | A small `internal static` mapper in Api turns `HealthStatus.Healthy` into `200` and anything else (`Degraded`, `Unhealthy`) into `503`. Treating `Degraded` as unhealthy is the fail-safe choice. The body is only the status word. Exception text is never returned (CLAUDE.md, the 500 rule). The response sets `Cache-Control: no-store`, like the built-in health middleware does. |
| Startup check | `DependencyStartupCheck : IHostedService` in `src/OrderRouter.Api/Health/`. It reuses `HealthCheckService` with the dependency-tag predicate, so "reachable" has one definition (DRY). It logs each check's name and status, and never the connection string (NFR-11). |
| Port | The container listens on **HTTP 8080**, the .NET 10 image default for non-root (`ASPNETCORE_HTTP_PORTS=8080`). Compose maps `${API_PORT:-8080}:8080`. The README documents `http://localhost:8080`. TLS is out of scope here (NFR-11 says HTTPS in production, which ingress handles). `UseHttpsRedirection` stays as it is. Without an HTTPS port configured it doesn't redirect, so container HTTP works. If it does redirect, stop and ask. |
| Dockerfile | Lives at the repo root (`build: .`). **Build stage:** `mcr.microsoft.com/dotnet/sdk:10.0.x`. It copies `Directory.Build.props`, `.editorconfig` and the four `src` csproj files, restores, then copies `src/` and runs `dotnet publish src/OrderRouter.Api -c Release -o /app /p:UseAppHost=false`. **Final stage:** `mcr.microsoft.com/dotnet/aspnet:10.0.x` (the ASP.NET Core runtime image; the plain `runtime` image can't host ASP.NET). It sets `USER $APP_UID` (the built-in non-root `app` user), `EXPOSE 8080`, and `ENTRYPOINT ["dotnet","OrderRouter.Api.dll"]`. Image tags are pinned to an exact patch, checked at implementation time, the same way compose pins `postgres:18.1`. No CSVs or `Docs/` are copied (FR-17). |
| `.dockerignore` | Excludes `**/bin`, `**/obj`, `tests/`, `tools/`, `Docs/`, `Plans/`, `TestResults/`, `.git`, `.env`, `.claude/`. |
| Compose `api` service | `build: .`, `depends_on: postgres: condition: service_healthy`, `ports: "${API_PORT:-8080}:8080"`. Environment: `ASPNETCORE_ENVIRONMENT: Development`, `ConnectionStrings__OrderRouter: Host=postgres;Port=5432;Database=orderrouter;Username=orderrouter;Password=${POSTGRES_PASSWORD:-orderrouter-dev-only}`, `Authentication__DevApiKey: ${DEV_API_KEY:-}` (OR-008 binds or renames this). There is no migrate or seed command (SC-22). The header comment says this file is never used in production. |
| `.env.example` / `.gitignore` | `.env.example` holds `POSTGRES_PASSWORD`, `POSTGRES_PORT`, `API_PORT` and `DEV_API_KEY=dev-only-change-me`, with a header marking every value as dev-only (SC-32). `.gitignore` gets an explicit `.env` line. The existing `*.env` already matches it, but OR-008 AC6 asks for `.env` to be listed. |
| README | Add a minimal **Run locally with Docker** section: copy `.env.example` to `.env`, run `docker compose up`, the API is at `http://localhost:8080`, `GET /health`, then run the integration tests to migrate and seed. That last step is required (SC-24, SC-33). OR-025 writes the full README later. |
| Layers / DDD | This is infrastructure and hosting only: no domain concepts, aggregates, events or money. Domain and Application don't change. `LayerDependencyTests` must stay green. |

### Target structure

```
Dockerfile                                       multi-stage, non-root, aspnet:10.0 runtime
.dockerignore
.env.example                                     dev-only placeholders (SC-32)
.gitignore                                       + explicit .env
docker-compose.yml                               + api service (Development, depends_on healthy)
README.md                                        + Run locally with Docker (port 8080)
src/OrderRouter.Api/
  Program.cs                                     DB wiring, health checks, /health, startup check
  appsettings.Development.json                   + ConnectionStrings:OrderRouter (dev-only)
  Health/HealthEndpoint.cs                       MapGet /health + OpenAPI metadata
  Health/HealthStatusMapper.cs                   Healthy→200, else 503
  Health/DependencyStartupCheck.cs               fail-fast hosted service
  Health/HealthTags.cs                           "dependency" tag (or in Infrastructure; see Phase 1)
src/OrderRouter.Infrastructure/
  OrderRouter.Infrastructure.csproj              + HealthChecks.EntityFrameworkCore
  Persistence/PersistenceServiceCollectionExtensions.cs  + AddOrderRouterDatabaseHealthCheck
tests/OrderRouter.Tests/
  Api/Health/HealthStatusMapperTests.cs
  Api/Health/DependencyStartupCheckTests.cs
  Infrastructure/Persistence/PersistenceRegistrationTests.cs   + health check registered with tag
  Architecture/ContainerConfigurationTests.cs    Dockerfile / compose / .env guards
tests/OrderRouter.Api.IntegrationTests/
  ApiFactory.cs (or helper)                      WebApplicationFactory with a connection string
  Health/HealthEndpointTests.cs                  AC3, AC4, AC5, OpenAPI
  Health/StartupTests.cs                         AC6, fail-fast
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer change (Api, Infrastructure, hosting), so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review in Phase 1;
   - §3 is this plan, and §4 implementation runs through Phases 1–3;
   - §5 post-implementation review in Phase 4.

   Key points:
   - **C#:** C# 14 / .NET 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, the final `return` on its own line, and no null-forgiving `!` on configuration. Private constants are camelCase per `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members. Comments explain *why* and cite the requirement (e.g. `// SC-23`, `// SC-33`). Comment the Dockerfile and compose lines whose reason isn't obvious.
   - **REST/API (CLAUDE.md):** `/health` is the only anonymous endpoint, is documented in OpenAPI (200, 503), and leaks no internals.
   - **Never migrate or seed** from the API or compose. `SeedingBoundaryTests` must stay green.
   - **Tests:** follow the nearby style: `MethodName_Condition_ExpectedResult` names, no `Arrange`/`Act`/`Assert` comments, `TestContext.Current.CancellationToken`. Use `ThrowawayDatabase` for empty or unmigrated cases and the shared DB for the reachable case. Cover edge cases and error paths. Target ≥ 85% coverage of the new code.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d postgres`) and run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including the existing architecture, domain, normalizer, schema, seeder and read-path tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-007-health-and-container.md`;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or library behaviour is unclear or contradicts another, stop and ask the engineer. Don't guess. Examples: `AddDbContextCheck` fails on an unmigrated database, `UseHttpsRedirection` redirects container HTTP, or the aspnet image has no non-root `APP_UID`.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-007-health-and-container`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-007-health-and-container.md` with this plan.
- [x] Confirm the four decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Wire the database and add `GET /health` (AC3, AC4, AC5) ✅ Done

- [x] Write the `ddd-implementation` §1/§2 notes under **Phase 1 notes**:
  - no domain concepts change; this is hosting and infrastructure only;
  - the layer rule (Api → Infrastructure through the DI extension; no EF types in Api);
  - fail safe: an unknown or degraded status counts as unhealthy;
  - no events, money, PHI or authorization here (auth is OR-008).
- [x] Add `ConnectionStrings:OrderRouter` to `appsettings.Development.json` (dev-only comment in the plan and README; JSON has no comments).
- [x] `Program.cs`:
  - read the connection string, and throw `InvalidOperationException` if it's missing;
  - call `AddOrderRouterDatabase(...)`;
  - `AddHealthChecks().AddOrderRouterDatabaseHealthCheck()`;
  - map `/health`.
- [x] Infrastructure: add the package, `AddOrderRouterDatabaseHealthCheck`, and the dependency tag constant. Put the tag in Infrastructure so both the registration and the Api endpoint can reference it.
- [x] Api: add `HealthEndpoint` (MapGet, `AllowAnonymous`, `Produces<string>(200, "text/plain")`, `Produces<string>(503, "text/plain")`, a summary and description) and `HealthStatusMapper`.
- [x] Unit tests:
  - `HealthStatusMapperTests`: Healthy → 200; Degraded and Unhealthy → 503; the body is the status name;
  - `PersistenceRegistrationTests`: the health check is registered under `postgresql` with the dependency tag.
- [x] Integration tests, `HealthEndpointTests`. Use a `WebApplicationFactory<Program>` in Development, with `UseSetting("ConnectionStrings:OrderRouter", ...)` for the throwaway DB:
  - **AC3:** shared DB, no credentials → `200`, body `Healthy`, `Cache-Control: no-store`;
  - **AC5:** an empty, unmigrated `ThrowawayDatabase` → `200 Healthy`;
  - **AC4:** start on a `ThrowawayDatabase`, check for `200`, dispose it (`DROP … WITH (FORCE)` + `ClearAllPools`), then expect `503`, body `Unhealthy`, and no exception text;
  - **OpenAPI:** `/openapi/v1.json` contains `/health` with `200` and `503`.
- [x] The existing `ApiHostTests` still pass. They now use the Development connection string, or `ConnectionStrings__OrderRouter` when it is set.

**Verify:** working rule 2. `SeedingBoundaryTests` and `LayerDependencyTests` stay green.

**Result:** 387 tests pass, format clean. Deviations: the health check uses a custom test query (`CanConnectAsync` with a 5 s timeout) because `AddDbContextCheck` has no timeout option; `Program.cs` is unchanged apart from wiring (`Program` stays internal, `WebApplicationFactory` already reaches it).

### Phase 1 notes
- **Domain (§1):** no domain concepts change. This is hosting and infrastructure only: no aggregates, events, money, PHI or authorization (auth is OR-008).
- **Architecture (§2):** Api reaches Infrastructure only through the DI extensions (`AddOrderRouterDatabase`, `AddOrderRouterDatabaseHealthCheck`); no EF types in Api. `HealthTags` lives in Infrastructure so the registration, the endpoint and the startup check share one tag.
- **Fail safe:** `Degraded`, `Unhealthy` and any unknown status map to `503`.

---

## Phase 2: Fail fast at startup, never read data (AC6) ✅ Done

- [x] Add `DependencyStartupCheck : IHostedService`. In `StartAsync` it runs `HealthCheckService.CheckHealthAsync(dependency predicate)`:
  - if the result isn't `Healthy`, log each failing check's name and status at `Error`, then throw `InvalidOperationException("Dependency check failed at startup: postgresql is Unhealthy.")`;
  - never log the connection string;
  - `StopAsync` does nothing.
- [x] Register it in `Program.cs` with `AddHostedService`.
- [x] Unit tests, `DependencyStartupCheckTests` (stub `HealthCheckService` subclass):
  - Healthy → completes;
  - Unhealthy or Degraded → throws, and the message names the check;
  - the message and logs don't contain the stub's exception text with secrets;
  - only the dependency-tagged checks run (predicate).
- [x] Integration tests, `StartupTests`:
  - **AC6:** start the host on an empty, unmigrated `ThrowawayDatabase`, using `ConfigureTestServices` → `ConfigureDbContext<OrderRouterDbContext>` to add the existing `CapturingCommandInterceptor`. Startup succeeds, **no SQL command** reaches the server, and `information_schema.tables` in `public` stays empty (no migration, SC-33);
  - **fail fast:** with `Host=unreachable.invalid;Timeout=1`, `CreateClient()` throws, and the exception chain contains the startup-check message.

**Verify:** working rule 2.

**Result:** 394 tests pass. Deviation: EF's `CanConnectAsync` sends a bare `SELECT 1` on Npgsql, so "no SQL command" became "only `SELECT 1`, no table read, no table created". That still satisfies AC6/SC-33.

---

## Phase 3: Container, compose, `.env.example`, README (AC1, AC2, AC7-split) ✅ Done

- [x] Add `Dockerfile` and `.dockerignore` as described in the design table. Pin the SDK and aspnet tags to the current 10.0 patch.
- [x] Add the `api` service to `docker-compose.yml`, and update its header comment (OR-007 is now done; no migrate or seed).
- [x] Add `.env.example` and the explicit `.env` line in `.gitignore`.
- [x] Add the README **Run locally with Docker** section with port 8080, `/health`, and the required "run the integration tests" step.
- [x] Unit tests, `ContainerConfigurationTests` (file guards under `TestRepository.Root`):
  - the Dockerfile has at least two `FROM` lines;
  - the final `FROM` uses `dotnet/aspnet:10.0`;
  - a `USER` line comes after the last `FROM` and isn't `root` or `0`;
  - the Dockerfile has no `COPY` of `Docs`;
  - compose's `api` service sets `ASPNETCORE_ENVIRONMENT: Development` and has no `migrate`, `ef` or `seed` command;
  - `.env.example` exists and every value is marked dev-only.
- [x] Manual checks (record the output under **Result**):
  - `docker compose down -v && cp .env.example .env && docker compose up -d --build --wait` → `docker compose ps` shows `postgres` healthy and `api` running (**AC1**);
  - `curl -i http://localhost:8080/health` → `200 Healthy` on the fresh, unmigrated DB (**AC3, AC5**), and `\dt` shows no tables (**AC6**, SC-33);
  - `docker compose exec api id -u` → not `0`, and `docker image inspect` shows `Config.User` set (**AC2**);
  - `docker compose exec api printenv ASPNETCORE_ENVIRONMENT` → `Development`, and `DEV_API_KEY` reaches the container (**AC7, our part**);
  - `docker compose stop postgres` → `curl` gives `503 Unhealthy` (**AC4**); `docker compose start postgres` → `200` again;
  - with `postgres` stopped, `docker compose restart api` → the API exits with the startup-check error (fail fast);
  - `git check-ignore .env` → ignored;
  - with the stack running, `dotnet test` from the host is still green (it seeds the API's DB, SC-24).

**Verify:** working rule 2 plus the manual checks above.

**Result:** 401 tests pass, format clean. Images pinned: `sdk:10.0.404`, `aspnet:10.0.15`. Manual checks on the real stack (all passed):
- `docker compose ps`: postgres healthy, api running (AC1). `curl -i /health` → `200`, `Cache-Control: no-store`, body `Healthy` (AC3).
- `id -u` → `1654`; image `Config.User` → `1654` (AC2). `ASPNETCORE_ENVIRONMENT=Development` and `Authentication__DevApiKey=dev-only-change-me` reach the container (AC7, our part).
- `stop postgres` → `503 Unhealthy`; `start postgres` → `200` again (AC4). With postgres stopped, `restart api` logged `Dependency check failed at startup: postgresql is Unhealthy.` and `Hosting failed to start`; no connection string or password in the logs.
- `git check-ignore .env` → ignored. `dotnet test` from the host against the running stack is green.
- **Not done:** `docker compose down -v` on a fresh volume (the permission prompt denied deleting the dev database volume). The fresh/unmigrated case is covered by `HealthEndpointTests` and `StartupTests` on throwaway databases. The dev volume already had tables from earlier test runs, so the manual `\dt` check was not run.

---

## Phase 4: Post-implementation review and follow-ups ✅ Done

- [x] `ddd-implementation` §5 review: list what **applies and is met** (layering, fail-safe, REST/OpenAPI, no migrate or seed, non-root, no secrets in logs), what is **N/A** and why (domain events, money, authorization: OR-008), and what was **not verified**. Be honest, e.g. coverage %.
- [x] Record follow-ups (don't do them here):
  - **OR-008:** bind the dev key from `Authentication__DevApiKey` (or rename it in compose and `.env.example` together), keep `/health` anonymous (OR-008 AC7), and check that **OR-007 AC7**'s "a request with the dev key passes authentication" holds via `docker compose up`;
  - **OR-025:** expand the README (build, test, status codes, DEV-07/DEV-14).
- [x] Final run: `docker compose up -d --build`, then `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes`, all green.

---

**Result (§5 review):**
- **Applies and is met:** layering (`LayerDependencyTests` green; no EF types in Api); fail-safe status mapping; REST/OpenAPI for `/health` (200, 503, anonymous, no internals); the API never migrates or seeds (`SeedingBoundaryTests` green); non-root container; no secrets in logs or responses (tested).
- **N/A:** domain events, money, PHI and authorization (OR-008 owns auth).
- **Not verified:** code coverage % was not measured; the fresh-volume `down -v` manual run (see Phase 3); HTTPS/TLS is out of scope; `UseHttpsRedirection` logs "Failed to determine the https port" in tests but does not redirect HTTP.
- **Follow-ups (not done here):** OR-008 binds `Authentication__DevApiKey`, keeps `/health` anonymous and checks that a request with the dev key passes authentication via `docker compose up`; OR-025 expands the README.
- Final run: `docker compose up -d --build`, `dotnet build` (0 errors, 0 warnings), `dotnet test` (401 pass), `dotnet format --verify-no-changes` (clean).

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-007-health-and-container.md`, `Dockerfile`, `.dockerignore`, `.env.example`, `src/OrderRouter.Api/Health/*` |
| Modified | `src/OrderRouter.Api/Program.cs`, `src/OrderRouter.Api/appsettings.Development.json`, `docker-compose.yml`, `.gitignore`, `README.md`, `src/OrderRouter.Infrastructure/OrderRouter.Infrastructure.csproj`, `src/OrderRouter.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` |
| Reused | `AddOrderRouterDatabase` (OR-004/OR-006); `OrderRouterDbContextFactory.DefaultConnectionString` / `ConnectionStringVariable`; `ThrowawayDatabase`; `CapturingCommandInterceptor`; `ReferenceDataFixture` connection resolution; the `SeedingBoundaryTests` / `TestRepository.Root` source-guard pattern; `ApiHostTests` factory pattern |
| Unchanged | `src/OrderRouter.Domain/**`, `src/OrderRouter.Application/**`, migrations, the seeder, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Stack starts | Manual `docker compose up` + README port | 3 |
| AC2: Non-root | `ContainerConfigurationTests` (`USER`) + manual `id -u` | 3 |
| AC3: Healthy | `HealthEndpointTests` (anonymous, 200) + manual curl | 1, 3 |
| AC4: Database down | `HealthEndpointTests` (dropped DB → 503) + manual `stop postgres` | 1, 3 |
| AC5: Empty data healthy | `HealthEndpointTests` (unmigrated throwaway DB → 200) + manual curl on a fresh volume | 1, 3 |
| AC6: Startup doesn't check data | `StartupTests` (no SQL commands, no tables created) + `SeedingBoundaryTests` | 2 |
| AC7: Compose runs in Development | Manual `printenv` + `ContainerConfigurationTests`; dev-key authentication → **OR-008 follow-up** | 3, 4 |

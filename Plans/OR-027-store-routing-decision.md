# Plan: OR-027: Store each routing decision and return its id

**Ticket:** [Docs/Tickets/OR-027-store-routing-decision.md](../Docs/Tickets/OR-027-store-routing-decision.md)
**Depends on:** OR-015 (done: `RouteOrderUseCase`, `RouteResponseMapper`, `RoutingDecision`), OR-026 (tables; plan in `Plans/OR-026-routing-order-tables.md`, **must be done first**).
**References:** [ADR-0007](../Docs/ADRs/ADR-0007-routing-decision-persistence.md); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md) (caller hash); Technical Requirements FR-02, FR-20, §9.2, §9.4, SC-28, SC-29, SC-30, NFR-11; `CLAUDE.md` (REST API conventions); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-021 (logs after the save, with `id` and `caller_hash`), OR-028 (reads by `id` and `caller_hash`), OR-022, OR-023, OR-025.

> **Save location:** this file is `Plans/OR-027-store-routing-decision.md`. Update it as each phase is done.

## Context

Today `POST /api/v1/route` routes and returns 200 with nothing stored. This ticket stores every valid outcome (routed, partially routed, not routed) in one transaction with a new UUIDv7 `id`, then returns **201 Created** with `id`, `order_id` and `Location: /api/v1/route/{id}`. A retry creates a new record. The caller is stored only as an HMAC-SHA256 hash from a new `ICallerHasher`. If saving fails the response is 500 with no `id`.

Existing seams to build on:
- `RouteOrderUseCase.RouteAsync` already holds the command, the `RoutingDecision` and the mapper call. The save goes between routing and the response.
- `RouteResponse.Id` already exists and is omitted when null (comment: "Omitted until OR-027").
- `RouteEndpoints.HandleAsync` returns `Ok` with a comment "200 until OR-027 switches this to 201".
- `CallerIdentity.Resolve(user)` gives the raw caller ID (nullable); it is never logged (NFR-11).
- OR-026 provides the row types and tables.

Out of scope: `GET` (OR-028), the decision log (OR-021), `diagnostics` (OR-022), retention (OQ-27).

### Decisions made with the engineer
- **No caller ID:** when the token has no caller claim, store `caller_hash = NULL`. The order is stored and returned with 201, but OR-028 can never match it, so nobody can read it back (fail safe). `ICallerHasher` is only called for a non-null caller.

### Design choices (from the ticket, ADR-0007 and the code)

| Topic | Choice and reason |
|---|---|
| Port | `IRoutingOrderStore` (Application, async): `Task SaveAsync(StoredRoutingOrder order, CancellationToken)`. The Infrastructure EF implementation inserts order, items and warnings with one `SaveChangesAsync` (a single transaction), so a failure leaves no partial rows (AC7). |
| What is stored | `StoredRoutingOrder` (Application record): `Id`, `OrderId`, `CallerHash?`, `CustomerZip`, `MailOrder`, the decision (status, routed lines, unfulfilled lines), the response `warnings`, experiment arms and reasons, `ConfigVersion`, `SearchMode?`, `CreatedAt`, and the **requested lines in request order**. Built from the command, `RoutingDecision` and the mapped response so the stored data equals what is returned. |
| Item rows | One per requested line, 1-based `line_number`. Routed line: `supplier_id`, snapshot `supplier_name`, `fulfillment_mode`, `category`. Unfulfilled line: `reason_code`, `message` (same text the mapper uses). A line held back by all-or-nothing has neither, and `status` explains it. Routed and unfulfilled lines are both in request order, so matching them to requested lines is a single ordered walk, which also handles duplicate product codes (DEV-10). |
| `id` and `created_at` | `Guid.CreateVersion7()` and `TimeProvider.GetUtcNow()` in the use case (`TimeProvider` is injected so tests can fix the time). |
| `ICallerHasher` | Application port `string Hash(string callerId)`; Infrastructure `HmacCallerHasher` (HMAC-SHA256, secret from `CallerHashing:Secret`, lowercase hex). Deterministic, so OR-021 and OR-028 reuse it. Library use (`System.Security.Cryptography.HMACSHA256`) commented. |
| Secret handling | `CallerHashingOptions` bound from `CallerHashing`, validated with `ValidateOnStart`: missing or blank secret stops the host (AC10). `.env.example` gets a dev-only placeholder (`CALLER_HASHING_SECRET`) beside `DEV_API_KEY`; `docker-compose.yml` passes it as `CallerHashing__Secret`; README/docs note user-secrets for local runs. No real secret is committed. Test hosts (`ApiFactory`, `AuthApiFactory`, `RouteApiFactory`) supply a test secret. |
| Response | `RouteResponse.Id` filled from the stored `id`. Endpoint returns `TypedResults.Created($"/api/v1/route/{id}", response)` for both v1 and the alias (AC5): `Location` always points to the v1 path. |
| Failure | A store exception is not caught in the use case. The existing global handler returns a 500 ProblemDetails without internals; no `id` is in the body (AC7). A 400 or 401 never reaches the use case, so nothing is stored (AC6). |
| OpenAPI (AC9) | Document 201 with `Location`, 400, 401 and 500 on both paths; examples for routed, partially routed and not routed in `RouteOpenApiTransformer`. |
| Existing tests | OR-015 and OR-019 assertions of 200 become 201 (ticket says so). Find them with a search for `HttpStatusCode.OK` in `tests/OrderRouter.IntegrationTests/Routing`. |
| Layering | Ports in Application; EF store and hasher in Infrastructure; endpoint change in Api. `LayerDependencyTests` stay green. Scoped store (same lifetime as the DbContext); hasher is a singleton. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code. Run `ddd-implementation` §1 and §2 at the start of Phase 1, and §5 in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`.
   - **REST:** `POST` is not idempotent: no de-duplication on `order_id`; 201 plus `Location`; Problem Details for 500; IDs server-generated UUIDv7; OpenAPI documents every status (CLAUDE.md).
   - **Docs and comments:** XML docs on public types; comments explain *why* and cite the requirement or ADR (`// FR-20`, `// SC-29`).
   - **Compliance:** raw caller IDs are never stored or logged, only the hash (NFR-11). Never return an `id` that can't be read back.
   - **Tests:** xUnit v3, copy nearby test naming, no Arrange/Act/Assert comments. Never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Start PostgreSQL first (`docker compose up -d postgres`), then from the repo root:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing tests. Record each existing test changed (expected: 200 → 201 assertions and test hosts that now need the secret).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading from `⬜ Not started` to `✅ Done` in this file, tick its checklist and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - OR-026 not done or its tables differ from ADR-0007;
   - an existing test or fixture that needs a different secret mechanism;
   - the ordered walk can't match routed or unfulfilled lines to requested lines;
   - `LayerDependencyTests` objecting to the new types;
   - the global exception handler leaking details or returning something other than a 500 ProblemDetails.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-027-store-routing-decision`).

---

## Phase 0: Check prerequisites ✅ Done

- [x] Confirm OR-026 is done (tables and migration present, its plan marked done). If not, stop and ask.
- [x] Baseline: `dotnet build`, `dotnet test` and format green before any change.

**Verify:** all green. No code changes.

**Result:** OR-026 is done (tables, migration, tests). Baseline was green: 1250 tests, build clean, format clean except the known `Program.cs` import issue.

---

## Phase 1: Caller hasher and secret ✅ Done

- [x] `ddd-implementation` §1 and §2 (concepts: pseudonymized caller; no aggregate).
- [x] Add `ICallerHasher`, `HmacCallerHasher`, `CallerHashingOptions` and its validator; register with `ValidateOnStart`.
- [x] `.env.example` placeholder, `docker-compose.yml` variable, `appsettings*.json` has **no** secret.
- [x] Update the test hosts to supply a test secret.
- [x] Tests:
  - same caller, same hash; different callers, different hashes; different secret, different hash;
  - known-answer HMAC value;
  - the hash does not contain the raw caller ID (AC8, unit level);
  - host fails at startup when the secret is missing or blank;
  - AC10: only the placeholder is in `.env.example`; `appsettings*.json` has no secret.

**Verify:** build, test, format.

**Result:** 1270 tests pass. Added `ICallerHasher` (Application), `HmacCallerHasher`, `CallerHashingOptions` and its validator (Infrastructure, `CallerHashing/`), registered with `ValidateOnStart`. `.env.example` has the dev-only `CALLER_HASHING_SECRET`; `docker-compose.yml` passes it as `CallerHashing__Secret` with no default. Existing tests changed: `ApiFactory`, `AuthApiFactory`, `AuthenticationTests` and `ApiHostTests` now set a test secret (`TestCallerHashing.Secret`) because the host refuses to start without one. New: `CallerHashingTests` (unit, known-answer HMAC from Python) and `CallerHashingHostTests` (blank secret fails startup, no secret in appsettings, placeholder only in `.env.example`). Note: local `dotnet run` now needs user-secrets or the env var; the running docker api container needs `.env` to include `CALLER_HASHING_SECRET`.

---

## Phase 2: Store port and EF implementation ✅ Done

- [x] Add `IRoutingOrderStore`, `StoredRoutingOrder` and the mapping from command + decision + response (ordered walk, see Design choices).
- [x] Add the EF `RoutingOrderStore` in Infrastructure (one `SaveChangesAsync`, one transaction); register it scoped.
- [x] Integration tests against the migrated database (`ThrowawayDatabase`, seeded where needed):
  - routed order: one order row and one item row per requested line, with `supplier_id`, name and `fulfillment_mode` (AC1);
  - not-routed order with an unknown product: unknown line has `UNKNOWN_PRODUCT` and its message (AC2);
  - experiment arms, `config_version`, `search_mode`, `created_at` stored (AC3);
  - warnings stored when present;
  - duplicate product codes and held-back lines map to the right `line_number`;
  - a failing insert leaves no partial rows (AC7, store level);
  - caller hash stored, raw caller ID absent from every column (AC8); null caller stores `NULL`.

**Verify:** build, test, format.

**Result:** 1285 tests pass. Added `IRoutingOrderStore`, `StoredRoutingOrder`/`StoredOrderItem` and `StoredRoutingOrderFactory` (Application, `Routing/Storage`) and `EfRoutingOrderStore` (Infrastructure, one `SaveChangesAsync` = one transaction; registered scoped). Items are matched to requested lines with one ordered walk (duplicates and held-back lines covered); the unfulfilled message comes from the mapped response so stored text equals returned text. Tests: `StoredRoutingOrderFactoryTests` (unit) and `RoutingOrderStoreTests` (migrated throwaway database, including rollback of a bad second row). No existing test changed.

---

## Phase 3: Use case, 201 and Location ✅ Done

- [x] `RouteOrderUseCase`: generate `id` and `created_at`, hash the caller, save, then return the response with `Id`. Inject `TimeProvider`.
- [x] `RouteEndpoints`: return 201 with `Location`; same handler for v1 and the alias.
- [x] Change the OR-015 and OR-019 200 assertions to 201 (list each in the Result).
- [x] Integration tests through the real host:
  - AC1/AC2/AC3: 201, UUID `id`, `order_id`, `Location: /api/v1/route/{id}`, rows in the database;
  - AC4: same body twice → two different `id`s and two stored orders;
  - AC5: `/api/route` also stores and returns 201, `Location` is the v1 path;
  - AC6: 400 and 401 store nothing;
  - AC7: a simulated insert failure → 500 ProblemDetails, no `id`, no rows;
  - AC8: caller `user-123` appears nowhere in the order tables.
- [x] Use-case unit tests with a fake store and fixed `TimeProvider`: store is called once, after routing, with the response's `id`.

**Verify:** build, test, format.

**Result:** 1300 tests pass (two runs). `RouteOrderUseCase` now takes `ICallerHasher`, `IRoutingOrderStore` and `TimeProvider` (registered with `TryAddSingleton`), saves before answering and returns the response with the stored `Id`. `RouteEndpoints` returns `Created` with `Location: /api/v1/route/{id}` on both paths and throws (500) if the use case returned no id. Existing tests changed: 200 → 201 in `PartialRoutingApiTests`, `RouteOrderEndToEndTests` (and its `id` assertion), `RouteValidationTests` (one test renamed Returns201), `ExperimentAssignmentHostTests`, `RouteAliasTests` (produced codes 201/400/500), `RouteOpenApiTests` (key 201); `RecordingRouteUseCase` now returns a fixed stored id; `RouteOrderUseCaseTests` got the three new dependencies. New: use-case unit tests (stores once with the returned id, save failure throws, retry gives new id, hash only, null caller) and `RouteStorageApiTests` (AC1–AC8 through the real host and the seeded database, including a real database rejection for AC7).

---

## Phase 4: OpenAPI, docs and post-implementation review ✅ Done

- [x] AC9: `v1` OpenAPI documents 201 with `Location`, 400, 401 and 500 for `POST /api/v1/route` and its alias, with routed, partially routed and not-routed examples; test in `RouteOpenApiTests`.
- [x] Update `RouteResponse.Id` and endpoint comments; the `.http` file shows the 201.
- [x] ADR-0007 short note (OR-027): nullable `caller_hash`, one transaction, secret handling. ADR-0004 if the 201 detail fits.
- [x] Run the `ddd-implementation` §5 review and report honestly. Expected met: transaction boundary, no raw caller, save before respond, fail safe on no caller. N/A with reasons: aggregates, money, domain events. Not verified: `GET` (OR-028), decision log (OR-021), p95 latency with the insert (NFR-03, load test), coverage % if not measured.
- [x] Record follow-ups (don't do them here): OR-028, OR-021, OQ-27 retention, OQ-28 duplicates.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

**Verify:** all three green.

**Result:** 1302 tests pass; build clean; format clean except the known `Program.cs` import issue. OpenAPI: both paths document 201 (with a `Location` header and routed, partially routed and not routed examples, all with an `id`), 400, 401 and 500; new `RouteOpenApiTests` theory covers both paths. `.http` comment updated. README has the secret setup note. ADR-0007 note added (ADR-0004 already described 201, so no change).

**§5 review (`ddd-implementation`):**
- **Met:** ports in Application and adapters in Infrastructure (DIP); the use case holds no routing rule; one transaction and save-before-respond; no raw caller ID stored (hash only, null for no caller, fail safe); POST stays non-idempotent (new id every time, no de-duplication); the endpoint cannot send a `Location` without an id; secret required at startup and never committed.
- **N/A, with reasons:** aggregates and domain events (the stored order is a persistence record of an already-made decision, not a new domain concept); money; authorization of reads (OR-028).
- **Not verified:** p95 < 50 ms with the extra insert (NFR-03, needs the load test); `GET` read-back (OR-028); coverage % (not measured); the running Docker stack was not restarted, so its `.env` needs `CALLER_HASHING_SECRET` before `docker compose up`.
- **Follow-ups (not done here):** OR-028 (GET), OR-021 (decision log after the save), OQ-27 retention, OQ-28 duplicates.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-027-store-routing-decision.md`; `ICallerHasher`, `IRoutingOrderStore`, `StoredRoutingOrder` (Application); `HmacCallerHasher`, `CallerHashingOptions`, `RoutingOrderStore` (Infrastructure); unit and integration tests |
| Modified | `RouteOrderUseCase.cs`; `RouteEndpoints.cs`; `RouteOpenApiTransformer.cs`; `Program.cs` (DI, options); `.env.example`; `docker-compose.yml`; test host factories; OR-015/OR-019 tests (200 → 201); ADR-0007 (note) |
| Reused | OR-026 rows and `DbSet`s; `RoutingDecision`; `RouteResponseMapper`; `CallerIdentity.Resolve`; `ThrowawayDatabase`; `RouteApiFactory` |
| Unchanged | Domain routing rules, reference-data tables, seeder, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: routed order stored | store and host tests | 2, 3 |
| AC2: not-routed order stored | store and host tests | 2, 3 |
| AC3: experiment data stored | store and host tests | 2, 3 |
| AC4: retry creates a new record | host test | 3 |
| AC5: alias also stores | host test | 3 |
| AC6: rejected requests not stored | host tests (400, 401) | 3 |
| AC7: save failure → 500, no rows | store and host tests | 2, 3 |
| AC8: caller stored as a hash | hasher, store and host tests | 1, 2, 3 |
| AC9: OpenAPI | `RouteOpenApiTests` | 4 |
| AC10: secret not committed | repo and startup tests | 1 |

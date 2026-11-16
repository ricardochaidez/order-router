# Plan: OR-022: Add the optional diagnostics field

**Ticket:** [Docs/Tickets/OR-022-diagnostics-field.md](../Docs/Tickets/OR-022-diagnostics-field.md)
**Depends on:** OR-016 (done: `IExperimentAssigner`, arms and reasons in the decision) and OR-028 (done: `GET /api/v1/route/{id}`, `StoredOrderResponse`, `StoredOrderResponseMapper`; plan in `Plans/OR-028-get-stored-order.md`). OR-027 already stores `experiment_arms` (jsonb), `search_mode` and `config_version`.
**References:** [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md) (envelope, `diagnostics` rule); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md); [ADR-0007](../Docs/ADRs/ADR-0007-routing-decision-persistence.md); Technical Requirements AB-05, OQ-08, §9.5; `CLAUDE.md` (REST API conventions); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-023 (acceptance tests), OR-025 (README and docs).

> **Save location:** this file is `Plans/OR-022-diagnostics-field.md`. Update it as each phase is done.

## Context

The v1 response has no way to see which experiment arm an order got or whether the plan search was exact or greedy. Both are stored (OR-027) and logged (AB-04), but not returned. This ticket adds an optional `diagnostics` object to the response of `POST /api/v1/route`, its alias `POST /api/route`, and `GET /api/v1/route/{id}`. It is shown **only** when `Routing:ExposeDiagnostics = true`. The default is `false`, and clients must not depend on it (AB-05, OQ-08). It is an additive change within v1 (ADR-0004).

Existing seams to build on:
- `RoutingOptions` (section `Routing`, today only `NationwideZipRange`) and `RoutingOptionsValidator`; the section is already bound in `RoutingServiceCollectionExtensions`.
- `StoredRoutingOrderFactory` writes `ExperimentArmsJson` (one entry per experiment, `{ "arm": ..., "reason": ... }`, snake_case keys) and `SearchMode` (`exact`, `greedy` or `null`). `StoredRoutingOrder` is what `GET` reads back.
- `RouteOrderUseCase` builds the `StoredRoutingOrder` before it answers; `GetStoredOrderUseCase` and `StoredOrderResponseMapper` rebuild the GET body from the same record.
- `RouteResponse` and `StoredOrderResponse` are the envelopes (optional fields use `JsonIgnore(WhenWritingNull)`). JSON is snake_case.
- `RouteApiFactory` boots the API for host tests and can override configuration.

Out of scope: changing what is stored, the decision log, any use of `diagnostics` for routing decisions, the 400 body (no order exists, so no arms), reload of the flag without a restart.

### Decisions already made by the engineer

| Topic | Decision |
|---|---|
| `experiment_arms` shape | Arm **and** reason per experiment, keyed by the snake_case experiment name, exactly as stored: `{ "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" }, ... }`. |
| `search_mode` when no search ran | The field is **omitted** (never `null`). `experiment_arms` is still shown. |

### Design choices (defaults from the ticket, ADR-0004 and the code)

Change one before Phase 1 if you disagree.

| Topic | Choice and reason |
|---|---|
| Flag | `RoutingOptions.ExposeDiagnostics` (`bool`, default `false`), read from `Routing:ExposeDiagnostics`. Read through `IOptions<RoutingOptions>`, so a change needs a restart (same as `NationwideZipRange`). No validator rule is needed for a `bool`. |
| One source for POST and GET | A static `DiagnosticsMapper` (Application) builds `Diagnostics` from a `StoredRoutingOrder` (`ExperimentArmsJson`, `SearchMode`). POST maps the record it just stored; GET maps the record it read. They can't drift, and AC3 ("stored arms and `search_mode`") holds by construction. |
| Contract | New `Diagnostics` record in `Application/Routing/Contracts` (`ExperimentArms`: ordered map name → `ExperimentDiagnostic(Arm, Reason)`; `SearchMode`: `string?`, omitted when null). Added as `Diagnostics? Diagnostics` (omitted when null) on `RouteResponse` and `StoredOrderResponse`. Names follow the existing snake_case policy. |
| Where the flag is applied | In the two use cases, not in the mappers: the mappers stay pure. When the flag is off, `Diagnostics` stays `null`, so the field is absent and no stored JSON is parsed. |
| Bad stored JSON | If the stored arms cannot be parsed, throw (500 with no internals, fail safe). Never guess an arm. Only reachable when the flag is on. |
| Alias | `POST /api/route` shares the v1 handler, so it gets the same field. No new path. |
| OpenAPI | Document `diagnostics` as optional on both 201 and 200 schemas, with a note that it is off by default and clients must not depend on it. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code. Run `ddd-implementation` §1 and §2 at the start of Phase 1, and §5 in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`.
   - **REST:** additive-only change within v1; `GET` stays safe and idempotent; collections never `null`; one JSON naming policy (snake_case); documented in OpenAPI with examples (CLAUDE.md).
   - **Docs and comments:** XML docs on public types, with `<example>` where it helps; comments explain *why* and cite the requirement or ADR (`// AB-05`, `// OQ-08`).
   - **Compliance:** diagnostics hold only arms, reasons and `search_mode`. Never the caller ID or its hash, never `config_version` or targeting rules (not asked for). Off by default (AB-05).
   - **Tests:** xUnit v3, copy nearby test naming, no Arrange/Act/Assert comments. Integration tests use their own `order_id` because the shared database accumulates stored orders. Never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Start PostgreSQL first (`docker compose up -d postgres`), then from the repo root:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing tests. Record each existing test changed (none expected; the default is off, so existing bodies are unchanged).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading from `⬜ Not started` to `✅ Done` in this file, tick its checklist and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - the stored `experiment_arms` JSON not matching the shape above (for example a missing experiment);
   - POST and GET showing different `diagnostics` for the same order;
   - an existing test failing because a body now has `diagnostics` while the flag is off;
   - `LayerDependencyTests` objecting to the new types.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-022-diagnostics-field`).

---

## Phase 0: Check prerequisites ✅ Done

- [x] Confirm OR-016 and OR-028 are done (commit `bd082a4` for OR-028; `Plans/OR-028-get-stored-order.md` all phases ✅). If not, stop and ask.
- [x] Confirm the stored arms JSON has one `{ arm, reason }` entry for each of the three experiments (`StoredRoutingOrderFactory`). If it differs, stop and ask.
- [x] Baseline: PostgreSQL up, `dotnet build`, `dotnet test` and format green before any change.

**Verify:** all green. No code changes.

**Result:** Baseline 1366 tests green; stored arms JSON has all three experiments.

---

## Phase 1: Flag, contract and mapper (Application) ✅ Done

- [x] `ddd-implementation` §1 and §2 (concepts: diagnostics as a read-only view of stored evidence; no new aggregate; layers affected: Application and Api; compliance: off by default, no identity data).
- [x] Add `RoutingOptions.ExposeDiagnostics` (default `false`) with XML docs; add `"ExposeDiagnostics": false` to `appsettings.json` under `Routing`.
- [x] Add `Diagnostics` and `ExperimentDiagnostic` records (Application/Routing/Contracts); add the optional `Diagnostics` property to `RouteResponse` and `StoredOrderResponse` (omitted when null).
- [x] Add `DiagnosticsMapper` (static, no business rule): `StoredRoutingOrder` → `Diagnostics`; `SearchMode` omitted when null; unparseable or incomplete arms JSON throws.
- [x] Unit tests:
  - mapper: stored arms with all three experiments map to the arm and reason of each, under the snake_case names;
  - `search_mode` `exact` and `greedy` are carried over; `null` stays `null` and serializes with no `search_mode` field;
  - invalid arms JSON and a missing experiment throw;
  - serialization: `diagnostics` is absent when null on both envelopes, and snake_case when present;
  - `RouteResponse` and `StoredOrderResponse` still never have `null` collections.

**Verify:** build, test, format.

**Result:** Mapper, contracts and serialization unit tests added (`DiagnosticsTests`); 1392 tests green.

---

## Phase 2: Apply the flag in the use cases ✅ Done

- [x] `RouteOrderUseCase`: inject `IOptions<RoutingOptions>`; when `ExposeDiagnostics` is true, set `Diagnostics` from the `StoredRoutingOrder` it just built (`response with { Id = id, Diagnostics = ... }`). When false, leave it `null`. The stored record and the decision log are unchanged.
- [x] `GetStoredOrderUseCase`: inject the same options; when true, set `Diagnostics` from the stored order it read. `StoredOrderResponseMapper` is unchanged.
- [x] Unit tests (fake store and reader, `Options.Create`):
  - flag off (default): both use cases return `Diagnostics == null`;
  - flag on: POST shows the arms and reasons of the assignment and the `search_mode` of the decision;
  - flag on: GET shows the stored arms and `search_mode`;
  - flag on: the diagnostics from POST equal the diagnostics from GET for the same stored order;
  - flag on, no product known (`search_mode` null): `experiment_arms` present, `search_mode` absent;
  - a use case that returns null (404 path) never builds diagnostics.

**Verify:** build, test, format. Existing use case tests pass unchanged.

**Result:** Use case tests added. Deviation: the two existing use case test helpers (`RouteOrderUseCaseTests.UseCase()`, `GetStoredOrderUseCaseTests.UseCase()`) changed for the new `IOptions<RoutingOptions>` constructor parameter, and the fake policy factory now keeps the assignment it is given. No assertion changed.

---

## Phase 3: API host tests ✅ Done

No new endpoint is needed: the existing handlers return the records the use cases fill. This phase proves it end to end.

- [x] Extend `RouteApiFactory` (or use `WithWebHostBuilder`) to set `Routing:ExposeDiagnostics` per test class.
- [x] Host tests (seeded database), each with its own `order_id`:
  - AC1: default configuration, POST ORD-002: the 201 body has no `diagnostics` property;
  - AC2: flag on, POST: `diagnostics.experiment_arms` has the three experiments, each with `arm` and `reason`, and `search_mode` is `exact` for a normal order;
  - AC2: flag on with `experiments.json` set so an experiment is `treatment` (use the existing experiment host-test pattern): that arm shows `treatment` with reason `bucketed`;
  - AC2: flag on, an order with only unknown products: `experiment_arms` present, no `search_mode`;
  - AC3: store one order with the flag off, GET it: no `diagnostics`; GET the same stored order with the flag on: `diagnostics` equals the POST `diagnostics` of that order (use two hosts on the same database, or one host and assert the stored values directly);
  - the alias `POST /api/route` follows the flag too;
  - a 400 response never has `diagnostics`, flag on or off;
  - a 404 on GET never has `diagnostics`, flag on or off.

**Verify:** build, test, format.

**Result:** `RouteApiFactory.Create` gained `exposeDiagnostics` and `experimentsFile` parameters; `DiagnosticsApiTests` added (AC1-AC3, alias, 400, 404); 1403 tests green.

---

## Phase 4: OpenAPI, docs and post-implementation review ✅ Done

- [x] OpenAPI: the `v1` document shows an optional `diagnostics` object (`experiment_arms`, `search_mode`) on the 201 and the GET 200 schemas, with a description that it is off by default and clients must not depend on it. Add a 201 and a 200 example with `diagnostics`. Extend `RouteOpenApiTests` to check the property is present and not required.
- [x] Docs: add `ExposeDiagnostics` to `appsettings.json` (done in Phase 1) and to `.env.example` if other `Routing` settings are listed there; add an "Implementation notes (OR-022)" section to ADR-0004 (flag, shape, `search_mode` omitted when null, same mapper for POST and GET). Leave README changes to OR-025 unless the engineer wants them here.
- [x] `ddd-implementation` §5 review; report honestly what is met, N/A and not verified.
- [x] Final full run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

**Verify:** build, test, format; every AC below traced to a passing test.

**Result:** OpenAPI examples and ADR-0004 notes added. Deviation: the 201 gained a fourth example (`routed_with_diagnostics`) and the GET 200 moved from one `example` to two named `examples`, so two assertions in `RouteOpenApiTests` were updated. `.env.example` lists no `Routing` settings, so unchanged. Final run: 1405 tests green, format clean except the known `Program.cs` import order.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-022-diagnostics-field.md`; `Diagnostics`, `ExperimentDiagnostic` (Application/Routing/Contracts); `DiagnosticsMapper` (Application/Routing); unit and host tests |
| Modified | `RoutingOptions.cs`; `RouteResponse.cs`; `StoredOrderResponse.cs`; `RouteOrderUseCase.cs`; `GetStoredOrderUseCase.cs`; `appsettings.json`; `RouteApiFactory.cs`; `RouteOpenApiTests.cs`; OpenAPI transformer for examples; ADR-0004 (note) |
| Reused | `StoredRoutingOrder` (arms JSON and `SearchMode`); `StoredRoutingOrderFactory`; `RouteApiFactory`; existing experiment host-test setup |
| Unchanged | Domain routing rules, what is stored (no schema change, no migration), the decision log, the 400 envelope, status codes, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: off by default | use case unit tests, host test (no `diagnostics` property) | 2, 3 |
| AC2: on when enabled | mapper and use case unit tests, host tests (arms, reasons, `search_mode`) | 1, 2, 3 |
| AC3: same on GET | use case unit test (POST equals GET), host test (flag off, then on) | 2, 3 |

# Plan: OR-028: Add GET to read a stored order

**Ticket:** [Docs/Tickets/OR-028-get-stored-order.md](../Docs/Tickets/OR-028-get-stored-order.md)
**Depends on:** OR-027 (done: `IRoutingOrderStore`, `StoredRoutingOrder`, `EfRoutingOrderStore`, `ICallerHasher`, 201 + `Location`; plan in `Plans/OR-027-store-routing-decision.md`).
**References:** [ADR-0007](../Docs/ADRs/ADR-0007-routing-decision-persistence.md); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); Technical Requirements FR-19, FR-21, §9.4, §9.5, SC-31, NFR-11; `CLAUDE.md` (REST API conventions); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-022 (`diagnostics` on GET), OR-023 (acceptance tests), OR-025 (README and docs).

> **Save location:** this file is `Plans/OR-028-get-stored-order.md`. Update it as each phase is done.

## Context

`POST /api/v1/route` already stores every valid decision (OR-027) and answers 201 with `Location: /api/v1/route/{id}`, but nothing reads it back. This ticket adds `GET /api/v1/route/{id}`. It returns the stored order (request details, requested items, decision), **never re-routes**, and only to the caller that created it. Every other case (unknown id, non-UUID, another caller, an order stored without a caller) is the same 404.

Existing seams to build on:
- OR-026/OR-027 tables and rows: `routing_orders`, `routing_order_items` (one row per requested line, supplier name snapshot), `routing_order_warnings`. Rows are read through `OrderRouterDbContext`.
- `ICallerHasher` + `CallerIdentity.Resolve(user)`: the same hash that was stored on POST. A null caller ID has no hash, so there is nothing to match.
- `RouteResponseMapper` builds `routing`, `unfulfilled_items` and `errors` for POST. The read path must rebuild the **same** values from stored rows (ADR-0007: `errors[]` is derived from the unfulfilled messages).
- `RouteEndpoints` maps the POST paths; `RouteCallerEndpointExtensions.RequireRouteCaller()` gives authentication; `SecuritySchemeTransformer` adds the 401 to OpenAPI; `RouteOpenApiTransformer` documents POST.
- JSON is snake_case via `ConfigureHttpJsonOptions`; enums use snake_case strings.

Out of scope: `diagnostics` on GET (OR-022, which depends on this ticket), the decision log, retention (OQ-27), listing or deleting orders, `GET` on `/api/route/{id}`.

### Design choices (from the ticket, ADR-0007 and the code)

No ambiguity or conflict was found. These are the defaults the plan uses; change one before Phase 1 if you disagree.

| Topic | Choice and reason |
|---|---|
| Read port | New `IRoutingOrderReader` (Application): `Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken)`. Separate from `IRoutingOrderStore` (ISP: the write path doesn't need a read method). Returns the existing `StoredRoutingOrder` record, so no second persistence model. |
| Ownership | The EF query filters by `id` **and** `caller_hash` in one `WHERE` (ADR-0007). A miss and another caller's order are indistinguishable (SC-31), and no existence check happens before the ownership filter. |
| No caller ID | `CallerIdentity.Resolve(user)` is null → 404 without touching the database. Such an order was stored with `caller_hash = NULL`, which never matches, so it can never be read back (OR-027 decision, fail safe). |
| Non-UUID id | The handler takes `id` as a `string` and parses it with `Guid.TryParseExact(value, "D", ...)` (canonical hyphenated form only). A route constraint `{id:guid}` is **not** used: it would turn an invalid id into a bodiless 404 *before* authentication, so an anonymous call to `/api/v1/route/abc` would not get 401. Auth runs first, then the parse. |
| 404 body | One Problem Details (RFC 9457) body for every 404 case, with the same title and detail, so nothing reveals whether an id exists (CLAUDE.md). |
| Response contract | New `StoredOrderResponse` record in `Application/Routing/Contracts` with `Id`, `OrderId`, `CustomerZip`, `MailOrder`, `CreatedAt`, `Items` (`product_code`, `quantity`), then the same `Status`, derived `Feasible`, `Routing`, `UnfulfilledItems`, `Warnings`, `Errors` as POST. `Feasible` is derived from `Status` and the collections are never null (same invariants as `RouteResponse`). `Routing` reuses `SupplierRouting`, `RoutedItem`, `UnfulfilledItem` and `RoutingWarning`. |
| Rebuilding the decision | A mapper (`StoredOrderResponseMapper`, Application, static, no business rule) turns `StoredRoutingOrder` into `StoredOrderResponse`: routed items grouped by supplier in order of the supplier's first line (FR-15, same rule as `RouteResponseMapper`), unfulfilled items from rows with a reason, `errors` = their messages, held-back lines appear only in `items`. An unknown `status` text throws (fail safe: 500, never a guessed value). |
| Use case | `GetStoredOrderUseCase` (Application) behind `IGetStoredOrderUseCase`: parse nothing, hash the caller, call the reader, map. Returns `StoredOrderResponse?` (null = 404). The Api only depends on the interface (DIP). |
| Read query | `AsNoTracking`, `Include` items and warnings with `AsSplitQuery` as needed, items ordered by `line_number`, warnings by `ordinal`. Reads only from the order tables. Reference data is never joined, so a re-seed cannot change the result (AC4). |
| `created_at` | Written as ISO 8601 UTC. Check the serialized shape against §9.5 (`...Z`); fix with a converter only if it comes out as `+00:00`. |
| Alias | No `GET` mapped under `/api/route`. `GET /api/route/{id}` matches no route, so it is a 404 by default (AC8); a test pins it. |
| OpenAPI | Own operation name (`GetStoredOrderV1`), tag `Routing`, 200 with example, 401 (from `SecuritySchemeTransformer`), 404 and 500 as Problem Details. It does **not** carry `RouteEnvelopeMetadata`, which is POST-only (the 400 envelope). |
| Layering | Ports and use case in Application; EF reader in Infrastructure; endpoint in Api. `LayerDependencyTests` stay green. Reader is scoped (same lifetime as the DbContext). |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code. Run `ddd-implementation` §1 and §2 at the start of Phase 1, and §5 in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`.
   - **REST:** `GET` is safe and idempotent: it never creates or changes state and never re-routes. 200 for the read; 401 with `WWW-Authenticate`; 404 for not found and not visible (don't reveal existence); 500 with no internals; Problem Details (RFC 9457); additive-only change within v1; documented in OpenAPI with status codes and examples (CLAUDE.md).
   - **Docs and comments:** XML docs on public types, with `<example>` where it helps; comments explain *why* and cite the requirement or ADR (`// FR-21`, `// SC-31`).
   - **Compliance:** raw caller IDs are never logged or returned, only the hash is compared (NFR-11). Never reveal whether another caller's order exists. Fail safe: ambiguous stored data → 500, never a guess.
   - **Tests:** xUnit v3, copy nearby test naming, no Arrange/Act/Assert comments. Integration tests use their own `order_id` because the shared database accumulates stored orders. Tests that change reference data (AC4) use a throwaway database, never the shared seeded one. Never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Start PostgreSQL first (`docker compose up -d postgres`), then from the repo root:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing tests. Record each existing test changed (none expected).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading from `⬜ Not started` to `✅ Done` in this file, tick its checklist and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - a stored row set that cannot be rebuilt into the same `routing` / `unfulfilled_items` / `errors` as POST returned;
   - `created_at` or another field serializing differently from the POST response or §9.5;
   - an anonymous call getting 404 instead of 401, or the 404 bodies differing between cases;
   - `LayerDependencyTests` objecting to the new types;
   - the global exception handler leaking details or returning something other than a 500 Problem Details.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-028-get-stored-order`).

---

## Phase 0: Check prerequisites ✅ Done

- [x] Confirm OR-027 is done (commit `e3044a9`, its plan marked done, `IRoutingOrderStore`, `ICallerHasher` and the 201 + `Location` present). If not, stop and ask.
- [x] Baseline: PostgreSQL up, `dotnet build`, `dotnet test` and format green before any change.

**Verify:** all green. No code changes.

**Result:** OR-027 present (`e3044a9`). Baseline: build 0 errors, 1325 tests pass, format clean apart from the known `Program.cs` import order.

---

## Phase 1: Read model, mapper and use case (Application) ✅ Done

- [x] `ddd-implementation` §1 and §2 (concepts: stored order as a read-only record of a past decision; no new aggregate; ownership by caller hash; layers affected: Application, Infrastructure, Api; compliance: hash only, 404 never reveals existence).
- [x] Add `IRoutingOrderReader`, `StoredOrderResponse` (with a `RequestedItem` record), `StoredOrderResponseMapper`, `IGetStoredOrderUseCase` and `GetStoredOrderUseCase`; register in `RoutingServiceCollectionExtensions`.
- [x] Unit tests (fake reader):
  - mapper: a stored routed order maps to the same `routing` the POST mapper builds (build both from the same decision and compare);
  - partially routed: `routing`, `unfulfilled_items` and `errors` match the POST response;
  - all-or-nothing hold-back (AC3): `items` lists every line including the routable ones, `routing` is empty;
  - duplicate product codes keep request order; supplier groups follow the first line;
  - `warnings` are carried over; collections are never null;
  - unknown stored `status` throws;
  - use case: caller with no ID → null and the reader is **not** called (AC5 at unit level); hash of the resolved caller is what the reader receives; reader miss → null.

**Verify:** build, test, format.

**Result:** build clean; unit tests added for the mapper (9) and use case (6). No existing test changed. Deviation: the `RequestedItem` record lives in `StoredOrderResponse.cs`; the mapper and `IRoutingOrderReader` live in `Routing/Storage`.

---

## Phase 2: EF reader (Infrastructure) ✅ Done

- [x] Add `EfRoutingOrderReader`: one query filtered by `id` and `caller_hash`, `AsNoTracking`, items by `line_number`, warnings by `ordinal`; maps rows to `StoredRoutingOrder`; register as scoped.
- [x] Integration tests against PostgreSQL (existing `RoutingOrderStoreTests` style, `ThrowawayDatabase` where reference data changes):
  - save with `EfRoutingOrderStore`, read with the reader: every field equal, items in request order, warnings in order;
  - wrong caller hash → null; unknown id → null; `NULL` stored hash never matches any hash;
  - AC4: store an order, rename its supplier in `suppliers` (re-seed), read: the supplier name is still the routing-time name;
  - the read issues only `SELECT`s and touches only the order tables (use `CapturingCommandInterceptor`).

**Verify:** build, test, format.

**Result:** 1349 tests pass, format clean. Deviation: the arms JSON is compared as JSON in the round-trip test, because `jsonb` normalizes whitespace.

---

## Phase 3: GET endpoint and API tests ✅ Done

- [x] Map `GET /api/v1/route/{id}` in `RouteEndpoints` (name `GetStoredOrderV1`), `RequireRouteCaller()`, handler: parse `id` (`TryParseExact` `D`), resolve the caller, call the use case, return `Ok(StoredOrderResponse)` or one shared 404 Problem Details. Nothing is mapped under `/api/route`.
- [x] Check that the 401 comes first for an anonymous call, also with an invalid id, and carries `WWW-Authenticate`.
- [x] Host tests (`RouteApiFactory`, seeded database), each with its own `order_id`:
  - AC1: POST ORD-002 as a caller, GET the `Location` as the same caller: 200 with `id`, `order_id`, `customer_zip`, `mail_order`, `created_at`, `items` in request order, and `feasible`, `status`, `routing`, `unfulfilled_items`, `warnings`, `errors` equal to the POST body;
  - AC2: an order routed to `SUP-T003`/`SUP-T004` is read 10 times and always shows the supplier from the POST;
  - AC3: an order stored `not_routed` by the all-or-nothing arm lists every requested line in `items`;
  - AC5: another caller reads it → 404, and the body equals the unknown-id body;
  - AC6: a random UUID and `abc` → 404 (both with a valid caller);
  - AC7: no credentials → 401 with `WWW-Authenticate`, including for `abc`;
  - AC8: `GET /api/route/{id}` → 404 (also with a valid caller);
  - a token with no caller claim: POST works (201), GET → 404;
  - `GET` leaves the table counts unchanged (safe method, no re-routing);
  - `created_at` serializes as ISO 8601 UTC.

**Verify:** build, test, format.

**Result:** 1364 tests pass, format clean. Deviation: `created_at` serialized with `+00:00`, so `UtcTimestampJsonConverter` (Application/Contracts) writes it with a `Z`, as the plan allowed. 404 bodies are identical except for the per-request `trace_id`, which the tests ignore. Counts test is scoped to its own `order_id` (the shared database is written by parallel tests).

---

## Phase 4: OpenAPI, docs and post-implementation review ✅ Done

- [x] OpenAPI (AC9): summary and description, a 200 example (the §9.5 shape), 404 and 500 as Problem Details, the 401 from `SecuritySchemeTransformer`; the operation does not carry the POST 400 envelope. Extend `RouteOpenApiTests`: the `v1` document describes `GET /api/v1/route/{id}` with 200, 401 and 404, and has no `GET` under `/api/route`.
- [x] Docs: add the GET to `src/OrderRouter.Api/OrderRouter.Api.http`; add an "Implementation notes (OR-028)" section to ADR-0007 (ownership filter, 404 sameness, null caller, parse-after-auth). Leave README changes to OR-025 unless the engineer wants them here.
- [x] `ddd-implementation` §5 review; report honestly what is met, N/A and not verified.
- [x] Final full run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

**Verify:** build, test, format; every AC below traced to a passing test.

**Result:** 1366 tests pass, 0 build warnings, format clean apart from the known `Program.cs` import order. README left to OR-025. `ddd-implementation` §5: tests and naming met; async, DI and error handling follow .NET practice; no new aggregate, no money, no domain events (N/A: read-only query); compliance: only the caller hash is compared, one shared 404 (met). Not verified: p95 latency of the GET (needs the load test).

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-028-get-stored-order.md`; `IRoutingOrderReader`, `IGetStoredOrderUseCase`, `GetStoredOrderUseCase`, `StoredOrderResponse`, `StoredOrderResponseMapper` (Application); `EfRoutingOrderReader` (Infrastructure); unit and integration tests |
| Modified | `RouteEndpoints.cs`; `RouteOpenApiTransformer.cs` (or a small sibling transformer for the GET); `RoutingServiceCollectionExtensions.cs` and `PersistenceServiceCollectionExtensions.cs` (DI); `RouteOpenApiTests.cs`; `OrderRouter.Api.http`; ADR-0007 (note) |
| Reused | OR-026/OR-027 rows and `DbSet`s; `StoredRoutingOrder`; `ICallerHasher`; `CallerIdentity.Resolve`; `RouteApiFactory`; `ThrowawayDatabase`; `CapturingCommandInterceptor` |
| Unchanged | Domain routing rules, the POST contract and status codes, reference-data tables, seeder, migrations (no schema change), `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: read back | mapper unit tests, reader and host tests | 1, 2, 3 |
| AC2: no re-routing | host test (10 reads, tie-break order) | 3 |
| AC3: held-back items listed | mapper unit test, host test | 1, 3 |
| AC4: supplier name snapshot | reader integration test (throwaway DB) | 2 |
| AC5: other caller → 404 | use case, reader and host tests | 1, 2, 3 |
| AC6: unknown or invalid id → 404 | host tests | 3 |
| AC7: no credentials → 401 | host test | 3 |
| AC8: not on the alias | host test, OpenAPI test | 3, 4 |
| AC9: OpenAPI | `RouteOpenApiTests` | 4 |

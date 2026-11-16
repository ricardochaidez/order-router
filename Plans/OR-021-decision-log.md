# Plan: OR-021: Log one routing decision event per request

**Ticket:** Docs/Tickets/OR-021-decision-log.md
**Depends on:** OR-016 (done: assigner, arms, reasons, `config_version`). **OR-026 and OR-027 (NOT done)**: they provide the order tables, the UUIDv7 `id`, `ICallerHasher` (`CallerHashing:Secret`), the 201 response and the 500-on-save-failure path.
**References:** ADR-0005 (Exposure / decision logging), ADR-0007, Requirements AB-04, NFR-06, NFR-11; CLAUDE.md; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`

## Context
AB-04 wants one structured `RoutingDecisionMade` event per valid POST. It is the data source for later A/B analysis. It is written only after the order is committed, includes the stored `id`, and never contains the raw caller ID (only the HMAC `caller_hash`) or patient data beyond `customer_zip`. 400/401 requests and failed saves produce no event.

`RoutingDecision` already carries most of the data: status, routed lines, unfulfilled lines, `Assignment` (arms, reasons, `ConfigVersion`), `TieBreak` (chosen plus tied candidates) and `SearchMode`. So this ticket is mainly a new event, a `[LoggerMessage]` emitter, and one call placed after the save in the use case.

### Decision made with the engineer
- **Prerequisite plans:** `Plans/OR-026-routing-order-tables.md` and `Plans/OR-027-store-routing-decision.md` (do them first). In OR-027 a caller with no caller claim has `caller_hash = NULL`, so the event logs `caller_hash` as null in that case.
- **Dependency order:** OR-021 is planned **after OR-027**. Phase 0 verifies OR-026/OR-027 are done. If not, stop and ask. No stubs, no temporary id or hasher.

## Working rules (apply to every phase)
1. **Follow AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md`, `.claude/rules/dotnet-ddd-architecture.md`. Run `ddd-implementation` §1 (domain analysis) and §2 (architecture review) at the start of Phase 1, and §5 (post-implementation review) in Phase 4.
   - C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null`/`is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`, XML docs on public types, comments explain *why* and cite AB-04/ADR.
   - Tests: xUnit v3, copy nearby test naming, no Arrange/Act/Assert comments. Never edit `Docs/Data/*`.
   - Compliance: never log the raw caller ID, tokens, or patient data other than `customer_zip` (NFR-11).
2. **Tests must pass after each phase.** Start PostgreSQL (`docker compose up -d postgres`), then from the repo root: `dotnet build` (0 errors, no new warnings), `dotnet test` (all pass), `dotnet format --verify-no-changes` (the known `Program.cs` import-order issue predates this work; ignore it).
3. **Mark each phase done.** When checks pass, change the heading from `⬜ Not started` to `✅ Done` in `Plans/OR-021-decision-log.md`, tick the checklist, and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop, summarize changes and test results, and ask the engineer before starting the next one.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - OR-026/OR-027 not complete (`ICallerHasher`, stored `id`, save-before-respond);
   - a field the ticket lists that the domain does not expose (see "Field mapping" below);
   - `LayerDependencyTests` objecting to the new types;
   - an existing test depending on the response flow that OR-027 changed.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-021-decision-log`).

## Design choices
| Topic | Choice |
|---|---|
| Emission | `[LoggerMessage]` source-generated method, event name `RoutingDecisionMade`, Information level, one call per valid POST (NFR-06, ADR-0005). |
| Placement | Application layer: a small `IRoutingDecisionLog` port plus an `ILogger`-based implementation, called by `RouteOrderUseCase` **after** the OR-027 commit, with the stored `id`. The use case stays free of business rules. |
| Event data | An immutable record built from `RoutingDecision`, the stored `id`, `caller_hash` (from `ICallerHasher`) and `customer_zip`. It holds supplier IDs and modes only. |
| Privacy | The event type has no raw caller field, so the raw ID cannot be logged by mistake (AC3). Only `customer_zip` from the order; no product, quantity or patient data. |
| Failure | Not saved means no event: the call sits after the successful save, never in a `finally`. A logging fault must not turn a saved order into a 500. |

### Field mapping (AC1/AC2)
`id` (OR-027), `order_id`, `caller_hash` (ICallerHasher), per experiment `arm` + `reason` (`Assignment.Arms`, `Reasons`), chosen suppliers and modes (`RoutedLines`), tied candidates and pick (`TieBreak`, logged when `UsedRandom`), `shipment_count` and `plan_quality` (chosen `RoutingPlan`), `unrated_chosen` (count), `unfulfilled_count` (`Unfulfilled.Count`), `search_mode`, `config_version`, `customer_zip`.
Not-routed orders have no plan: log zero/empty for plan fields and `search_mode` null (AB-04 says every outcome). **Verify in Phase 1** that `RoutingPlan` exposes shipment count, quality and unrated count; if one is missing, stop and ask.

---

## Phase 0: Save plan and check prerequisites ✅ Done
- [x] Create `Plans/OR-021-decision-log.md` with this plan (headings and statuses as written).
- [x] Confirm OR-026/OR-027 are implemented: order tables and migration, `ICallerHasher`, `CallerHashing:Secret` startup check, 201 + `id`, save failure → 500. If not, stop and ask.

**Verify:** file exists; build and tests green on the OR-027 baseline.

**Result:** OR-026 and OR-027 are done (both plans marked done). Confirmed in code: tables and migration, `ICallerHasher` and the `CallerHashing:Secret` startup check, 201 with the stored `id`, save failure → 500 (tested with a real database rejection). `RoutingPlan` exposes `ShipmentCount` and `Quality`; the unrated count comes from `Supplier.Rating` of the routed suppliers, so no field is missing. Baseline: 1302 tests, build clean, format clean except the known `Program.cs` import issue.

## Phase 1: Event model and emitter ✅ Done
- [x] `ddd-implementation` §1 and §2 (concepts: routing decision, exposure, pseudonymized caller; no aggregate or domain event).
- [x] Add the event record and the `[LoggerMessage]` emitter (see Design choices); register in DI.
- [x] Unit tests with a capturing logger (assert structured properties, not text):
  - AC1: every listed field present for a routed order;
  - AC2: tie recorded with tied candidates and pick; no tie → no random pick;
  - partially routed and not routed produce an event (unfulfilled count, null plan fields);
  - AC3: raw caller ID `user-123` absent from the whole log output; hash present.

**Verify:** build, test, format; `LayerDependencyTests` green.

**Result:** 1313 tests pass (11 new). Added `IRoutingDecisionLog`, `RoutingDecisionLogEntry` (flat scalar fields, no raw caller field), `RoutingDecisionLogEntryFactory` and `LoggerRoutingDecisionLog` (`[LoggerMessage]`, event `RoutingDecisionMade`, id 2100, Information) in `Application/Routing/Logging`; registered as a singleton. Field names are the AB-04 names (snake_case placeholders, so the parameters are snake_case with a scoped `IDE1006` pragma). Decisions: `shipment_count` and `plan_quality` describe the routed plan, so they are 0/null when all-or-nothing held every line back; the tie is logged whenever the random tie-break ran; the emitter swallows provider exceptions so a log fault cannot fail a stored order. No existing test changed. Format clean except the known `Program.cs` import issue.

## Phase 2: Wire after the save ✅ Done
- [x] `RouteOrderUseCase`: call the log once, after the OR-027 commit, with the stored `id`.
- [x] Update existing use-case unit tests for the new dependency.

**Verify:** build, test, format.

**Result:** 1316 tests pass (3 new use-case tests). `RouteOrderUseCase` takes `IRoutingDecisionLog` and calls it once, after `SaveAsync`, with the stored `id`, the hash and `customer_zip`. Tests: one event after the save (call order `save`, `log`), none when the save fails, hash and never the caller ID. `RouteOrderUseCaseTests` got a fake log and shared call list. Format clean except the known `Program.cs` import issue.

## Phase 3: Integration tests through the real host ✅ Done
- [x] AC1/AC3: valid POST → exactly one `RoutingDecisionMade` whose `id` equals the response `id`; caller `user-123` absent from all captured logs.
- [x] AC4: 400 (validation) and 401 (no/bad token) → no event.
- [x] AC5: simulate an insert failure (reuse OR-027's AC7 technique) → 500 and no event.
- [x] Retry of the same body → two events with different `id`s (SC-29).

**Verify:** build, test, format.

**Result:** 1325 tests pass (two runs; 9 new in `RoutingDecisionLogApiTests`). Through the real host with a capturing log provider and the seeded database: one event per valid POST whose `id` equals the response `id` and which has every AB-04 field (AC1); ORD-003-style order lists both tied candidates and a pick equal to the supplier in the response (AC2); `user-123` appears nowhere in anything the host logged, only its hash (AC3); 400 and 401 (no token, bad token) write no event (AC4); a real database rejection returns 500, no event and no rows (AC5); a retry writes two events with different ids (SC-29); a not-routed order still logs (unfulfilled count 1, shipment count 0). Tests read only the events of their own `order_id`, because the host logs and database are shared in parallel.

## Phase 4: Docs and post-implementation review ✅ Done
- [x] Short implementation note in ADR-0005 (Exposure / decision logging): fields, after-commit rule, privacy.
- [x] `ddd-implementation` §5, reported honestly. Met: rule placement, no raw caller, after-commit. N/A with reasons: aggregates, money, domain events. Not verified: anything not measured (e.g. coverage %).
- [x] Follow-ups (not done here): OR-022 diagnostics, OR-024 matrix, OR-025 README.
- [x] Final `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

**Result:** 1325 tests pass; build 0 warnings and 0 errors; format clean except the known `Program.cs` import issue. ADR-0005 note added (fields, after-commit rule, privacy, value formats, not-routed behaviour, log-fault trade-off).

**§5 review (`ddd-implementation`):**
- **Met:** the use case only orchestrates (no rule added); the entry factory copies values from the domain decision; the port is in Application (DIP); after-commit placement proven by a call-order test and by host tests (400, 401 and a real database rejection write no event); no raw caller ID can reach the log (the entry has no such field and the factory takes only the hash); `caller_hash` is null for a token with no caller claim; only `customer_zip` is patient-adjacent; `[LoggerMessage]` source generation with AB-04 field names.
- **N/A, with reasons:** aggregates and domain events (the log is observability of an already-made decision, not a new domain concept); money; authorization (no endpoint).
- **Not verified:** coverage % (not measured); the partially routed event through the real host (needs an EXP-3 treatment config; covered at unit level only); that a log shipper keeps the snake_case field names (depends on the logging provider the deployment chooses).
- **Trade-off to know:** the emitter swallows log-provider exceptions so a log fault cannot fail a stored order; a broken provider can therefore lose events silently.
- **Follow-ups (not done here):** OR-022 diagnostics, OR-024 combination matrix, OR-025 README (should document the event fields).

---

## Critical files
| Action | Path |
|---|---|
| New | `Plans/OR-021-decision-log.md`; event record, `IRoutingDecisionLog` and its `[LoggerMessage]` implementation (Application/Routing); unit and integration tests |
| Modified | `RouteOrderUseCase.cs`; `RoutingServiceCollectionExtensions.cs`; use-case tests; ADR-0005 (note) |
| Reused | `RoutingDecision`, `TieBreakResult`, `ExperimentAssignment`/`ExposureReason`, OR-027 `ICallerHasher` and stored `id`, OR-016 `RouteApiFactory` |
| Unchanged | Domain routing rules, endpoint contract, `Docs/Data/**` |

## Verification summary
| AC | Verified by | Phase |
|---|---|---|
| AC1 event fields | emitter unit tests; host test (id matches response) | 1, 3 |
| AC2 tie recorded | emitter unit tests | 1 |
| AC3 caller pseudonymized | unit and host tests search all log output | 1, 3 |
| AC4 no event for 400/401 | host tests | 3 |
| AC5 no event when save fails | host test with simulated insert failure | 3 |

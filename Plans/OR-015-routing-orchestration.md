# Plan: OR-015: Wire routing end to end and map the response

**Ticket:** [Docs/Tickets/OR-015-routing-orchestration.md](../Docs/Tickets/OR-015-routing-orchestration.md)
**Depends on:** OR-005 (seeder), OR-009 (endpoint, envelope, `IRouteOrderUseCase` seam), OR-012 (`IPlanSearchService`), OR-014 (`ITieBreaker`, `SystemRandomSource`). All done.
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (pipeline, `OrderRoutingService`, `RoutingDecision`); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md) (envelope, `feasible` from `status`); Technical Requirements FR-09, FR-10, FR-14, FR-15, FR-16, DEV-10, §4.3, §6.2, §9.2, §9.4; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-016 (replaces the all-control assignment), OR-019/OR-020 (partial routing, warnings), OR-021 (decision log reads `RoutingDecision`), OR-022 (`search_mode`), OR-023 (acceptance suite), OR-027 (stores the decision, 200 → 201).

> **Save location:** Phase 0 copies this plan to `Plans/OR-015-routing-orchestration.md`. Update that file as each phase is done.

## Context

Every routing building block exists: capability resolution (OR-006), eligibility (OR-010), plan search with guard rails (OR-011/012), control policies (OR-013) and the random tie-breaker (OR-014). But `POST /api/v1/route` still runs the placeholder `RoutingNotAvailableUseCase`, which returns 500. OR-015 connects the pieces. It resolves the products, finds eligible suppliers, searches plans, applies the policies and the tie-break, and maps the result to the v1 envelope. Until OR-016 lands, every request uses `ExperimentAssignment.AllControl`. Success returns **200** for now; OR-027 changes it to 201 and updates the 200 assertions written here.

Out of scope: experiment assigner (OR-016), treatment arms (OR-017–019), dependency warnings (OR-020; `warnings` stays empty), decision log (OR-021), `diagnostics`/`search_mode` in the body (OR-022), storing the decision, `id`, 201 and `Location` (OR-027), reference-data cache (ADR-0006).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Where the routing rules live (ADR-0001 calls `OrderRoutingService` a domain service, but `IPlanSearchService` is in Application) | **"The Domain has all business rules; the Application calls the Domain."** `OrderRoutingService` is a pure, synchronous **Domain** service holding the whole pipeline (eligibility → candidates → search → keys 2–4 → key 5 → EXP-3). The **Application** use case only does I/O (resolve products, load suppliers), builds the policy, calls the domain service and maps the result. Plan search is reached through a **Domain port**: `IPlanSearchService` moves from `Application/Routing/PlanSearch` to `Domain/Routing/PlanSearch` with an unchanged signature, and `PlanSearchService` (config limits plus the greedy log) stays in Application and implements it. *This is my reading of the answer. If it is wrong, correct it before Phase 1.* |

### Design choices (from the ticket, ADRs, earlier-plan follow-ups and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Domain input | `OrderToRoute(ZipCode CustomerZip, bool MailOrder, IReadOnlyList<OrderLine> Lines)` and `OrderLine(string ProductCode, int Quantity, Category? Category)` in `Domain/Routing/`. `Category` is null when the product is unknown. Lines stay in request order. The Domain cannot see `RouteOrderCommand`, which belongs to Application. |
| `OrderRoutingService(SupplierEligibility, IPlanSearchService, ITieBreaker)` | `RoutingDecision Route(OrderToRoute order, IReadOnlyList<Supplier> suppliers, RoutingPolicy policy)`. Steps: (1) unknown lines → `UnfulfilledLine(code, qty, null, "UNKNOWN_PRODUCT")`; (2) evaluate eligibility once for each distinct known category, and an ineligible category makes each of its lines unfulfilled with `reason.Code()`; (3) eligible categories → `CategoryDemand(category, lineCount)`, so same-category lines (duplicates included, DEV-10) are fulfilled together (FR-10); (4) merge eligible suppliers into one `SupplierCandidate` each (all its eligible categories, its mode, `policy.Rating.EffectiveRating(supplier)`); (5) **no demand → no search, plan null** (AC6, all unknown); otherwise `PlanSearchRequest(demand, candidates, Parameters.SimilarityBand, Selection.MaxExtraShipments)` → `planSearch.Search`; (6) `Selection.SelectBest` → `tieBreaker.Pick`; (7) `Unfulfillable.Apply(chosen, unfulfilled-in-request-order)`. |
| Fail safe | If there is demand but the search returns no plans, throw `InvalidOperationException` (every demanded category has an eligible supplier, so a cover must exist, and the code never invents or drops one). A supplier with two modes for one order, which FR-09 says is impossible, also throws. Unknown reason codes throw in the mapper. Each of these becomes a 500 with no internals. |
| `UNKNOWN_PRODUCT` constant | It sits beside `UnfulfilledLine` in the Domain, for example `UnfulfilledLine.UnknownProductCode`, so the reason-code strings live in the Domain next to `UnfulfillableReason.Code()`. |
| `RoutingDecision` (Domain, immutable) | `Status` (`RoutingOutcomeStatus`), `RoutedLines` (`RoutedLine(ProductCode, Quantity, Category, Supplier, FulfillmentMode)` in request order, empty when nothing is routed), `Unfulfilled`, `Assignment`, `TieBreak` (`TieBreakResult?`, null when nothing was searched), `SearchMode?`/`FallbackReason?`. Invariant: routed lines are present exactly when the outcome has a plan. It feeds OR-021/022/027 (AB-04). |
| Application use case | `RouteOrderUseCase : IRouteOrderUseCase` (scoped). It calls `ICapabilityResolver.ResolveAsync` (batch) **once** per request (OR-006 follow-up), and a code missing from the result counts as unknown. It calls `IReferenceDataReader.GetSuppliersHandlingAsync` once for the distinct known categories and **skips it when there are none**. Then `IRoutingPolicyFactory.Create(ExperimentAssignment.AllControl)` with a `// TEMPORARY until OR-016` comment, then `OrderRoutingService.Route`, then the mapper. |
| Response mapping | `RouteResponseMapper` (static, Application) maps: `order_id` echoed; `RoutingOutcomeStatus` → `RoutingStatus`; `routing` grouped by supplier, with groups in order of the supplier's first line and items in request order (FR-15); `category` from the product data (FR-16); `fulfillment_mode` `local`/`mail`; `unfulfilled_items` in request order with `category` null for unknown products; `errors` with one message per unfulfilled item, the same text as its `message`; `warnings` empty; `id` omitted. `feasible` stays computed by `RouteResponse`, never set (AC7). Messages: `UNKNOWN_PRODUCT` → "Product code '{code}' was not found in the catalog." (§9.2); `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` → "No supplier delivers '{category}' to the delivery ZIP."; `NO_SUPPLIER_FOR_CATEGORY` → "No mail-order supplier handles '{category}'." |
| `NationwideZipRange` (OR-010 follow-up) | `RoutingOptions` (Application, section `Routing`, `NationwideZipRange` default `00100-99999`). A validator checks `ZipRange.Parse` and is registered with `ValidateOnStart`, so a bad range stops the host. `SupplierEligibility` is registered as a singleton built from the options. `appsettings.json` gets the key. |
| DI (Api composition root, OR-013/014 follow-ups) | Singletons: `IRoutingPolicyFactory` → `RoutingPolicyFactory`, `IRandomSource` → `SystemRandomSource`, `ITieBreaker` → `UniformRandomTieBreaker`, `SupplierEligibility`, `OrderRoutingService`. `IPlanSearchService` stays registered as today. Scoped: `IRouteOrderUseCase` → `RouteOrderUseCase`, matching the scoped EF ports. |
| Placeholder removal (OR-009 follow-up) | Delete `RoutingNotAvailableUseCase`. The placeholder-500 test in `RouteAuthTests` becomes a test with a throwing fake use case, so the "500 with no internals" check stays. Update the `RouteApiFactory` doc ("null keeps the real use case") and the `.http` comment. Add routed and not-routed **200** examples in `RouteOpenApiTransformer`. |
| Tests | xUnit v3, `MethodName_Condition_ExpectedResult`, following the nearby style with no Arrange/Act/Assert comments. Domain tests use a small test adapter that implements `IPlanSearchService` over `PlanSearcher` and counts calls, plus `FixedRandomSource`. Reuse `PlanSearchTestData`, the `SupplierEligibilityTests` helpers and `RouteApiFactory`, and use `ThrowawayDatabase` (migrated, empty) for AC6. Random outcomes are checked by membership (ORD-003 ∈ {SUP-023, SUP-065}). Target ≥ 85% coverage of new Domain/Application code; measure it or say it was not measured. |

### Target structure

```
src/OrderRouter.Domain/Routing/
  OrderToRoute.cs, OrderLine.cs                 domain input
  OrderRoutingService.cs                        the pipeline (pure)
  RoutingDecision.cs, RoutedLine.cs             result (AB-04)
  PlanSearch/IPlanSearchService.cs              MOVED from Application (port)
  Policies/UnfulfilledLine.cs                   + UnknownProductCode
src/OrderRouter.Application/Routing/
  RouteOrderUseCase.cs                          I/O + call domain + map
  RouteResponseMapper.cs                        decision → v1 envelope
  RoutingOptions.cs, RoutingOptionsValidator.cs NationwideZipRange
  PlanSearch/PlanSearchService.cs               implements the Domain port (using change only)
src/OrderRouter.Api/
  Program.cs                                    DI + options; placeholder removed
  Routing/RoutingNotAvailableUseCase.cs         DELETED
  Routing/RouteOpenApiTransformer.cs            200 examples
  appsettings.json, OrderRouter.Api.http
tests/OrderRouter.UnitTests/Domain/Routing/     OrderRoutingServiceTests, RoutingDecisionTests, CountingPlanSearch
tests/OrderRouter.UnitTests/Application/Routing/ RouteOrderUseCaseTests, RouteResponseMapperTests, RoutingOptionsValidatorTests
tests/OrderRouter.IntegrationTests/Routing/      RouteOrderEndToEndTests (AC1–AC8), RouteAuthTests (placeholder test replaced)
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer feature, so use the `ddd-implementation` skill (`/ddd-implementation`): run §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–3; run the §5 post-implementation review in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, records for value objects, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members. Comments explain *why* and cite the requirement or ADR (`// FR-15`, `// DEV-10`, `// ADR-0001`).
   - **Fail safe:** never invent a plan or a reason; an ambiguous state throws and never auto-advances.
   - **REST (CLAUDE.md):** business outcomes such as "not routed" or an unknown product are 200 with a body, not errors. Collections are never null. No internals in a 500.
   - **Logging:** no ZIP, order id or caller in logs (NFR-11).
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run these from the repo root. Start PostgreSQL first with `docker compose up -d postgres` so the integration tests run too.
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: everything passes, including every existing test. The only intended changes to existing tests are the `IPlanSearchService` `using` updates (Phase 1) and the replaced placeholder-500 test (Phase 3); note both in the phase result.
   - `dotnet format --verify-no-changes`: clean. The known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it and don't fix it here.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-015-routing-orchestration.md`, tick its checklist items, and add a short **Result** line (test counts and any deviation).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - seeded data does not give the §6.2 / AC1–AC3 results (for example ORD-001 is not `SUP-0810`, or ORD-002 is not `SUP-0375` + `SUP-042`);
   - an architecture test (`LayerDependencyTests`, `ReadPathBoundaryTests`) objects to the moved port or to the new types;
   - `RouteOpenApiTests` or other existing tests depend on the placeholder in a way that goes beyond the one 500 test;
   - a supplier comes out with different modes for different categories of one order.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-015-routing-orchestration`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-015-routing-orchestration.md` with this plan.
- [x] Confirm the layering decision is recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so there is nothing to test.

**Result:** Plan saved. The layering decision is recorded: all routing rules live in the Domain `OrderRoutingService`, and `IPlanSearchService` becomes a Domain port. No code changed, so no tests were run.

---

## Phase 1: Domain pipeline (`OrderRoutingService`, `RoutingDecision`, port move) ✅ Done

- [x] Run `ddd-implementation` §1 and §2 (concepts: order line, category demand, candidate, plan, decision; no aggregate or persistence; rules stay apart from state).
- [x] Move `IPlanSearchService` to `Domain/Routing/PlanSearch/` (same signature). `PlanSearchService`, `Program.cs` and the existing tests only change `using`.
- [x] Add `OrderToRoute`, `OrderLine`, `RoutedLine`, `RoutingDecision` (with its invariant) and `UnfulfilledLine.UnknownProductCode`.
- [x] Add `OrderRoutingService`, following the steps and fail-safe rules in *Design choices*.
- [x] Tests (`OrderRoutingServiceTests`, `RoutingDecisionTests`) cover:
  - all lines routed by one supplier;
  - two-supplier plan with routed lines in request order;
  - duplicate and same-category lines → one demand, same supplier, separate routed lines (DEV-10);
  - one unknown line under control → `NotRouted`, a single `UNKNOWN_PRODUCT` line;
  - all unknown → the search is never called and no tie-break happens;
  - ineligible category → `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` / `NO_SUPPLIER_FOR_CATEGORY` for each of its lines;
  - an unrated supplier gets 0 effective rating via the policy;
  - band and `MaxExtraShipments` passed into the request;
  - a full tie uses the random source, and no tie does not;
  - empty plans despite demand → `InvalidOperationException`;
  - invariant violations of `RoutingDecision`.
- [x] `LayerDependencyTests` still green.

**Verify:** build, test, format.

**Result:** 1044 tests pass; build clean; format shows only the known `Program.cs` import-order issue. Deviation: `Program.cs` can no longer name Domain types (`LayerDependencyTests` forbids Api→Domain), so the Domain-typed DI registrations go through a new Application extension, `AddOrderRouterRouting` (`RoutingServiceCollectionExtensions`); `SystemRandomSource` is registered from an Infrastructure extension in Phase 3. The `IPlanSearchService` `using` changes in the existing tests were needed in `PlanSearchConfigurationTests` only.

---

## Phase 2: Application use case and response mapping ✅ Done

- [x] Add `RouteOrderUseCase` (batch resolve once, load suppliers once and skip it when nothing is known, `AllControl` policy, call the domain service, map the result).
- [x] Add `RouteResponseMapper` (rules in *Design choices*).
- [x] Add `RoutingOptions` and `RoutingOptionsValidator` (`NationwideZipRange`, default `00100-99999`).
- [x] Tests:
  - `RouteOrderUseCaseTests` with fake ports: the resolver is called once; a code missing from the resolver result counts as unknown; the reader is not called when everything is unknown; the reader is called with the distinct known categories.
  - `RouteResponseMapperTests`: grouping by supplier and request order (AC4); `feasible` true only for `routed`, for all three statuses (AC7); `category` null for unknown products; one error per unfulfilled item that matches its `message`; empty `routing` when not routed; `warnings` empty and never null.
  - `RoutingOptionsValidatorTests`: valid, malformed and empty ranges.

**Verify:** build, test, format.

**Result:** unit suite 922 pass (integration to be run in Phase 3). Deviation: `AddOrderRouterRouting` also registers the policy factory, tie-breaker, `SupplierEligibility` (from `RoutingOptions`), `OrderRoutingService` and the scoped use case, so `Program.cs` stays free of Domain types.

---

## Phase 3: Wire the API and end-to-end tests (AC1–AC8) ✅ Done

- [x] In `Program.cs`, register the services listed under *DI*, bind `RoutingOptions` with `ValidateOnStart`, and replace the placeholder registration. Add the `Routing` section to `appsettings.json`.
- [x] Delete `RoutingNotAvailableUseCase`. Replace its 500 test with a throwing-fake test that keeps the no-internals assertions. Update the `RouteApiFactory` doc and the `.http` comment.
- [x] Add the routed and not-routed 200 examples to `RouteOpenApiTransformer` and to `RouteOpenApiTests`.
- [x] `RouteOrderEndToEndTests`: real host on the seeded DB, run on both paths (`RouteApiFactory.Paths`). Each test asserts 200 and `feasible == (status == "routed")`.
  - AC1: ORD-001 → `routed`, one shipment from `SUP-0810`, all items `local`.
  - AC2: ORD-002 → `SUP-0375` (hospital bed, patient lift, commode) and `SUP-042` (BP monitor), all `local`.
  - AC3: ORD-003 → one shipment from `SUP-023` or `SUP-065`, all items `mail`.
  - AC4: each item has `product_code`, `quantity`, `category`, `fulfillment_mode`, in request order within its supplier.
  - AC5: one unknown code → `not_routed`, empty `routing`, the item in `unfulfilled_items` with `UNKNOWN_PRODUCT`, and one matching `errors` line.
  - AC6: migrated empty DB (`ThrowawayDatabase` + migrate) → 200, `feasible:false`, every item `UNKNOWN_PRODUCT`.
  - AC8: a single-line order, and two lines with the same `product_code` → both routed as separate lines from the same supplier.
- [x] OR-006 follow-up: after a routing POST, the reference-table row counts and `xmin` are unchanged (read only).
- [x] Manual check: `docker compose up -d --build --wait`, then `curl` ORD-001 with the dev key → 200 routed body.

**Verify:** build, test, format.

**Result:** 1084 tests pass (unit + integration, PostgreSQL running); build 0 warnings; format clean apart from the known `Program.cs` import order. Manual check: `docker compose up -d --build --wait` then `curl` ORD-001 → 200, `routed`, `SUP-0810`, both items `local`. Seeded data matched AC1–AC3 with no surprises. Deviations: the OpenAPI 200 response uses two named `examples` (`routed`, `not_routed`) instead of one `example`, and `RouteOpenApiTests` was updated to match; `RouteApiFactory` gained a `ThrowingRouteUseCase`; DI for `SystemRandomSource` is in a new Infrastructure extension `AddOrderRouterRandomness`. The AC "no-supplier-in-area" case is covered end to end with ZIP 00050. The only existing tests changed: the `IPlanSearchService` `using` (Phase 1), the placeholder-500 test, and the OpenAPI example assertion.

---

## Phase 4: Docs and post-implementation review ✅ Done

- [x] ADR-0001: add a one-line note that `OrderRoutingService.Route(OrderToRoute, suppliers, RoutingPolicy) → RoutingDecision` is pure and that `IPlanSearchService` is a Domain port implemented in Application (OR-015). Make the diagram signature match.
- [x] Update the OR-002 plan note: the `NationwideZipRange` binding is done in OR-015 (OR-010 follow-up).
- [x] Run the `ddd-implementation` §5 review and report it honestly:
  - **met:** rules in the Domain, Application only orchestrates I/O, DIP via ports, `feasible` derived, fail safe, collections never null, one batch lookup;
  - **N/A, with reasons:** aggregates, domain events, money, persistence, authorization;
  - **not verified:** decision log (OR-021), storage and 201 (OR-027), treatment arms, coverage % if it was not measured.
- [x] Record follow-ups (don't do them here):
  - OR-016 replaces `ExperimentAssignment.AllControl` in `RouteOrderUseCase`;
  - OR-020 fills `warnings`;
  - OR-021 logs `RoutingDecision`, including `TieBreak`;
  - OR-022 exposes `SearchMode`;
  - OR-027 switches to 201 + `Location` and updates this ticket's 200 assertions;
  - OR-023 reuses `RouteOrderEndToEndTests` patterns.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** ADR-0001 and OR-002 notes added. Final run: `dotnet build` 0 errors / 0 warnings; `dotnet test` 1084 passed; `dotnet format --verify-no-changes` reports only the known `Program.cs` import order.

**§5 review**
- **Met:** all routing rules live in the Domain `OrderRoutingService`; the use case only does I/O, builds the policy and maps; DIP through ports (`IPlanSearchService`, `ICapabilityResolver`, `IReferenceDataReader`, `ITieBreaker`); `feasible` is derived, never set; ambiguous or impossible states throw (no plan despite demand, two modes for one supplier, unknown reason code); collections never null; one batch lookup per request and no supplier read when nothing is known; the layer rule holds (Api references only Application and Infrastructure).
- **N/A:** aggregates (no new one; `RoutingDecision` is an immutable result), domain events, money, persistence writes, authorization (unchanged).
- **Not verified:** decision log (OR-021), storage and 201 (OR-027), treatment arms (OR-017–019), code coverage (not measured).

**Follow-ups (not done here)**
- OR-016 replaces `ExperimentAssignment.AllControl` in `RouteOrderUseCase`.
- OR-020 fills `warnings`.
- OR-021 logs `RoutingDecision`, including `TieBreak`.
- OR-022 exposes `SearchMode`.
- OR-027 switches to 201 + `Location` and updates the 200 assertions in `RouteOrderEndToEndTests`.
- OR-023 reuses the `RouteOrderEndToEndTests` patterns.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-015-routing-orchestration.md`; the Domain, Application and test files in *Target structure* |
| Moved | `src/OrderRouter.Application/Routing/PlanSearch/IPlanSearchService.cs` → `src/OrderRouter.Domain/Routing/PlanSearch/` |
| Modified | `src/OrderRouter.Api/Program.cs`, `appsettings.json`, `OrderRouter.Api.http`, `Routing/RouteOpenApiTransformer.cs`; `src/OrderRouter.Domain/Routing/Policies/UnfulfilledLine.cs`; `tests/.../Routing/RouteAuthTests.cs`, `RouteApiFactory.cs`, `RouteOpenApiTests.cs`; ADR-0001 (note); `Plans/OR-002-domain-reference-model.md` (note) |
| Deleted | `src/OrderRouter.Api/Routing/RoutingNotAvailableUseCase.cs` |
| Reused | `SupplierEligibility`, `UnfulfillableReason.Code()` (`Domain/Eligibility`); `ICapabilityResolver`, `IReferenceDataReader`, `CapabilityResolution` (`Domain/ReferenceData`); `PlanSearchRequest`, `CategoryDemand`, `SupplierCandidate`, `PlanSearcher` (`Domain/Routing/PlanSearch`); `RoutingPolicyFactory`, `RoutingOutcome`, `UnfulfilledLine` (`Domain/Routing/Policies`); `ExperimentAssignment.AllControl`; `UniformRandomTieBreaker`, `SystemRandomSource`; `RouteResponse` and contracts (`Application/Routing/Contracts`); `RouteApiFactory`, `ThrowawayDatabase`, `ReferenceDataFixture`, `FixedRandomSource`, `PlanSearchTestData` |
| Unchanged | Migrations, Infrastructure persistence, `Docs/Data/**`, policy classes, `PlanSearcher` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: ORD-001 → `SUP-0810`, local | `RouteOrderEndToEndTests` | 3 |
| AC2: ORD-002 → `SUP-0375` + `SUP-042` | `OrderRoutingServiceTests` (two-supplier), `RouteOrderEndToEndTests` | 1, 3 |
| AC3: ORD-003 → `SUP-023` or `SUP-065`, mail | `RouteOrderEndToEndTests` (membership) | 3 |
| AC4: response shape and order | `RouteResponseMapperTests`, `RouteOrderEndToEndTests` | 2, 3 |
| AC5: unknown product (control) | `OrderRoutingServiceTests`, `RouteResponseMapperTests`, `RouteOrderEndToEndTests` | 1–3 |
| AC6: empty database → 200, all `UNKNOWN_PRODUCT` | `RouteOrderUseCaseTests` (no reader call), `RouteOrderEndToEndTests` (`ThrowawayDatabase`) | 2, 3 |
| AC7: `feasible` only when `routed` | `RouteResponseMapperTests` (all statuses), and asserted in every end-to-end test | 2, 3 |
| AC8: single line and duplicate lines | `OrderRoutingServiceTests`, `RouteOrderEndToEndTests` | 1, 3 |

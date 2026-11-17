# Plan: OR-017: EXP-1 treatment: allow one extra shipment for much better quality

**Ticket:** [Docs/Tickets/OR-017-exp1-bounded-tradeoff.md](../Docs/Tickets/OR-017-exp1-bounded-tradeoff.md)
**Depends on:** OR-016 (done: assigner, `ExperimentArms.Exp1ConsolidationVsQuality`, `RoutingParameters.QualityUpliftThreshold`, `experiments.json` already carries `Exp1ConsolidationVsQuality.Parameters.QualityUpliftThreshold`). OR-013 (done: `IPlanSelectionPolicy`, `StrictPriorityPolicy`, `RoutingPolicyFactory`). OR-011/OR-012 (done: `PlanSearcher` already honours `MaxExtraShipments`).
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (seams, composition); [ADR-0003](../Docs/ADRs/ADR-0003-plan-search-algorithm.md) (the band applies within each shipment count, never across counts); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md) (parameters, reload); Technical Requirements §7.3, §8.1 EXP-1, SC-01, OQ-10, AB-03, AB-06; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds / related:** OR-021 (decision log already records the arm), OR-024 (combination matrix), OR-025 (README).

> **Save location:** this file is `Plans/OR-017-exp1-bounded-tradeoff.md`. Update it as each phase is done.

## Context

EXP-1 control (`StrictPriorityPolicy`) always picks the fewest shipments, so a better-rated plan with one more shipment can never win. SC-01 asks to test that trade-off. OR-017 adds the treatment arm, `BoundedTradeOffPolicy`:
- `MaxExtraShipments = 1`, so the plan search also returns plans with one more shipment than the minimum;
- the extra-shipment plan wins only when its quality is at least `quality_uplift_threshold` (default 2.0) better than the plan strict priority would pick;
- otherwise it behaves exactly like strict priority.

Most of the plumbing already exists:
- `PlanSearcher` explores `minimum + MaxExtraShipments`, and applies the similarity band within each shipment count (ADR-0003). The search already returns the minimum-shipment plans the policy must compare against.
- `OrderRoutingService` already passes `policy.Selection.MaxExtraShipments` into `PlanSearchRequest` and calls `policy.Selection.SelectBest(search.Plans)` before the tie-break.
- `RoutingParameters.QualityUpliftThreshold` (validated `> 0`, default 2.0) is read from the reloadable `experiments.json` by `ExperimentsOptionsMapper` and arrives in `ExperimentAssignment.Parameters`.
- `RoutingPolicyFactory` throws `NotSupportedException` for the EXP-1 treatment arm. This is the last treatment throw; its remark says the treatment ticket replaces it.

So the change is one new Domain class, a small shared ranking helper, one factory line, and tests. No Application, Infrastructure or Api production change is expected. AC4 (threshold from config) needs a test that proves the next request gets the new threshold, not new reload code.

Out of scope: OR-018/019 (done); the decision log fields (OR-021); the combination matrix (OR-024); README (OR-025); a weighted-score EXP-1 variant (the requirements say it would be a later, separate strategy).

### Decision confirmed with the engineer

| Topic | Decision |
|---|---|
| What the uplift is measured against | **The plan strict priority would pick** (fewest shipments, then the similarity band, then most local, then highest quality). The best extra-shipment plan wins when `its quality − that plan's quality ≥ QualityUpliftThreshold`. Not the highest quality among minimum-shipment plans: the ticket says "otherwise it behaves like strict priority", so the plan we would have shipped anyway is the one to beat. |

### Design choices (from the ticket, ADRs and the code; no question needed)

| Topic | Choice and reason |
|---|---|
| Layer | **Domain only** (pure, no I/O, no configuration). The policy lives in `src/OrderRouter.Domain/Routing/Policies/`. |
| New type | `BoundedTradeOffPolicy : IPlanSelectionPolicy`, immutable. Constructor `(decimal similarityBand, decimal qualityUpliftThreshold)`. It validates `similarityBand ≥ 0` and `qualityUpliftThreshold > 0`, and reports both errors in one `DomainValidationException` (the `RoutingParameters` pattern). `MaxExtraShipments => 1`. |
| Selection rule | 1. Empty input → empty. 2. `min` = the lowest shipment count. 3. **Baseline** = the strict-priority pick among plans with `min` shipments. 4. **Challenger** = the same ranking (band → most local → highest quality) among plans with `min + 1` shipments. 5. No challenger → baseline. 6. `challenger.Quality − baseline.Quality ≥ QualityUpliftThreshold` (inclusive, "at least") → return the challenger set; otherwise the baseline set. Plans with `min + 2` or more shipments are ignored (AC3 defence in depth: the search never returns them, and the policy never picks them even if a caller passes them). Returns every plan still tied, so the tie-break (OR-014) picks fairly. |
| Why the band is applied inside each group | ADR-0003 and §7.2: the band applies within each shipment count, never across counts. The same helper ranks both groups, so control and treatment cannot drift apart. |
| Shared ranking | Extract the keys 2–4 logic that `StrictPriorityPolicy.SelectBest` has today into one internal helper (for example `PlanRanking.BestWithinSize(plans, band)`), and have both policies call it. `StrictPriorityPolicy` behaviour must not change; its existing tests are the guard. If the extraction would change any existing result, stop and ask. |
| Factory | Replace the EXP-1 `Treatment` throw with `new BoundedTradeOffPolicy(assignment.Parameters.SimilarityBand, assignment.Parameters.QualityUpliftThreshold)`. Per request, so AC4 holds (ADR-0001: policies never capture configuration). After this, no arm throws `NotSupportedException`; remove the unused `NotAvailable` helper, the `<exception>` doc line and the stale remarks, and update every test that listed the EXP-1 treatment as throwing. |
| Fail safe | Ambiguity never advances toward a worse outcome: with no challenger, or a challenger under the threshold, the order gets the control (fewest-shipment) plan. A bounded policy can add at most one shipment (BR-12, SC-01). |
| Tests | xUnit v3, `MethodName_Condition_ExpectedResult`, nearby style, no Arrange/Act/Assert comments. Reuse `PlanSearchTestData` and the sentinel ids used in `StrictPriorityPolicyTests`. Target ≥ 85% line coverage of the new code (measure it, or say it was not measured). |

### Target structure

```
src/OrderRouter.Domain/Routing/Policies/
  BoundedTradeOffPolicy.cs      new: EXP-1 treatment
  PlanRanking.cs                new (internal): keys 2-4 within one shipment count, shared
  StrictPriorityPolicy.cs       modified: calls PlanRanking (behaviour unchanged)
  RoutingPolicyFactory.cs       modified: EXP-1 treatment -> BoundedTradeOffPolicy; remove throw helper
tests/OrderRouter.Tests/Domain/Routing/Policies/
  BoundedTradeOffPolicyTests.cs new
  RoutingPolicyFactoryTests.cs  modified
  StrictPriorityPolicyTests.cs  unchanged (guards the refactor)
tests/OrderRouter.Tests/Domain/Routing/
  (a service-level EXP-1 test next to OrderRoutingServiceTests / ColdStartRoutingTests)
tests/OrderRouter.Api.IntegrationTests/Routing/
  (host test for AC4 reload, extending the OR-016 temp-file pattern)
Docs/ADRs/ADR-0001, ADR-0005   implementation note (OR-017)
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. Run `ddd-implementation` §1 domain analysis and §2 architecture review at the start of Phase 1 (concepts: plan quality, shipment count, similarity band, uplift threshold, selection policy; no aggregate, no persistence, no domain event; Domain only). If the skill's steps are skipped as a small change behind an existing seam, say so in the phase result. Run the §5 post-implementation review in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, `decimal` for quality and thresholds, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML docs on public types and members (with `<example>` where useful). Comments explain *why* and cite the requirement or ADR (`// SC-01`, `// ADR-0003`, `// BR-12`).
   - **Compliance:** CLAUDE.md "Never let an order bypass a compliance gate". The policy only chooses between plans that are already eligible; it never changes eligibility, and it never routes an unfulfilled line.
   - **No configuration in policies:** parameters come only from `ExperimentAssignment.Parameters`.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run these from the repo root. Start PostgreSQL first with `docker compose up -d postgres` so integration tests run too.
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: everything passes, including every existing test. The only intended changes to existing tests are the `RoutingPolicyFactoryTests` cases that expect the EXP-1 treatment to throw (Phase 2). Record any other change in the phase result.
   - `dotnet format --verify-no-changes`: clean. The known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it and don't fix it here.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `✅ Done` to `✅ Done` in `Plans/OR-017-exp1-bounded-tradeoff.md`, tick its checklist items, and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - extracting `PlanRanking` changes any existing `StrictPriorityPolicy`, plan-search or oracle test result;
   - an AC can only pass by changing plan search (for example `PlanSearcher` does not return the minimum-shipment plans together with the `+1` plans when the `+1` plan is better by more than the band);
   - the seeded data has no order where a `+1` shipment plan exists with a measurable uplift, so an end-to-end test cannot be written without changing `Docs/Data/*` (never edit it; ask instead);
   - an architecture test (`LayerDependencyTests` etc.) objects to the new type;
   - enabling the treatment arm changes any existing test that relied on the factory throwing.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-017-exp1-bounded-tradeoff`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-017-exp1-bounded-tradeoff.md` with this plan.
- [x] Record the engineer's decision (uplift baseline) under *Decision confirmed with the engineer*.

**Verify:** the file exists. No code changes, so there is nothing to test.

**Result:** Plan saved. Decision recorded: the uplift is measured against the plan strict priority would pick. No code changed, so no tests were run.

---

## Phase 1: `BoundedTradeOffPolicy` and the shared ranking (AC1, AC2, AC3, AC5) ✅ Done

- [x] `ddd-implementation` §1 and §2 (see Working rules).
- [x] Add the internal `PlanRanking` helper (keys 2–4 within one shipment count) and make `StrictPriorityPolicy.SelectBest` call it. Behaviour unchanged.
- [x] Add `BoundedTradeOffPolicy` as described in *Design choices*: constructor validation (all errors at once), `MaxExtraShipments = 1`, selection rule, XML docs with the worked numbers from the ticket.
- [x] Tests (`BoundedTradeOffPolicyTests`):
  - AC1: 1-shipment plan at 6.0, 2-shipment plan at 9.0, threshold 2.0 → the 2-shipment plan.
  - AC2: 1-shipment plan at 8.0, 2-shipment plan at 9.0 → the 1-shipment plan.
  - AC3: add a 3-shipment plan at 10.0 → it is never returned, whatever its quality, with or without the 2-shipment plan present; with only 1- and 3-shipment plans the 1-shipment plan wins.
  - AC5: the control `StrictPriorityPolicy` with the same plans (6.0 / 9.0) → the 1-shipment plan; and `MaxExtraShipments` is 0 for control and 1 for treatment.
  - Threshold edges: uplift exactly equal to the threshold wins (inclusive); just under loses (for example 1.99 vs 2.0); a custom threshold flips AC1 and AC2 outcomes (threshold 4.0 makes the 6.0 / 9.0 case lose; 0.5 makes 8.0 / 9.0 win).
  - Baseline rule (the engineer's decision): a minimum-shipment local plan at 8.7 and a mail plan at 9.0 (band 0.5) → the baseline is the local plan; the challenger must beat 8.7, not 9.0 (a challenger at 10.8 wins, one at 10.6 does not with threshold 2.0).
  - Band applies within each group: inside the `+1` group the same band/local/quality ranking picks the challenger; a lower-quality `+1` plan is dropped before the uplift comparison.
  - Ties: tied baseline plans are all returned (input for OR-014); tied challengers are all returned; input order kept.
  - Only the minimum-shipment plans present (no challenger) → the strict result; empty → empty; null → `ArgumentNullException`.
  - Constructor: negative band, threshold 0 and negative threshold rejected, both errors in one exception; band 0 and a tiny positive threshold accepted.
- [x] Existing `StrictPriorityPolicyTests`, plan-search and oracle tests stay green and unchanged.

**Verify:** build, test, format (working rule 2).

**Result:** Implemented `PlanRanking`, `BoundedTradeOffPolicy` and `BoundedTradeOffPolicyTests` (plus a real-search test). `ddd-implementation` §1/§2 skipped as a small change behind an existing seam. Existing strict-priority, plan-search and oracle tests unchanged and green. No deviations.

---

## Phase 2: Factory wiring and threshold per request (AC4, AC5) ✅ Done

- [x] `RoutingPolicyFactory`: the EXP-1 `Treatment` arm builds `BoundedTradeOffPolicy` from `assignment.Parameters.SimilarityBand` and `QualityUpliftThreshold`. Remove the unused `NotAvailable` helper, the `<exception cref="NotSupportedException">` line and the stale remarks (all three treatment arms now exist).
- [x] Update `RoutingPolicyFactoryTests`: EXP-1 treatment → `BoundedTradeOffPolicy` with `MaxExtraShipments == 1`; no treatment arm throws any more; the other arms and the undefined-arm / null behaviour are unchanged.
- [x] Tests for AC4 at the factory level: two assignments that differ only in `QualityUpliftThreshold` give two independent policies, and the same plans flip between them (no captured or shared state); the band from `Parameters.SimilarityBand` also reaches the policy.
- [x] Service-level test with a real `OrderRoutingService`, a stub plan search that returns 1- and 2-shipment plans, and `AllControl` vs EXP-1 treatment (AC1, AC2 and AC5 through the pipeline, including the `MaxExtraShipments` the service sends to the search: 0 for control, 1 for treatment).
- [x] `LayerDependencyTests` and other architecture tests still green.

**Verify:** build, test, format. Existing tests that assumed the throw are the only expected changes.

**Result:** Factory builds the policy per request; unused `NotAvailable` helper and stale docs removed. `RoutingPolicyFactoryTests` rewritten for EXP-1 (the only changed existing test); added `BoundedTradeOffRoutingTests` (service level, real search). Unit tests 1188 passing.

---

## Phase 3: Real host, real search, reload (AC4, AC5 end to end) ✅ Done

- [x] Look in `Docs/Data/Seed` (read only) for a sample or sentinel order where a minimum-shipment plan and a `+1` plan both exist with a quality gap above and below 2.0. If none exists, **stop and ask** (working rule 5); never edit `Docs/Data/*`.
- [x] Plan-search-level test with real `PlanSearcher` and `PlanSearchTestData`: with `MaxExtraShipments = 1` the search returns both the minimum and `+1` plans (including the case where the `+1` plan is better than the minimum plan by more than the band, ADR-0003), then `BoundedTradeOffPolicy` picks per AC1 and AC2.
- [x] Host test (extend the OR-016 pattern, `RouteApiFactory` with a temp `experiments.json` and `reloadOnChange`; real use case, real factory, no recording factory): EXP-1 enabled at 100%.
  - Treatment arm, uplift above the threshold → the response routes the `+1` plan.
  - Edit the file in place, raising `QualityUpliftThreshold` above that uplift → the **next** request routes the strict-priority plan, with no restart (AC4). Poll with a timeout; no fixed sleep.
  - Control arm (experiment disabled) with the same order → the fewest-shipment plan (AC5).
  - Invalid threshold (`0` or negative) written to the file → rejected by the OR-016 validator, last good config kept (existing behaviour; assert only that the treatment request still routes under the previous threshold).
- [x] An existing OR-016 host test that used the recording factory for EXP-1 treatment stays green; record any change.

**Verify:** build, test, format. Start PostgreSQL first.

**Result:** Seed sentinels at ZIP 00007 (SUP-T007 6.0 vs SUP-T008/T009 10.0) gave a 4.0 uplift, so no data edits. Added `BoundedTradeOffHostTests` (treatment, control, in-place threshold reload, invalid threshold keeps last good). Full `dotnet test`: 1524 passed, 0 failed.

---

## Phase 4: Docs and post-implementation review ✅ Done

- [x] ADR-0001 and ADR-0005: add an implementation note (OR-017). Cover: `BoundedTradeOffPolicy` is a stateless-per-request policy built from `Parameters`; the uplift is measured against the strict-priority pick; the policy never selects a plan with two or more extra shipments; the `+1` group is ranked with the same band logic; the known risk "EXP-1 treatment returns 500" is gone, and no treatment arm throws any more.
- [x] Run the `ddd-implementation` §5 review and report it honestly:
  - **met:** rules in the Domain, one interface per experiment (AB-06: one class and one factory line), immutable per-request policies, no configuration in the Domain, fail safe (never more than one extra shipment, control plan when unsure), AB-03 (disabled equals the written spec);
  - **N/A, with reasons:** aggregates, domain events, money, persistence, authorization;
  - **not verified:** the OR-024 combination matrix (8 combinations), the real-world effect of the 2.0 default (OQ-10 asks the stakeholder to confirm it), coverage % if it was not measured, p95 latency with `MaxExtraShipments = 1` on large mail orders (NFR-03; the search explores one more size).
- [x] Record follow-ups (don't do them here): OR-024 covers EXP-1 × EXP-2 × EXP-3; OR-025 documents `QualityUpliftThreshold` in `experiments.json`; OQ-10 confirmation of the defaults.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** ADR-0001 and ADR-0005 notes added. Review: met: Domain-only rules, one class plus one factory line (AB-06), immutable per-request policy, no configuration in Domain, fail safe, AB-03. N/A: aggregates, events, money, persistence, authorization. Not verified: OR-024 combination matrix, effect of the 2.0 default (OQ-10), coverage % (not measured), p95 latency with `MaxExtraShipments = 1` on large mail orders (NFR-03). Follow-ups: OR-024, OR-025, OQ-10.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-017-exp1-bounded-tradeoff.md`; `src/OrderRouter.Domain/Routing/Policies/BoundedTradeOffPolicy.cs`, `PlanRanking.cs`; `tests/.../Policies/BoundedTradeOffPolicyTests.cs`; the service-level and host tests in *Target structure* |
| Modified | `src/OrderRouter.Domain/Routing/Policies/StrictPriorityPolicy.cs` (calls the shared helper), `RoutingPolicyFactory.cs`; `tests/.../Policies/RoutingPolicyFactoryTests.cs`; ADR-0001 and ADR-0005 (notes) |
| Reused | `RoutingPlan` (`ShipmentCount`, `Quality`, `LocalLineCount`); `RoutingParameters.QualityUpliftThreshold` and `SimilarityBand`; `PlanSearcher` (`MaxExtraShipments`); `OrderRoutingService`; `DomainValidationException` and the all-errors-at-once pattern; `PlanSearchTestData`; `RouteApiFactory` and the OR-016 temp-file host-test pattern |
| Unchanged | `PlanSearcher`, `OrderRoutingService`, Application, Infrastructure, Api, `experiments.json` (already carries the threshold), migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: big uplift wins (6.0 vs 9.0) | `BoundedTradeOffPolicyTests`; service-level test; host test | 1, 2, 3 |
| AC2: small uplift loses (8.0 vs 9.0) | `BoundedTradeOffPolicyTests`; service-level test; host test | 1, 2, 3 |
| AC3: never two extra | `BoundedTradeOffPolicyTests` (3-shipment plan ignored); the search never returns it | 1, 3 |
| AC4: threshold from config, next request | Factory test (independent per-request policies); host test with in-place edit of `experiments.json` | 2, 3 |
| AC5: control unchanged | `StrictPriorityPolicy` with the same plans; `MaxExtraShipments` 0; host test with the experiment disabled | 1, 2, 3 |

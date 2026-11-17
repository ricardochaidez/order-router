# Plan: OR-013: Add the routing policy seams and the control arms

**Ticket:** [Docs/Tickets/OR-013-routing-policies-control.md](../Docs/Tickets/OR-013-routing-policies-control.md)
**Depends on:** OR-011 / OR-012 (done: `PlanSearcher`, `RoutingPlan`, `SupplierCandidate`, `PlanScoring` in `src/OrderRouter.Domain/Routing/PlanSearch/`).
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (policy seams, composition); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md) (`ExperimentAssignment` shape); Technical Requirements §7.3, §8.1, FR-11, FR-12, FR-14, AB-03, AB-06; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-014 (`ITieBreaker` picks from `SelectBest`'s tied list), OR-015 (`OrderRoutingService` uses `RoutingPolicy`, all-control assignment), OR-016 (assigner produces `ExperimentAssignment`), OR-017 / OR-018 / OR-019 (treatment arms replace the factory's `NotSupportedException`).

> **Save location:** Phase 0 copies this plan to `Plans/OR-013-routing-policies-control.md`. Update that file as each phase is done.

## Context

ADR-0001 composes routing behaviour from one policy interface per experiment, so each A/B arm can be swapped without touching the routing core (AB-06). This ticket creates the three seams and their **control** arms, which together reproduce the spec as written (AB-03):

| Seam | Control arm | Behaviour |
|---|---|---|
| `IEffectiveRatingPolicy` (EXP-2) | `UnratedLastPolicy` | Unrated supplier → effective rating 0 (still eligible) |
| `IPlanSelectionPolicy` (EXP-1) | `StrictPriorityPolicy` | Fewer shipments, then the similarity band → more local lines → higher quality (§7.3, DEV-04). `MaxExtraShipments = 0` |
| `IUnfulfillablePolicy` (EXP-3) | `AllOrNothingPolicy` | Any unfulfillable line → nothing routed, `not_routed` |

Plus the `RoutingPolicy` record and `IRoutingPolicyFactory`, which builds a policy per request from an `ExperimentAssignment`. Policies never read configuration.

Out of scope: tie-break and `IRandomSource` (OR-014), `OrderRoutingService`, use case, DI wiring and response mapping (OR-015), the assigner and `experiments.json` (OR-016), treatment arms (OR-017/018/019), decision log (OR-021).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Effective rating 0 vs `SupplierCandidate` (it rejects anything outside 1–10) | **Widen `SupplierCandidate.EffectiveRating` to 0–10.** The raw `Rating` value object stays 1–10. Update the existing OR-011 test that rejects 0 (intentional change; note it in the phase result). |
| How parameters reach a policy | **Per-request immutable instances.** The factory builds them from `Assignment.Parameters`, e.g. `new StrictPriorityPolicy(similarityBand)`. ADR signatures stay as written (`SelectBest(plans)`, `EffectiveRating(supplier)`). Arms with no parameters (`UnratedLastPolicy`, `AllOrNothingPolicy`) are shared instances. "Stateless" means no mutable state. |
| `ExperimentAssignment` scope | **Arms + Parameters only:** `ExperimentArms` (EXP-1/2/3 → `Control` / `Treatment`), `RoutingParameters` (band 0.5, uplift 2.0, prior 6.5), `static AllControl`. OR-016 adds `ConfigVersion` and the exposure reasons. |
| Treatment arm requested before it exists | **Factory throws `NotSupportedException`** naming the experiment (fail safe: no order is routed under a mislabelled arm). OR-017/018/019 each replace their throw with the new policy. |

### Design choices (from the ticket, ADR and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Layer | All new types are **Domain** (pure, no I/O, no config): ADR-0001 calls them domain interfaces. Experiment types in `Domain/Routing/Experiments/`, policies in `Domain/Routing/Policies/`. No Application, Infrastructure or Api changes. |
| Plan type | ADR's `CandidatePlan` = the existing `RoutingPlan` (reuse; no new type). |
| `StrictPriorityPolicy.SelectBest` | Empty input → empty. Keep plans with the minimum `ShipmentCount` (key 2). Among them, best quality *Q*; keep quality ≥ *Q* − band; then most `LocalLineCount`; then highest `Quality`. Returns **every** plan still tied (key 5 belongs to OR-014's tie-breaker). Band validated ≥ 0. Implementation follows §7.3 "find Q, keep ≥ Q − band, prefer most-local, then highest quality" because the band is not transitive. |
| Plan quality (AC6) | Already computed line-weighted by `PlanScoring.Score` (OR-011, FR-12: each order line counts once). Not re-implemented; AC6 is proved through `PlanSearcher`, including a case where a 0 effective rating counts in the mean. |
| Unfulfillable inputs/outputs | New domain record `UnfulfilledLine(string ProductCode, int Quantity, Category? Category, string ReasonCode)` (mirrors the v1 contract; the Domain can't reference `Application.Contracts`). New `RoutingOutcome(RoutingOutcomeStatus Status, RoutingPlan? Plan, IReadOnlyList<UnfulfilledLine> Unfulfilled)` with `RoutingOutcomeStatus { Routed, PartiallyRouted, NotRouted }`. Named differently from the Application `RoutingStatus` to avoid a clash; OR-015 maps it. Invariant: `Routed` ⇔ plan present and no unfulfilled lines. |
| `AllOrNothingPolicy.Apply(plan, unfulfilled)` | Any unfulfilled line → `NotRouted`, plan dropped, lines returned unchanged. No plan (null) → `NotRouted` (fail safe). Otherwise `Routed`. Never `PartiallyRouted`. |
| `RoutingParameters` validation | `SimilarityBand ≥ 0`, `QualityUpliftThreshold > 0`, `UnratedPrior` 1–10. All errors in one `DomainValidationException` (same pattern as `PlanSearchRequest`). `static Default` = (0.5, 2.0, 6.5). Defaults match `PlanSearchRequest.DefaultSimilarityBand` (reuse that constant). |
| Factory | `RoutingPolicyFactory : IRoutingPolicyFactory`, stateless, Domain. `switch` per arm; an undefined enum value → `ArgumentOutOfRangeException`; `Treatment` → `NotSupportedException`. `RoutingPolicy` keeps the `Assignment` for the decision log (AB-04). |
| Tests | xUnit v3; `MethodName_Condition_ExpectedResult`; follow nearby style; no Arrange/Act/Assert comments. Reuse `PlanSearchTestData` (`tests/OrderRouter.Tests/Domain/Routing/PlanSearch/`). Sentinel ids (`SUP-T007` etc.) used in test data for readability. Target ≥ 85% coverage of new code (measure it, or say it was not measured). |

### Target structure

```
src/OrderRouter.Domain/Routing/Experiments/
  ExperimentArm.cs            Control | Treatment
  ExperimentArms.cs           Exp1ConsolidationVsQuality, Exp2UnratedSuppliers, Exp3PartialRouting; AllControl
  RoutingParameters.cs        SimilarityBand, QualityUpliftThreshold, UnratedPrior; validated; Default
  ExperimentAssignment.cs     Arms, Parameters; AllControl
src/OrderRouter.Domain/Routing/Policies/
  IEffectiveRatingPolicy.cs, UnratedLastPolicy.cs
  IPlanSelectionPolicy.cs, StrictPriorityPolicy.cs
  IUnfulfillablePolicy.cs, AllOrNothingPolicy.cs
  UnfulfilledLine.cs, RoutingOutcome.cs, RoutingOutcomeStatus.cs
  RoutingPolicy.cs, IRoutingPolicyFactory.cs, RoutingPolicyFactory.cs
src/OrderRouter.Domain/Routing/PlanSearch/SupplierCandidate.cs   (modified) effective rating 0–10
tests/OrderRouter.Tests/Domain/Routing/Experiments/
  RoutingParametersTests.cs, ExperimentAssignmentTests.cs
tests/OrderRouter.Tests/Domain/Routing/Policies/
  UnratedLastPolicyTests.cs, PlanQualityTests.cs, StrictPriorityPolicyTests.cs,
  AllOrNothingPolicyTests.cs, RoutingOutcomeTests.cs, RoutingPolicyFactoryTests.cs
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is new domain behaviour, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–5; §5 post-implementation review in Phase 6.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, records for value objects, `decimal` for ratings, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members; comments explain *why* and cite the requirement or ADR (`// FR-12`, `// ADR-0001`).
   - **Fail safe:** ambiguous input never advances an order (no plan → `NotRouted`; unknown arm → exception, never a guess).
   - **No configuration in policies:** parameters come only from `ExperimentAssignment.Parameters`.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL with `docker compose up -d postgres` so the integration tests run too):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test (OR-011/OR-012 plan-search and oracle tests stay green; the only intended change to an existing test is the `SupplierCandidate` 0-rating case in Phase 1).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it, don't fix it here).
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-013-routing-policies-control.md`, tick its checklist items, and add a short **Result** line (test counts and any deviation).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - widening `SupplierCandidate` to 0 changes an OR-011/OR-012 result other than the one intended test;
   - an AC can only pass by changing plan search (e.g. the band in `PlanSearcher` and in `StrictPriorityPolicy` disagree);
   - `UnfulfilledLine` turns out not to fit what OR-015 needs (reason codes such as `UNKNOWN_PRODUCT`);
   - an architecture test (`LayerDependencyTests` etc.) objects to the new namespaces.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-013-routing-policies-control`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-013-routing-policies-control.md` with this plan.
- [x] Confirm the four decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so nothing to test.

**Result:** Plan saved. The four decisions (widen the effective rating to 0–10, per-request policy instances, an assignment with arms and parameters only, the factory throws for treatment arms) are recorded. No code changed, so no tests were run.

---

## Phase 1: Experiment assignment types and effective-rating range ✅ Done

- [x] `ddd-implementation` §1 and §2: concepts (experiment, arm, assignment, routing parameters, effective rating, policy seam), invariants, affected layer (Domain only), no aggregates or domain events (pure computation, stateless services).
- [x] `ExperimentArm` enum; `ExperimentArms` record with `static AllControl`.
- [x] `RoutingParameters` record: validation (all errors at once), `static Default` (0.5 / 2.0 / 6.5), band default reuses `PlanSearchRequest.DefaultSimilarityBand`.
- [x] `ExperimentAssignment(ExperimentArms Arms, RoutingParameters Parameters)` with null checks and `static AllControl`.
- [x] `SupplierCandidate`: accept effective rating 0–10; update its XML doc and validation message.
- [x] Tests: `RoutingParametersTests` (defaults; boundaries band 0, prior 1 and 10, uplift just above 0; negative band / prior 0.99 / 10.01 / uplift 0 rejected; every error reported at once); `ExperimentAssignmentTests` (`AllControl` is all control with default parameters; null arms/parameters rejected); `SupplierCandidate` tests: 0 accepted, −0.01 and 10.01 rejected.

**Verify:** build, test, format (working rule 2).

**Result:** Experiment types, `RoutingParameters`, `SupplierCandidate` 0–10 and their tests added. No existing test rejected a 0 rating (the old one used 11), so no existing test changed.

---

## Phase 2: Effective rating seam and plan quality (AC1, AC6) ✅ Done

- [x] `IEffectiveRatingPolicy { decimal EffectiveRating(Supplier supplier); }`.
- [x] `UnratedLastPolicy`: `supplier.Rating?.Value ?? 0m`; shared `static Instance`; null supplier → `ArgumentNullException`.
- [x] Tests (`UnratedLastPolicyTests`): AC1 unrated → 0; rated 6.0 / 10 → same value; rounding kept (e.g. 9.25).
- [x] Tests (`PlanQualityTests`, through `PlanSearcher` with `PlanSearchTestData`): AC6, 2 lines in one category at 9.0 + 1 line at 6.0 → quality 8.0; an unrated supplier at effective rating 0 is still routable and pulls the mean down; a rated supplier beats an unrated one for the same category.

**Verify:** build, test, format.

**Result:** `UnratedLastPolicy`, `UnratedLastPolicyTests`, `PlanQualityTests` added; AC1 and AC6 covered.

---

## Phase 3: Plan selection seam (AC2–AC5) ✅ Done

- [x] `IPlanSelectionPolicy { int MaxExtraShipments { get; } IReadOnlyList<RoutingPlan> SelectBest(IReadOnlyList<RoutingPlan> plans); }`.
- [x] `StrictPriorityPolicy(decimal similarityBand)`: `MaxExtraShipments = 0`; band ≥ 0 (else `DomainValidationException`); selection rule as in *Design choices*; returns all plans tied after keys 2–4 in input order; null list → `ArgumentNullException`; empty → empty.
- [x] Tests (`StrictPriorityPolicyTests`):
  - AC2: 1-shipment `SUP-T007` at 6.0 vs 3-shipment 10.0 specialists (`SUP-T008`–`SUP-T010`) → the 1-shipment plan.
  - AC3: same shipment count, 9.0 vs 8.0 (no local difference) → 9.0.
  - AC4: 9.0 mail vs 8.7 local, band 0.5 → local.
  - AC5: 9.0 mail vs 8.0 local, band 0.5 → 9.0 mail.
  - Edges: exactly on the band edge (8.5, band 0.5) counts as similar; band 0 → pure quality; same local count inside the band → higher quality wins; non-transitive chain (9.0 / 8.6 / 8.2, band 0.5) keeps only plans ≥ 8.5; full tie returns every tied plan (input for OR-014); empty → empty; `MaxExtraShipments == 0`.

**Verify:** build, test, format.

**Result:** `StrictPriorityPolicy` and tests added; AC2–AC5 and the edge cases covered.

---

## Phase 4: Unfulfillable seam (AC7) ✅ Done

- [x] `UnfulfilledLine`, `RoutingOutcomeStatus`, `RoutingOutcome` (factory methods or constructor enforcing the invariant: `Routed` ⇔ plan present and no unfulfilled lines; `Unfulfilled` never null).
- [x] `IUnfulfillablePolicy { RoutingOutcome Apply(RoutingPlan? plan, IReadOnlyList<UnfulfilledLine> unfulfilled); }`.
- [x] `AllOrNothingPolicy`: rules as in *Design choices*; shared `static Instance`.
- [x] Tests (`AllOrNothingPolicyTests`, `RoutingOutcomeTests`): AC7, a plan for the routable lines + one unfulfilled line → `NotRouted`, no plan, the line listed; no unfulfilled + plan → `Routed` with that plan; no plan + no lines → `NotRouted`; every line returned unchanged; `PartiallyRouted` never produced; outcome invariant violations rejected.

**Verify:** build, test, format.

**Result:** `UnfulfilledLine`, `RoutingOutcome(Status)`, `AllOrNothingPolicy` and tests added; AC7 covered.

---

## Phase 5: `RoutingPolicy` and the factory (AC8) ✅ Done

- [x] `RoutingPolicy(IEffectiveRatingPolicy Rating, IPlanSelectionPolicy Selection, IUnfulfillablePolicy Unfulfillable, ExperimentAssignment Assignment)` record (null checks).
- [x] `IRoutingPolicyFactory { RoutingPolicy Create(ExperimentAssignment assignment); }` and `RoutingPolicyFactory` (stateless; per-request `StrictPriorityPolicy` with `Parameters.SimilarityBand`).
- [x] XML doc on the factory: how a treatment ticket adds its arm (one class + one mapping, AB-06).
- [x] Tests (`RoutingPolicyFactoryTests`): AC8, `ExperimentAssignment.AllControl` → `UnratedLastPolicy`, `StrictPriorityPolicy`, `AllOrNothingPolicy`, assignment kept; a custom band reaches the selection policy (shown by AC4-style data flipping with band 0.5 vs 0.1); each experiment set to `Treatment` → `NotSupportedException` naming that experiment; undefined arm value → `ArgumentOutOfRangeException`; null → `ArgumentNullException`; two calls with different parameters give independent policies (no shared state).
- [x] `LayerDependencyTests` and other architecture tests still green.

**Verify:** build, test, format.

**Result:** `RoutingPolicy`, factory and tests added; AC8 covered; architecture tests still green.

---

## Phase 6: Post-implementation review ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (one interface per experiment, OCP/AB-06, stateless immutable policies, no configuration in the Domain, fail safe, all control = written spec AB-03), what is **N/A** and why (aggregates, domain events, money, persistence, authorization), and what was **not verified** (end-to-end routing waits for OR-015; combination matrix waits for OR-024; coverage % if not measured). Be honest.
- [x] Record follow-ups (do not do them here): OR-014 `ITieBreaker` consumes `SelectBest`'s tied list; OR-015 builds `OrderRoutingService`, registers `IRoutingPolicyFactory` in DI, applies `Rating.EffectiveRating` when building `SupplierCandidate`s, passes `Selection.MaxExtraShipments` and `Parameters.SimilarityBand` into `PlanSearchRequest`, and maps `RoutingOutcomeStatus`/`UnfulfilledLine` to the v1 contract; OR-016 adds `ConfigVersion` and reasons to `ExperimentAssignment`; OR-017/018/019 replace the factory's throws.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** Review below. Final build: 0 warnings, 0 errors; `dotnet test` all pass; `dotnet format` reports only the known `Program.cs` import-order issue.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-013-routing-policies-control.md`; Domain files and tests listed in *Target structure* |
| Modified | `src/OrderRouter.Domain/Routing/PlanSearch/SupplierCandidate.cs` (0–10); its existing tests (the 0-rating case) |
| Reused | `RoutingPlan`, `PlanSearcher`, `PlanScoring` (quality); `PlanSearchRequest.DefaultSimilarityBand`; `Supplier.Rating` / `Rating`; `DomainValidationException` and the all-errors-at-once pattern from `PlanSearchRequest`; `PlanSearchTestData` |
| Unchanged | Application, Infrastructure, Api, migrations, `RoutingNotAvailableUseCase` (OR-015), `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Unrated last → 0 | `UnratedLastPolicyTests` | 2 |
| AC2: Fewer shipments wins | `StrictPriorityPolicyTests` | 3 |
| AC3: Higher quality wins | `StrictPriorityPolicyTests` | 3 |
| AC4: Local preferred within the band | `StrictPriorityPolicyTests` | 3 |
| AC5: Quality wins outside the band | `StrictPriorityPolicyTests` | 3 |
| AC6: Plan quality is the item-weighted mean (8.0) | `PlanQualityTests` (through `PlanSearcher`) | 2 |
| AC7: All-or-nothing → `not_routed` | `AllOrNothingPolicyTests` | 4 |
| AC8: Default is all control | `RoutingPolicyFactoryTests` | 5 |

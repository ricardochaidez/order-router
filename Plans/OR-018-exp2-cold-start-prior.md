# Plan: OR-018: EXP-2 treatment: give unrated suppliers a starting rating

**Ticket:** [Docs/Tickets/OR-018-exp2-cold-start-prior.md](../Docs/Tickets/OR-018-exp2-cold-start-prior.md)
**Depends on:** OR-016 (done: assigner, `RoutingParameters.UnratedPrior`, `experiments.json` already carries `Exp2UnratedSuppliers.Parameters.UnratedPrior`).
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md); Technical Requirements §8.1 EXP-2, SC-02, OQ-10, AB-03, AB-06; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-021 (counts unrated suppliers chosen), OR-024 (combination matrix), OR-025 (README).

> **Save location:** Phase 0 copies this plan to `Plans/OR-018-exp2-cold-start-prior.md`. Update that file as each phase is done.

## Context

EXP-2 control (`UnratedLastPolicy`) gives an unrated supplier an effective rating of 0, so it never wins against a rated one. SC-02 asks to test giving new suppliers a chance. OR-018 adds the treatment arm, `ColdStartPriorPolicy`: an unrated supplier's effective rating is `RoutingParameters.UnratedPrior` (default 6.5), so it can win some orders.

Most of the plumbing exists already, so the change is small:
- `IEffectiveRatingPolicy` is the seam. `OrderRoutingService` calls `policy.Rating.EffectiveRating(supplier)` when it builds each `SupplierCandidate`.
- `RoutingParameters.UnratedPrior` is validated (1–10, default 6.5).
- `ExperimentsOptionsMapper` and `ExperimentConfiguration` already read `Exp2UnratedSuppliers.Parameters.UnratedPrior` from the reloadable `experiments.json` and put it in `ExperimentAssignment.Parameters`.
- `RoutingPolicyFactory` throws `NotSupportedException` for the EXP-2 treatment arm. The comment on it says the treatment ticket replaces that throw.

So AC4 (prior from config) mostly needs a test that proves the policy takes the prior from the per-request parameters and never captures it.

Out of scope: OR-017 and OR-019 (the factory keeps throwing for EXP-1 and EXP-3 treatment); the decision log's unrated count (OR-021); the "Bayesian average once ratings exist" step the requirements call optional and later; README (OR-025).

### Design choices (from the ticket, ADR-0001, ADR-0005 and the code; no question needed)

| Topic | Choice and reason |
|---|---|
| New type | `ColdStartPriorPolicy : IEffectiveRatingPolicy` in `src/OrderRouter.Domain/Routing/Policies/`. Constructor takes the prior and validates 1–10 with a `DomainValidationException`, the same pattern as `StrictPriorityPolicy`. A rated supplier keeps its own rating, and an unrated one gets the prior. Immutable and stateless, so it is safe to share. |
| Prior source | `RoutingPolicyFactory` builds it per request with `assignment.Parameters.UnratedPrior` (ADR-0001, "policies never read configuration or capture it at startup"). That gives AC4: the next request after a reload gets the new prior. |
| Factory | Replace only the EXP-2 treatment `throw` with `new ColdStartPriorPolicy(assignment.Parameters.UnratedPrior)`. EXP-1 and EXP-3 still throw (AB-06: adding an arm changes one line in the factory and nothing in the routing core). Update the class remark and the exception doc. |
| Layering | Pure Domain change. No Application, Infrastructure or Api change, because the config, assigner and DI already carry the parameter. `LayerDependencyTests` must stay green. |
| AC2 fixture note | The band (default 0.5) only decides local-over-mail among plans of equal shipment count. For "unrated (6.5) beats 6.0" the two suppliers must have the same fulfillment mode, because 6.5 and 6.0 are within the band and a local 6.0 would then beat a mail 6.5. Build the AC2/AC3 fixtures with equal modes and say so in a test comment. |
| Known risk | After this ticket, enabling only EXP-2 works. Enabling EXP-1 or EXP-3 in a deployment still makes targeted treatment requests return 500 until OR-017 and OR-019 land. Record it in the ADR-0005 note. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. Run the `ddd-implementation` skill (`/ddd-implementation`) §1 domain analysis and §2 architecture review at the start of Phase 1, even though the change is small (it adds a policy to an existing seam; the skill says to skip only for small edits, so state that decision in the phase result). Run the §5 post-implementation review in Phase 3.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `decimal` for ratings, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types; comments explain *why* and cite the requirement or ADR (`// SC-02`, `// ADR-0001`).
   - **Tests:** xUnit v3, `MethodName_Condition_ExpectedResult`, no Arrange/Act/Assert comments, nearby style.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL first with `docker compose up -d postgres` so the integration tests run too):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: everything passes, including every existing test. Record any change to an existing test in the phase result. The expected one is `RoutingPolicyFactoryTests`, which now asserts the treatment type for EXP-2.
   - `dotnet format --verify-no-changes`: clean. The known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it and don't fix it here.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-018-exp2-cold-start-prior.md`, tick its checklist items, and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - an existing test or sentinel fixture that depends on the EXP-2 treatment throwing, or on unrated suppliers being 0 under treatment;
   - a `LayerDependencyTests` objection to the new type;
   - AC2 failing with equal modes (it would mean the band or tie-break interacts with the prior in a way the ticket does not describe).
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-018-exp2-cold-start-prior`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-018-exp2-cold-start-prior.md` with this plan (headings and statuses as written here).

**Verify:** the file exists. No code changes, so there is nothing to test.

**Result:** Plan saved to `Plans/OR-018-exp2-cold-start-prior.md`. No code changed, so no tests were run.

---

## Phase 1: `ColdStartPriorPolicy` and factory wiring ✅ Done

- [x] Run `ddd-implementation` §1 and §2 (concepts: effective rating, cold-start prior; no aggregate, persistence or event; rules stay apart from state).
- [x] Add `ColdStartPriorPolicy` (see *Design choices*).
- [x] `RoutingPolicyFactory`: EXP-2 treatment → `new ColdStartPriorPolicy(assignment.Parameters.UnratedPrior)`. Update the remark and exception doc.
- [x] Unit tests, new `ColdStartPriorPolicyTests` (same style as `UnratedLastPolicyTests`):
  - AC1: an unrated supplier with the default prior → 6.5.
  - A custom prior (for example 8.0) → 8.0; the policy returns what it was given, not a constant.
  - A rated supplier keeps its own rating, whatever the prior.
  - The prior's range check: 0.99 and 10.01 throw `DomainValidationException`; 1 and 10 are accepted.
  - A null supplier throws `ArgumentNullException`.
- [x] `RoutingPolicyFactoryTests`: EXP-2 treatment → `ColdStartPriorPolicy`; control → `UnratedLastPolicy`; AC4 at factory level: two assignments with different `UnratedPrior` give policies that return different ratings; EXP-1 and EXP-3 treatment still throw `NotSupportedException`; the three arms are independent.
- [x] `OrderRoutingServiceTests`: with the cold-start policy, an unrated supplier reaches the search with effective rating 6.5 (the mirror of `Route_UnratedSupplier_GetsEffectiveRatingZeroFromThePolicy`).

**Verify:** build, test, format (`LayerDependencyTests` green).

**Result:** Build 0 warnings/errors; all tests pass; format clean except the known `Program.cs` import order. `ddd-implementation` skill not run in full (small change behind an existing seam); §1/§2 checked inline. Existing test changed: `RoutingPolicyFactoryTests` theory no longer lists EXP-2 as throwing.

---

## Phase 2: Routing and configuration acceptance tests ✅ Done

- [x] AC2 and AC3, through the real search and selection (no fakes), with the same two suppliers and equal fulfillment modes:
  - treatment arm: the unrated supplier and a 6.0 supplier compete → the unrated one is chosen;
  - control arm: the 6.0 supplier is chosen.
- [x] Edge cases: a 7.0 supplier still beats the unrated one at the default prior (the prior is a nudge, not a free pass); an unrated supplier does not lose eligibility under control (still eligible, only ranked last, AB-03).
- [x] AC4 end to end: the `ConfigurationExperimentAssigner` / `ExperimentConfiguration` tests (OR-016) already show that `UnratedPrior` in `experiments.json` reaches `ExperimentAssignment.Parameters`. Add a test that changes `UnratedPrior` and checks the next assignment drives a different winner through the real factory. If a host test fits (real host with the temp experiments file from OR-016's `RouteApiFactory`), add one that enables EXP-2 at 100%, routes an order whose candidates include an unrated supplier, then raises or lowers the prior and routes again. Use a seeded unrated supplier if the dataset has one that fits an order; otherwise stay at unit level and say so.
- [x] Confirm the shipped `experiments.json` is unchanged and still all disabled (AC1 of OR-016 stays true).

**Verify:** build, test, format.

**Result:** New `ColdStartRoutingTests` (5 tests). The 1217 tests pass. No host test: stayed at unit level, with the real assigner, factory, search and selection. `experiments.json` unchanged; `ShippedExperimentsFileTests` still confirms all disabled.

---

## Phase 3: Docs and post-implementation review ✅ Done

- [x] ADR-0001 / ADR-0005: add a short implementation note (OR-018): `ColdStartPriorPolicy` takes the prior per request from `ExperimentAssignment.Parameters`; the equal-mode note for AC2; the remaining 500 risk for EXP-1 and EXP-3 treatment until OR-017 and OR-019.
- [x] Run the `ddd-implementation` §5 review and report it honestly:
  - **met:** rule in the Domain behind an existing seam, OCP (one factory line), no configuration read in the policy, immutable policy;
  - **N/A, with reasons:** aggregates, domain events, money, persistence, authorization, API surface;
  - **not verified:** the decision log's unrated count (OR-021), the combination matrix (OR-024), coverage % if it was not measured.
- [x] Record follow-ups (don't do them here): OR-017/019 replace the other two factory throws; OR-021 logs the unrated count; OR-024 covers the EXP-1 × EXP-2 combinations; OR-025 documents `UnratedPrior`.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** ADR-0005 note added. §5 review: met = rule in Domain behind an existing seam, one factory line (OCP), no config read in the policy, immutable policy. N/A = aggregates, domain events, money, persistence, authorization, API surface. Not verified = OR-021 unrated count, OR-024 combination matrix, coverage %. Follow-ups: OR-017/019 replace the other factory throws; OR-021 logs the unrated count; OR-024 EXP-1 x EXP-2 matrix; OR-025 documents `UnratedPrior`.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-018-exp2-cold-start-prior.md`; `src/OrderRouter.Domain/Routing/Policies/ColdStartPriorPolicy.cs`; `tests/OrderRouter.UnitTests/Domain/Routing/Policies/ColdStartPriorPolicyTests.cs` |
| Modified | `src/OrderRouter.Domain/Routing/Policies/RoutingPolicyFactory.cs`; `tests/.../Policies/RoutingPolicyFactoryTests.cs`; `tests/.../OrderRoutingServiceTests.cs`; ADR-0001 or ADR-0005 (note) |
| Reused | `IEffectiveRatingPolicy`, `UnratedLastPolicy` (pattern and test style), `RoutingParameters.UnratedPrior` and its validation, `ExperimentsOptionsMapper`, `StrictPriorityPolicy` (constructor-validation pattern), the OR-016 test factories |
| Unchanged | `OrderRoutingService`, plan search, `experiments.json`, Infrastructure, Api, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: unrated → 6.5 by default | `ColdStartPriorPolicyTests`; `OrderRoutingServiceTests` | 1 |
| AC2: unrated beats 6.0 under treatment | Real search and selection test, equal modes | 2 |
| AC3: control picks the 6.0 supplier | Same fixture on the control arm | 2 |
| AC4: prior from `experiments.json` | Factory test with different priors; assigner/reload test through the real factory; host test if the data allows | 1, 2 |

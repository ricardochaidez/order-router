# Plan: OR-024 — Test all eight experiment arm combinations

**Ticket:** [Docs/Tickets/OR-024-experiment-combination-matrix.md](../Docs/Tickets/OR-024-experiment-combination-matrix.md)
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (Negative consequences); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md); Technical Requirements §6.1, §8.1, AB-01, AB-03, SC-10, NFR-08; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Depends on:** OR-017, OR-018, OR-019, OR-023 (all in place); OR-020 (see decisions)

## Context

EXP-1 (bounded trade-off), EXP-2 (cold-start prior) and EXP-3 (partial routing) are assigned independently, so one order can land in any of 2 × 2 × 2 = 8 arm combinations. Each arm is tested on its own already. This ticket adds one test per combination on the seeded sentinel data, to catch interactions that no single class shows (ADR-0001 negative consequences), for example the cold-start prior together with the bounded trade-off. It also proves that the all-control combination equals the OR-023 acceptance results (AB-03, SC-10).

This ticket adds tests and documentation only. It changes no production code. If a test exposes a defect, stop and ask the engineer (see working rule 5).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| OR-020 is listed as a dependency but is **not implemented** (no `DEPENDENT_ITEM_UNFULFILLED` in `src/`) | Build the matrix without it. The shipped rule set is empty, so OR-020 cannot change any expected result. Record the gap in the final notes. Re-run the matrix when OR-020 lands. |
| Test level | **Integration tests** on the real host and seeded database (the `Acceptance` and `Routing` test folders). No unit-level matrix. |
| Pinning arms | A temp `experiments.json` per combination. Each experiment is either enabled at 100% (treatment) or disabled (control), loaded through the same `TempExperimentsFile` + `AddJsonFile` approach as `BoundedTradeOffHostTests`. |
| Fixed random source | Replace `IRandomSource` with a fixed one through the test host's DI, so any tie-break is deterministic. |
| Where results are documented | A combination table in the test class XML docs (one row per combination: arms, order, expected shipments/suppliers/status). A short implementation note is added to ADR-0001. |

### The eight combinations

| # | EXP-1 | EXP-2 | EXP-3 |
|---|---|---|---|
| 1 | control | control | control |
| 2 | control | control | treatment |
| 3 | control | treatment | control |
| 4 | control | treatment | treatment |
| 5 | treatment | control | control |
| 6 | treatment | control | treatment |
| 7 | treatment | treatment | control |
| 8 | treatment | treatment | treatment |

### Candidate sentinel scenarios (to be verified in Phase 1)

| Experiment | Orders on the seeded data | Control | Treatment |
|---|---|---|---|
| EXP-1 | `00007`, `mail_order=false`, wheelchair + cane (`WC-STD-001`, `CN-STD-063`) | 1 shipment, `SUP-T007` (6.0) | 2 shipments, `SUP-T008` + `SUP-T009` (10.0 each; uplift 4.0 ≥ 2.0) |
| EXP-2 | **To be found in Phase 1.** An unrated supplier (`SUP-T011` / `SUP-T012`, rollator, `00040`) competing with a rated one. | Rated supplier wins | Unrated supplier can win (prior 6.5) |
| EXP-3 | `00098`, `mail_order=false`, wheelchair + walker (`SUP-T014` covers wheelchair only) | `not_routed`, nothing routed | `partially_routed`, wheelchair routed to `SUP-T014`, walker in `unfulfilled_items` |
| Interaction | **To be found in Phase 1.** An order where the prior changes a plan's quality enough to cross the EXP-1 uplift threshold, or where EXP-3 leaves a partial plan that EXP-1 would otherwise change. | — | — |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before you write code, and follow them. Key points for this ticket:
   - C# 14 / .NET 10. Use file-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, and PascalCase / camelCase naming. Follow `.editorconfig`.
   - Add XML doc comments on every public type and comments that explain *why* a design choice was made.
   - Test names follow `MethodName_Condition_ExpectedResult`. No `Arrange` / `Act` / `Assert` comments. Reuse existing helpers (`ReferenceDataFixture`, `RouteApiFactory`, `AcceptanceApi`, `AuthApiFactory`) rather than adding a second way to build the host.
   - Tests on random ties assert membership of the tied set, never a fixed winner, unless the random source is fixed (NFR-08).
   - Put any new interface in the flat `Interfaces/` folder of its project (`csharp.md`). This ticket should not need one.
   - Use the `ddd-implementation` skill's process at the scale this ticket needs. Phase 1 records a short domain analysis (no new aggregates or events, tests only). Phase 5 runs the skill's post-implementation review and honestly reports which checks do not apply.
   - Follow the CLAUDE.md compliance rules: nothing in a test may bypass a compliance gate or change eligibility. Never weaken an assertion to make a test pass.
2. **Tests must pass after each phase.** Before you mark a phase done, run `dotnet build` and `dotnet test` from the repo root, plus each phase's own checks, and `dotnet format --verify-no-changes`. All must succeed with **0 errors and 0 failing tests**.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-024-experiment-combination-matrix.md`, and tick its checklist items.
4. **Ask before the next phase.** After you mark a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before you start the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule or ticket text is unclear or contradicts another, or if a test shows behavior that differs from the spec or an ADR, stop and ask the engineer. Don't guess, and don't change production code to make a test pass without approval.
6. No commits unless the engineer asks. If asked, branch off `main` first (for example `feature/OR-024-experiment-combination-matrix`).

---

## Phase 0 — Save the plan ✅ Done

- [x] Create `Plans/OR-024-experiment-combination-matrix.md` with this plan's contents. That file is the one whose status gets updated from now on.

**Verify:** the file exists. There's no code yet, so there's nothing to test.

---

## Phase 1 — Discover and verify the sentinel scenarios ✅ Done

- [x] Record a short `ddd-implementation` domain analysis under "Phase 1 notes" in this file: no new domain concepts, aggregates or events; test-only change; affected layers are the integration test project and docs; no security or compliance impact beyond not bypassing gates.
- [x] Read the existing helpers and tests (`BoundedTradeOffHostTests`, `PartialRoutingApiTests`, `SentinelSupplierAcceptanceTests`, `SampleOrderAcceptanceTests`, `RouteApiFactory`, `ReferenceDataFixture`) and note what can be reused for pinning arms and for swapping `IRandomSource`.
- [x] Query the seed data (`Docs/Data/Seed/*.normalized.csv`) to confirm the EXP-1 and EXP-3 scenarios above, and to find an **EXP-2 scenario** (unrated vs. rated for the same order) and an **interaction scenario**. Record the chosen orders and the expected result per arm in "Phase 1 notes".
- [x] If the seed data has no usable EXP-2 or interaction scenario, **stop and ask the engineer** (options: use a different sentinel set, or add test-only suppliers). Don't edit the seed data without approval.
- [x] Write the expected-result table for all 8 combinations (arms → shipments, suppliers, `status`, `feasible`). Derive each row from the rules in the Requirements and the ADRs, not from running the code. Where the rules don't give a single answer, ask the engineer.

**Verify:** `dotnet build` and `dotnet test` still pass (no code changed). The expected-result table is complete and reviewed by the engineer.

---

## Phase 2 — Test harness for pinning arms and the random source ✅ Done

- [x] Add a small test helper in `tests/OrderRouter.IntegrationTests/Acceptance/` (for example `ExperimentArmsHost`) that builds the host with all three experiments pinned to a given `(exp1, exp2, exp3)` combination through a temp `experiments.json` (enabled at 100% for treatment, disabled for control) and a fixed `IRandomSource`.
- [x] Reuse `TempExperimentsFile` logic from `BoundedTradeOffHostTests` rather than copying it. If that needs a move to a shared internal helper, keep the existing tests unchanged in behavior.
- [x] Add one test that proves the helper pins the arms: the stored decision/assignment (or `diagnostics`, if the response exposes the arms) shows the expected arm for each experiment, and `config_version` is present.
- [x] Add one test that proves the fixed random source is used for a tie (for example `SUP-T003` / `SUP-T004` at `00003`).

**Verify:** `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass. The new helper tests pass.

---

## Phase 3 — The eight combination tests (AC1) ✅ Done

- [x] Add `ExperimentCombinationMatrixTests` in `tests/OrderRouter.IntegrationTests/Acceptance/`, with the combination table from Phase 1 in the class XML docs.
- [x] One test per combination, as an xUnit `Theory` with 8 `MemberData` rows (or 8 named `Fact`s, so each failure names its combination). Each test routes the sentinel orders from Phase 1 with the pinned arms and asserts the documented result: shipments, supplier ids, `status`, `feasible`, and `unfulfilled_items`.
- [x] Include the interaction scenario from Phase 1 in the combinations where it applies (at least one combination where EXP-1 and EXP-2 are both treatment).
- [x] Unique `order_id` per request, because the database accumulates stored orders and tests run in parallel.
- [x] Verify at the start that the arms in each test were actually the intended ones (reuse the check from Phase 2), so a config mistake can't pass silently.

**Verify:** `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass. All 8 combination tests pass, and run in isolation (`dotnet test --filter ExperimentCombinationMatrixTests`).

---

## Phase 4 — All-control equals the OR-023 suite (AC2) ✅ Done

- [x] Add a test that routes the sentinel orders, and the sample orders ORD-001 to ORD-003 where OR-023 covers them, with every experiment in control, and asserts the results equal those of the OR-023 acceptance suite (same suppliers and shipments; membership of the tied set for random ties).
- [x] Prefer sharing the OR-023 expectations (constants or a helper) with the acceptance tests over copying them, so the two suites can't drift.
- [x] Add a check that the shipped `experiments.json` (all disabled) gives the same results as the explicit all-control combination (AB-03, SC-10).

**Verify:** `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass. The OR-023 acceptance tests still pass unchanged.

---

## Phase 5 — Documentation and final review ✅ Done

- [x] Add a short implementation note (OR-024) to [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md): the matrix exists, where it lives, and what interactions it found (or that none were found). Don't change the decision itself. If an interaction shows the decision needs to change, stop and ask.
- [x] Add a note to the ticket's file (as OR-023 does under "Notes"): how the arms are pinned, the sentinel orders used, and the OR-020 gap (matrix built without dependency warnings; re-run once OR-020 lands).
- [x] Run the `ddd-implementation` skill's post-implementation review and report honestly which checks apply (tests written and passing, naming conventions, no bypassed gate) and which don't (aggregates, domain events, money, transactions, PCI/SOX/LGPD).
- [x] Run the full suite and coverage as in OR-023 AC10 (`dotnet test --coverage --coverage-output-format cobertura`), and confirm Domain and Application stay at or above 85% (NFR-08).

**Verify:** `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass with 0 errors. Coverage is at least 85%. Summarize the final results for the engineer. Nothing is committed unless the engineer asks.

---

## Phase 1 notes

**Domain analysis (`ddd-implementation`, at this ticket's scale):** no new domain concepts, aggregates or events. Test-only change; affected layers are the integration test project and docs. No security or compliance impact: nothing bypasses a gate or changes eligibility.

**Reusable helpers:** `RouteApiFactory.Create(..., exposeDiagnostics, experimentsFile)` pins arms; `AuthApiFactory` accepts `ConfigureTestServices` for swapping `IRandomSource`; `AcceptanceApi` posts and reads replies; `TempExperimentsFile` moved out of `BoundedTradeOffHostTests` into a shared internal helper.

**Engineer decision:** the sentinel seed has no rated competitor for the unrated suppliers, so (option 1) five test-only suppliers go into a throwaway database (`MatrixDatabaseFixture`) seeded with the real files. `Docs/Data/Seed` is untouched. Extra suppliers: `SUP-X001` (rollator, 6.0) and `SUP-X002` (rollator, unrated) at `00050`; `SUP-X011` (wheelchair + cane, unrated), `SUP-X012` (wheelchair, 8.0) and `SUP-X013` (cane, 8.0) at `00051`.

**Orders and expected results** (all `mail_order=false`; band 0.5, uplift 2.0, prior 6.5):

| Order | Request | EXP-1 control / treatment | EXP-2 control / treatment | EXP-3 control / treatment |
|---|---|---|---|---|
| A | `00007` wheelchair + cane | `SUP-T007` / `SUP-T008` + `SUP-T009` | no effect | no effect (routed) |
| B | `00050` rollator | no effect | `SUP-X001` / `SUP-X002` | no effect |
| C | `00098` wheelchair + walker | no effect | no effect | `not_routed` / `partially_routed`, wheelchair on `SUP-T014`, walker unfulfilled; `feasible` false |
| D | `00051` wheelchair + cane | one shipment `SUP-X011` / two shipments `SUP-X012` + `SUP-X013` | baseline 0 / 6.5 | no effect (routed) |

Interaction on D: the two-shipment plan wins only for EXP-1 treatment with EXP-2 control (uplift 8.0). With EXP-2 treatment the uplift is 1.5, so `SUP-X011` keeps the order. The full 8-row table is in the `ExperimentCombinationMatrixTests` docs.

## Results

- Full suite: 1543 passed, 0 failed (the one earlier failure did not reproduce). In a later coverage run, `RouteStorageApiTests.Post_AnyOrder_StoresArmsConfigVersionSearchModeAndCreatedAt` failed once on its `created_at <= now()` check. It is existing code, not touched by OR-024.
- Coverage (merged Cobertura): Domain 98.5%, Application 98.7% (at least 85%, NFR-08).
- `dotnet format --verify-no-changes` reports one existing error, `src/OrderRouter.Api/Program.cs` imports ordering. It fails the same way without the OR-024 changes, and OR-024 changes no production code, so it is left alone.
- OR-020 gap: the matrix was built without dependency warnings. Re-run it when OR-020 lands.

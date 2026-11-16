# Plan: OR-019: EXP-3 treatment: route what we can

**Ticket:** [Docs/Tickets/OR-019-exp3-partial-routing.md](../Docs/Tickets/OR-019-exp3-partial-routing.md)
**Depends on:** OR-016 (done: assigner, `ExperimentArms.Exp3PartialRouting`). OR-027 (not done; see decision below).
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); Technical Requirements FR-14, §8.1 EXP-3, SC-04, SC-05, SC-10; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds / related:** OR-020 (dependency warnings, FR-18), OR-021 (decision log), OR-024 (combination matrix), OR-025 (README), OR-027 (HTTP 201).

> **Save location:** Phase 0 copies this plan to `Plans/OR-019-exp3-partial-routing.md`. Update that file as each phase is done.

## Context

EXP-3 control (`AllOrNothingPolicy`) routes nothing when any line is unfulfillable. SC-04/SC-05 want the treatment, `PartialRoutingPolicy`: route the lines that can be routed and return `status:"partially_routed"`, `feasible:false`, with the unfulfilled lines in `unfulfilled_items` and `errors`.

Most plumbing already exists:
- `IUnfulfillablePolicy.Apply(RoutingPlan?, IReadOnlyList<UnfulfilledLine>)` is the seam. `OrderRoutingService` already calls it, builds `RoutedLinesFor` from `outcome.Plan`, and passes unfulfilled lines through.
- `RoutingOutcome` already validates the `PartiallyRouted` shape (plan present and at least one unfulfilled line) and `RoutingOutcomeStatus.PartiallyRouted` exists.
- `RouteResponseMapper` already maps `PartiallyRouted` to `partially_routed`, fills `unfulfilled_items` and `errors`. `RouteResponse` derives `feasible` from the status.
- `Exp3PartialRouting` is already in the assigner, config and `ExperimentArms`.
- `RoutingPolicyFactory` throws `NotSupportedException` for the EXP-3 treatment arm; this ticket replaces that throw.

So the change is one new Domain class, one factory line, and tests. No Application, Infrastructure or Api production change is expected.

Out of scope: dependency warnings (OR-020; `Warnings` stays `[]`), the decision log (OR-021), storing the decision and returning 201 (OR-027), the combination matrix (OR-024), README (OR-025). EXP-1 treatment (OR-017) is a separate ticket.

### Decision made with the engineer
- **AC1 says 201, the endpoint returns 200 today** (`RouteEndpoints.cs` comment: "200 until OR-027 switches this to 201"). OR-027 is a dependency but not implemented. **Chosen:** do not block. Tests assert the body fields and HTTP 200, with a comment that OR-027 changes it to 201 (OR-027 already says it updates the 200 assertions). Record this as a known gap in the Phase 3 result.

### Design choices (from the ticket, ADR-0001 and the code)

| Topic | Choice and reason |
|---|---|
| New type | `PartialRoutingPolicy : IUnfulfillablePolicy` in `src/OrderRouter.Domain/Routing/Policies/`, stateless singleton (`Instance`) like `AllOrNothingPolicy`. |
| Logic | `plan is null` → `NotRouted` (AC2). `plan` present and no unfulfilled → `Routed` (AC3). `plan` present and unfulfilled → `PartiallyRouted` (AC1). Fail safe: no plan is never routed or partial. |
| Factory | Replace only the EXP-3 `Treatment` throw with `PartialRoutingPolicy.Instance`. Update the class remark and the exception doc (now only EXP-1 throws). |
| AC4 | Validation runs before routing (OR-009), so 400 with nothing routed should already hold under every arm. Prove it with a test; make no production change unless the test fails (then stop and ask). |
| Layering | Pure Domain change. `LayerDependencyTests` must stay green. |
| Known risk | Enabling EXP-1 treatment still makes targeted requests return 500 until OR-017. Note it in the ADR-0001/0005 note. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` and follow them. Run `ddd-implementation` §1 domain analysis and §2 architecture review at the start of Phase 1 (state the decision if skipped as a small change behind an existing seam). Run §5 post-implementation review in Phase 3.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML docs on public types; comments explain *why* and cite the requirement or ADR (`// FR-14`, `// SC-05`).
   - **Compliance:** CLAUDE.md "Fail safe on compliance" and "never bypass a gate": partial routing only affects lines already past eligibility; it must never route an unfulfilled line.
   - **Tests:** xUnit v3, `MethodName_Condition_ExpectedResult`, no Arrange/Act/Assert comments, match nearby style.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL first with `docker compose up -d postgres` so integration tests run):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing tests. Record any change to an existing test in the phase result (expected: the `RoutingPolicyFactoryTests` theory that lists EXP-3 treatment as throwing).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-019-exp3-partial-routing.md`, tick its checklist, and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - an existing test or sentinel fixture that depends on the EXP-3 treatment throwing;
   - the AC4 test failing (validation not blocking under the treatment arm);
   - `LayerDependencyTests` objecting to the new type;
   - the mapper or `RouteResponse` rejecting a partial outcome.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-019-exp3-partial-routing`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-019-exp3-partial-routing.md` with this plan (headings and statuses as written here).

**Verify:** the file exists. No code changes, so no tests to run.

**Result:** Plan saved to `Plans/OR-019-exp3-partial-routing.md`. No code changed, so no tests were run.

---

## Phase 1: `PartialRoutingPolicy` and factory wiring ✅ Done

- [x] Run `ddd-implementation` §1 and §2 (concepts: unfulfillable policy, routing outcome, partial route; no aggregate, persistence or event).
- [x] Add `PartialRoutingPolicy` (see *Design choices*).
- [x] `RoutingPolicyFactory`: EXP-3 treatment → `PartialRoutingPolicy.Instance`. Update the remark and exception doc.
- [x] Unit tests, new `PartialRoutingPolicyTests` (same style as `AllOrNothingPolicyTests`):
  - plan and unfulfilled lines → `PartiallyRouted`, same plan, same lines (AC1);
  - no plan, unfulfilled lines → `NotRouted`, no plan (AC2);
  - plan and no unfulfilled lines → `Routed` (AC3);
  - null `unfulfilled` throws `ArgumentNullException`;
  - `Instance` is shared.
- [x] `RoutingPolicyFactoryTests`: EXP-3 treatment → `PartialRoutingPolicy`; control → `AllOrNothingPolicy`; EXP-1 treatment still throws `NotSupportedException`; arms stay independent.

**Verify:** build, test, format (`LayerDependencyTests` green).

**Result:** 1224 tests pass (was a theory of 2 throwing cases, now split: EXP-1 throws, EXP-3 builds `PartialRoutingPolicy`). Build clean; format only reports the known `Program.cs` import issue. No deviation; §1/§2 skipped as a small change behind an existing seam.

---

## Phase 2: Routing and API acceptance tests ✅ Done

- [x] `OrderRoutingServiceTests` (real search and selection, treatment arm), with the real policy:
  - AC1: one routable line plus one unknown product → `PartiallyRouted`; the routable line is in `RoutedLines`, the unknown one in `Unfulfilled` with `UNKNOWN_PRODUCT`;
  - AC1 variant: a known product with no supplier in the ZIP (`NO_SUPPLIER_FOR_CATEGORY_IN_AREA`) also goes partial;
  - AC2: no routable line → `NotRouted`, no routed lines;
  - AC3: fully routable → `Routed`, no unfulfilled lines;
  - control arm on the AC1 order → `NotRouted` with nothing routed (EXP-3 control unchanged, SC-10).
- [x] `RouteResponseMapperTests`: partial decision → `status:"partially_routed"`, `feasible:false`, routed item in `routing`, unfulfilled item in `unfulfilled_items` and its message in `errors`; `warnings` empty.
- [x] API integration test through the real host with a temp `experiments.json` enabling EXP-3 at 100% (reuse OR-016's `RouteApiFactory`):
  - AC1: response body as above; assert HTTP 200 with a comment that OR-027 changes it to 201 (decision above);
  - AC2 and AC3 through HTTP;
  - AC4: invalid ZIP under the treatment arm → 400, same envelope as OR-009, nothing routed (empty `routing`, `status:"not_routed"`).
- [x] Confirm the shipped `experiments.json` is unchanged and still all disabled (`ShippedExperimentsFileTests`).

**Verify:** build, test, format.

**Result:** 1234 tests pass. `ShippedExperimentsFileTests` already covers the unchanged, all-disabled `experiments.json`. AC1 API test asserts 200 (known gap: AC1 says 201 until OR-027). AC4 held with no production change.

---

## Phase 3: Docs and post-implementation review ✅ Done

- [x] ADR-0001 (and ADR-0004 if it fits): short implementation note (OR-019): `PartialRoutingPolicy` is stateless; one factory line; HTTP 201 comes with OR-027; dependency warnings come with OR-020; EXP-1 treatment still 500s until OR-017.
- [x] Run the `ddd-implementation` §5 review and report honestly:
  - **met:** rule in Domain behind an existing seam, OCP (one factory line), no config read, immutable policy, fail safe (no plan never routes);
  - **N/A, with reasons:** aggregates, domain events, money, persistence, authorization, API surface change;
  - **not verified:** 201 + `Location` (OR-027), dependency warnings (OR-020), decision log count (OR-021), combination matrix (OR-024), coverage % if not measured.
- [x] Record follow-ups (don't do them here): OR-017 replaces the last factory throw; OR-020 adds warnings; OR-021 logs unfulfilled count; OR-024 EXP-3 × other arms; OR-025 documents EXP-3.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** ADR-0001 note added. §5 review: met = rule in Domain behind an existing seam, one factory line, no config read, immutable policy, fail safe. N/A = aggregates, events, money, persistence, authorization, API surface. Not verified = 201 + `Location` (OR-027), warnings (OR-020), decision log count (OR-021), combination matrix (OR-024), coverage %. Follow-ups: OR-017 replaces the last factory throw; OR-020 warnings; OR-021 log unfulfilled count; OR-024 EXP-3 x other arms; OR-025 README.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-019-exp3-partial-routing.md`; `src/OrderRouter.Domain/Routing/Policies/PartialRoutingPolicy.cs`; `tests/OrderRouter.UnitTests/Domain/Routing/Policies/PartialRoutingPolicyTests.cs` |
| Modified | `src/OrderRouter.Domain/Routing/Policies/RoutingPolicyFactory.cs`; `RoutingPolicyFactoryTests.cs`; `OrderRoutingServiceTests.cs`; `RouteResponseMapperTests.cs`; API integration tests; ADR-0001 (note) |
| Reused | `IUnfulfillablePolicy`, `AllOrNothingPolicy` (pattern and tests), `RoutingOutcome` and its validation, `RouteResponseMapper`, OR-016 `RouteApiFactory` and temp-config helpers |
| Unchanged | `OrderRoutingService`, plan search, `RouteEndpoints` (stays 200), `experiments.json`, Infrastructure, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: partial route | `PartialRoutingPolicyTests`; `OrderRoutingServiceTests`; mapper test; API test (200 until OR-027) | 1, 2 |
| AC2: nothing routable → `not_routed` | Policy, service and API tests | 1, 2 |
| AC3: all routable → `routed`, `feasible:true` | Policy, service and API tests | 1, 2 |
| AC4: invalid ZIP → 400, nothing routed | API test under the treatment arm | 2 |

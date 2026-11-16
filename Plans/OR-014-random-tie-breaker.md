# Plan: OR-014: Break full ties at random

**Ticket:** [Docs/Tickets/OR-014-random-tie-breaker.md](../Docs/Tickets/OR-014-random-tie-breaker.md)
**Depends on:** OR-013 (done: `IPlanSelectionPolicy.SelectBest` returns every plan still tied after keys 2–4, in input order).
**References:** [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (`ITieBreaker`, `IRandomSource`, `UniformRandomTieBreaker`); Technical Requirements FR-13, NFR-01, NFR-08 (a–d), SC-17, SC-18, AB-04, §7.3 key 5; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-015 (`OrderRoutingService` calls `ITieBreaker.Pick` after `SelectBest`, registers `IRandomSource`/`ITieBreaker` in DI, carries `TieBreakResult` into `RoutingDecision`), OR-021 (decision log records tied candidates + pick), OR-023 (endpoint tie tests: result ∈ {T003, T004}), OR-024 (fixed random source in the matrix).

> **Save location:** Phase 0 copies this plan to `Plans/OR-014-random-tie-breaker.md`. Update that file as each phase is done.

## Context

The ranking has five keys (§7.3). OR-013 implemented keys 2–4 and returns every plan still tied. Key 5 (FR-13, SC-17) says: on a **full** tie, pick one plan **uniformly at random**, so the same supplier does not always win. The pick is truly random (not seeded) and not repeatable (SC-18: the first response is authoritative). Randomness must come only from an injected `IRandomSource` (NFR-08a), must never be used when there is no tie (NFR-01, NFR-08d), and the tied candidates plus the pick must be returned for the audit/decision log (AB-04).

Out of scope: `OrderRoutingService`, `RoutingDecision`, DI wiring and response mapping (OR-015); decision log event (OR-021); endpoint tie tests (OR-023); README wording on random ties (OR-025).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Return shape (ticket AC4 vs ADR `Pick → CandidatePlan`) | **`Pick` returns `TieBreakResult`** (chosen plan, tied candidates, whether randomness was used). Add a short note to ADR-0001 recording the signature refinement. |
| How AC2–AC4 ("routing runs", "routing decision") are proved before OR-015 | **Domain pipeline tests:** `PlanSearcher` → `StrictPriorityPolicy.SelectBest` → tie-breaker, on sentinel data. `TieBreakResult` stands in for the decision; OR-015 carries it into `RoutingDecision`. |
| Real random source location / DI | **`IRandomSource`, `ITieBreaker`, `UniformRandomTieBreaker`, `TieBreakResult` in Domain** (pure). **`SystemRandomSource` (wraps thread-safe `Random.Shared`) in Infrastructure.** No DI registration in this ticket (OR-015 adds it with its consumer). |

### Design choices (from the ticket, ADR and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| `IRandomSource` | `int NextInt(int maxExclusive)`, exactly as ADR-0001. Contract: returns 0 ≤ n < `maxExclusive`; `maxExclusive` ≥ 1. 0-based index, so AC1 "returns 2 → third plan". |
| `UniformRandomTieBreaker.Pick(IReadOnlyList<RoutingPlan> tied)` | Null → `ArgumentNullException`. Empty → `ArgumentException` (the caller must handle "no plan" before tie-break; never invent a plan). **One plan → returned, random source never called** (NFR-08d). Two or more → `tied[random.NextInt(tied.Count)]`. A source returning a value out of range → `InvalidOperationException` (fail safe: a broken source never silently picks a wrong or default plan). Stateless; the random source is constructor-injected (DIP). |
| `TieBreakResult` | Immutable record: `RoutingPlan Chosen`, `IReadOnlyList<RoutingPlan> TiedCandidates` (never null; a defensive read-only copy of the input, in input order), `bool UsedRandom` (true only when `TiedCandidates.Count > 1`). Invariant: `Chosen` is one of `TiedCandidates`. XML doc says it feeds the decision log (AB-04). |
| Candidate order | Kept as given. `PlanSearchResult.Plans` is ordered by shipment count, then supplier ids (ordinal), and `SelectBest` keeps input order, so a fixed index maps to a stable plan in tests. |
| `SystemRandomSource` | Infrastructure, `sealed`, uses `Random.Shared.Next(maxExclusive)` (thread-safe, not seeded: SC-17 "truly random"). Validates `maxExclusive` ≥ 1 (`ArgumentOutOfRangeException`). Comment explains why it is not seeded and why it lives outside the Domain. |
| Tests | xUnit v3; `MethodName_Condition_ExpectedResult`; follow nearby style; no Arrange/Act/Assert comments. A hand-written `FixedRandomSource` test double (returns a set value, counts calls) in `tests/OrderRouter.UnitTests/Domain/Routing/TieBreaking/`. Reuse `PlanSearchTestData` (`Local(...)`, `Ids(...)`). AC3 runs 200 iterations; the chance of a false failure is 2⁻¹⁹⁹ (negligible), so no retry is needed. Target ≥ 85% coverage of new code (measure it, or say it was not measured). |

### Target structure

```
src/OrderRouter.Domain/Routing/TieBreaking/
  IRandomSource.cs              NextInt(int maxExclusive)
  ITieBreaker.cs                TieBreakResult Pick(IReadOnlyList<RoutingPlan> tied)
  TieBreakResult.cs             Chosen, TiedCandidates, UsedRandom
  UniformRandomTieBreaker.cs    FR-13 key 5
src/OrderRouter.Infrastructure/Randomness/
  SystemRandomSource.cs         Random.Shared
tests/OrderRouter.UnitTests/Domain/Routing/TieBreaking/
  FixedRandomSource.cs, TieBreakResultTests.cs, UniformRandomTieBreakerTests.cs, TieBreakPipelineTests.cs
tests/OrderRouter.UnitTests/Infrastructure/Randomness/
  SystemRandomSourceTests.cs
Docs/ADRs/ADR-0001-routing-strategy-composition.md   (note: Pick returns TieBreakResult)
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is new domain behaviour, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–3; §5 post-implementation review in Phase 4.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, records for value objects, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members; comments explain *why* and cite the requirement or ADR (`// FR-13`, `// NFR-08d`, `// ADR-0001`).
   - **Fail safe:** never invent a plan; an empty tie set or a broken random source throws, it never picks a default.
   - **Randomness only through `IRandomSource`:** no `Random`, `Random.Shared` or `Guid` in the Domain.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL with `docker compose up -d postgres` so the integration tests run too):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test (OR-011/012/013 tests stay green; no existing test should change).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it, don't fix it here).
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-014-random-tie-breaker.md`, tick its checklist items, and add a short **Result** line (test counts and any deviation).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - `SelectBest` on the T003/T004 data does not return both plans (would mean keys 2–4 are not a full tie, and OR-013 needs a look);
   - an architecture test (`LayerDependencyTests` etc.) objects to the new namespaces or to Infrastructure code that has no DI registration;
   - plan order from `PlanSearcher` is not stable, so a fixed index cannot map to a known plan.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-014-random-tie-breaker`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-014-random-tie-breaker.md` with this plan.
- [x] Confirm the three decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so nothing to test.

**Result:** Plan saved. The three decisions are recorded: `Pick` returns `TieBreakResult` (with an ADR-0001 note), the ACs are proved with domain pipeline tests, and `SystemRandomSource` goes in Infrastructure with DI deferred to OR-015. No code changed, so no tests were run.

---

## Phase 1: Tie-break contracts and the uniform tie-breaker (AC1, AC2 unit level) ✅ Done

- [x] `ddd-implementation` §1 and §2: concepts (full tie, tied candidates, pick, random source), invariants (chosen ∈ candidates; no randomness for a single plan; uniform choice), affected layer (Domain only in this phase), no aggregates or domain events (pure stateless domain service), compliance note (no PII; audit data for AB-04).
- [x] `IRandomSource`, `ITieBreaker`, `TieBreakResult` (invariants enforced in the constructor), `UniformRandomTieBreaker` (rules in *Design choices*).
- [x] `FixedRandomSource` test double (fixed value, call counter, records the `maxExclusive` it was asked for).
- [x] Tests (`UniformRandomTieBreakerTests`):
  - AC1: three tied plans + source returning 2 → third plan chosen; `TiedCandidates` has all three in order; `UsedRandom` true; source called once with `maxExclusive` 3.
  - AC2 (unit): one plan → that plan, `UsedRandom` false, source called 0 times.
  - Indices 0 and `Count − 1` pick first and last; source returning −1 or `Count` → `InvalidOperationException`; empty → `ArgumentException`; null list → `ArgumentNullException`; null source in the constructor → `ArgumentNullException`; mutating the caller's list afterwards does not change `TiedCandidates`.
- [x] Tests (`TieBreakResultTests`): chosen not in candidates rejected; empty candidates rejected; `UsedRandom` matches candidate count.

**Verify:** build, test, format (working rule 2).

**Result:** Domain contracts and `UniformRandomTieBreaker` added with `FixedRandomSource` and `TieBreakTestData` test helpers. `TieBreakResult` invariant violations throw `DomainValidationException`. Tests passed.

---

## Phase 2: Real random source (Infrastructure) ✅ Done

- [x] `SystemRandomSource : IRandomSource` in `src/OrderRouter.Infrastructure/Randomness/` using `Random.Shared` (rules in *Design choices*). No DI registration (OR-015).
- [x] Tests (`SystemRandomSourceTests`): `NextInt(1)` always 0; many calls to `NextInt(3)` stay in [0, 3) and hit every value; `maxExclusive` 0 or negative → `ArgumentOutOfRangeException`.
- [x] `LayerDependencyTests` and other architecture tests still green.

**Verify:** build, test, format.

**Result:** `SystemRandomSource` added; architecture tests stay green. 1013 tests passed.

---

## Phase 3: Pipeline tests on sentinel data (AC2, AC3, AC4) and the ADR note ✅ Done

- [x] `TieBreakPipelineTests` (unit): `PlanSearcher.Search` → `StrictPriorityPolicy(0.5m).SelectBest` → `UniformRandomTieBreaker.Pick`, using `PlanSearchTestData`.
  - AC2: one best plan (e.g. `SUP-T003` 8.0 vs another cane supplier at 7.0, or a single supplier) → `FixedRandomSource` called 0 times; 20 repeated runs give the same supplier.
  - AC3: `SUP-T003` and `SUP-T004` (cane, 8.0, local) with `SystemRandomSource`, 200 runs → both appear, and every chosen supplier is in {`SUP-T003`, `SUP-T004`}. Also a 3-way tie with a non-tied 7.0 supplier added → the 7.0 supplier is never chosen.
  - AC4: a tie was broken → `TieBreakResult.TiedCandidates` holds the T003 and T004 plans, `Chosen` is one of them (exact with `FixedRandomSource`: 1 → `SUP-T004`), `UsedRandom` true.
- [x] ADR-0001: one-line note under *Policy seams* / class diagram that `ITieBreaker.Pick` returns `TieBreakResult` (chosen + tied candidates) so the decision log gets the audit data (OR-014, AB-04). Diagram signature updated to match.

**Verify:** build, test, format.

**Result:** `TieBreakPipelineTests` (4) added and the ADR-0001 note and diagram updated. 1017 tests passed. T003/T004 tie as expected, so OR-013 needed no change.

---

## Phase 4: Post-implementation review ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (randomness only via `IRandomSource`, only on a full tie, uniform pick, stateless domain service, DIP, fail safe, audit data returned), what is **N/A** and why (aggregates, domain events, money, persistence, authorization), and what was **not verified** (end-to-end routing and endpoint tie tests wait for OR-015/OR-023; DI registration waits for OR-015; decision log waits for OR-021; coverage % if not measured). Be honest.
- [x] Record follow-ups (do not do them here): OR-015 registers `IRandomSource` → `SystemRandomSource` and `ITieBreaker` → `UniformRandomTieBreaker` as singletons, calls `Pick` only when `SelectBest` returned at least one plan, and puts `TieBreakResult` into `RoutingDecision`; OR-021 logs `TiedCandidates` (supplier ids) and `Chosen` when `UsedRandom`; OR-023 asserts membership of the tied set; OR-025 documents that a tie pick is random and the first response is authoritative.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** Final run: build 0 errors and 0 warnings, 1017 tests pass, format clean apart from the known `Program.cs` import-order issue. Met: randomness only via `IRandomSource`, only on a full tie, uniform pick, stateless service, fail safe, audit data returned. N/A: aggregates, domain events, money, persistence, authorization. Not verified: end-to-end routing and endpoint tie tests (OR-015/OR-023), DI registration (OR-015), decision log (OR-021), and code coverage % was not measured.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-014-random-tie-breaker.md`; Domain, Infrastructure and test files listed in *Target structure* |
| Modified | `Docs/ADRs/ADR-0001-routing-strategy-composition.md` (signature note only) |
| Reused | `RoutingPlan`, `PlanSearcher`, `PlanSearchRequest`, `PlanSearchResult` (`src/OrderRouter.Domain/Routing/PlanSearch/`); `StrictPriorityPolicy` (`src/OrderRouter.Domain/Routing/Policies/`); `PlanSearchTestData` (`tests/OrderRouter.UnitTests/Domain/Routing/PlanSearch/`) |
| Unchanged | Application, Api (`Program.cs`, no DI changes), `RoutingPolicy` / `RoutingPolicyFactory` (the tie-breaker is shared, not per arm, ADR-0001), migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Fixed source returning 2 → third plan | `UniformRandomTieBreakerTests` | 1 |
| AC2: No tie, no randomness, repeatable | `UniformRandomTieBreakerTests` (unit), `TieBreakPipelineTests` (pipeline) | 1, 3 |
| AC3: T003/T004, real source, 200 runs: both appear, no other supplier | `TieBreakPipelineTests` | 3 |
| AC4: Tied candidates and the pick returned | `TieBreakPipelineTests`, `TieBreakResultTests` | 1, 3 |

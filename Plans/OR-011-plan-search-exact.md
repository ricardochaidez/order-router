# Plan: OR-011: Search for the fewest-shipment plans

**Ticket:** [Docs/Tickets/OR-011-plan-search-exact.md](../Docs/Tickets/OR-011-plan-search-exact.md)
**Depends on:** OR-010 (**not built yet**, see decisions below).
**References:** [ADR-0003](../Docs/ADRs/ADR-0003-plan-search-algorithm.md); Technical Requirements §7.2, §7.3, FR-09, FR-10, FR-12, FR-13; [ADR-0001](../Docs/ADRs/ADR-0001-routing-strategy-composition.md) (`MaxExtraShipments`); `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-012 (guard rails and greedy fallback wrap this search), OR-013 (selection policies rank the plans returned here), OR-014 (tie-break), OR-015 (orchestration).

> **Save location:** Phase 0 copies this plan to `Plans/OR-011-plan-search-exact.md`. Update that file as each phase is done.

## Context

The router must pick suppliers for an order using the fewest shipments (set cover, NP-hard in general, tiny here). ADR-0003 chose an exact search: group lines by category, give each category a bit, drop look-alike suppliers that can never win, try covers of size 1, 2, … and stop at the first size that works (one more size when `MaxExtraShipments = 1`). It must return **all** plans still in the running, not the first one, so the random tie-break (FR-13) and the EXP-1 policy (OR-017) have something to choose from.

Out of scope: eligibility rules (OR-010), the work budget, the 8-category limit and the greedy fallback (OR-012), policies and ranking keys 2–5 (OR-013, OR-014), orchestration and the HTTP response (OR-015).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| OR-010 is missing | **Plan search takes eligible candidates as input.** It receives per-category demand plus a list of candidates (supplier id, eligible categories, fulfillment mode, effective rating). It does not read suppliers, ZIPs or `mail_order`. Unit tests build candidates by hand. AC3 and AC4 are verified at search level using the same suppliers and ratings as the seed data; the end-to-end check on seeded data is left to OR-010 + OR-015. |
| Quality weighting | **By line items.** Plan quality = mean of supplier effective ratings with each order line counting once (a category with two lines counts twice). Quantity is not used. ORD-002 gives (3 × 9.2 + 1 × 9.0) / 4 = 9.15. |

### Design choices (from the ticket, ADR and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Layer | **Domain only** (`src/OrderRouter.Domain/Models/PlanSearch/`): a stateless domain service plus immutable value objects (rules: business logic in Domain, records, no I/O). No Api or Infrastructure change. `LayerDependencyTests` stays green. |
| Inputs | `PlanSearchRequest(IReadOnlyList<CategoryDemand> Demand, IReadOnlyList<SupplierCandidate> Candidates, decimal SimilarityBand, int MaxExtraShipments)`. `CategoryDemand(Category, int LineCount)`. `SupplierCandidate(string SupplierId, IReadOnlySet<Category> EligibleCategories, FulfillmentMode Mode, decimal EffectiveRating)`. The caller (OR-015) precomputes the effective rating through the EXP-2 policy (OR-013), so this ticket never decides what an unrated supplier is worth. `SimilarityBand` and `MaxExtraShipments` come from the policy later; OR-011 takes plain values (defaults 0.5 and 0). |
| Fulfillment mode | FR-09 defines mode **per supplier** for an order. ADR-0003 says "mode per category". Both agree for one order, so a candidate carries one mode. Dominance compares (mask, mode). Noted for the §5 review, not a conflict. |
| Output | `PlanSearchResult(IReadOnlyList<RoutingPlan> Plans, int MinimumShipments)`. `RoutingPlan(IReadOnlyDictionary<Category, string> Assignments, int ShipmentCount, decimal Quality, int LocalLineCount)`. Plans are in a stable order (shipment count, then supplier ids ordinal) so tests and the later tie-break are deterministic. No cover → empty plan list (fail safe: never invent a plan). |
| Categories not coverable | The caller removes unfulfillable categories before searching (ADR-0003). If a demanded category has no candidate, the search returns no plans. |
| Assignment inside a cover | When several chosen suppliers can take a category, give it to the higher effective rating, then to local. Remaining exact ties become separate plans (ADR-0003). A plan counts only if every chosen supplier receives at least one category; otherwise it is a smaller cover already found. |
| Bitmask | One `uint` bit per distinct demanded category (≤ 32; the 8-category limit is OR-012). Over 32 categories throws an `ArgumentException` here as a safety net until OR-012 adds the greedy path. |
| Bound | Branch-and-bound is an optimisation only: it must never change the result. It is verified against a brute-force oracle in tests. It is applied **within one shipment count only** (AC8, AC9). |
| Tests | xUnit v3 in `tests/OrderRouter.Tests/Domain/Services/PlanSearch/`, naming `MethodName_Condition_ExpectedResult` (follow nearby style, no Arrange/Act/Assert comments). Target ≥ 85% coverage of new code (measure it, or say it was not measured). |

### Target structure

```
src/OrderRouter.Domain/Models/PlanSearch/
  FulfillmentMode.cs             Local | Mail
  CategoryDemand.cs, SupplierCandidate.cs, PlanSearchRequest.cs   inputs, validated on creation
  RoutingPlan.cs, PlanSearchResult.cs                             outputs
  CandidatePreparer.cs           bit assignment + dominance pruning (keeps equal-rating twins)
  PlanSearcher.cs                exact cover by increasing size, +1 when requested, quality band per size
tests/OrderRouter.Tests/Domain/Services/PlanSearch/
  PlanSearchRequestTests.cs, CandidatePreparerTests.cs, PlanSearcherTests.cs, PlanSearcherOracleTests.cs
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is new domain behavior, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–4; §5 post-implementation review in Phase 5.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching, `nameof`, records for value objects, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members; comments explain *why* and cite the requirement or ADR (`// FR-10`, `// ADR-0003`).
   - **Fail safe:** ambiguity never turns into a plan. No cover means no plans.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL with `docker compose up -d postgres` if integration tests are included):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-011-plan-search-exact.md`, tick its checklist items, and add a short **Result** line (test counts and any deviation).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - the ADR's "mode per category" versus FR-09's per-supplier mode (treated as equal for one order; stop if a case shows they differ);
   - an AC outcome (for example AC4 plan list) that the real seed data contradicts once OR-010 exists;
   - the bound removing a plan that the oracle test keeps.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-011-plan-search-exact`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-011-plan-search-exact.md` with this plan.
- [x] Confirm the two decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so nothing to test.

**Result:** Plan saved. The two decisions (search takes eligible candidates as input; quality weighted by line items) are recorded. No code changed, so no tests were run.

---

## Phase 1: Domain model for plan search ✅ Done

Types only: inputs, outputs and their invariants.

- [x] Run `ddd-implementation` §1 and §2; record the short result in **Result** (expected: ubiquitous language = category demand, candidate, cover, plan, shipment; invariants = at least one demanded category, line count ≥ 1, no duplicate candidate ids, band ≥ 0, extra shipments 0 or 1, ≤ 32 categories).
- [x] Add `FulfillmentMode`, `CategoryDemand`, `SupplierCandidate`, `PlanSearchRequest`, `RoutingPlan`, `PlanSearchResult`. Creation reports every invalid field in one `DomainValidationException` (match `Supplier`).
- [x] Unit tests for each invariant and for immutability/equality.

**Result:** §1/§2 done: language = category demand, candidate, cover, plan, shipment; invariants as listed, all enforced in the constructors (`DomainValidationException`, all errors at once); domain-only, no layer change. Deviations: reused the existing `OrderRouter.Domain.Eligibility.FulfillmentMode` (added by OR-010, same `Local | Mail`) instead of a second enum; the 32-category limit is a request invariant (`DomainValidationException`), so the searcher has no separate `ArgumentException`. Build 0 warnings; 573 tests passed (14 new unit tests); format clean.

---

## Phase 2: Grouping, bits and dominance pruning ✅ Done

Covers AC1, AC2, AC5, AC6 at preparation level.

- [x] `CandidatePreparer`: one bit per demanded category; candidate mask = eligible categories ∩ demanded categories; drop candidates with mask 0; dominance pruning by (mask, mode) keeping the max effective rating and **all** equal-rating twins.
- [x] Tests: two CPAP lines become one category with line count 2 (AC1); a quantity-3 line is one demand unit and never split (AC2); `SUP-T003`/`SUP-T004` (cane, 8.0, local) both kept (AC5); same mask and mode with ratings 8.0 and 9.0 keeps only 9.0, and different modes are not dominated (AC6); deterministic order.

**Result:** `CandidatePreparer` + `PreparedSearch` added (categories in ordinal order so input order never matters). 8 new tests cover AC1, AC2, AC5, AC6 and ordering.

---

## Phase 3: Exact cover search by increasing size ✅ Done

Covers AC3, AC4, AC7 and the all-plans requirement.

- [x] `PlanSearcher`: depth-first cover search for size s = 1, 2, …, branching on the lowest uncovered category; stop at the first size with a cover; also search s + 1 when `MaxExtraShipments = 1`; build plans with the assignment rule above; every chosen supplier must be used.
- [x] Tests:
  - AC3: `SUP-T007` (cane, walker, wheelchair, 6.0) with `SUP-T008`–`SUP-T010` (10.0) for wheelchair + cane + walker → only the 1-shipment `SUP-T007` plan, no larger plans;
  - AC4: ORD-002 shape (bed, lift, commode, BP monitor; `SUP-0375` 9.2 covering three, `SUP-042` 9.0 covering BP monitor, no 1-supplier cover) → minimum 2, plans include `SUP-0375` + `SUP-042`, quality 9.15;
  - AC7: `MaxExtraShipments = 1` returns minimum and minimum + 1 plans; `0` returns the minimum only;
  - no cover → empty result; a plan never lists an unused supplier; stable output order.

**Result:** `PlanSearcher` added; AC3, AC4, AC5, AC7, no-cover, unused-supplier and stable-order tests pass. Behaviour to know: with `MaxExtraShipments = 1`, a size-(min+1) set counts whenever every chosen supplier wins at least one category, even if a smaller cover also exists (e.g. a better-rated specialist taking one category from a single-supplier cover). That follows the plan's assignment rule and the oracle agrees.

---

## Phase 4: Quality band per size and the bound ✅ Done

Covers AC8, AC9.

- [x] Quality = mean of effective ratings, one weight per line. Keep only plans with quality ≥ best of the **same shipment count** − `SimilarityBand`. Never compare across counts.
- [x] Add the optimistic upper-bound pruning inside the search (optimisation only).
- [x] Tests: AC8 band 0.5 drops a plan 0.6 below the best of its size and keeps one exactly 0.5 below (boundary); AC9 `MaxExtraShipments = 1`, band 0.5, best 1-shipment 8.0 and 2-shipment 9.0 → both returned; a lower-quality 2-shipment plan is dropped against the best 2-shipment plan, not against the 1-shipment one.
- [x] `PlanSearcherOracleTests`: compare the searcher with a brute-force enumeration (Cartesian product, then the same band rule) over a fixed set of generated cases (seeded random, small k and few candidates); results must be identical, so the pruning is proven not to drop plans.

**Result:** per-size band and the bound (`UpperBound`, within one size only) are in `PlanSearcher`. `PlanSearcherOracleTests` compares against brute force on 300 seeded cases: all identical. Mutation check: making the bound prune too hard (dropping `- band`) made 15 tests fail, so the oracle does exercise the pruning; reverted. Final: 897 tests passed (integration included), build 0 warnings, format clean. Phases 3 and 4 were built in one pass because the bound lives inside the same search class; checks ran after both.

---

## Phase 5: Post-implementation review ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (stateless domain service, immutable value objects, one invariant place per type, all-plans output, no cross-layer change, fail safe), what is **N/A** and why (domain events, money, aggregates, persistence), the ADR-0003 "mode per category" note, and what was **not verified** (coverage % if not measured; end-to-end on seeded data, which waits for OR-010 and OR-015). Be honest.
- [x] Record follow-ups (do not do them here): **OR-012** adds the node budget, the category limit and the greedy fallback around `PlanSearcher` (and replaces the 32-category safety throw); **OR-013** supplies band, `MaxExtraShipments` and effective ratings from the policy; **OR-015** builds candidates from OR-010 eligibility and calls the search.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result (review):**
- **Applies and met:** stateless domain service, immutable value objects (inputs copied on creation), one invariant place per type, all plans returned, no cross-layer change (`LayerDependencyTests` green), fail safe (no cover gives an empty list; nothing invented).
- **N/A:** domain events, money, aggregates and persistence (pure computation, no state).
- **ADR-0003 "mode per category" vs FR-09 per-supplier mode:** treated as equal for one order, so a candidate carries one mode and dominance compares (mask, mode). No case showed a difference.
- **Not verified:** code coverage % was not measured (the tests cover every branch I could identify, but this is not a measurement); no end-to-end run on seeded data, which waits for OR-010 and OR-015. AC3/AC4 are checked at search level only.
- **Follow-ups (not done here):** OR-012 adds the node budget, the 8-category limit and the greedy fallback around `PlanSearcher`; OR-013 supplies band, `MaxExtraShipments` and effective ratings; OR-015 builds candidates from OR-010 eligibility and calls the search. Also OR-012 should note the number of plans can grow with `MaxExtraShipments = 1` (several redundant-looking splits).

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-011-plan-search-exact.md`, `src/OrderRouter.Domain/Models/PlanSearch/*`, `tests/OrderRouter.Tests/Domain/Services/PlanSearch/*` |
| Reused | `Category` (`src/OrderRouter.Domain/Entities/ReferenceData/Category.cs`, ordinal equality and hashing); `Rating` for ratings; `DomainValidationException` (`src/OrderRouter.Domain/Models/`) and the `Supplier` constructor's all-errors-at-once pattern |
| Unchanged | Api, Infrastructure, migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Same category together | `CandidatePreparerTests` | 2 |
| AC2: Quantity never split | `CandidatePreparerTests` | 2 |
| AC3: Single cover found | `PlanSearcherTests` | 3 |
| AC4: Two-supplier cover | `PlanSearcherTests` | 3 |
| AC5: Equal twins kept | `CandidatePreparerTests` + `PlanSearcherTests` | 2, 3 |
| AC6: Weaker look-alike dropped | `CandidatePreparerTests` | 2 |
| AC7: Extra shipment on request | `PlanSearcherTests` | 3 |
| AC8: Quality bound per size | `PlanSearcherTests` | 4 |
| AC9: Band never prunes across counts | `PlanSearcherTests` + `PlanSearcherOracleTests` | 4 |

# Plan: OR-012: Add plan-search limits and the greedy fallback

**Ticket:** [Docs/Tickets/OR-012-plan-search-guard-rails.md](../Docs/Tickets/OR-012-plan-search-guard-rails.md)
**Depends on:** OR-011 (done: `PlanSearcher`, `CandidatePreparer` in `src/OrderRouter.Domain/Routing/PlanSearch/`).
**References:** [ADR-0003](../Docs/ADRs/ADR-0003-plan-search-algorithm.md) (Guard rails); Technical Requirements NFR-03, NFR-05, AB-04, FR-20; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-015 (orchestration calls the plan-search service), OR-021 / OR-027 (record `search_mode` in the decision log and stored order), OR-022 (`diagnostics.search_mode`).

> **Save location:** Phase 0 copies this plan to `Plans/OR-012-plan-search-guard-rails.md`. Update that file as each phase is done.

## Context

The exact search from OR-011 is fast for realistic orders (≤ 4 categories), but its worst case grows with the number of categories and candidates. NFR-03 needs p95 < 50 ms. ADR-0003 adds two guard rails, configured through `appsettings` (NFR-05):

- `PlanSearch:MaxExactCategories` (default 8): more distinct categories than this goes straight to greedy.
- `PlanSearch:NodeBudget` (default 200,000): if the exact search checks more combinations than this, it stops and greedy runs instead.

When greedy runs, a warning is logged and the result says `search_mode = greedy`; otherwise `exact`. Writing `search_mode` into the decision log, stored order and response belongs to OR-021, OR-027 and OR-022. This ticket only puts it on the search result.

Out of scope: orchestration and the HTTP response (OR-015), decision log (OR-021), persistence (OR-027), diagnostics field (OR-022), policies (OR-013), the NFR-03 benchmark that tunes the defaults.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| How ACs are verified (the route endpoint is still the OR-015 placeholder) | AC1–AC3 are verified on the plan-search service (domain and application level), as in OR-011. AC4 is verified by starting the real API host with overridden `PlanSearch:*` settings and checking that the resolved service uses them. End-to-end via `POST` waits for OR-015. |
| Layering | **Domain:** limits value object, node budget inside `PlanSearcher`, `GreedyPlanSearcher`, `SearchMode` and fallback reason on the result (pure, no logging). **Application:** `IPlanSearchService` reads `IOptions<PlanSearchOptions>` and logs the warning (adds `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions` to Application). **Api:** binds the `PlanSearch` section with `ValidateOnStart`. |
| Greedy output | **One deterministic plan.** Repeatedly pick the candidate that covers the most uncovered categories; ties go to the higher effective rating, then local, then supplier id (ordinal). Categories inside the cover are assigned with the same rule as exact (higher rating, then local; a remaining exact tie goes to the lower supplier id so there is one plan). `SimilarityBand` and `MaxExtraShipments` are ignored in greedy mode. |
| 32-category cap | **Keep it.** The catalog has 24 categories, so it can't be reached; greedy reuses the bitmask preparer. Config validation requires `1 ≤ MaxExactCategories ≤ 32` and `NodeBudget ≥ 1`. Update the XML comment on `PlanSearchRequest.MaxCategories` that says OR-012 removes it. |

### Design choices (from the ticket, ADR and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| What a "node" is | One call of the depth-first step (`SizeSearch.Extend`). The count is **cumulative across all sizes** for one request. Exceeded means count > `NodeBudget`. Dominance pruning and the bound reduce the count, so the budget measures real work. |
| Budget hit mid-search | Discard every partial exact result and run greedy from scratch (ADR-0003 flowchart: budget exceeded → greedy). Never mix exact and greedy plans. |
| Category limit check | Distinct demanded categories `k > MaxExactCategories` → greedy, without starting the exact search. `k == MaxExactCategories` stays exact (boundary test). |
| Result shape | `PlanSearchResult` gains `SearchMode Mode` (`Exact` / `Greedy`) and `SearchFallbackReason? FallbackReason` (`TooManyCategories` / `NodeBudgetExceeded`; `null` when exact). This is an additive change; existing callers and tests keep working. `MinimumShipments` in greedy mode = the greedy plan's shipment count (documented as "not guaranteed minimum"). |
| No cover | Greedy also returns an empty plan list when the candidates can't cover every category (fail safe). Mode is still reported. |
| Warning log | Application `PlanSearchService` logs one `Warning` via `[LoggerMessage]` (same pattern as `DependencyStartupCheck`, `DevApiKeyIgnoredNotice`), with reason, category count, `MaxExactCategories` and `NodeBudget`. No PII: no ZIP, order id or caller. Exact runs log nothing at warning level. |
| Config validation | `PlanSearchOptions` (Application, `SectionName = "PlanSearch"`) with defaults 8 / 200,000. An `IValidateOptions<PlanSearchOptions>` reuses the domain `PlanSearchLimits` rules, so bad values stop the API at startup (fail fast, like `JwtAuthenticationOptions`). |
| Defaults in `appsettings.json` | Add the `PlanSearch` section with the defaults so it is visible and overridable by environment (`PlanSearch__NodeBudget`). |
| Tests | xUnit v3; `MethodName_Condition_ExpectedResult`; follow nearby style; no Arrange/Act/Assert comments. Reuse `PlanSearchTestData` (`tests/OrderRouter.Tests/Domain/Routing/PlanSearch/PlanSearchTestData.cs`). Integration tests reuse `AuthApiFactory` / `RouteApiFactory`. Target ≥ 85% coverage of new code (measure it, or say it was not measured). |

### Target structure

```
src/OrderRouter.Domain/Routing/PlanSearch/
  SearchMode.cs                 Exact | Greedy
  SearchFallbackReason.cs       TooManyCategories | NodeBudgetExceeded
  PlanSearchLimits.cs           MaxExactCategories, NodeBudget; validated; Default (8, 200,000)
  GreedyPlanSearcher.cs         greedy set cover, one deterministic plan
  PlanSearcher.cs               (modified) Search(request, limits): category check, node budget, greedy fallback
  PlanSearchResult.cs           (modified) + Mode, FallbackReason
src/OrderRouter.Application/Routing/PlanSearch/
  PlanSearchOptions.cs, PlanSearchOptionsValidator.cs
  IPlanSearchService.cs, PlanSearchService.cs   reads options, calls PlanSearcher, logs the warning
src/OrderRouter.Api/Program.cs  bind PlanSearch section, ValidateOnStart, register IPlanSearchService
src/OrderRouter.Api/appsettings.json   PlanSearch defaults
tests/OrderRouter.Tests/Domain/Routing/PlanSearch/
  PlanSearchLimitsTests.cs, GreedyPlanSearcherTests.cs, PlanSearcherGuardRailTests.cs
tests/OrderRouter.Tests/Application/Routing/PlanSearch/
  PlanSearchOptionsValidatorTests.cs, PlanSearchServiceTests.cs
tests/OrderRouter.Api.IntegrationTests/Routing/PlanSearchConfigurationTests.cs
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is new domain behavior plus a cross-layer change, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–4; §5 post-implementation review in Phase 5.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, records for value objects, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members; comments explain *why* and cite the requirement or ADR (`// NFR-05`, `// ADR-0003`). Comment the purpose of each new package reference in the `.csproj`.
   - **Fail safe:** no cover means no plans, in both modes. Invalid limits stop startup.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run from the repo root (start PostgreSQL with `docker compose up -d postgres` when integration tests are included):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test (OR-011's `PlanSearcherTests` and `PlanSearcherOracleTests` must stay green unchanged).
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-012-plan-search-guard-rails.md`, tick its checklist items, and add a short **Result** line (test counts and any deviation).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - the node budget changes an exact result for any existing OR-011 test (it must not at the default budget);
   - greedy produces more shipments than exact on a case where both run (expected and allowed by ADR-0003, but note it in the test, don't "fix" it);
   - adding packages to Application trips `LayerDependencyTests` or another architecture test.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-012-plan-search-guard-rails`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-012-plan-search-guard-rails.md` with this plan.
- [x] Confirm the four decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so nothing to test.

**Result:** Plan saved. The four decisions (AC verification level, layering, greedy returns one plan, keep the 32-category cap) are recorded. No code changed, so no tests were run.

---

## Phase 1: Domain types for limits and search mode ✅ Done

- [x] `ddd-implementation` §1 and §2: state concepts (search mode, guard rail, node budget, greedy fallback), invariants, affected layers (Domain, Application, Api), and that there are no aggregates or domain events (pure computation).
- [x] `SearchMode` and `SearchFallbackReason` enums.
- [x] `PlanSearchLimits` record: `MaxExactCategories` (1–32, i.e. ≤ `PlanSearchRequest.MaxCategories`), `NodeBudget` (≥ 1); all errors in one `DomainValidationException` (same pattern as `PlanSearchRequest`); `static Default` = (8, 200,000).
- [x] `PlanSearchResult` gains `Mode` and `FallbackReason`; the current exact path returns `Exact` / `null`.
- [x] Update the `PlanSearchRequest.MaxCategories` and `PlanSearcher` XML comments that refer to OR-012.
- [x] Tests: `PlanSearchLimitsTests` (defaults, boundaries 1 and 32, 0 / 33 / negative budget rejected, every error reported at once); existing `PlanSearcherTests` assert mode `Exact`.

**Verify:** build, test, format (working rule 2).

**Result: `SearchMode`, `SearchFallbackReason`, `PlanSearchLimits` and the two new `PlanSearchResult` members added. 767 unit tests green at the time.**

---

## Phase 2: Greedy set cover ✅ Done

- [x] `GreedyPlanSearcher` (stateless) taking a `PlanSearchRequest`; reuses `CandidatePreparer` (dominance pruning is still safe).
- [x] Step rule: most uncovered categories, then higher effective rating, then local, then supplier id (ordinal). Assign categories with the exact search's rule; final tie by supplier id. Compute `Quality` (line-weighted) and `LocalLineCount` the same way as `PlanSearcher` (extract a shared helper rather than duplicating it).
- [x] Returns one plan, or an empty list when no cover exists. Result mode `Greedy`.
- [x] Tests (`GreedyPlanSearcherTests`): single generalist chosen; ORD-002 shape gives 2 shipments; tie on coverage broken by rating, then local, then id; no cover → empty; a known case where greedy uses more shipments than exact (documents ADR-0003's accepted trade-off); 9-category order covered; band and `MaxExtraShipments` ignored.

**Verify:** build, test, format.

**Result: `GreedyPlanSearcher` plus an internal `PlanScoring` helper shared with `PlanSearcher` (compare and score). 777 unit tests green.**

---

## Phase 3: Guard rails in `PlanSearcher` ✅ Done

Covers AC1, AC2, AC3 at domain level.

- [x] `PlanSearcher.Search(PlanSearchRequest request, PlanSearchLimits limits)`; keep `Search(request)` as an overload using `PlanSearchLimits.Default` so OR-011 tests are untouched.
- [x] `k > MaxExactCategories` → greedy, reason `TooManyCategories`, exact search not started.
- [x] Node counter shared across all `SizeSearch` runs; when it passes `NodeBudget`, abort (no exceptions for control flow; return a flag), drop partial results, run greedy, reason `NodeBudgetExceeded`.
- [x] Tests (`PlanSearcherGuardRailTests`):
  - AC1: 9 distinct categories, default limits → `Greedy`, `TooManyCategories`, one plan; 8 categories → `Exact` (boundary).
  - AC2: normal order with `NodeBudget = 1` → `Greedy`, `NodeBudgetExceeded`; a budget exactly equal to the nodes needed stays exact (boundary).
  - AC3: each sample-order shape from OR-011 tests and `Docs/Data/sample_orders.json` categories with default limits → `Exact`, same plans as before.
  - Oracle tests still pass with default limits.

**Verify:** build, test, format.

**Result: node budget and category check in `PlanSearcher`; OR-011 tests and oracle tests unchanged and green. 785 unit tests green.**

---

## Phase 4: Configuration, application service and warning log ✅ Done

Covers the "warning is logged" part of AC1/AC2, and AC4.

- [x] Application: add `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions` (10.0.x, matching the other Microsoft.Extensions packages), each with a purpose comment.
- [x] `PlanSearchOptions` (`SectionName = "PlanSearch"`, defaults 8 / 200,000, `ToLimits()`), `PlanSearchOptionsValidator : IValidateOptions<PlanSearchOptions>` (reuses `PlanSearchLimits` rules, reports every failure).
- [x] `IPlanSearchService` / `PlanSearchService(IOptions<PlanSearchOptions>, ILogger<PlanSearchService>)`: calls `PlanSearcher`, logs one `[LoggerMessage]` warning when mode is `Greedy` (reason, category count, limits; no PII).
- [x] Api `Program.cs`: `AddOptions<PlanSearchOptions>().Bind(...).ValidateOnStart()`, register the validator and `IPlanSearchService` as singletons. `appsettings.json`: `"PlanSearch": { "MaxExactCategories": 8, "NodeBudget": 200000 }`.
- [x] Unit tests: validator (valid, each invalid field, all errors at once); service logs a warning for AC1 and AC2 cases and nothing at warning level for AC3 (use a capturing logger like `tests/OrderRouter.Api.IntegrationTests/Persistence/CapturingLogger.cs`, or `FakeLogger` if already referenced; otherwise a small test fake); service uses the options it was given.
- [x] Integration test `PlanSearchConfigurationTests` (AC4): host with `PlanSearch:MaxExactCategories = 2` and `PlanSearch:NodeBudget = 5` overrides → resolved `IOptions<PlanSearchOptions>` has those values and the resolved `IPlanSearchService` falls back on a 3-category order; host with no override → defaults; invalid value (`NodeBudget = 0`) → host fails to start.
- [x] `LayerDependencyTests` and other architecture tests still green.

**Verify:** build, test (integration included, PostgreSQL up), format.

**Result: Application options, validator, service; Api wiring and `appsettings.json`; Application packages at 10.0.15. Full suite 937 tests green (integration included). Architecture tests unaffected.**

---

## Phase 5: Post-implementation review ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (stateless domain services, immutable value objects, rules separate from config, fail safe in both modes, one place for validation), what is **N/A** and why (aggregates, domain events, money, persistence), and what was **not verified** (end-to-end `POST` waits for OR-015; defaults not benchmarked against NFR-03; coverage % if not measured). Be honest.
- [x] Record follow-ups (do not do them here): OR-015 calls `IPlanSearchService`; OR-021 / OR-027 / OR-022 carry `Mode` as `search_mode` (`exact` / `greedy`); the NFR-03 benchmark confirms or tunes the defaults (ADR-0003 is still `proposed`).
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result: review below; final build, test all green.**

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-012-plan-search-guard-rails.md`; Domain and Application files listed in *Target structure*; their tests |
| Modified | `src/OrderRouter.Domain/Routing/PlanSearch/PlanSearcher.cs`, `PlanSearchResult.cs`, `PlanSearchRequest.cs` (comment only); `src/OrderRouter.Application/OrderRouter.Application.csproj`; `src/OrderRouter.Api/Program.cs`; `src/OrderRouter.Api/appsettings.json` |
| Reused | `CandidatePreparer`, `PreparedSearch` (bitmasks, dominance pruning); `DomainValidationException` and the all-errors-at-once pattern from `PlanSearchRequest`; `[LoggerMessage]` pattern from `src/OrderRouter.Api/Health/DependencyStartupCheck.cs`; `PlanSearchTestData`; `AuthApiFactory` / `RouteApiFactory` for host tests |
| Unchanged | Infrastructure, migrations, route endpoint and `RoutingNotAvailableUseCase` (OR-015), `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Too many categories → greedy, warning, `search_mode = greedy` | `PlanSearcherGuardRailTests` + `PlanSearchServiceTests` | 3, 4 |
| AC2: Budget exceeded → greedy and logged | `PlanSearcherGuardRailTests` + `PlanSearchServiceTests` | 3, 4 |
| AC3: Normal orders stay exact | `PlanSearcherGuardRailTests` + unchanged OR-011 tests | 3 |
| AC4: Limits configurable in `appsettings` | `PlanSearchConfigurationTests` (real host, overrides, invalid value fails startup) | 4 |

---

## Phase 5 review notes

**Applies and is met:** stateless domain services (`PlanSearcher`, `GreedyPlanSearcher`); immutable, validated value object (`PlanSearchLimits`); rules kept apart from configuration (options/validator in Application read the domain rules); fail safe in both modes (no cover gives no plans; invalid limits stop startup); one place for limit validation.

**N/A:** aggregates, domain events, money, persistence (pure computation).

**Not verified:** end-to-end `POST` (waits for OR-015); defaults not benchmarked against NFR-03; coverage percentage not measured.

**Deviations / notes:** `dotnet format --verify-no-changes` reports an import-order error in `src/OrderRouter.Api/Program.cs` (line 1). It was there before this work (checked with my changes stashed) and was left alone.

**Follow-ups:** OR-015 calls `IPlanSearchService`; OR-021 / OR-027 / OR-022 carry `Mode` as `search_mode`; the NFR-03 benchmark confirms or tunes the defaults (ADR-0003 is still `proposed`).

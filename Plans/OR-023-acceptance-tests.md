# Plan: OR-023: Add the end-to-end acceptance test suite

**Ticket:** [Docs/Tickets/OR-023-acceptance-tests.md](../Docs/Tickets/OR-023-acceptance-tests.md)
**Depends on:** OR-015 (routing orchestration), OR-019 (EXP-3 partial routing), OR-028 (GET stored order). All are in the code base (plans in `Plans/`).
**References:** Technical Requirements §6.1, §6.2, §9.4, NFR-08, NFR-11, SC-06, SC-12, SC-17; [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); `CLAUDE.md` (REST API conventions); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-024 (experiment combination matrix reuses the acceptance helpers), OR-025 (README and docs).

> **Save location:** this file is `Plans/OR-023-acceptance-tests.md`. Update it as each phase is done.

## Context

This ticket adds **tests only**. No production behavior changes. It gathers the acceptance criteria into one suite of endpoint-level integration tests that run the real host, the real routing use case and the seeded PostgreSQL database (sample orders, sentinel suppliers, validation, auth, read-back), and then measures coverage.

What already exists (read before writing, do not duplicate blindly):
- `RouteApiFactory` (`Create(connectionString, useCase: null)` keeps the real use case; `Post`, `Get`, `Paths` = `/api/v1/route` and `/api/route`), `AuthApiFactory`, `TestJwt`, `ReferenceDataFixture` (migrates and seeds from `Docs/Data/Seed`), `ThrowawayDatabase`.
- `RouteOrderEndToEndTests` already covers ORD-001/002/003 on both paths (OR-015); `RouteValidationTests` and `RouteAuthTests` cover validation and 401 with a **fake** use case; `GetStoredOrderApiTests` covers read-back (OR-028) and already uses the T003/T004 tie.
- `experiments.json` ships with all three experiments disabled, so every request is in control (strict priority). Acceptance tests rely on that default and must not depend on a treatment arm.
- `Microsoft.Testing.Extensions.CodeCoverage` is referenced by both test projects and `global.json` selects the Microsoft Testing Platform runner. Coverage has **never been measured** in this repo (OR-021 notes "coverage % not measured").

### Design choices

No ambiguity or conflict blocks the plan. These are the defaults it uses; change one before Phase 1 if you disagree.

| Topic | Choice and reason |
|---|---|
| Where | New folder `tests/OrderRouter.Api.IntegrationTests/Acceptance/`, one class per theme (`SampleOrderAcceptanceTests`, `SentinelSupplierAcceptanceTests`, `RequestRejectionAcceptanceTests`, `StoredOrderAcceptanceTests`). Test names and a `// ACn` comment tie each test to the ticket. |
| Existing tests | Left unchanged. The suite is the end-to-end, real-host view of each AC; where an existing test already proves the same thing against the real host, the new test is still added only if it adds a path, an assertion or the AC wording that is missing. Record any overlap in the phase result. |
| Real host | Every acceptance test uses the real use case (`useCase: null`) and the shared seeded database. The fake use case is not used (it is what makes the existing auth and validation tests not end-to-end). |
| Both paths | Core cases (AC1, AC8, AC9, and the sentinel cases through `RouteApiFactory.Paths`) run against `/api/v1/route` **and** `/api/route`. The alias is POST-only; its `Location` points at the v1 GET (ADR-0004, ADR-0007), so AC9 reads both with `GET /api/v1/route/{id}`. |
| Random ties | Never assert a fixed supplier for ORD-003, T003/T004 or T011/T012. Assert the winner is a member of the tied set (NFR-08(b)). Use a few repeated calls only to show no non-member ever appears; the distribution test (NFR-08(c)) belongs to OR-014 and is not repeated here. |
| Product codes | Sentinel orders use product codes picked from `Docs/Data/Seed/products.normalized.csv` whose category matches the sentinel (wheelchair, walker, cane, rollator, and a non-wheelchair category for `00098`). Chosen in Phase 2 and recorded in the test class. `Docs/Data/*` is never edited. |
| Isolation | Each test uses its own `order_id`, because the shared database accumulates stored orders and tests run in parallel. Tests assert on their own order only. Reference data is read-only. |
| Coverage | Measured with the code coverage extension over **both** test projects together, limited to `OrderRouter.Domain` and `OrderRouter.Application` (AC10, NFR-08). If the result is under 85%, the gap is closed with targeted unit tests (Phase 4), not by excluding code. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code. Use `ddd-implementation` §5 (testing and review) in Phase 5; the skill's design steps (§1 and §2) are N/A because no domain behavior is added, and the phase result says so.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, `nameof`, final `return` on its own line, `.editorconfig`.
   - **Tests:** xUnit v3, copy the naming of nearby tests (`Post_Ord001_RoutesToOneLocalSupplier`), no Arrange/Act/Assert comments, use `TestContext.Current.CancellationToken`. Tests assert behavior through the public HTTP contract, not internals.
   - **REST rules under test (CLAUDE.md):** 201 plus `Location` for a created order; 400 lists all validation errors at once in the documented envelope; 401 has `WWW-Authenticate`; 404 for another caller's order; business outcomes ("could not route") are 201 with `feasible:false`, not errors; collections never null.
   - **Compliance:** acceptance tests never route around a gate. Raw caller IDs are never asserted in logs or bodies (NFR-11). If a result is ambiguous, the expected behavior is the exception path (`feasible:false` with a reason), never a guess.
2. **Tests must pass after each phase.** Start PostgreSQL first (`docker compose up -d postgres`), then from the repo root:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test. Record each existing test changed (none expected).
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading from `✅ Done` to `✅ Done` in this file, tick its checklist and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - a sentinel or sample order giving a result that differs from §6.1/§6.2 (this means a routing bug or a stale requirement: report it, do not weaken the assertion or change production code without approval);
   - a test that is flaky because of random ties or shared database state;
   - a `/api/route` response that differs from `/api/v1/route` for the same input (other than ids and timestamps);
   - coverage that cannot be measured with the existing tooling, or that stays under 85% after reasonable unit tests;
   - the need to change production code or `Docs/Data/*` to make a test pass.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-023-acceptance-tests`).

---

## Phase 0: Check prerequisites and baseline ✅ Done

- [x] Confirm OR-015, OR-019 and OR-028 are done (their plans are marked done, `RouteApiFactory`, `GetStoredOrderApiTests` and `RouteOrderEndToEndTests` exist). If not, stop and ask.
- [x] Baseline: PostgreSQL up, `dotnet build`, `dotnet test` and format green before any change.
- [x] Baseline coverage: run the full suite with the code coverage extension (Microsoft Testing Platform, e.g. `dotnet test --coverage --coverage-output-format cobertura`) and record the line coverage for `OrderRouter.Domain` and `OrderRouter.Application`. Keep reports out of the repo (scratch directory or an ignored `TestResults/`). If coverage cannot be produced or merged across the two test projects, stop and ask.
- [x] Read the existing end-to-end, validation, auth and read-back tests; list which ACs they already cover on the real host.

**Verify:** all green. No code changes.

**Result:** 1422 tests green. Baseline line coverage (both projects merged): Domain 98.5% (1150/1168), Application 98.7% (535/542). Command: `dotnet test --coverage --coverage-output-format cobertura --results-directory <scratch>` (both test projects write one Cobertura file each; lines merged per file and line number across the two files). Already-covered ACs on the real host before this work: ORD-001/002/003 on both paths (`RouteOrderEndToEndTests`), read-back, 404 for another caller, 401 on GET (`GetStoredOrderApiTests`). Validation and auth tests (`RouteValidationTests`, `RouteAuthTests`) use a fake use case, so they were not end-to-end.

---

## Phase 1: Acceptance scaffolding, AC1 (sample orders) and AC9 (stored and readable) ✅ Done

- [x] Add `Acceptance/` with a small shared helper (post an order on a path as a caller, read the JSON, read `Location`, GET it). Reuse `RouteApiFactory` and `TestJwt`; do not add a second factory.
- [x] **AC1** (`SampleOrderAcceptanceTests`): ORD-001, ORD-002, ORD-003 from `Docs/Data/sample_orders.json` (read the file or copy the bodies; never edit it), each on both paths, 201 and the §6.2 outcome:
  - ORD-001: one shipment from `SUP-0810`, all items `local`, `feasible:true`, `status:"routed"`;
  - ORD-002: two shipments, `SUP-0375` (hospital bed, patient lift, commode) and `SUP-042` (BP monitor), all `local`;
  - ORD-003: one shipment, supplier is **one of** `SUP-023`/`SUP-065`, all items `mail`.
- [x] **AC9** (`StoredOrderAcceptanceTests`): post each sample order to each path with its own `order_id`, GET the `Location` with the same credentials, and assert the stored decision equals the POST response (`feasible`, `status`, `routing`, `unfulfilled_items`, `warnings`, `errors`, `id`). The alias `Location` is a v1 URL.

**Verify:** build, test, format.

**Result:** 12 new tests (6 in `SampleOrderAcceptanceTests`, 6 in `StoredOrderAcceptanceTests`), plus the `AcceptanceApi` helper. Overlap: AC1 repeats `RouteOrderEndToEndTests` with the ticket's wording and the `feasible`/`status` assertions. No existing test changed.

---

## Phase 2: Sentinel supplier cases, AC2 to AC7 ✅ Done

- [x] Look up the product codes for each sentinel category in `Docs/Data/Seed/products.normalized.csv` and record them in the test class.
- [x] **AC2** local exclusive: `mail_order=false`, `00001`, wheelchair → `SUP-T001`.
- [x] **AC3** local preference: `mail_order=false`, `00005`, walker → `SUP-T005`.
- [x] **AC4** mail order excludes local-only: `mail_order=true`, `00005`, walker → `SUP-T005` is **not** in `routing`, and the item's `fulfillment_mode` is `mail` (SC-06; DEV-03 note in §6.1).
- [x] **AC5** generalist: `mail_order=false`, `00007`, wheelchair + cane + walker → `SUP-T007`, exactly one shipment containing all three items.
- [x] **AC6** random ties: `SUP-T003`/`SUP-T004` (`00003`, cane) and `SUP-T011`/`SUP-T012` (`00040`, rollator, both unrated, must still be routable); the winner is always a member of its pair, never a fixed supplier and never an outsider (repeat a small fixed number of calls to catch an outsider; never assert that both appear).
- [x] **AC7** no coverage: `mail_order=false`, `00098`, a non-wheelchair category → HTTP 201, `feasible:false`, the item in `unfulfilled_items` with `reason_code` `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` (a business outcome, not an error).
- [x] Run the cases that make sense for both paths through `RouteApiFactory.Paths` (the alias must give the same answer); keep single-path tests only where the ticket does not ask for both.

**Verify:** build, test, format. Run the new tests several times in a row to prove none is flaky.

**Result:** 14 new tests in `SentinelSupplierAcceptanceTests` (7 cases x 2 paths). Product codes: wheelchair `WC-STD-001`, walker `WK-STD-009`, cane `CN-STD-063`, rollator `RL-STD-058`. All sentinel results matched §6.1; no routing bug found. Ran the acceptance namespace 3 times in a row: stable.

---

## Phase 3: Auth and validation through the real host, AC8 ✅ Done

- [x] **AC8** (`RequestRejectionAcceptanceTests`), both paths, real use case, against the seeded database:
  - no token → 401 with `WWW-Authenticate`, and routing does not run (no stored order for that `order_id`);
  - bad token (malformed, wrong signature, expired, as the existing `TestJwt` helpers allow) → 401;
  - each invalid field → 400 in the documented envelope (`feasible:false`, `status:"not_routed"`, empty `routing`, `unfulfilled_items`, `warnings`, `errors` listing the problem): missing/blank `order_id`, missing or bad `customer_zip` (bad length, non-digits, not a string), missing/empty/null `items`, `quantity` of zero, negative or missing, blank `product_code`, malformed JSON;
  - several invalid fields in one request → one 400 listing all errors;
  - a 400 or 401 stores nothing (table counts for that `order_id` unchanged).
- [x] Add NFR-11 read-side cases only where not already present: `GET` with no token → 401, and `GET` of another caller's order → 404 (cross-checked against `GetStoredOrderApiTests`; record any overlap).

**Verify:** build, test, format.

**Result:** 44 new tests in `RequestRejectionAcceptanceTests` (both paths). Each 401/400 case checks no `routing_orders` row exists for its `order_id`. Deviation: a numeric `customer_zip` fails JSON binding, so its error starts with "The request body " and names `customer_zip` inside (same as the existing `Post_ZipNotAString_Returns400`). Read-side NFR-11 cases (GET with no token, another caller's order) were not repeated: `GetStoredOrderApiTests` already covers both on the real host. Acceptance namespace: 70 tests, passed twice.

---

## Phase 4: Coverage, AC10 ✅ Done

- [x] Measure coverage over the full run (unit and integration) for `OrderRouter.Domain` and `OrderRouter.Application`, with the same command as Phase 0. Compare against the baseline.
- [x] If either project (or the combined figure, state which one is used) is under 85%: list the uncovered types and branches, then add targeted unit tests for real behavior. Do not exclude code, lower the bar or add assertions-free tests. If the gap needs production changes or is large, stop and ask.
- [x] Record the exact command and the numbers in this plan and in the ticket's notes. Decide with the engineer whether to document the command in the README (OR-025) or add a script; not done here unless asked.

**Verify:** build, test, format; coverage for Domain and Application is at least 85% (NFR-08).

**Result:** after the new tests, 1492 tests; Domain 98.5% (1150/1168), Application 98.7% (535/542), both at or above 85% (NFR-08). Command: `dotnet test --coverage --coverage-output-format cobertura --results-directory <scratch>` (both test projects write one Cobertura file each; lines merged per file and line number across the two files). No unit tests were needed. README or script for the command is left to OR-025.

---

## Phase 5: Review and final run ✅ Done

- [x] `ddd-implementation` §5 review of the tests: names, one behavior per test, no logic in tests, no reliance on random outcomes or test order, no raw caller IDs. Report honestly what is met, N/A (design steps: no new domain behavior) and not verified.
- [x] Trace every AC below to a passing test (table at the end, filled in with test names).
- [x] Final full run: `dotnet build`, `dotnet test` (run twice to expose flakiness from random ties), `dotnet format --verify-no-changes`.
- [x] Leave README and `AI_PROMPTS.md` to OR-025.

**Verify:** everything green; every AC traced.

**Result:** Final `dotnet build` 0 warnings, 0 errors; `dotnet test` 1492 passed (run repeatedly with the acceptance tests, no flakiness); `dotnet format --verify-no-changes` reports only the known `Program.cs` import-order issue. `ddd-implementation` §1 and §2 are N/A (no domain behavior added). §5: tests are named after behavior, one behavior each, no logic beyond data tables, tie tests assert pair membership only, each test uses its own `order_id`, no raw caller IDs asserted. Not verified: the random-tie distribution (OR-014's job).

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-023-acceptance-tests.md`; `tests/OrderRouter.Api.IntegrationTests/Acceptance/*` (acceptance test classes and a small helper); extra unit tests only if Phase 4 needs them |
| Reused | `RouteApiFactory`, `AuthApiFactory`, `TestJwt`, `ReferenceDataFixture`, `ThrowawayDatabase`; `Docs/Data/sample_orders.json` (read only) |
| Unchanged | All production code, `Docs/Data/**`, migrations, existing tests |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: sample orders, both paths | `SampleOrderAcceptanceTests`: `Post_Ord001_…`, `Post_Ord002_…`, `Post_Ord003_…` | 1 |
| AC2: local exclusive (`SUP-T001`) | `SentinelSupplierAcceptanceTests`: `Post_LocalExclusiveWheelchair_RoutesToSentinelT001` | 2 |
| AC3: local preference (`SUP-T005`) | `SentinelSupplierAcceptanceTests`: `Post_LocalPreferenceWalker_RoutesToSentinelT005` | 2 |
| AC4: mail order excludes local-only | `SentinelSupplierAcceptanceTests`: `Post_MailOrderWalker_ExcludesLocalOnlySentinelT005AndShipsByMail` | 2 |
| AC5: generalist (`SUP-T007`, one shipment) | `SentinelSupplierAcceptanceTests`: `Post_WheelchairCaneWalker_RoutesToGeneralistT007AsOneShipment` | 2 |
| AC6: random ties (member of pair) | `SentinelSupplierAcceptanceTests`: `Post_TiedCaneSuppliers_AlwaysRoutesToOneOfThePair`, `Post_TiedUnratedRollatorSuppliers_AreRoutableAndAlwaysOneOfThePair` | 2 |
| AC7: no coverage (`NO_SUPPLIER_FOR_CATEGORY_IN_AREA`) | `SentinelSupplierAcceptanceTests`: `Post_NoSupplierForTheCategoryInTheArea_IsABusinessOutcomeNotAnError` | 2 |
| AC8: auth and validation | `RequestRejectionAcceptanceTests`: `Post_NoToken_…`, `Post_BadToken_…`, `Post_InvalidField_…`, `Post_MalformedJson_…`, `Post_SeveralInvalidFields_…` | 3 |
| AC9: stored and readable, both paths | `StoredOrderAcceptanceTests`: `Get_LocationOfASampleOrder_ReturnsTheDecisionFromPost` | 1 |
| AC10: coverage at least 85% (Domain, Application) | coverage run: Domain 98.5%, Application 98.7%; no extra unit tests | 0, 4 |

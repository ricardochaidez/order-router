# Plan: OR-010: Find the eligible suppliers for each category

**Ticket:** [Docs/Tickets/OR-010-eligibility-rules.md](../Docs/Tickets/OR-010-eligibility-rules.md)
**Depends on:** OR-002 (done, `Supplier.Handles/Serves/IsMailOnly`, `ZipRange`, `Category`), OR-006 (done, `IReferenceDataReader`)
**References:** Technical Requirements §7.1, FR-06, FR-07, FR-08, FR-09, SC-06, SC-07, SC-09, SC-12, §9.4 reason codes; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-011 (plan search uses the eligible sets), OR-013 (ranking key 4 uses the mode), OR-015 (maps reasons to `reason_code`; binds `NationwideZipRange`)

> **Save location:** Phase 0 copies this plan to `Plans/OR-010-eligibility-rules.md`. That file is the one to update as each phase is done.

## Context

The routing engine needs a fixed, experiment-proof rule for "which suppliers may fulfill this category for this order, and how would they ship". OR-002 gave `Supplier` its primitives and OR-006 reads suppliers from the database. OR-010 adds the **pure domain rules** on top:

- **Eligible(category)** per §7.1: handles the category AND (`mail_order = false` → serves the ZIP and is not mail-only; `mail_order = true` → `can_mail_order = y`, ZIP ignored).
- **Fulfillment mode** per FR-09: mail-only → `mail`; else `local` if it serves the ZIP; else `mail`.
- **No eligible supplier** → `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` (mail off) or `NO_SUPPLIER_FOR_CATEGORY` (mail on), §9.4.

Out of scope: `UNKNOWN_PRODUCT` and the response mapping (OR-015); plan search (OR-011); ranking and tie-break (OR-013/014); config binding of `NationwideZipRange` (OR-015); any API or DB change.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| `NationwideZipRange` (FR-09) | **Domain parameter only.** The eligibility service takes the range in its constructor; tests pass `00100-99999`. Config binding, `ZipRange.Parse` and startup validation move to **OR-015**, where the service is wired. This overrides the OR-002 plan note that assigned binding to OR-010; record the change in the Result of the last phase. |

### Design choices (from the ticket, requirements and existing code; no question needed)

| Topic | Choice and reason |
|---|---|
| Location | `src/OrderRouter.Domain/Entities/Eligibility/`. Stateless domain service, no I/O, no EF. The caller (OR-015) loads suppliers with `IReferenceDataReader.GetSuppliersHandlingAsync` and passes them in. This keeps rules apart from persistence and state (CLAUDE.md "Decouple rules from state"). |
| Service | `SupplierEligibility(ZipRange nationwideRange)` with `EligibilityResult Evaluate(Category category, ZipCode customerZip, bool mailOrder, IEnumerable<Supplier> suppliers)`. It re-checks `Handles(category)` itself, so a wider supplier list is safe (the reader returns suppliers for any requested category). Reuses `Supplier.Handles`, `Serves`, `IsMailOnly`. |
| Mode | `FulfillmentMode { Local, Mail }`. One private rule: `IsMailOnly → Mail; Serves(zip) → Local; else Mail` (AC5). Exposed through `EligibleSupplier(Supplier, FulfillmentMode)`. Under `mail_order = false` every eligible supplier is `Local` by construction; under `mail_order = true` it is `Mail` for mail-only or non-serving suppliers, `Local` for serving ones. |
| Result | `EligibilityResult`: immutable, `IReadOnlyList<EligibleSupplier> Suppliers` (never null, ordered by supplier ID ordinal for determinism), `bool IsEligible`, `UnfulfillableReason? Reason`. Factories keep it consistent: eligible has no reason; empty always has one. |
| Reasons | `UnfulfillableReason { NoSupplierForCategoryInArea, NoSupplierForCategory }` plus a `Code` extension returning the exact §9.4 strings. `mail_order = false` → InArea; `mail_order = true` → NoSupplierForCategory, including when the category has no supplier at all. Mapping to the response and `UNKNOWN_PRODUCT` stay in OR-015. |
| Fail safe | An empty result is a value, never an exception or `null`. Null arguments throw `ArgumentNullException`. A default `Category` throws through `Category.Value`, which is the existing behavior (not hidden). |
| Fixed rules | No experiment or config flag can change eligibility (ticket Description). The service takes no policy or experiment parameter; a source-level guard is not needed, the signature proves it. |
| Domain events, money, authorization | None: pure query rules, no state change, no money, no API surface. State this in the review; don't claim them as met. |

### Target structure

```
src/OrderRouter.Domain/Entities/Eligibility/
  FulfillmentMode.cs            Local | Mail
  UnfulfillableReason.cs        enum + Code() extension (§9.4 strings)
  EligibleSupplier.cs           Supplier + FulfillmentMode
  EligibilityResult.cs          eligible list | reason, never null
  SupplierEligibility.cs        the §7.1 + FR-09 rules (stateless domain service)
tests/OrderRouter.Tests/Domain/Entities/Eligibility/
  SupplierEligibilityTests.cs   AC1–AC7 with hand-built suppliers
  EligibilityResultTests.cs     invariants, immutability, ordering
  UnfulfillableReasonTests.cs   exact reason code strings
tests/OrderRouter.Api.IntegrationTests/Controllers/V1/
  SeededEligibilityTests.cs     AC1, AC2, AC4, AC6, AC7 on the seeded DB via IReferenceDataReader
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` and follow them. This adds domain behavior, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 analysis and §2 architecture review in Phase 1, §3 is this plan, §4 implementation in Phases 1–3, §5 review in Phase 4. Key points:
   - **C#:** C# 14 / .NET 10. File-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, final `return` on its own line, private constants camelCase (`.editorconfig`).
   - **Docs and comments:** XML docs on every public type and member, with `<example>` on the service. Comments say *why* and cite the rule (`// SC-12`, `// FR-09`).
   - **Layers:** Domain gets no EF, Npgsql, config or logging reference; `LayerDependencyTests` stay green. No Api or Infrastructure change.
   - **Fail safe:** never auto-advance an ambiguous case; no supplier means a reason, not a guess.
   - **Tests:** nearby style, `MethodName_Condition_ExpectedResult`, no Arrange/Act/Assert comments, `TestContext.Current.CancellationToken`. Integration tests use `ReferenceDataFixture` and containment assertions. Cover edge and error paths; ≥ 85% coverage of new code.
   - **Data:** never edit `Docs/Data/**`; tests only read reference data (NFR-08).
2. **Tests must pass after each phase.** Start PostgreSQL (`docker compose up -d`) and run from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing architecture, domain, normalizer, schema, seeder and read-path tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When checks pass, change its heading from `⬜ Not started` to `✅ Done` in `Plans/OR-010-eligibility-rules.md`, tick its checklist, and add a short **Result** line (test counts, deviations).
4. **Ask before the next phase.** After marking a phase done, stop, summarize changes and test results, and ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts** (ticket, requirements, ADRs, rules, or data). Don't guess. Example: if the seeded data doesn't match a sentinel the ticket names.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-010-eligibility-rules`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-010-eligibility-rules.md` with this plan, including the confirmed decision.

**Verify:** the file exists. No code changes, so no tests.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Domain analysis, architecture review and result types ✅ Done

- [x] Write `ddd-implementation` §1 and §2 under **Phase 1 notes**: terms (eligible supplier, local vs mail fulfillment, mail-only, unfulfillable item), no new aggregate (stateless service over `Supplier`), invariants (§7.1, FR-09), layer impact (Domain only), why no events/money/authorization.
- [x] Add `FulfillmentMode`, `UnfulfillableReason` (with `Code`), `EligibleSupplier` and `EligibilityResult`.
- [x] `UnfulfillableReasonTests`: exact strings `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` and `NO_SUPPLIER_FOR_CATEGORY`.
- [x] `EligibilityResultTests`: eligible has no reason; unfulfillable has a reason and an empty list; the list is never null and is read-only; eligible suppliers are ordered by ID (ordinal); an empty eligible set cannot be built without a reason.

**Verify:** working rule 2.

**Result:** 532 tests green (unit + integration), format clean. `EligibilityResult` wraps the ordered array with `Array.AsReadOnly`.

### Phase 1 notes (ddd-implementation §1, §2)

- **Terms:** *eligible supplier* handles the category and passes the §7.1 filter; *local* fulfillment is delivery by a supplier that serves the ZIP; *mail* fulfillment is shipping (mail-only, or not serving the ZIP); *mail-only* is `can_mail_order = y` with a service area exactly equal to the nationwide range (SC-09); *unfulfillable* is a category with no eligible supplier, reported with a §9.4 reason.
- **Aggregates:** no new aggregate. A stateless domain service over the read-only `Supplier` aggregate. No state changes, so no consistency boundary to protect.
- **Invariants:** §7.1 eligibility, FR-09 mode, SC-12 (local excludes mail-only), SC-06/SC-07 (mail ignores ZIP, needs `can_mail_order`). An empty result always carries a reason.
- **Layers:** Domain only. No Api or Infrastructure change.
- **Single responsibility:** the service decides eligibility; the result types carry the outcome; the reader (Infrastructure) loads suppliers. Rules depend on no abstraction because they have no I/O.
- **Events, money, authorization:** none. Pure query rules, no state change, no money, no API surface.

---

## Phase 2: Eligibility and fulfillment mode (AC1–AC7, unit) ✅ Done

- [x] Add `SupplierEligibility` per the design rows.
- [x] `SupplierEligibilityTests` with hand-built suppliers (nationwide range `00100-99999`):
  - **AC1:** local order at `00001`, `wheelchair`: only handles + serves + not mail-only are eligible (a supplier that serves but lacks the category, one that doesn't serve, and a mail-only one are all out).
  - **AC2:** `mail_order = false`: a mail-only supplier that handles the category is excluded (SC-12).
  - **AC3:** `00100-99999` with `can_mail_order = n`, ZIP inside the range: eligible as `Local` (SC-07). ZIP below `00100` (e.g. `00001`): not eligible.
  - **AC4:** `mail_order = true`: only `can_mail_order = y`, whatever the ZIP; a no-mail supplier that serves the ZIP is excluded (SC-06).
  - **AC5:** mail-only → `Mail`; mail-capable serving the ZIP → `Local`; mail-capable not serving → `Mail`; local order → all `Local`.
  - **AC6:** `mail_order = false`, nobody eligible → `NoSupplierForCategoryInArea`.
  - **AC7:** `mail_order = true`, no mail-capable supplier handles it → `NoSupplierForCategory`; a category nobody handles gives the same.
  - Edge cases: empty supplier list; suppliers for other categories passed in; category case differences (`new Category("WheelChair")`); a custom nationwide range (`00500-99999`: a `00100-99999` supplier is no longer mail-only, FR-09/AC9); boundary ZIPs `00100`, `99999`; a supplier with several segments; null arguments throw; deterministic order regardless of input order.

**Verify:** working rule 2.

**Result:** 553 tests green on the first full run except one wrong assumption, fixed. Deviation: the plan said a default `Category` throws through `Category.Value`. It does not, because `Handles` uses `HashSet.Contains`, which never reads `Value`. A default category matches nothing and yields `NoSupplierForCategoryInArea`, which is still fail-safe. The test asserts that behavior.

---

## Phase 3: Seeded-data integration tests (AC1, AC2, AC4, AC6, AC7) ✅ Done

- [x] `SeededEligibilityTests` (shared seeded DB via `ReferenceDataFixture`; load suppliers with `IReferenceDataReader`, evaluate with `SupplierEligibility(00100-99999)`). Use containment, not exact counts:
  - **AC1:** `mail_order = false`, `00001`, `wheelchair`: `SUP-T001` is eligible as `Local`; every eligible supplier handles `wheelchair`, serves `00001` and is not mail-only.
  - **AC2/SC-12:** at a ZIP covered by mail-only suppliers (e.g. `10015`, ORD-001), no mail-only supplier appears in a local result.
  - **AC4/SC-06:** `mail_order = true`, `wheelchair`: `SUP-T001` is absent, and every supplier has `CanMailOrder`.
  - **AC6:** `mail_order = false`, `00098`, a category other than `wheelchair` (e.g. `oxygen`): unfulfillable with `NO_SUPPLIER_FOR_CATEGORY_IN_AREA` (`SUP-T014`, the only supplier at `00098`, handles only `wheelchair`).
  - **AC7:** `mail_order = true` and a category no mail-capable supplier handles: `NO_SUPPLIER_FOR_CATEGORY`. Find the category from the seed data first; if none exists, use a seeded unused category through a throwaway DB, or **stop and ask**.
  - Optional: ORD-001 at `10015` shows 4 supplier candidates with 3 mail-only excluded (cross-check §6.2).
- [x] Confirm each sentinel exists in `Docs/Data/Seed/suppliers.normalized.csv` before asserting on it; stop and ask if the data differs from the requirements.

**Verify:** working rule 2 (needs PostgreSQL).

**Result:** 559 tests green. Sentinels `SUP-T001` and `SUP-T014` confirmed in `suppliers.normalized.csv` (`SUP-T014` is the only supplier serving `00098`). Deviation for AC7: every seeded category has mail-capable suppliers, so no seeded category triggers `NO_SUPPLIER_FOR_CATEGORY` on its own. Covered two ways with the real reader: a category absent from the seed, and the seeded non-mail sentinels (`SUP-T001`, `SUP-T014`) as the only candidates. The optional ORD-001 cross-check was not added; the AC2 test uses `cpap` at `10015`, where seeded mail-only suppliers serve.

---

## Phase 4: Post-implementation review and follow-ups ✅ Done

- [x] Run `ddd-implementation` §5 and write a **Review report**: *Applies and met* (layering/SOLID, rules fixed and experiment-free, fail safe, tests per AC), *N/A with reasons* (domain events/audit, money, authorization, PCI-DSS/SOX/LGPD), *Not verified* (coverage %, performance with large supplier sets).
- [x] Record follow-ups, not done here:
  - **OR-015:** bind `NationwideZipRange` (default `00100-99999`, `ZipRange.Parse`, validate at startup), register `SupplierEligibility`, map `UnfulfillableReason.Code` to `reason_code`, and add `UNKNOWN_PRODUCT` for unresolved products.
  - **OR-011/OR-013:** consume `EligibilityResult` and `FulfillmentMode` (ranking key 4).
  - Update the OR-002 plan note: binding moved from OR-010 to OR-015.
- [x] Final run: `docker compose up -d`, then `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

---

**Result:** final run green: `dotnet build` 0 warnings, 0 errors; `dotnet test` 559 passed; `dotnet format --verify-no-changes` clean.

### Review report (ddd-implementation §5)

**Applies and met**
- Layering and SOLID: Domain only, no EF, config or logging reference. Stateless service, one responsibility per type. Layer tests stay green.
- Rules are fixed: the service takes no policy or experiment parameter, only the nationwide range.
- Fail safe: no supplier returns a reason, never `null`, an exception or a guess. Null suppliers throw `ArgumentNullException`.
- Tests per AC: AC1–AC7 unit and seeded (AC3 and AC5 unit only), plus edge cases (empty list, case, custom nationwide range, boundaries, several segments, ordering).

**N/A with reasons**
- Domain events and audit trail: no state change.
- Money and rounding: none.
- Authorization: no API surface or aggregate mutation.
- PCI-DSS, SOX, LGPD: no payment, financial or personal data handled.

**Not verified**
- Coverage percentage (not measured; every branch of the new code is exercised by tests).
- Performance with large supplier sets. Evaluation is a linear pass per call.

### Follow-ups (not done here)
- **OR-015:** bind `NationwideZipRange` (default `00100-99999`, `ZipRange.Parse`, validate at startup), register `SupplierEligibility`, map `UnfulfillableReason.Code` to `reason_code`, add `UNKNOWN_PRODUCT`. This overrides the OR-002 plan note that assigned binding to OR-010.
- **OR-011/OR-013:** consume `EligibilityResult` and `FulfillmentMode` (ranking key 4).
- Update the OR-002 plan note: binding moved from OR-010 to OR-015 (that file was not edited here).

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-010-eligibility-rules.md` |
| New (Domain) | `src/OrderRouter.Domain/Entities/Eligibility/{FulfillmentMode,UnfulfillableReason,EligibleSupplier,EligibilityResult,SupplierEligibility}.cs` |
| New (tests) | see *Target structure* |
| Reused | `Supplier.Handles/Serves/IsMailOnly` (`ReferenceData/Supplier.cs`), `ZipRange`, `ZipCode`, `Category`, `IReferenceDataReader`, `ReferenceDataFixture`, `SeedFiles.Real` |
| Unchanged | Api, Infrastructure, migrations, `Docs/Data/**` |

## Verification summary

| AC | Verified by | Phase |
|---|---|---|
| AC1 Local order | unit + seeded (`SUP-T001`) | 2, 3 |
| AC2 Mail-only excluded locally | unit + seeded | 2, 3 |
| AC3 Nationwide local-only | unit | 2 |
| AC4 Mail order | unit + seeded (`SUP-T001` excluded) | 2, 3 |
| AC5 Fulfillment mode | unit | 2 |
| AC6 No supplier, local | unit + seeded (`00098`) | 2, 3 |
| AC7 No supplier, mail | unit + seeded | 2, 3 |

End to end: `docker compose up -d && dotnet build && dotnet test && dotnet format --verify-no-changes`.

# Plan: OR-002 — Model suppliers, products and ZIP coverage in the domain

**Ticket:** [Docs/Tickets/OR-002-domain-reference-model.md](../Docs/Tickets/OR-002-domain-reference-model.md)
**Depends on:** OR-001 (done, see [OR-001-solution-skeleton.md](OR-001-solution-skeleton.md))
**References:** Technical Requirements §3, §4.4, §5.1, §7.1, FR-09, DQ-04, DQ-06, DQ-09, DQ-10, DQ-14, SC-07, SC-09, NFR-10; [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`

## Context

OR-001 created the layered solution with no domain code. OR-002 adds the first domain types: the reference data that the routing rules use. They are plain domain objects in `OrderRouter.Domain`, with no database, configuration or framework code. Later tickets build on them:

- **OR-003** (CSV normalizer, references Domain only) builds these objects and skips rows they reject, logging the reason. Error messages must therefore be clear and specific.
- **OR-005** (seeder) builds these objects from the normalized files and fails the seed on any rejected row (for example a 4-digit ZIP or a rating of `11`). The domain must **never** pad, fix or guess.
- **OR-006** (read path) maps database rows (`int` ZIPs, `numeric` rating) to these objects.
- **OR-010** (eligibility) calls `Handles`, `Serves` and `IsMailOnly`.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Meaning of "exactly the nationwide range" (AC5, §7.1) | **Single segment.** A supplier is mail-only only when `CanMailOrder` is true **and** its service area is exactly one segment equal to the nationwide range. `{00100-99999, 00050}` or `{00100-50000, 50001-99999}` is **not** mail-only. Segments are not merged. This matches the data: all 60 nationwide suppliers have a single `00100-99999` segment. |
| Where `NationwideZipRange` is configured (FR-09, AC9) | **Domain parameter only.** `Supplier.IsMailOnly(ZipRange nationwideRange)` takes the range as an argument; the domain never reads configuration. AC9 is tested at domain level. Binding the appsettings value (default `00100-99999`) is a follow-up for OR-010 (see Phase 5). |
| Supplier invariants | A supplier **must** have at least one category **and** at least one service-area segment. Fail-safe (NFR-10), and the data has none missing. |
| Rating precision (DQ-06) | A rating is a `decimal` from 1 to 10 inclusive, **rounded to 2 decimal places, with halves rounded up** (`Math.Round(value, 2, MidpointRounding.AwayFromZero)`): `9.255` → `9.26`, `9.254` → `9.25`. The domain does this rounding itself, explicitly, so the database never rounds again. **Conflict to resolve:** ADR-0002 stores `numeric(3,1)`, which would round again to one decimal place (`9.25` → `9.3`). OR-004 must use `numeric(4,2)` and ADR-0002 must be updated (follow-up, Phase 5). **Edge case, decided provisionally (not confirmed by the engineer):** the 1–10 range check runs on the **raw** value, before rounding, so `0.996` and `10.004` are rejected. This is the fail-safe reading (NFR-10). Flip it in `Rating` if the engineer wants the rounded value checked. |

### Other design choices (from the rules and requirements; no question needed)

| Topic | Choice and reason |
|---|---|
| `Category` shape | `readonly record struct` that trims and lower-cases (invariant culture) on construction and rejects empty or whitespace. Equality and hash code are ordinal on the normalized value, so `HashSet<Category>` needs no custom comparer (§5.1). |
| `ZipCode` | Strict: exactly 5 ASCII digits (`char.IsAsciiDigit`, so Unicode digits like `٠` are rejected). **No trimming or padding**: OR-005 must reject a 4-digit ZIP, and padding is OR-003's job. `00000`–`00099` are valid (sentinel ZIPs, DQ-14). Exposes the string value and the numeric value (for range comparison and OR-006's `int` columns). |
| `ZipRange` | `readonly record struct` with inclusive `Start`/`End`; `Start <= End`, else reject. A single ZIP is `Start == End`. Has a strict `Parse` for `"NNNNN"` or `"NNNNN-NNNNN"` so the seeder (OR-005) and later config binding (OR-010) share one parser. |
| `ServiceArea` | Immutable, non-empty list of `ZipRange` segments, kept as given (overlaps allowed; the data has overlapping ranges). `Contains(ZipCode)` and `IsExactly(ZipRange)`. |
| `Rating` | Its own value object (`readonly record struct` over `decimal`) so the 1–10 range and the 2-decimal rounding rule live in one place. `Supplier.Rating` is `Rating?`: `null` means unrated (AC7), never `0`. |
| `SupplierId`, `ProductCode` | Kept as **opaque trimmed strings**, non-empty. Never re-padded or re-formatted (DQ-09). Case-insensitive product lookup is OR-006's job (FR-05), so `Product` keeps the code's case. |
| `Supplier` identity | Entity; equality by `SupplierId` only (DQ-10: duplicate names are distinct suppliers). It is the root of a small read-only reference-data aggregate. |
| Errors | One domain exception type, `DomainValidationException` (in `OrderRouter.Domain/Common`), with a clear message naming the field and the bad value. `ZipCode`, `ZipRange` and `Category` also get `TryParse`/`TryCreate` so callers that only want a yes/no (request boundary, normalizer) don't use exceptions for flow control. |
| Domain events | **None.** Reference data has no business state changes in this ticket; it's loaded and read. Stated in the domain analysis, not claimed as met. |
| Money / `decimal` rules | N/A: no money. `decimal` is still used for `Rating` (DQ-06: string comparison would rank `9` above `10.0`). |

### Target domain model

```
OrderRouter.Domain/
  Common/
    DomainValidationException.cs
  ReferenceData/
    Category.cs        readonly record struct   trim + lower-case, non-empty
    ZipCode.cs         readonly record struct   exactly 5 ASCII digits
    ZipRange.cs        readonly record struct   Start <= End, inclusive, Contains(ZipCode), Parse
    ServiceArea.cs     sealed class             non-empty segments, Contains(ZipCode), IsExactly(ZipRange)
    Rating.cs          readonly record struct   1..10, rounded to 2 decimal places (halves up)
    Supplier.cs        sealed class (entity)    Id, Name, Categories, ServiceArea, Rating?, CanMailOrder
                                                Handles(Category), Serves(ZipCode), IsMailOnly(ZipRange nationwideRange)
    Product.cs         sealed class             ProductCode, Name, Category
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before you write code, and follow them. This ticket adds a new domain model, so use the `ddd-implementation` skill (`/ddd-implementation`): §1 domain analysis and §2 architecture review in Phase 1, §4 implementation in Phases 1–4, §5 post-implementation review in Phase 5. Key points:
   - C# 14 / .NET 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof` instead of member-name strings.
   - XML doc comments on every public type and member, with `<example>`/`<code>` where useful (e.g. `Category`, `ZipRange.Parse`). Comments explain *why* (cite the requirement ID, e.g. `// SC-09`).
   - Immutable value objects; domain logic lives in the domain types (rich model), not in helpers.
   - Domain stays free of framework, configuration and EF Core references (the OR-001 architecture tests enforce the project graph).
   - Tests: `MethodName_Condition_ExpectedResult`, no `Arrange`/`Act`/`Assert` comments, follow the style in `tests/OrderRouter.UnitTests`. Cover edge cases and error scenarios; test value-object equality and immutability. Target ≥ 85% coverage of the new domain code.
2. **Tests must pass after each phase.** Before you mark a phase done, run from the repo root `dotnet build` (0 errors, 0 new warnings), `dotnet test` (all pass, including the OR-001 architecture tests) and `dotnet format --verify-no-changes` (clean), plus the phase's own checks.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in this file, tick its checklist items, and add a short **Result** line (test counts, anything that deviated from the plan).
4. **Ask before the next phase.** After you mark a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before you start the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule or ticket text is unclear or contradicts another, stop and ask the engineer. Don't guess.
6. No commits unless the engineer asks. If asked, branch off `main` first (for example `feature/OR-002-domain-reference-model`).

---

## Phase 0 — Save the plan ✅ Done

- [x] Create `Plans/OR-002-domain-reference-model.md` with this plan. This file is the one whose status gets updated from now on.

**Verify:** the file exists. No code changes, so there's nothing to test.

---

## Phase 1 — Domain analysis, error type and `Category` (AC1, AC2) ✅ Done

- [x] Write the `ddd-implementation` §1 domain analysis and §2 architecture review under **Phase 1 notes** below. Cover:
  - Concepts and relationships (Supplier → Categories, Supplier → ServiceArea → ZipRanges, Product → Category) and the ubiquitous-language terms from §3 (*Service area*, *Mail-only supplier*, *Local fulfillment*).
  - Aggregate boundary: `Supplier` is the root; `ServiceArea`, `Rating` and the category set are owned values. `Product` is a separate small root. No cross-aggregate references beyond `Category` values.
  - Invariants (from the tables above), affected layers (Domain only), no domain events (and why), and security/compliance (no PHI or payment data; fail-safe rejection per NFR-10).
- [x] Add `src/OrderRouter.Domain/Common/DomainValidationException.cs`: a sealed exception for broken domain invariants, with a message that names the field and value.
- [x] Add `src/OrderRouter.Domain/ReferenceData/Category.cs`:
  - `readonly record struct Category` with a validating constructor; `Value` holds the trimmed, `ToLowerInvariant` key.
  - Rejects `null`, empty and whitespace with `DomainValidationException` (AC2).
  - `TryCreate(string?, out Category)` for callers that don't want exceptions.
  - `ToString()` returns `Value`.
  - Document the `default(Category)` caveat (a struct can't stop `default`), and make `Value` of a default instance fail clearly rather than return `null`.
- [x] Add `tests/OrderRouter.UnitTests/Domain/ReferenceData/CategoryTests.cs`:
  - AC1: `" CPAP "` and `"cpap"` are equal and have the same hash code; they collapse to one entry in a `HashSet<Category>`.
  - AC2: `""`, `"   "`, `null` → `DomainValidationException` with a clear message (`[Theory]`).
  - Mixed case/inner text kept (`"Hospital Bed"` → `"hospital bed"`); `TryCreate` returns `false` for bad input.

**Verify:** build, test, format all pass (working rule 2). `CategoryTests` all green.

**Result:** 24 tests passed after Phase 1; 0 warnings; format clean.

### Phase 1 notes

**Concepts.** Supplier → Categories; Supplier → ServiceArea → ZipRanges; Product → Category. Terms follow Technical Requirements §3: *Service area*, *Mail-only supplier*, *Local fulfillment*.

**Aggregates.** `Supplier` is the root; `ServiceArea`, `Rating` and the category set are owned values. `Product` is a separate small root. The only link between them is the `Category` value.

**Invariants.** As in the decisions tables above. Each is enforced in the constructor of the type that owns it.

**Layers.** Domain only. **Events:** none, because reference data is loaded and read and has no business state changes. **Security/compliance:** no PHI or payment data; invalid input is rejected, never repaired (NFR-10).

**Architecture review.** Domain has no project or package references (checked by the OR-001 architecture tests). Value objects are immutable structs; entities compare by id.

---

## Phase 2 — `ZipCode`, `ZipRange` and `ServiceArea` (AC3, AC4 at value level) ✅ Done

- [x] Add `ZipCode.cs`:
  - Accepts exactly 5 ASCII digits; no trimming, no padding. Rejects `"2130"`, `"0213A"`, `" 02130"`, `"021300"`, `null`, Unicode digits.
  - `Parse(string)`, `TryParse(string?, out ZipCode)`, and `FromNumber(int)` (0–99999, formats as zero-padded 5 digits; exists because ADR-0002 stores ZIPs as `int`, so OR-006 needs it).
  - Exposes `Value` (string) and `Number` (int). Implements `IComparable<ZipCode>` by `Number`.
- [x] Add `ZipRange.cs`:
  - `ZipRange(ZipCode start, ZipCode end)`; rejects `start > end`.
  - `Single(ZipCode)` for `start == end`.
  - `Contains(ZipCode)`: inclusive on both ends.
  - `Parse(string)` / `TryParse`: strict `"NNNNN"` or `"NNNNN-NNNNN"`, no spaces, no padding. `ToString()` returns the same format.
- [x] Add `ServiceArea.cs`:
  - Built from a non-empty collection of `ZipRange`; copies it into an immutable list (callers can't change it later). Empty → `DomainValidationException`.
  - `Contains(ZipCode)`: true if any segment contains the ZIP. Overlaps allowed.
  - `IsExactly(ZipRange)`: true only when there is exactly one segment and it equals the given range (confirmed "single segment" rule).
- [x] Add tests under `tests/OrderRouter.UnitTests/Domain/ReferenceData/`:
  - `ZipCodeTests`: AC3 (`"02130"` accepted; `"2130"`, `"0213A"` rejected), plus the edge cases above, `00000`, `99999`, `FromNumber` bounds (`-1`, `100000` rejected; `2130` → `"02130"`), equality.
  - `ZipRangeTests`: AC4 at range level (`02114-02212` contains `02114` and `02212`, not `02213` or `02113`); start > end rejected; single ZIP; `Parse` valid and invalid (`"2114-2212"`, `"02114 - 02212"`, `"02114-"`).
  - `ServiceAreaTests`: empty rejected; overlapping segments; `IsExactly` true for `{00100-99999}`, false for `{00100-99999, 00050}` and `{00100-50000, 50001-99999}`; list is immutable from outside.

**Verify:** build, test, format all pass.

**Result:** done together with Phases 3 and 4; see Phase 4.

---

## Phase 3 — `Rating` and `Product` (AC8 at value level) ✅ Done

> **Open edge case, provisionally resolved to "raw value" (see decisions table). Original question:** does the 1–10 check run on the raw value or the rounded value? It matters only for values just outside the range. For example, `0.996` rounds to `1.00` and `10.004` rounds to `10.00`. Checking the raw value rejects both; checking the rounded value accepts both. Record the answer in the decisions table, then continue.

- [x] Add `Rating.cs`:
  - `readonly record struct Rating` over `decimal`. On creation, round to 2 decimal places with `MidpointRounding.AwayFromZero` (`9.255` → `9.26`, `9.254` → `9.25`) and enforce 1 to 10 inclusive (before or after rounding, per the engineer's answer). Rejects `0`, `11`, `0.99`, `10.01` with `DomainValidationException`.
  - Equality is numeric on the rounded value (`10` equals `10.0`, DQ-06; `9.255` equals `9.26`). Document why `decimal` (no binary float drift), why `AwayFromZero` (the engineer's rule; .NET's default `ToEven` would turn `9.245` into `9.24`), and why the domain rounds explicitly (one place, so the database never rounds again).
- [x] Add `Product.cs`:
  - `ProductCode` (trimmed, non-empty, case kept, never re-formatted), `Name` (trimmed, non-empty), `Category`.
  - Equality by `ProductCode` (ordinal).
- [x] Add `RatingTests`: bounds `1`, `10`, `1.00`, `9.25` accepted; `0`, `11`, `0.99`, `10.01` rejected; `10 == 10.0`. Rounding: `9.255` → `9.26`, `9.254` → `9.25`, `9.245` → `9.25` (proves halves go up, not to even), `9.2` stays `9.2`. The edge-case values `0.996` and `10.004` are tested per the engineer's answer.
- [x] Add `ProductTests` (valid build; empty code/name rejected; code trimmed but case kept).

**Verify:** build, test, format all pass.

---

## Phase 4 — `Supplier` (AC4–AC9) ✅ Done

- [x] Add `Supplier.cs`:
  - Constructor/factory takes `id`, `name`, categories, `ServiceArea`, `decimal? rating`, `canMailOrder`. It builds the `Rating` itself, so AC8 holds literally: creating a supplier with `0` or `11` fails. Validates: `id` trimmed and non-empty (opaque, not re-formatted, DQ-09); `name` non-empty; at least one category (duplicates after normalization collapse into one); `ServiceArea` not null. Report every invalid field in the exception message, not just the first, so OR-003 logs a useful reason.
  - Exposes `Categories` as `IReadOnlySet<Category>` (immutable copy).
  - `Handles(Category)`: *do you handle this category?*
  - `Serves(ZipCode)`: *do you serve this ZIP?* Delegates to `ServiceArea.Contains`.
  - `IsMailOnly(ZipRange nationwideRange)`: *are you mail-only?* `CanMailOrder && ServiceArea.IsExactly(nationwideRange)` (SC-09). XML doc explains the parameter (FR-09: configurable, never stored, ADR-0002) and cites SC-07 for the `can_mail_order = n` case.
  - Equality and hash code by `Id` only (DQ-10).
- [x] Add `SupplierTests` (shared test builder for valid suppliers to keep tests short):
  - AC4: serves `02114-02212` → `02114` yes, `02212` yes, `02213` no.
  - AC5: `can_mail_order = y`, area exactly `00100-99999`, nationwide `00100-99999` → mail-only.
  - AC6: `can_mail_order = n`, area `00100-99999` → not mail-only (SC-07).
  - AC7: no rating → `Rating` is `null`.
  - AC8: rating `0` or `11` → creating the supplier throws `DomainValidationException` (`[Theory]`).
  - AC9: nationwide configured as `00500-99999`; supplier on exactly `00500-99999` is mail-only, supplier on exactly `00100-99999` is not.
  - Extra: `Handles` with differently cased input (`Category(" CPAP ")` vs supplier's `cpap`); empty categories rejected; empty/whitespace id or name rejected; message lists all bad fields; equality by id (two suppliers with the same name but different ids are not equal); mail-capable supplier with nationwide plus an extra segment is not mail-only.

**Result:** Phases 2–4 done in one pass: 105 tests passed (unit + integration), 0 warnings, format clean. Deviations: constants are camelCase to satisfy `.editorconfig`; `ZipCode` stores an `int`, so `default(ZipCode)` is `00000` (valid, documented); `Product` and `Supplier` keep id/code as trimmed strings.

**Verify:** build, test, format all pass. Every ticket AC maps to at least one passing test (see table below).

---

## Phase 5 — Clean-up, full verification and post-implementation review ✅ Done

- [x] Delete `tests/OrderRouter.UnitTests/PlaceholderTests.cs`. Its comment says real tests replace it from OR-002 on.
- [ ] Run coverage for the Domain project (`dotnet test --collect:"XPlat Code Coverage"` or the Microsoft.Testing.Platform coverage extension, whichever works with the OR-001 `global.json` runner setting). Target ≥ 85% for `OrderRouter.Domain/ReferenceData`. If adding a coverage package is needed, ask first.
- [x] Run the `ddd-implementation` §5 review and record it under **Review report** below. Report honestly:
  - **Applies:** value objects immutable with value equality; invariants in the domain; ubiquitous language matches §3; SRP; no framework references in Domain; XML docs; test naming; edge-case and error tests; `decimal` for ratings.
  - **N/A (say why, don't claim met):** domain events and audit trail, money/rounding, transactions, async/DI, compliance (PCI-DSS, SOX, LGPD).
- [x] Record the follow-ups for other tickets (don't do them here):
  - **OR-004 / ADR-0002:** change `suppliers.rating` from `numeric(3,1)` to `numeric(4,2)` so ratings rounded to 2 decimal places are stored without rounding again. Update ADR-0002's schema diagram.
  - **OR-010:** bind `NationwideZipRange` from configuration (default `00100-99999`), parse it with `ZipRange.Parse`, validate at startup, and pass it to `Supplier.IsMailOnly`. *Done in OR-015: `RoutingOptions` + `RoutingOptionsValidator` (`ValidateOnStart`), and `SupplierEligibility` is built from the option.*
  - **OR-003:** use `TryParse`/`TryCreate` or catch `DomainValidationException` to skip rows with a reason; do the 4-digit ZIP padding before building `ZipCode`.
- [x] Final `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` from the repo root: all green.

### Review report

**Applies:** immutable value objects with value equality; invariants enforced in the domain; ubiquitous language matches §3; no framework references in Domain; XML docs on public members; `MethodName_Condition_ExpectedResult` test names; edge-case and error tests; `decimal` for ratings; every AC (AC1–AC9) has a passing test.

**N/A:** domain events and audit trail (no state changes), money/rounding (no money; rating rounding is the only rule), transactions, async/DI, compliance (PCI-DSS, SOX, LGPD).

**Not done:** coverage measurement. `dotnet test --coverage` needs `Microsoft.Testing.Extensions.CodeCoverage`, which is not referenced; adding a package needs the engineer's approval. Every public member is exercised by the tests, but the ≥ 85% target is not measured.

**Follow-ups (other tickets):** OR-004/ADR-0002 `numeric(4,2)` for `suppliers.rating`; OR-010 binds `NationwideZipRange`; OR-003 uses `TryParse`/`TryCreate` and pads 4-digit ZIPs first.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-002-domain-reference-model.md` |
| New (domain) | `src/OrderRouter.Domain/Common/DomainValidationException.cs`; `src/OrderRouter.Domain/ReferenceData/{Category,ZipCode,ZipRange,ServiceArea,Rating,Supplier,Product}.cs` |
| New (tests) | `tests/OrderRouter.UnitTests/Domain/ReferenceData/{Category,ZipCode,ZipRange,ServiceArea,Rating,Product,Supplier}Tests.cs` |
| Deleted | `tests/OrderRouter.UnitTests/PlaceholderTests.cs` |
| Unchanged | `src/OrderRouter.Domain/DomainAssemblyMarker.cs` (still used by the architecture tests) |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Category ignores case and spaces | `CategoryTests`: equal and same hash code | 1 |
| AC2: Empty category rejected | `CategoryTests`: empty/whitespace/null throw `DomainValidationException` | 1 |
| AC3: ZIP is exactly 5 digits | `ZipCodeTests`: only `"02130"` accepted | 2 |
| AC4: ZIP ranges are inclusive | `ZipRangeTests` and `SupplierTests`: `02114` yes, `02212` yes, `02213` no | 2, 4 |
| AC5: Mail-only supplier | `SupplierTests`: `y` + exactly `00100-99999` → mail-only | 4 |
| AC6: Nationwide local-only is not mail-only | `SupplierTests`: `n` + `00100-99999` → not mail-only | 4 |
| AC7: Unrated supplier | `SupplierTests`: `Rating` is `null` | 4 |
| AC8: Rating out of range | `RatingTests` and `SupplierTests`: `0` and `11` rejected | 3, 4 |
| AC9: Nationwide range configurable | `SupplierTests`: `00500-99999` configured → first mail-only, second not | 4 |

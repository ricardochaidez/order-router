# Plan: OR-003: Parse and normalize the supplier and product CSV files

**Ticket:** [Docs/Tickets/OR-003-csv-normalization.md](../Docs/Tickets/OR-003-csv-normalization.md)
**Depends on:** OR-002 (done, see [OR-002-domain-reference-model.md](OR-002-domain-reference-model.md))
**References:** Technical Requirements §4.4, §5.1, §6 (DQ-01 to DQ-12, DQ-14), FR-04, FR-05, NFR-04, NFR-10; [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Seeding and migrations, CsvHelper); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`

## Context

OR-002 added the reference-data domain types (`Supplier`, `Product`, `Category`, `ZipCode`, `ZipRange`, `ServiceArea`, `Rating`, `DomainValidationException`). They are strict: they **never** pad, fix or guess. The raw CSVs in `Docs/Data` are messy:

- 1,090 ZIP tokens have only 4 digits.
- Lists and ranges are mixed in one cell.
- Ratings come as `10` / `10.0` / `no ratings yet`.
- The header has a typo (`suplier_name`) and a column named `can_mail_order?`.
- Categories mix `CPAP` and `cpap`.
- 5 product codes appear twice, and the duplicates are identical.

OR-003 is the **only** place the §6 rules run. A separate console tool reads the raw files and builds domain objects from them; that step is the validation. It writes clean files to `Docs/Data/Seed/`, and those files are committed. OR-005's seeder later loads them with no normalization. The tool owns all data-quality reporting (NFR-04).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Conflicting duplicate products (AC8 / FR-04 / DQ-12 vs NFR-10) | **The whole run fails.** Nothing is written, and the error names the product code. A duplicate counts as **conflicting if any normalized field differs** (category **or** name). This is the fail-safe reading. A duplicate is **identical** only when code, name and category match after normalization; one copy is kept and a warning is logged. NFR-10 says the row is "excluded", which contradicts this; it is recorded as a doc follow-up (Phase 6). |
| Where the tests live | In the existing `tests/OrderRouter.Tests`, which gets a `ProjectReference` to `tools/OrderRouter.DataNormalizer`. The API never references the tool. "Ships with neither the API nor the tests" means the tool is not part of the API deployment or test runtime behaviour. |
| Normalized file format | **Source shape, cleaned.** `suppliers.normalized.csv`: `supplier_id,supplier_name,service_zips,product_categories,rating,can_mail_order`. `products.normalized.csv`: `product_code,product_name,category`. Details below. |
| Logging | The pure functions return a **report object**, and tests assert against it. The console host logs through `ILogger` (`Microsoft.Extensions.Logging.Console`, tool project only). `CsvHelper` (named in ADR-0002) is added to the tool project only. |

### Other design choices (from the rules and requirements; no question needed)

| Topic | Choice and reason |
|---|---|
| Output file details | **suppliers:** `service_zips` is a quoted, `", "`-joined list in source order. A single ZIP is written `NNNNN`, a range `NNNNN-NNNNN`; both parse with OR-002's `ZipRange.Parse`. `product_categories` holds lower-case keys, de-duplicated and sorted ordinally so the output is deterministic. `rating` is invariant-culture with format `0.##` (`10.0` → `10`, `6.7` → `6.7`), or **empty** when unrated. `can_mail_order` is `y`/`n`. **products:** values trimmed, category lower-case. **Both:** UTF-8 without BOM, **LF** line endings, header row always written, rows in **source order** (first occurrence wins). This keeps the output stable and diffs readable (AC13). |
| Row numbers | The physical line number in the source file, with the header as line 1, so it matches what you see in an editor or spreadsheet (AC9, AC12). |
| ZIP tokens (DQ-01, DQ-02, DQ-03) | Split the cell on `,` and trim each token. Each token is `Z` or `Z-Z`. Left-pad each `Z` that has 1–4 ASCII digits to 5 digits **before** building `ZipCode` / `ZipRange`. Reject the **row** (skip with a reason) for an empty token, a non-digit, more than 5 digits, a malformed range, or start > end. Sentinel ZIPs `00001`–`00098` are valid (DQ-14). Overlaps are kept as given (`ServiceArea` allows them). |
| Rating (DQ-06, AC3) | Trim. `no ratings yet` (case-insensitive) → `null`. Otherwise parse as `decimal` with `NumberStyles.Number` and `CultureInfo.InvariantCulture`, then build it through `Supplier`, so OR-002's `Rating` enforces 1–10 and the 2-decimal-place rounding. Unparseable or out-of-range → skip the row (AC9, NFR-10). |
| Mail flag (DQ-08, AC5) | `y`/`n`, case-insensitive, trimmed. Anything else → skip the row. |
| Headers (DQ-07, DQ-08) | Map columns by header name: `supplier_name` or `suplier_name`, and `can_mail_order?`. If both name spellings are present, or a required column is missing, the **file** fails (it isn't a single bad row). Unknown extra columns are ignored (tolerant reader). |
| Supplier IDs (DQ-09, DQ-10, AC6) | Passed as-is to `Supplier` (trimmed only, never re-padded). Identity is the ID. A **duplicate supplier ID** (none in the data) fails the run, the same fail-safe as conflicting products, because there is no safe way to pick one. |
| Product duplicate key | Trimmed code, compared **case-insensitively**. FR-05 looks products up case-insensitively, so `ab-1` and `AB-1` would collide later. The kept row keeps the case of its first occurrence. |
| Domain row errors | The `DomainValidationException` message becomes the skip reason. Callers use `TryParse`/`TryCreate` where they exist, per the OR-002 follow-up. |
| Purity | Normalization functions take `TextReader`s and return results. All file I/O lives in a thin runner and `Program`. Inputs are opened with `FileAccess.Read` / `FileShare.Read` only (AC11). Outputs are written only after **both** files normalize without a fatal error, so a failed run leaves `Docs/Data/Seed` as it was. |
| Paths (AC14) | By default the tool finds the repo root by walking up from the current directory to `OrderRouter.slnx`, then uses `Docs/Data` → `Docs/Data/Seed`. It creates the output folder if needed. Optional `--input <dir>` / `--output <dir>` arguments are used by tests. Exit code `0` on success, `1` on a fatal error. |
| Domain events / money / async | None. This is an offline, synchronous file transform. File I/O is small (≈2,300 rows), so sync is fine in a console tool. State this in the review; don't claim it as met. |

### Target structure

```
tools/OrderRouter.DataNormalizer/
  OrderRouter.DataNormalizer.csproj   Exe; refs Domain only; CsvHelper, Microsoft.Extensions.Logging.Console
  Program.cs                          arg/path resolution, logging, exit code
  NormalizationRunner.cs              read files → normalize → write both or neither
  Parsing/ServiceZipsParser.cs        DQ-01/02/03 → IReadOnlyList<ZipRange> | error
  Parsing/RatingParser.cs             DQ-06 → decimal? | error
  Parsing/MailOrderFlagParser.cs      DQ-08 → bool | error
  Suppliers/SupplierNormalizer.cs     TextReader → SupplierNormalizationResult (suppliers + report)
  Products/ProductNormalizer.cs       TextReader → ProductNormalizationResult (products + report) | conflict error
  Reporting/FileNormalizationReport.cs  RowsRead, RowsWritten, SkippedRows (RowNumber, Reason), DuplicatesRemoved (Code, RowNumbers)
  Reporting/NormalizationException.cs   file-level/fatal failures (missing column, conflicting duplicate, duplicate supplier id)
  Output/NormalizedCsvWriter.cs       deterministic writer (format above)
Docs/Data/Seed/suppliers.normalized.csv, products.normalized.csv   generated, committed
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code, and follow them. This is a non-trivial, cross-project feature, so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review in Phase 1.
   - §4 implementation in Phases 1–5.
   - §5 post-implementation review in Phase 6.

   Key points:
   - **C#:** C# 14 / .NET 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`.
   - **Docs and comments:** XML doc comments on every public type and member, with `<example>`/`<code>` where useful (e.g. `ServiceZipsParser`). Comments explain *why* and cite the requirement ID (e.g. `// DQ-01`). Comment each library's purpose where it's used (CsvHelper, Microsoft.Extensions.Logging).
   - **Domain boundary:** never weaken or bypass OR-002 domain invariants. The normalizer prepares input and the domain validates it. Domain stays dependency-free.
   - **Tests:** follow the style in `tests/OrderRouter.Tests`: `MethodName_Condition_ExpectedResult` names and no `Arrange`/`Act`/`Assert` comments. Cover edge cases and error paths. Target ≥ 85% coverage of the new code.
   - **Source data:** **never edit** `Docs/Data/suppliers.csv`, `products.csv` or `sample_orders.json`.
2. **Tests must pass after each phase.** Before marking a phase done, run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including the OR-001 architecture tests and the OR-002 domain tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in this file;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text or data finding is unclear or contradicts another, stop and ask the engineer. Don't guess.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-003-csv-normalization`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-003-csv-normalization.md` with this plan. From now on, that file is the one whose status gets updated.

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Domain analysis, project skeleton, architecture guard ✅ Done

- [x] Write the `ddd-implementation` §1 domain analysis and §2 architecture review under **Phase 1 notes**:
  - Concepts: raw row → normalized row → `Supplier`/`Product`. Terms from §3.
  - Layers: the tool sits outside Domain/Infrastructure and references only Domain.
  - Invariants: they stay in the domain. The normalizer only cleans syntax (padding, splitting, casing, header aliases).
  - No domain events, and why.
  - Fail-safe behavior (NFR-10).
  - No PHI in the data.
- [x] Create `tools/OrderRouter.DataNormalizer` with `dotnet new console`:
  - `ProjectReference` to Domain only.
  - Add `CsvHelper` and `Microsoft.Extensions.Logging.Console` (latest stable versions).
  - Add a stub `Program.cs` that exits `0`.
  - The project inherits `Directory.Build.props`.
- [x] Add a `/tools/` folder with the project to `OrderRouter.slnx`.
- [x] Add a `ProjectReference` from `tests/OrderRouter.Tests` to the tool project.
- [x] Extend `tests/OrderRouter.Tests/Architecture/LayerDependencyTests.cs`, reusing its csproj-reading approach and `FindRepositoryRoot`:
  - the tool references **only** Domain;
  - no `src/` project references the tool (so it never ships with the API).
- [x] Add `Docs/Data/Seed/*.csv text eol=lf` to a `.gitattributes`, so committed output stays byte-stable on every OS (AC13).

**Verify:** working rule 2. `dotnet run --project tools/OrderRouter.DataNormalizer` exits 0. The new architecture tests pass.

### Phase 1 notes

**Domain analysis (ddd-implementation §1)**
- Concepts: a raw CSV row becomes a normalized row, which becomes a `Supplier` / `Product`. Terms follow Technical Requirements §3 (supplier, product, category, service area, ZIP range, rating).
- Aggregate boundaries: unchanged. `Supplier` and `Product` stay OR-002's read-only reference data; the tool builds them but does not own or extend them.
- Layers: the tool sits outside Domain/Infrastructure, references only Domain, and nothing under `src/` references it (both enforced by `LayerDependencyTests`).
- Invariants stay in the domain. The normalizer cleans syntax only: header aliases, ZIP padding, splitting, casing. Rating range, ZIP validity, non-empty fields and category rules are enforced by building the domain objects.
- Fail-safe (NFR-10): a bad row is skipped and reported; a fatal file-level problem (missing/ambiguous column, conflicting duplicate product, duplicate supplier id) fails the run and writes nothing.
- No PHI: the data is supplier and product reference data only. No patient, prescriber or order data is read.

**Architecture review (§2)**
- SRP: parsers, normalizers, writer, runner and host are separate types. Pure functions take `TextReader`/`TextWriter`; file I/O lives only in `NormalizationRunner`.
- No domain events: this is an offline file transform with no state change in the domain, so there is nothing business-significant to publish.
- Authorization: not applicable, no API surface.

**Result:** Tool project, solution folder, test reference, `.gitattributes` and 3 new architecture tests (tool references only Domain; no `src/` project references it). `dotnet run` exits 0. Build 0 warnings, `dotnet test` 110 passed, format clean. **Deviation:** `Microsoft.Extensions.Logging.Console` resolved to 11.0.0, the latest stable.

---

## Phase 2: Field parsers (AC1, AC2, AC3, AC5) ✅ Done

- [x] `Parsing/ServiceZipsParser.cs` builds `IReadOnlyList<ZipRange>` from a cell. Rules are in the decisions table.
  - Uses `ZipCode.TryParse` / `ZipRange` from OR-002 after padding.
  - Returns a success or an error with a clear reason (no exceptions used for flow control).
- [x] `Parsing/RatingParser.cs` maps `no ratings yet` → `null` and parses other values as invariant `decimal`; anything else returns an error. It doesn't check the 1–10 range, which stays in `Rating`.
- [x] `Parsing/MailOrderFlagParser.cs`: `y`/`Y` → true, `n`/`N` → false; anything else returns an error.
- [x] Tests in `tests/OrderRouter.Tests/DataNormalizer/Parsing/`:
  - **AC1:** `2130, 2114-2212` → `02130` and `02114-02212`.
  - **AC2:** `10059-10103, 90117, 60699` → one range and two single ZIPs.
  - Edge cases: whitespace; a trailing comma; `ABCDE`; `123456`; `5-3` (start > end); `00001`; `00100-99999`.
  - **AC3:** `10`, `10.0`, `6`, `no ratings yet` → 10, 10, 6, `null`. `abc` → error. `NO RATINGS YET` → `null`.
  - **AC5:** `y`, `Y`, `n` → true, true, false. `yes`, empty → error.

**Verify:** working rule 2.

**Result:** 3 parsers (`TryParse` pattern, no exceptions) and 41 test cases. **Deviation:** `RatingParser` uses `NumberStyles.AllowDecimalPoint | AllowLeadingSign`, not `NumberStyles.Number` as the plan said. `Number` allows thousands separators, so `7,5` parsed as 75 and `1,0` would silently become 10, which breaks the never-guess rule. Test added (`7,5` → error). Build 0 warnings, tests 151 passed, format clean.

---

## Phase 3: Supplier file normalization (AC3, AC4, AC5, AC6, AC9 at file level) ✅ Done

- [x] `Reporting/FileNormalizationReport.cs` holds `RowsRead`, `RowsWritten`, `SkippedRows` (row number + reason) and `DuplicatesRemoved`. All collections are always non-null.
- [x] `Reporting/NormalizationException.cs` is for fatal, file-level errors. Its message names the file and the cause.
- [x] `Suppliers/SupplierNormalizer.cs`:
  - Reads with CsvHelper (invariant culture, header mapping per the decisions table).
  - For each row, runs the Phase 2 parsers, then `new Supplier(...)`.
  - On a parser error or `DomainValidationException`, skips the row and records the row number and reason.
  - A missing or ambiguous header, or a duplicate supplier ID, throws `NormalizationException`.
  - Returns the suppliers in source order plus the report.
- [x] Tests in `tests/OrderRouter.Tests/DataNormalizer/Suppliers/`, using in-memory CSV strings:
  - **AC4:** a file with `suplier_name` and a file with `supplier_name` → name filled for both. Both headers present, or a required column missing → `NormalizationException`.
  - **AC6:** `SUP-005`, `SUP-0810`, `SUP-T003` kept exactly.
  - **AC9:** the row with an unparseable rating is skipped with the correct row number and reason, and the other rows are kept. Same for a bad ZIP token, rating `11`, a bad mail flag and empty categories.
  - Mixed-case categories collapse (`CPAP, cpap` → one `cpap`).
  - Quoted cells containing commas parse correctly.
  - CRLF input (the source files use CRLF) parses correctly.

**Verify:** working rule 2.

**Result:** `CsvSource` (shared CsvHelper reader with header-alias mapping), `SupplierNormalizer`, `FileNormalizationReport` (adds `FileName`), `NormalizationException`. 24 new tests, all passed (175 total). **Deviations:** (1) CsvHelper is pinned to **33.1.0**. 34.0.0 is a rewrite with a different byte/stream API (`CsvReaderOptions`, no `CsvConfiguration`) that does not fit the `TextReader` design. (2) The plan did not name the rating input column; the source header is `customer_satisfaction_score`, so that is what is read. (3) Blank tokens in `product_categories` (e.g. a trailing comma) are dropped, and the row is rejected only if no category is left.

---

## Phase 4: Product file normalization (AC7, AC8) ✅ Done

- [x] `Products/ProductNormalizer.cs` reads `product_code,product_name,category` and builds `Product`. Invalid rows are skipped with the row number and reason.
  - De-duplicates by case-insensitive trimmed code. When duplicates are **identical** after normalization, it keeps the first and records a duplicate (code + row numbers) in the report.
  - When duplicates have **any differing field**, it throws `NormalizationException` naming the product code and both row numbers.
- [x] Tests in `tests/OrderRouter.Tests/DataNormalizer/Products/`:
  - **AC7:** two identical `HB-MAN-019-BAS` rows → one product, one duplicate recorded.
  - **AC8:** same code with a different category → throws, and the message contains the code. Same code with a different name → throws.
  - Same code differing only in case or whitespace → treated as one product.
  - `CPAP` vs `cpap` category on otherwise identical rows → identical duplicate.
  - Empty code → row skipped.

**Verify:** working rule 2.

**Result:** `ProductNormalizer`; 13 new test cases, all passed (188 total). A duplicate counts as identical when the code matches case-insensitively and name (ordinal, trimmed) and category match. Three or more identical copies produce one `DuplicateRow` listing every row.

---

## Phase 5: Writer, runner, console host and generated files (AC10–AC14) ✅ Done

- [x] `Output/NormalizedCsvWriter.cs` writes suppliers and products in the exact format from the decisions table, through CsvHelper with `NewLine = "\n"` and invariant culture.
- [x] `NormalizationRunner.cs`:
  - Takes input and output directories.
  - Opens inputs read-only and normalizes both files.
  - Writes both outputs only if neither failed.
  - Returns both reports.
- [x] `Program.cs`:
  - Resolves paths (repo root via `OrderRouter.slnx`, or `--input`/`--output`).
  - Builds an `ILoggerFactory` (console) and runs the runner.
  - Logs each skipped row and each duplicate as a warning.
  - Logs **one summary per file** (AC12): rows read, written, skipped (with row numbers and reasons) and duplicates removed.
  - On `NormalizationException`, logs an error and returns exit code 1.
- [x] Tests in `tests/OrderRouter.Tests/DataNormalizer/`:
  - Writer formatting: padded ZIPs, single vs range, sorted lower-case categories, `10.0` → `10`, unrated → empty, `y`/`n`, LF.
  - Round trip: every written row rebuilds the same domain objects with `ZipRange.Parse`, `Category`, `Rating` and `Supplier`. This proves OR-005 can load the files without normalizing.
  - **AC10:** running the runner on the real `Docs/Data` into a temp dir creates both files.
  - **AC11:** the SHA-256 of `suppliers.csv`, `products.csv` and `sample_orders.json` is unchanged after a run.
  - **AC13:** two runs into two temp dirs produce byte-identical files.
  - A failed run (conflicting duplicate) writes nothing.
  - Committed `Docs/Data/Seed/*` equals a fresh generation. This catches stale seed files, the ADR-0002 risk.
- [x] **AC14:** run `dotnet run --project tools/OrderRouter.DataNormalizer` from the repo root. This generates `Docs/Data/Seed/suppliers.normalized.csv` and `products.normalized.csv` and prints the summary. Sanity-check the counts against §6:
  - suppliers: 1,100 read and 1,100 written, 0 skipped, 184 unrated;
  - products: 1,200 read and 1,195 written, 5 duplicates removed, 0 skipped.

  If the counts differ, stop and ask.
- [x] Confirm with `git status` and `git diff` that `Docs/Data/*.csv` and `sample_orders.json` are unchanged.

**Verify:** working rule 2 plus the AC14 run above.

**Result:** `NormalizedCsvWriter`, `NormalizationRunner`, `ToolPaths`, `NormalizationReportLogger`, `Program`; 27 new tests (215 total, all passed). AC14 run: suppliers 1,100 read / 1,100 written / 0 skipped / 184 unrated; products 1,200 read / 1,195 written / 5 duplicates removed (HB-MAN-019-BAS, WK-BAR-013-BAS, CM-DROP-049-PLU, CR-BAR-072-ECO, WK-STD-009-PLU) / 0 skipped. Matches §6. Output has no BOM and no CR. `git status` shows `Docs/Data/*.csv` and `sample_orders.json` unchanged. Format clean after `dotnet format` fixed one using order in a test file.

---

## Phase 6: Clean-up, full verification and post-implementation review ✅ Done

- [ ] Run coverage for the tool code (target ≥ 85%). If a coverage package is needed, ask first (same as OR-002). **Not done: needs `Microsoft.Testing.Extensions.CodeCoverage` (the runner is Microsoft.Testing.Platform, so `coverlet.collector` does not apply). Waiting for approval to add it.**
- [x] Run the `ddd-implementation` §5 review and record it under **Review report**. Report honestly:
  - **Applies:** domain invariants untouched; SRP (parsers / normalizers / writer / runner); pure functions; XML docs; test naming; edge-case and error tests; deterministic output; fail-safe behavior.
  - **N/A (say why, don't claim met):** domain events and audit trail, money, transactions, async/DI (a small offline tool; logging is the only injected dependency), PCI-DSS/SOX/LGPD.
- [x] Record follow-ups for other tickets (don't do them here):
  - **Requirements NFR-10:** reword it so that conflicting duplicate products **fail the run** (matching AC8 / FR-04 / DQ-12) rather than being excluded.
  - **OR-005:** read the normalized format defined here, parse `service_zips` with `ZipRange.Parse` per token, read empty `rating` as unrated, and read `can_mail_order` as `y`/`n`.
  - **README (OR-025):** document the regenerate command and the need to commit the regenerated `Docs/Data/Seed` files in the same change as any source CSV update.
- [x] Final `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root: all green.

### Review report

**Applies and met**
- Domain invariants untouched: no file under `src/` was changed. Every row is validated by building `Supplier` / `Product`.
- SRP: field parsers, file normalizers, shared CSV reader, writer, runner, path resolver, report logger and host are separate types.
- Pure functions: normalizers and writer take `TextReader` / `TextWriter`. File I/O is only in `NormalizationRunner`; inputs are opened `FileAccess.Read` / `FileShare.Read`.
- XML docs on every public type and member; comments cite requirement IDs and name the libraries (CsvHelper, Microsoft.Extensions.Logging).
- Test naming follows `MethodName_Condition_ExpectedResult`, no Arrange/Act/Assert comments. Edge and error paths are covered.
- Deterministic output: source order, sorted categories, LF, no BOM, `.gitattributes` for the seed files. Proven by the two-run byte comparison and the committed-equals-fresh test.
- Fail-safe: bad rows are skipped with row number and reason; conflicting duplicates, duplicate supplier ids and column problems fail the run, and a failed run writes nothing (tested).

**Not met / open**
- **Coverage (≥ 85%) was not measured.** It needs a coverage package that is not in the repo; awaiting approval. All public paths have tests, but this is not a measured figure.
- `SupplierNormalizer` and `ProductNormalizer` use `DomainValidationException` for flow control, because `Supplier` and `Product` have no `TryCreate`. Acceptable for an offline tool; a follow-up for OR-002 if wanted.
- Row numbers come from CsvHelper's physical line counter. For a quoted cell that spans lines it reports the record's last line; the source data has none.
- Output ZIPs are written as `NNNNN` or a range; a source range with equal ends (`5-5`) becomes a single ZIP. Equivalent for lookups.

**N/A (not claimed as met)**
- Domain events and audit trail: no business state change.
- Money / `decimal` rounding rules: no money. `decimal` is used for rating only; rounding is OR-002's `Rating`.
- Transactions and aggregates: no persistence. The run is write-both-or-neither on files, which is not a database transaction.
- Async / DI: a small offline synchronous tool (about 2,300 rows). Logging is the only injected dependency.
- PCI-DSS / SOX / LGPD: no PHI, payment or personal data in these files.

**Follow-ups for other tickets (not done here)**
- **Technical Requirements NFR-10:** reword so conflicting duplicate products **fail the run** (matching AC8 / FR-04 / DQ-12) rather than being excluded.
- **OR-005:** read the format defined here: parse `service_zips` with `ZipRange.Parse` per `", "`-separated token, read an empty `rating` as unrated, read `can_mail_order` as `y`/`n`, split `product_categories` on `", "`.
- **README (OR-025):** document `dotnet run --project tools/OrderRouter.DataNormalizer` and that regenerated `Docs/Data/Seed` files must be committed in the same change as any source CSV update.
- **ADR-0002:** note the CsvHelper 33.x pin (34.0 is a different API).

**Result:** Final `dotnet build` 0 errors / 0 warnings, `dotnet test` 215 passed (includes the OR-001 architecture and OR-002 domain tests), `dotnet format --verify-no-changes` clean. Coverage not measured, see Review report.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-003-csv-normalization.md` |
| New (tool) | `tools/OrderRouter.DataNormalizer/**` (see Target structure) |
| New (generated, committed) | `Docs/Data/Seed/suppliers.normalized.csv`, `Docs/Data/Seed/products.normalized.csv` |
| New (tests) | `tests/OrderRouter.Tests/DataNormalizer/**` |
| Modified | `OrderRouter.slnx` (adds `/tools/`), `tests/OrderRouter.Tests/OrderRouter.Tests.csproj` (tool reference), `tests/OrderRouter.Tests/Architecture/LayerDependencyTests.cs`, `.gitattributes` |
| Reused (unchanged) | `src/OrderRouter.Domain/Entities/ReferenceData/{Supplier,Product,Category,ZipCode,ZipRange,ServiceArea,Rating}.cs`, `src/OrderRouter.Domain/Models/DomainValidationException.cs` |
| Never modified | `Docs/Data/suppliers.csv`, `Docs/Data/products.csv`, `Docs/Data/sample_orders.json` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Leading zeros restored | `ServiceZipsParserTests` | 2 |
| AC2: Lists and ranges mixed | `ServiceZipsParserTests` | 2 |
| AC3: Ratings parsed safely | `RatingParserTests`, `SupplierNormalizerTests` | 2, 3 |
| AC4: Header typo accepted | `SupplierNormalizerTests` (both spellings) | 3 |
| AC5: Mail-order flag | `MailOrderFlagParserTests` | 2 |
| AC6: Supplier IDs unchanged | `SupplierNormalizerTests` | 3 |
| AC7: Identical duplicate products | `ProductNormalizerTests` (one kept, duplicate reported) | 4 |
| AC8: Conflicting duplicate products | `ProductNormalizerTests` (throws, names the code) | 4 |
| AC9: Bad rows skipped | `SupplierNormalizerTests` (row number + reason, others kept) | 3 |
| AC10: Normalized files written | `NormalizationRunnerTests` + manual run | 5 |
| AC11: Source files unchanged | `NormalizationRunnerTests` (SHA-256 before/after) + `git diff` | 5 |
| AC12: Normalization summary | Report contents tested; console summary checked in the manual run | 5 |
| AC13: Output is repeatable | `NormalizationRunnerTests` (two runs, byte-identical) + committed-equals-fresh test | 5 |
| AC14: One documented command | Manual `dotnet run --project tools/OrderRouter.DataNormalizer` | 5 |

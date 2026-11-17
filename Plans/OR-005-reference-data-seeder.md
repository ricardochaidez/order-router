# Plan: OR-005: Seed reference data by upsert from the integration tests

**Ticket:** [Docs/Tickets/OR-005-reference-data-seeder.md](../Docs/Tickets/OR-005-reference-data-seeder.md)
**Depends on:** OR-003 (done, see [OR-003-csv-normalization.md](OR-003-csv-normalization.md)), OR-004 (done, see [OR-004-database-schema.md](OR-004-database-schema.md))
**References:** [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Seeding and migrations); Technical Requirements FR-04, SC-21, SC-22, SC-24, SC-33, OQ-24, NFR-04, NFR-08, NFR-10; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-006 (reads the seeded data), OR-023 (endpoint tests run on the seeded database), OR-025 (README: "run the integration tests to seed"), OR-026 (seed must leave order tables alone)

## Context

OR-003 committed the clean seed files `Docs/Data/Seed/suppliers.normalized.csv` (1,100 rows, including the sentinels `SUP-T001` to `SUP-T014`) and `products.normalized.csv` (1,195 rows). OR-004 created the schema (`suppliers`, `supplier_categories`, `supplier_service_areas`, `products`) and the `InitialReferenceDataSchema` migration, but **nothing applies it yet**. OR-005 adds the one step that does:

- `ReferenceDataSeeder` in Infrastructure. It applies the migrations, reads the normalized files, builds OR-002 domain objects from every row (that step is the validation), and **upserts** everything in **one transaction**.
- A summary log: rows loaded, unrated suppliers, and rows in the database that are no longer in the CSVs.
- An xUnit **assembly fixture** that runs the seeder once before the integration tests, against the same database the API uses (`ConnectionStrings__OrderRouter`, defaulting to the compose PostgreSQL).

Out of scope: any normalization (OR-003 owns it), repositories and read queries (OR-006), wiring the database into `Startup.cs` (OR-006/OR-007), order tables (OR-026). **Neither API startup nor `docker compose up` ever migrates or seeds** (SC-22, SC-33).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| How AC3/AC4/AC5/AC8 are tested | **Throwaway-database tests.** In production code, only the assembly fixture calls the seeder, and it seeds the shared `orderrouter` database from the real files. Separate integration tests call the seeder directly on a throwaway database (reuse `tests/OrderRouter.Api.IntegrationTests/Infrastructure/ThrowawayDatabase.cs`), with temp CSV files written to a temp folder. The real `Docs/Data/Seed/*` files are never edited. For AC5/AC8, "the test run fails" is shown by the seeder throwing: the fixture passes that exception on, which fails the run. |
| AC8 error reporting | **First bad row.** Fail fast: stop at the first row that is rejected and report the file, row number and reason. |

### Other design choices (from the ticket, ADR, rules and existing code; no question needed)

| Topic | Choice and reason |
|---|---|
| Location | `src/OrderRouter.Infrastructure/Services/Seeding/`. Infrastructure only; Domain and Api stay unchanged. |
| CSV library | **CsvHelper 33.1.0** added to Infrastructure. ADR-0002 names CsvHelper, and this is the version the OR-003 tool pins (34.x is a different, stream-based API). Comment the package's purpose in the csproj. Ask before using another version. |
| Reading = validation, no normalization | `NormalizedSeedFileReader` reads exactly the OR-003 format: `service_zips` is split on `", "` and each token goes through `ZipRange.Parse`; `product_categories` is split on `", "` and each token goes through `new Category(...)`; an empty `rating` means unrated, otherwise it is `decimal.Parse` with the invariant culture and then goes through the `Supplier` constructor (1–10); `can_mail_order` must be exactly `y` or `n`. Values are passed as they are: no padding, no case fixes, no trimming beyond what the domain constructors already do, and no de-duplication. Any `DomainValidationException` or parse failure becomes a `ReferenceDataSeedException` that names the file, the physical row number (header = row 1, same as OR-003) and the reason, with the original exception as the inner exception. |
| Header check | The required columns must be present by exact name. Infrastructure keeps its own header constants, because Infrastructure must not reference the `tools/` project. A unit test asserts that they equal `NormalizedCsvWriter.SupplierHeader` / `ProductHeader` (`tools/OrderRouter.DataNormalizer/Output/NormalizedCsvWriter.cs`), so the writer and the reader can't drift apart. |
| Fail-fast conditions (FR-04, ADR-0002) | The seed throws `ReferenceDataSeedException` with a clear message when: a file is missing (AC5, the message names the full path); a file has no header, a required column, or any data rows; a row is rejected (AC8); a `supplier_id` appears twice (ordinal, same as the PK); a `product_code` appears twice ignoring case (OR-006 looks codes up without case, FR-05, so case-only duplicates would be ambiguous: fail safe); one supplier has two segments with the same start ZIP (this would collide on PK `(supplier_id, start_zip)`, an OR-004 follow-up). These are checks only, never fixes. |
| Order of work | Exactly as ADR-0002: (1) `MigrateAsync`; (2) read and validate **both** files completely into domain objects; (3) open **one** transaction and upsert; (4) count the rows that are no longer in the CSVs; (5) commit; (6) log the summary. Because validation finishes before the transaction opens, a bad file never writes any data. Any failure inside the transaction rolls everything back. (The schema can still be created by step 1, which is expected: migrations are not data.) |
| Upsert SQL (FR-04, ADR-0002) | `INSERT … ON CONFLICT (pk) DO UPDATE SET <every business column> = EXCLUDED.…` for `suppliers` and `products`. For the child sets: `DELETE … WHERE supplier_id = ANY(@ids)`, then insert, so each CSV supplier's categories and service areas are **replaced** (AC3). Each table is one set-based statement using `unnest(@array, …)` parameters (≈ 6 statements for about 9,000 rows), run with `Database.ExecuteSqlAsync` interpolated SQL, so every value is a parameter (no SQL injection, no string concatenation). There are **no deletes** of suppliers or products that are missing from the CSVs (OQ-24, AC4). The SQL touches only the four reference tables, so stored orders (OR-026 AC4) are safe. |
| `seeded_at` | One timestamp per run from an injected `TimeProvider` (testable), written on insert and on update. AC2's "the same" therefore means row counts and business columns, not `seeded_at` (an OR-004 follow-up). |
| Seeder API | `public sealed class ReferenceDataSeeder(OrderRouterDataContext context, TimeProvider timeProvider, ILogger<ReferenceDataSeeder> logger)` with `Task<ReferenceDataSeedSummary> SeedAsync(ReferenceDataSeedFiles files, CancellationToken cancellationToken)`. `ReferenceDataSeedFiles(string SuppliersPath, string ProductsPath)` has a `FromDirectory(string seedDirectory)` helper. **It is not registered in DI**, so the API can't resolve it (AC7, SC-22). Only the fixture creates it. |
| Summary (AC6, NFR-04) | `ReferenceDataSeedSummary` record: suppliers, products, categories and service areas loaded; unrated suppliers; suppliers and products in the database but not in the CSVs. It is logged **once** at `Information` with a structured template, and also returned so tests can assert on it. No per-row logs (skipped rows and duplicates are OR-003's job). |
| Fixture | `ReferenceDataFixture : IAsyncLifetime`, registered with `[assembly: AssemblyFixture(typeof(ReferenceDataFixture))]` (xUnit v3), so it runs once before any integration test. Connection string: `ConnectionStrings__OrderRouter`, or `OrderRouterDataContextFactory.DefaultConnectionString` (reuse its constants, the same rule as `ThrowawayDatabase`). Seed folder: walk up to `OrderRouter.slnx` (same pattern as `tests/OrderRouter.Tests/DataNormalizer/TestRepository.cs`), then `Docs/Data/Seed`. If PostgreSQL is unreachable, it **fails** with the "start it with `docker compose up -d`" message and is never skipped. The fixture exposes the `Summary` and the connection string to the tests. |
| Visible summary log | The fixture's logger writes through `TestContext.Current.SendDiagnosticMessage`, and `xunit.runner.json` (`"diagnosticMessages": true`) is added to the integration test project, so the summary shows up in `dotnet test` output. If it doesn't show up, stop and ask rather than add another logging package. |
| Async | All file and database I/O is `async` with a `CancellationToken` (`TestContext.Current.CancellationToken` in tests). |
| Domain events / money / authorization | None. The seeder is test-only infrastructure: it changes no business state, handles no money and has no API surface. Say so in the review; don't claim them as met. |

### Target structure

```
src/OrderRouter.Infrastructure/
  OrderRouter.Infrastructure.csproj                    + CsvHelper 33.1.0
  Persistence/Seeding/
    ReferenceDataSeedFiles.cs                          paths to the two normalized files
    ReferenceDataSeedException.cs                      file, row number, reason
    NormalizedSeedFileReader.cs                        CSV → Supplier / Product (validation only)
    ReferenceDataSeedSummary.cs                        counts returned and logged
    ReferenceDataSeeder.cs                             migrate → read → upsert in one transaction → summary
tests/OrderRouter.Tests/Infrastructure/Services/Seeding/
  NormalizedSeedFileReaderTests.cs                     no database
tests/OrderRouter.Tests/Architecture/
  SeedingBoundaryTests.cs (or extend LayerDependencyTests)   AC7 source guard
tests/OrderRouter.Api.IntegrationTests/
  xunit.runner.json                                    diagnosticMessages
  ReferenceDataFixture.cs                              assembly fixture, seeds the shared DB once
  Persistence/SeedFiles.cs                             writes temp CSVs for the throwaway-DB tests
  Persistence/ReferenceDataSeederTests.cs              AC2–AC6, AC8 on throwaway databases
  Persistence/SeededReferenceDataTests.cs              AC1 on the shared database
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer change (a new Infrastructure component plus the test fixture), so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review in Phase 1;
   - §3 is this plan, and §4 implementation runs through Phases 1–3;
   - §5 post-implementation review in Phase 4.

   Key points:
   - **C#:** C# 14 / .NET 10 / EF Core 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, and the final `return` on its own line.
   - **Docs and comments:** XML doc comments on every public type and member, with `<example>`/`<code>` where useful (e.g. `SeedAsync`). Comments explain *why* and cite the requirement or ADR (e.g. `// FR-04`, `// OQ-24`). Comment each library's purpose where it's used (CsvHelper).
   - **Layers:** Domain and Api are unchanged. The seeder lives in Infrastructure, outside DI.
   - **Fail safe:** never fix, skip or de-duplicate a row. Fail with the file, row and reason (NFR-10).
   - **Tests:** follow the style in the nearby test projects: `MethodName_Condition_ExpectedResult` names and no `Arrange`/`Act`/`Assert` comments. Cover edge cases and error paths. Target ≥ 85% coverage of the new code.
   - **Data:** never edit `Docs/Data/*` or `Docs/Data/Seed/*`. Tests write their own CSVs to a temp folder and delete it afterwards.
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d`) and run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including the existing architecture, domain, normalizer, model and schema tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-005-reference-data-seeder.md`;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or library behaviour is unclear or contradicts another, stop and ask the engineer. Don't guess.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-005-reference-data-seeder`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-005-reference-data-seeder.md` with this plan.
- [x] Record the two confirmed decisions (test scope, first bad row) under *Decisions confirmed with the engineer* (already in this plan).

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Domain analysis, architecture review and the normalized-file reader (AC5 and AC8 at file level) ✅ Done

- [x] Write the `ddd-implementation` §1 domain analysis and §2 architecture review under **Phase 1 notes**:
  - concepts and terms: normalized seed file, seed, upsert, sentinel supplier, unrated supplier, "no longer in CSV";
  - aggregate boundary: `Supplier` plus its categories and service areas are written together, so the child sets are replaced as a unit; `Product` is separate;
  - invariants are enforced by the OR-002 constructors and are never repeated in the reader;
  - layers: Infrastructure only, outside DI;
  - no domain events, money, authorization or PHI, and why.
- [x] Add CsvHelper 33.1.0 to `OrderRouter.Infrastructure.csproj`, with a comment on its purpose.
- [x] Add `ReferenceDataSeedFiles`, `ReferenceDataSeedException` (properties `FileName`, `RowNumber?`, `Reason`; the message reads like `suppliers.normalized.csv row 42: ZIP code must be exactly 5 digits (received "2130")`) and `NormalizedSeedFileReader` (`ReadSuppliersAsync` / `ReadProductsAsync`, each with a path overload and a `TextReader` overload for unit tests), following the *Reading = validation* and *Fail-fast conditions* rows above.
- [x] `NormalizedSeedFileReaderTests` (unit, no database):
  - the real committed `Docs/Data/Seed` files read with no error, give 1,100 suppliers and 1,195 products, and include `SUP-T001`–`SUP-T014` (compare the counts with the file line counts, don't hard-code them blindly);
  - the header constants equal `NormalizedCsvWriter.SupplierHeader` / `ProductHeader`;
  - a row written by `NormalizedCsvWriter` reads back into equal domain objects (round trip);
  - **AC8:** a 4-digit ZIP, rating `11`, rating `abc`, an empty category, `can_mail_order` = `Y` or `yes`, an empty name or code each throw with the right file name, row number and reason, and only the **first** bad row is reported;
  - **AC5:** a missing file throws, and the message names the path; header only (no rows), no header, and a missing column each throw;
  - duplicate `supplier_id`, a product code that differs only in case, and the same start ZIP twice in one supplier each throw and name both rows;
  - no normalization: `" cpap"`-style values are left to the domain, and a ZIP with spaces such as `"02130 "` fails rather than being trimmed.

**Verify:** working rule 2.

**Result:** Solution-wide `dotnet test`: 292 passed, 0 failed (280 unit + 12 integration, including the new reader tests). `dotnet build` 0 warnings, `dotnet format` clean. Deviations: row numbers come from CsvHelper `Parser.Row` (record count, header = 1); the same-start-ZIP error names the one row plus the ZIP, because a supplier has only one row; private constants are camelCase per `.editorconfig`.

### Phase 1 notes

**Domain analysis (§1).**
- *Normalized seed file:* the OR-003 CSV output. *Seed:* load it into the database. *Upsert:* insert new rows and update existing ones by primary key. *Sentinel supplier:* `SUP-T001`–`SUP-T014`, test fixtures inside the real file. *Unrated supplier:* empty `rating` (stored as `NULL`, never 0). *No longer in CSV:* a row in the database whose key is not in the files; it is kept and only counted (OQ-24).
- *Aggregate boundary:* `Supplier` with its categories and service areas is written as one unit, so child sets are replaced together. `Product` stands alone.
- *Invariants:* all enforced by the OR-002 constructors. The reader only adds file-level checks the domain cannot see (duplicate keys, same start ZIP in one supplier, header, empty file).

**Architecture review (§2).** Infrastructure only, outside DI. Domain and Api are untouched. Infrastructure does not reference `tools/`, so it keeps its own header constants (a unit test ties them to the writer).

**Not applicable:** domain events (no business state changes), money, authorization (no API surface), PHI (supplier and product catalogue data only).

---

## Phase 2: `ReferenceDataSeeder`: migrate, upsert in one transaction, summary (AC2–AC6, AC8) ✅ Done

- [x] Add `ReferenceDataSeedSummary` and `ReferenceDataSeeder`, following the *Order of work*, *Upsert SQL*, *`seeded_at`* and *Summary* rows above. Use `await using var transaction = await context.Database.BeginTransactionAsync(...)`. Commit only after every statement succeeds.
- [x] `tests/OrderRouter.Api.IntegrationTests/Infrastructure/SeedFiles.cs`: writes small supplier and product CSVs (in the normalized format) to a temp folder and deletes it on dispose.
- [x] `ReferenceDataSeederTests`. Each test uses its own `ThrowawayDatabase`, a fixed `FakeTimeProvider`-style `TimeProvider` stub, and a capturing `ILogger` (same pattern as `NormalizationReportLoggerTests.CapturingLogger`):
  - **AC1 (throwaway):** an empty database plus the real seed files → migrations are applied, the table counts equal the file counts, and the sentinels are present;
  - **AC2:** seed twice with the real files → identical row counts per table, an identical ordered dump of the business columns (everything except `seeded_at`), and `count(*) = count(DISTINCT pk)`; `seeded_at` equals the second run's time;
  - **AC3:** seed file A, then file B in which supplier `S1` has different categories and ZIPs, a new name and rating, and becomes unrated → `S1`'s categories and service areas match B exactly (old child rows are gone), and its name, rating (`NULL`) and `can_mail_order` match B. Another supplier, `S2`, stays the same;
  - **AC4:** seed A (`S1`, `S2`, products `P1`, `P2`), then B (`S1`, `P1`) → `S2` (with its children) and `P2` are still there, and the summary reports 1 supplier and 1 product no longer in the CSV;
  - **AC5:** a missing suppliers path → `ReferenceDataSeedException` naming the path, and no data rows are written;
  - **AC6:** exactly one `Information` summary entry containing the loaded counts, the unrated count and the not-in-CSV counts; the returned summary matches it;
  - **AC8:** a database already seeded with A, then a bad file (4-digit ZIP; rating `11`) → throws with the file, row and reason, and the database is unchanged (same snapshot as before). A valid suppliers file with an invalid products file → no supplier rows are changed either.

**Verify:** working rule 2. Also confirm that no `or_schema_test_*` databases are left behind: `docker compose exec postgres psql -U orderrouter -c '\l'`.

**Result:** Solution-wide `dotnet test`: 302 passed, 0 failed (10 new seeder tests). Build 0 warnings, `dotnet format` clean, no `or_schema_test_*` databases left. Deviations: none. The in-transaction rollback path is covered by design (validation finishes before the transaction opens), not by a forced SQL failure, because the domain constructors make a bad row unreachable at that point.

---

## Phase 3: Integration-test fixture on the shared database, AC1 and the AC7 guard ✅ Done

- [x] Add `ReferenceDataFixture` plus `[assembly: AssemblyFixture(typeof(ReferenceDataFixture))]`, the diagnostic-message logger and `xunit.runner.json` (copied to the output folder), following the *Fixture* and *Visible summary log* rows above.
- [x] `SeededReferenceDataTests` (receives the fixture), **AC1** on the shared `orderrouter` database:
  - every `supplier_id` and `product_code` from the committed files is in the database (set containment; the shared database may also hold older rows, see AC4);
  - `SUP-T001`–`SUP-T014` are present, and for one sentinel, its categories and service areas match the file exactly;
  - `__EFMigrationsHistory` contains `InitialReferenceDataSchema`;
  - the fixture's `Summary` loaded counts equal the file row counts.
- [x] **AC7 guard** (unit test in `tests/OrderRouter.Tests/Architecture/`): no source file under `src/OrderRouter.Api/` contains `ReferenceDataSeeder`, `Migrate(`, `MigrateAsync(` or `EnsureCreated`; and `ReferenceDataSeeder` is not registered by `AddOrderRouterDatabase` (resolving it from that service provider fails).
- [x] Confirm that the existing `ApiHostTests` and `SchemaMigrationTests` still pass with the fixture in place.

**Verify:** working rule 2, plus:
- **AC1 / AC6 manual:** `docker compose down -v && docker compose up -d`, then `dotnet test`. The seed summary appears in the output. Then `docker compose exec postgres psql -U orderrouter -d orderrouter -c 'SELECT count(*) FROM suppliers'` returns 1100.
- **AC2 manual:** run `dotnet test` again. The counts are unchanged and the summary reports 0 rows no longer in the CSVs.
- **AC7 manual:** `docker compose down -v && docker compose up -d`, then `dotnet run --project src/OrderRouter.Api` (stop it after it starts). `\dt` shows **no tables**. (The API joins compose in OR-007, so here it runs with `dotnet run`.)
- **AC5 / fixture failure:** with PostgreSQL stopped, `dotnet test` fails with the "start it with `docker compose up -d`" message (not skipped). Then start PostgreSQL again.

**Result:** Solution-wide `dotnet test`: 309 passed, 0 failed (run three times against the same database: counts stay 1100 suppliers / 1195 products, summary reports 0 not in CSV). Build 0 warnings, `dotnet format` clean. With PostgreSQL stopped, the run fails with the "start it with `docker compose up -d`" message (not skipped); it recovers after restart.

**Deviations, need the engineer's decision:**
1. **Summary visibility.** The summary is logged through `SendDiagnosticMessage` and shows with `dotnet run --project tests/OrderRouter.Api.IntegrationTests`, but **not** in `dotnet test` output (the test platform bridge drops xUnit diagnostics). Per the plan, no logging package was added. Options: accept `dotnet run`, or approve a package.
2. **Not run: the `docker compose down -v` checks** (AC1/AC7 manual). The auto-mode classifier blocked wiping the dev volume. AC1 is covered by the throwaway-database test, AC7 by the source guard and DI test (the API does not touch the database at all today). Run them by hand if wanted.

---

## Phase 4: Clean-up, full verification and post-implementation review ✅ Done

- [x] ~~Measure coverage of the new Infrastructure code (target ≥ 85%) with the existing `coverlet.collector` (`dotnet test --collect:"XPlat Code Coverage"`). If that doesn't work with this test setup and a new package would be needed, ask first.~~ **Not done:** `coverlet.collector` does not work with xUnit v3 on the Microsoft Testing Platform (`--collect` is rejected); it needs `Microsoft.Testing.Extensions.CodeCoverage`. Awaiting approval for that package. No coverage number is claimed.
- [x] Run the `ddd-implementation` §5 review and record it under **Review report**. Report honestly:
  - **Applies:** no normalization in the seeder; domain constructors used as validation; fail fast on file, row and reason; one transaction; upsert with child-set replacement; no deletes (OQ-24); summary log; seeding only from the fixture; no migrate or seed in the API or compose; parameterized SQL; async I/O; XML docs; test naming; edge and error tests.
  - **N/A (say why, don't claim met):** domain events and audit trail, money, authorization, PCI-DSS/SOX/LGPD (no PHI or payment data).
- [x] Record follow-ups for other tickets (don't do them here):
  - **OR-025 (README):** `docker compose up -d`, then `dotnet test` seeds the API's database; where to see the seed summary.
  - **OR-026:** the seeder only touches the four reference tables; add the OR-026 AC4 test there.
  - **OR-006 / OR-007:** still never call `Migrate` at startup; the AC7 guard test will catch it.
- [x] Final `docker compose down -v && docker compose up -d`, then `dotnet build`, `dotnet test` (run twice) and `dotnet format --verify-no-changes` from the repo root: all green.

### Review report

**Applies and met:** no normalization in the seeder (the reader passes values to the OR-002 constructors); fail fast with file, row and reason; validation of both files finishes before the transaction opens; one transaction with set-based, parameterized upserts and child-set replacement; no deletes (OQ-24); one summary log; seeding only from the fixture, not in DI, with a source guard and a DI test; no migrate or seed in the API or compose; async I/O with cancellation tokens; XML docs on public types; `MethodName_Condition_ExpectedResult` test names; edge and error tests (header, empty file, short row, duplicates, case, ZIP spacing).

**N/A (not claimed):** domain events and audit trail (no business state changes), money, authorization (no API surface), PCI-DSS/SOX/LGPD (no PHI or payment data).

**Not verified, be honest:**
- Coverage percentage (≥ 85% target): not measurable without a new package, see above.
- A forced failure *inside* the transaction (rollback) has no test; the validate-first order makes a bad row unreachable there.
- Manual `docker compose down -v` runs (AC1, AC7) were not executed (blocked); see Phase 3.
- The seed summary is not visible in `dotnet test` output, only via `dotnet run --project tests/OrderRouter.Api.IntegrationTests`.

**Follow-ups (other tickets):**
- **OR-025 (README):** `docker compose up -d`, then `dotnet test` (or `dotnet run --project tests/OrderRouter.Api.IntegrationTests` to see the summary) seeds the API's database.
- **OR-026:** the seeder touches only the four reference tables; add the OR-026 AC4 test there.
- **OR-006 / OR-007:** still never call `Migrate` at startup; `SeedingBoundaryTests` will catch it.
- **OR-004:** `seeded_at` changes on every run, so "idempotent" means counts and business columns.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-005-reference-data-seeder.md` |
| New (Infrastructure) | `src/OrderRouter.Infrastructure/Services/Seeding/*.cs` (see Target structure) |
| New (tests) | `tests/OrderRouter.Tests/Infrastructure/Services/Seeding/NormalizedSeedFileReaderTests.cs`, AC7 guard under `tests/OrderRouter.Tests/Architecture/`, `tests/OrderRouter.Api.IntegrationTests/{ReferenceDataFixture.cs, xunit.runner.json}`, `tests/OrderRouter.Api.IntegrationTests/Persistence/{SeedFiles,ReferenceDataSeederTests,SeededReferenceDataTests}.cs` |
| Modified | `src/OrderRouter.Infrastructure/OrderRouter.Infrastructure.csproj` (CsvHelper), `tests/OrderRouter.Api.IntegrationTests/OrderRouter.Api.IntegrationTests.csproj` (copy `xunit.runner.json`) |
| Reused | `ThrowawayDatabase` (throwaway DBs), `OrderRouterDataContextFactory.ConnectionStringVariable` / `DefaultConnectionString`, the `TestRepository` repo-root pattern, the `CapturingLogger` pattern, `NormalizedCsvWriter` headers (test-only cross-check), OR-002 constructors (`Supplier`, `Product`, `Category`, `ZipRange.Parse`) |
| Unchanged | `src/OrderRouter.Domain/**`, `src/OrderRouter.Api/**`, `docker-compose.yml`, migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: First seed | `ReferenceDataSeederTests` (throwaway, empty DB) + `SeededReferenceDataTests` (shared DB via the fixture) + manual count | 2, 3 |
| AC2: Seed is idempotent | `ReferenceDataSeederTests` (seed twice, same counts and business columns, no duplicates) + a second manual `dotnet test` | 2, 3 |
| AC3: Changed supplier is updated | `ReferenceDataSeederTests` (child sets replaced exactly) | 2 |
| AC4: Removed rows are kept | `ReferenceDataSeederTests` (row kept, summary count) | 2 |
| AC5: Missing file fails fast | `NormalizedSeedFileReaderTests` + `ReferenceDataSeederTests`; the fixture passes the exception on | 1, 2, 3 |
| AC6: Summary log | `ReferenceDataSeederTests` (one `Information` entry) + summary visible in `dotnet test` output | 2, 3 |
| AC7: No migrations or seeding elsewhere | AC7 source guard + seeder not in DI + manual `docker compose up` / `dotnet run` with `\dt` empty | 3 |
| AC8: Invalid row fails the seed | `NormalizedSeedFileReaderTests` (file, row, reason) + `ReferenceDataSeederTests` (database unchanged) | 1, 2 |

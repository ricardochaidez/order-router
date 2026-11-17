# Plan: OR-004: Create the PostgreSQL schema and EF Core migrations

**Ticket:** [Docs/Tickets/OR-004-database-schema.md](../Docs/Tickets/OR-004-database-schema.md)
**Depends on:** OR-002 (done, see [OR-002-domain-reference-model.md](OR-002-domain-reference-model.md))
**References:** [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Schema, Seeding and migrations, Read path); Technical Requirements FR-04, SC-09, SC-20, SC-21, SC-22, SC-33, NFR-08; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-005 (seeder applies these migrations), OR-006 (read path), OR-007 (adds the API to the same compose file and a health check on this context)

## Context

OR-002 added the strict, read-only reference-data domain (`Supplier`, `Product`, `Category`, `ZipCode`, `ZipRange`, `ServiceArea`, `Rating`). OR-003 produced the committed normalized seed files. OR-004 creates the place the data will live:

- a PostgreSQL service in `docker-compose`;
- one EF Core 10 (Npgsql) `OrderRouterDbContext` in Infrastructure, for reference data now and stored orders later (ADR-0007);
- four tables, `suppliers`, `supplier_categories`, `supplier_service_areas`, `products`, plus the ADR-0002 indexes;
- the initial migration.

Out of scope (other tickets): seeding and applying migrations from the test fixture (OR-005), repositories and read queries (OR-006), the API container, `/health` and `.env.example` (OR-007), order tables (ADR-0007 / OR-027). **Nothing in this ticket applies migrations at API startup or on `docker compose up`** (SC-22, SC-33).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| `suppliers.rating` type | **`numeric(4,2)`**, not the ADR's `numeric(3,1)`. OR-002's `Rating` rounds to 2 decimal places and states "the database never rounds again"; `numeric(3,1)` would silently round `6.75` to `6.8`. `numeric(4,2)` holds `1.00`–`10.00` exactly. ADR-0002's diagram is corrected as a doc follow-up (Phase 5). |
| How AC1/AC2 are tested | **Throwaway database on the compose PostgreSQL.** Each schema test creates a uniquely named database (e.g. `or_schema_test_<guid-n>`) on the compose server, applies the migrations, inspects the PostgreSQL catalog, then drops it. No new test dependencies. `dotnet test` therefore needs `docker compose up -d` first, which ADR-0002 already accepts. The shared `orderrouter` database is never touched by these tests. |
| API wiring | **Extension only, no wiring.** Infrastructure gets an `AddOrderRouterDatabase(...)` DI extension and a design-time factory for `dotnet ef`. `Program.cs` is **not** changed; OR-006/OR-007 call the extension when they need it. |

### Other design choices (from the ADR, rules and data; no question needed)

| Topic | Choice and reason |
|---|---|
| Persistence model | **Separate persistence row types** in Infrastructure (`SupplierRow`, `SupplierCategoryRow`, `SupplierServiceAreaRow`, `ProductRow`), not EF mappings of the domain classes. The domain types validate in their constructors and hold value objects/`HashSet`s; ADR-0002 says repositories project into domain objects with `AsNoTracking`. This keeps the Domain free of EF and its invariants untouched. |
| Physical schema | Exactly ADR-0002, with the rating change above. **suppliers:** `supplier_id text PK`, `name text NOT NULL`, `rating numeric(4,2) NULL` (NULL = unrated, DQ-06), `can_mail_order boolean NOT NULL`, `seeded_at timestamptz NOT NULL` (set by the OR-005 seeder). **supplier_categories:** PK `(supplier_id, category)`, FK → suppliers. **supplier_service_areas:** PK `(supplier_id, start_zip)`, `start_zip int NOT NULL`, `end_zip int NOT NULL`, FK → suppliers. **products:** `product_code text PK`, `name text NOT NULL`, `category text NOT NULL` (joins `supplier_categories.category` by value; no FK, per ADR). |
| No mail-only column (AC3, SC-09) | Not stored. `Supplier.IsMailOnly(nationwideRange)` derives it at read time from the configurable `NationwideZipRange` (FR-09). Guarded by a model test and a catalog test. |
| Indexes | `ix_supplier_categories_category_supplier_id` on `(category, supplier_id)`; `ix_supplier_service_areas_start_zip_end_zip` on `(start_zip, end_zip)`. B-tree (ADR: ~5,500 segments; `int4range` + GiST is the upgrade path, not done now). The FK lookups by `supplier_id` are served by the leading column of each PK, so no extra FK index. |
| Naming | snake_case set **explicitly** in `IEntityTypeConfiguration<T>` classes (`ToTable`, `HasColumnName`, `HasDatabaseName`), so no naming-convention package is added and the names match the ADR exactly. |
| Check constraints (defence in depth, fail safe) | `ck_suppliers_rating` (`rating IS NULL OR rating BETWEEN 1 AND 10`), `ck_supplier_service_areas_zip_bounds` (`start_zip BETWEEN 0 AND 99999 AND end_zip BETWEEN 0 AND 99999`), `ck_supplier_service_areas_start_le_end` (`start_zip <= end_zip`). They mirror domain invariants so a bad write can't sneak in. They are additive to the ADR; if the engineer objects during review, remove them. |
| Delete behaviour | FKs from both child tables use `ON DELETE CASCADE`: categories and service areas are part of the supplier aggregate. Suppliers are never deleted today (OQ-24), so this only matters later. |
| PK risk to note (not a blocker) | The domain allows overlapping ranges, so one supplier could in theory have two ranges with the same `start_zip`, which would collide on PK `(supplier_id, start_zip)`. The committed seed data has **0** such collisions (checked). The ADR's PK stays; this is recorded for OR-005, which would fail fast on it. |
| Product code case | PK on `product_code` is case-sensitive. OR-003 already de-duplicates case-insensitively, and case-insensitive lookup is OR-006's job (FR-05). No functional index now. |
| Migrations | EF Core migrations in `src/OrderRouter.Infrastructure/Persistence/Migrations`. One initial migration, `InitialReferenceDataSchema`. AC2 (repeatable) relies on EF's `__EFMigrationsHistory`. `dotnet-ef` is added as a **local tool** (`.config/dotnet-tools.json`) so every machine uses the same version. |
| Connection string | Name `OrderRouter`, read as `ConnectionStrings:OrderRouter` / env `ConnectionStrings__OrderRouter` (ADR-0002). The design-time factory and the test fixture default to the compose PostgreSQL when it's not set. |
| docker-compose | New `docker-compose.yml` at the repo root with one `postgres` service: image pinned to a specific current stable tag (multi-arch, runs on Apple Silicon), `POSTGRES_DB=orderrouter`, `POSTGRES_USER=orderrouter`, password from `${POSTGRES_PASSWORD:-<dev-only default>}`, port `${POSTGRES_PORT:-5432}:5432`, a named volume, and a `pg_isready` healthcheck. **No migration or seed step** (SC-22). OR-007 adds the API service and `.env.example`. The default password is dev-only and the file is never used in production (same stance as OR-007). |
| Packages | Infrastructure: `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x, latest stable) and `Microsoft.EntityFrameworkCore.Design` (10.x, `PrivateAssets=all`). Integration tests use Npgsql transitively via Api → Infrastructure. Comment each package's purpose where it's used. If the latest stable version brings a surprise (as CsvHelper 34 did in OR-003), stop and ask. |
| Async | All database I/O in tests and helpers is `async` with `TestContext.Current.CancellationToken`. |
| Domain events / money / authorization | None. This ticket is a schema with no business state change, no money and no API surface. Say so in the review; don't claim them as met. |

### Target structure

```
docker-compose.yml                                   postgres service only (OR-007 adds the API)
.config/dotnet-tools.json                            local tool: dotnet-ef 10.x
src/OrderRouter.Infrastructure/
  OrderRouter.Infrastructure.csproj                  + Npgsql EF Core provider, EF Core Design
  Persistence/
    OrderRouterDbContext.cs                          DbSets for the four row types; ApplyConfigurationsFromAssembly
    OrderRouterDbContextFactory.cs                   IDesignTimeDbContextFactory for `dotnet ef` only
    PersistenceServiceCollectionExtensions.cs        AddOrderRouterDatabase(IServiceCollection, string connectionString)
    ReferenceData/
      SupplierRow.cs, SupplierCategoryRow.cs, SupplierServiceAreaRow.cs, ProductRow.cs
      SupplierRowConfiguration.cs, SupplierCategoryRowConfiguration.cs,
      SupplierServiceAreaRowConfiguration.cs, ProductRowConfiguration.cs
    Migrations/
      <timestamp>_InitialReferenceDataSchema.cs (+ Designer), OrderRouterDbContextModelSnapshot.cs
tests/OrderRouter.Tests/Infrastructure/Persistence/
  OrderRouterDbContextModelTests.cs                  model metadata, no DB needed
tests/OrderRouter.Api.IntegrationTests/Persistence/
  ThrowawayDatabase.cs                               create / migrate / drop a uniquely named DB
  SchemaMigrationTests.cs                            AC1, AC2, AC3 against real PostgreSQL
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code, and follow them. This is a cross-layer change (new Infrastructure persistence, compose, migrations), so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review in Phase 1.
   - §3 is this plan; §4 implementation in Phases 1–4.
   - §5 post-implementation review in Phase 5.

   Key points:
   - **C#:** C# 14 / .NET 10 / EF Core 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, the final `return` on its own line.
   - **Docs and comments:** XML doc comments on every public type and member, with `<example>`/`<code>` where useful (e.g. `AddOrderRouterDatabase`). Comments explain *why* and cite the requirement or ADR (e.g. `// ADR-0002`, `// SC-09`). Comment each library's purpose where it's used (Npgsql EF Core provider, EF Core Design).
   - **Layers:** Domain gets **no** EF or Npgsql reference and no change. Persistence types live only in Infrastructure. Repository interfaces are OR-006's job.
   - **Fail safe / never auto-migrate:** nothing in this ticket applies migrations at API startup or in `docker-compose` (SC-22, SC-33).
   - **Tests:** follow the style in the nearby test projects: `MethodName_Condition_ExpectedResult` names and no `Arrange`/`Act`/`Assert` comments. Cover edge cases and error paths. Target ≥ 85% coverage of new non-generated code.
   - **Data:** never edit `Docs/Data/*` or `Docs/Data/Seed/*`.
   - **Generated code:** don't hand-edit the generated migration. If it's wrong, fix the configuration and regenerate.
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d`, from Phase 1 on) and run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including the OR-001 architecture tests, the OR-002 domain tests and the OR-003 normalizer tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in this file;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or generated output is unclear or contradicts another, stop and ask the engineer. Don't guess.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-004-database-schema`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-004-database-schema.md` with this plan. From now on, that file is the one whose status gets updated.
- [x] Confirm the three open points with the engineer (rating type, test strategy, API wiring). Recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Domain analysis, architecture review and PostgreSQL in docker-compose ✅ Done

- [x] Write the `ddd-implementation` §1 domain analysis and §2 architecture review under **Phase 1 notes**:
  - Concepts and terms (supplier, category, service area, ZIP range, product, rating) and how each maps to a table.
  - Aggregate boundary: `Supplier` + its categories + its service areas are one aggregate, so the child tables hang off `suppliers` with cascade; `Product` is separate and joins by category value.
  - Layers: Infrastructure only; Domain stays dependency-free; Api untouched.
  - Invariants stay in the domain; check constraints only mirror them as defence in depth.
  - Mail-only is derived (SC-09), never stored, and why.
  - No domain events, money or authorization, and why. No PHI in reference data.
- [x] Add `docker-compose.yml` at the repo root with the `postgres` service described in the design table (pinned tag, named volume, healthcheck, env-overridable password and port, no seed/migrate step).
- [x] Extend `LayerDependencyTests` (reuse `ReadProjectReferenceNames` / `FindRepositoryRoot`): **Domain's csproj has no `PackageReference`**, so EF Core can never leak into the domain.

**Verify:** working rule 2, plus:
- `docker compose up -d` → `docker compose ps` shows `postgres` healthy.
- `docker compose exec postgres pg_isready -U orderrouter` succeeds.
- `docker compose exec postgres psql -U orderrouter -d orderrouter -c '\dt'` shows **no tables** (compose does not migrate or seed, SC-22).

### Phase 1 notes

**Domain analysis (§1)**

| Concept | Table / storage |
|---|---|
| Supplier | `suppliers` (id, name, nullable rating, `can_mail_order`, `seeded_at`) |
| Category (a supplier's capability) | `supplier_categories` (supplier_id, category) |
| Service area = ZIP range | `supplier_service_areas` (supplier_id, start_zip, end_zip) |
| Product | `products` (code, name, category); joins categories by value, no FK |
| Rating | `suppliers.rating`, `numeric(4,2)`, NULL = unrated |

- **Aggregate boundary:** `Supplier` with its categories and service areas is one aggregate, so both child tables hang off `suppliers` with `ON DELETE CASCADE`. `Product` is a separate, read-only reference entity.
- **Mail-only (SC-09):** derived at read time by `Supplier.IsMailOnly(nationwideRange)` from the configurable `NationwideZipRange` (FR-09). Storing it would go stale when that range changes.
- **Invariants** stay in the domain constructors. The check constraints only mirror them as defence in depth.
- **No domain events, money or authorization:** this is a read-only reference schema with no business state change, no monetary values and no API surface. No PHI or payment data is stored.

**Architecture review (§2)**

- Infrastructure only. Domain gets no EF or Npgsql reference (now enforced by `PackageReferences_ForDomain_AreEmpty`). Api is untouched.
- Persistence uses separate row types, projected into domain objects by OR-006.
- Compose only starts PostgreSQL. Nothing migrates or seeds (SC-22, SC-33).

**Result:** `docker-compose.yml` added (`postgres:18.1`, healthy, `\dt` shows no tables). `LayerDependencyTests` gained `PackageReferences_ForDomain_AreEmpty`. `dotnet build` 0 warnings/errors, `dotnet test` 216 passed, `dotnet format --verify-no-changes` clean. No deviations.

---

## Phase 2: Persistence model, DbContext and model tests (AC3 at model level) ✅ Done

- [x] Add the packages to `OrderRouter.Infrastructure.csproj` (see the design table) with comments on their purpose.
- [x] Add the four row types under `Persistence/ReferenceData/`. Plain persistence shapes with XML docs saying they are **not** domain objects and are projected into the domain by OR-006.
- [x] Add one `IEntityTypeConfiguration<T>` per table, implementing the physical schema, names, indexes, check constraints and cascade FKs from the design table. `rating` is `numeric(4,2)`.
- [x] Add `OrderRouterDbContext` (`ApplyConfigurationsFromAssembly`), and `AddOrderRouterDatabase(IServiceCollection, string connectionString)` that registers it with `UseNpgsql`. It throws `ArgumentException` for an empty connection string. It **never** calls `Migrate` or opens a connection.
- [x] Add `OrderRouterDbContextFactory : IDesignTimeDbContextFactory<OrderRouterDbContext>`, used only by `dotnet ef`. It reads `ConnectionStrings__OrderRouter` and falls back to the compose default.
- [x] Tests in `tests/OrderRouter.Tests/Infrastructure/Persistence/OrderRouterDbContextModelTests.cs`. Build the context with `UseNpgsql` on a dummy connection string; no database is opened. Inspect `context.Model` / `GetRelationalModel()`:
  - exactly the four tables with the expected names;
  - each table's columns, types (`rating` is `numeric(4,2)`, ZIPs are `integer`), nullability and PKs;
  - **AC3:** `suppliers` has no column whose name contains `mail_only` / `mailonly` (case-insensitive), and its column set is exactly `supplier_id, name, rating, can_mail_order, seeded_at`;
  - the two named indexes and their column order;
  - the FKs cascade, and the check constraints exist;
  - `AddOrderRouterDatabase` with an empty or whitespace connection string throws; with a valid one, it resolves the context from DI.

**Verify:** working rule 2.

**Result:** Packages added, four row types, configurations, `OrderRouterDbContext`, `AddOrderRouterDatabase` and the design-time factory in place; 32 new model tests, 248 total passing, build 0 warnings, format clean. **Deviation:** `Microsoft.EntityFrameworkCore.Design` latest (11.0.0) targets net11 and won't install, so it is pinned to 10.0.4, the EF Core version Npgsql 10.0.3 brings in (10.0.15 caused MSB3277 assembly-unification warnings). Npgsql provider is 10.0.3.

---

## Phase 3: Initial migration ✅ Done

- [x] Add the local tool manifest and `dotnet-ef` (10.x, matching EF Core): `dotnet new tool-manifest` + `dotnet tool install dotnet-ef`.
- [x] Generate the migration:
  `dotnet ef migrations add InitialReferenceDataSchema --project src/OrderRouter.Infrastructure --output-dir Persistence/Migrations`
- [x] Review the generated SQL with `dotnet ef migrations script --idempotent --project src/OrderRouter.Infrastructure` and check it by hand: four tables, `numeric(4,2)` rating, no mail-only column, both indexes, check constraints, cascade FKs. Record anything unexpected in the Result. If the SQL disagrees with ADR-0002 (other than the agreed rating change), stop and ask.
- [x] Add a unit test `Model_ComparedWithLatestMigration_HasNoPendingChanges` using `context.Database.HasPendingModelChanges()`, so a configuration change without a new migration fails the build.
- [x] Make sure generated migration files don't break `dotnet format --verify-no-changes` or produce warnings. If they do, exclude `Persistence/Migrations` in `.editorconfig` with `generated_code = true` rather than hand-editing them.

**Verify:** working rule 2, plus:
- `dotnet ef database update --project src/OrderRouter.Infrastructure` against the compose database succeeds. This is a manual smoke test of the tooling only; the API and compose still never migrate. The shared database being migrated is harmless: OR-005's tests will migrate it anyway.

**Result:** `.config/dotnet-tools.json` (dotnet-ef 10.0.4, moved from the repo root where `dotnet new tool-manifest` put it) and migration `20261115232317_InitialReferenceDataSchema` generated. Idempotent SQL reviewed by hand: four tables, `numeric(4,2)` rating, no mail-only column, both indexes, three check constraints, cascade FKs; matches ADR-0002 apart from the agreed rating change. Added `Model_ComparedWithLatestMigration_HasNoPendingChanges` (249 tests passing). `dotnet format` flagged the generated migration (charset, file-scoped namespace), so `Persistence/Migrations` is marked `generated_code = true` in `.editorconfig`. `dotnet ef database update` against compose succeeded.

---

## Phase 4: Schema integration tests against PostgreSQL (AC1, AC2, AC3) ✅ Done

- [x] `tests/OrderRouter.Api.IntegrationTests/Persistence/ThrowawayDatabase.cs` (`IAsyncDisposable`):
  - reads `ConnectionStrings__OrderRouter` (defaults to the compose server);
  - creates `or_schema_test_<guid-n>` through a connection to the server's maintenance database;
  - exposes a connection string and an `OrderRouterDbContext` for it;
  - on dispose, drops it `WITH (FORCE)`, so a failed test leaves nothing behind.
  - If PostgreSQL is unreachable, the test **fails** with a clear message ("start it with `docker compose up -d`"). It is never silently skipped.
- [x] `SchemaMigrationTests.cs`, each test on its own throwaway database:
  - **AC1:** on an empty database, `MigrateAsync()` → `information_schema.tables` holds exactly the four tables (plus `__EFMigrationsHistory`), and `pg_indexes` holds `ix_supplier_categories_category_supplier_id` on `(category, supplier_id)` and `ix_supplier_service_areas_start_zip_end_zip` on `(start_zip, end_zip)`.
  - **AC2:** migrate, capture a schema snapshot (tables, columns + types, indexes + definitions, constraints, migration-history rows), migrate again → no exception, `GetPendingMigrationsAsync()` is empty, and the snapshot is identical.
  - **AC3:** `information_schema.columns` for `suppliers` is exactly `supplier_id, name, rating, can_mail_order, seeded_at`, with no mail-only column.
  - `rating` is `numeric(4,2)`: inserting `6.75` reads back `6.75` (proves no silent rounding); inserting `11` violates `ck_suppliers_rating`.
  - `start_zip > end_zip` violates the check constraint. Deleting a supplier cascades to its categories and service areas.
- [x] Confirm the existing `ApiHostTests` still pass. `Program.cs` is unchanged.

**Verify:** working rule 2 with `docker compose up -d` running. Also run `dotnet test` with PostgreSQL stopped once, to confirm the schema tests fail with the clear message, then start it again.

**Result:** `ThrowawayDatabase` and 11 `SchemaMigrationTests` cases (AC1, AC2, AC3, rating precision and bounds, ZIP checks, FK, cascade). 260 tests passing, no leftover `or_schema_test_*` databases. With PostgreSQL stopped, exactly those 11 tests fail with the "start it with `docker compose up -d`" message (not skipped). `ApiHostTests` still pass; `Program.cs` unchanged.

---

## Phase 5: Clean-up, full verification and post-implementation review ✅ Done

- [x] Run coverage for the new non-generated Infrastructure code (target ≥ 85%). If a coverage package is needed, ask first. This is still open from OR-003 (`Microsoft.Testing.Extensions.CodeCoverage`).
- [x] Run the `ddd-implementation` §5 review and record it under **Review report**. Report honestly:
  - **Applies:** Domain untouched and EF-free (tested); persistence separated from the domain; schema matches ADR-0002 (with the agreed rating change); mail-only not stored; no auto-migrate anywhere; XML docs; test naming; async I/O; edge and error tests.
  - **N/A (say why, don't claim met):** domain events and audit trail, money, authorization, PCI-DSS/SOX/LGPD (no PHI or payment data).
- [x] Record follow-ups for other tickets (don't do them here):
  - **ADR-0002:** change `rating numeric_3_1` to `numeric_4_2` in the schema diagram and give the reason. Mention the additive check constraints and the `(supplier_id, start_zip)` PK collision risk.
  - **OR-005:** the seeder applies these migrations with `MigrateAsync()`, sets `seeded_at`, and must fail fast on a `(supplier_id, start_zip)` PK collision. Note that re-seeding updates `seeded_at`, so "nothing changes" in OR-005 AC2 must mean row counts and business columns, not that timestamp.
  - **OR-006 / OR-007:** call `AddOrderRouterDatabase` from `Program.cs` with `ConnectionStrings:OrderRouter`. Never call `Migrate` at startup (SC-33).
  - **OR-007:** add `POSTGRES_PASSWORD` (and the API's `ConnectionStrings__OrderRouter`) to `.env.example`, and add the API service to this `docker-compose.yml`.
  - **README (OR-025):** `docker compose up -d` is needed before `dotnet test`.
- [x] Final `docker compose down -v && docker compose up -d`, then `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root: all green.

### Review report

**Applies (met):**
- Domain untouched and EF-free, enforced by `PackageReferences_ForDomain_AreEmpty`; Api and `Program.cs` untouched.
- Persistence types separate from the domain, Infrastructure only.
- Schema matches ADR-0002 apart from the agreed `numeric(4,2)` rating and the additive check constraints.
- Mail-only is not stored (model test and catalog test).
- No auto-migrate anywhere: the extension never calls `Migrate` or opens a connection, and compose has no migrate or seed step.
- XML docs on public types, `MethodName_Condition_ExpectedResult` test names, async I/O with `TestContext.Current.CancellationToken`, edge and error tests (blank/null connection string, constraint violations, FK, cascade, unreachable server).

**N/A (not claimed as met):**
- Domain events and audit trail: no business state change.
- Money: no monetary values.
- Authorization: no API surface.
- PCI-DSS / SOX / LGPD: no PHI or payment data.

**Open / caveats:**
- Coverage figure not measured (see Result).
- The design-time factory reads the process environment, so its unit test mutates `ConnectionStrings__OrderRouter` and restores it; no other unit test touches it.

**Follow-ups for other tickets (not done here):**
- **ADR-0002:** change `rating numeric_3_1` to `numeric_4_2` in the diagram with the reason; mention the additive check constraints and the `(supplier_id, start_zip)` PK collision risk.
- **OR-005:** apply migrations with `MigrateAsync()`, set `seeded_at`, fail fast on a `(supplier_id, start_zip)` PK collision. Re-seeding updates `seeded_at`, so "nothing changes" in OR-005 AC2 must mean row counts and business columns.
- **OR-006 / OR-007:** call `AddOrderRouterDatabase` from `Program.cs` with `ConnectionStrings:OrderRouter`; never migrate at startup (SC-33).
- **OR-007:** add `POSTGRES_PASSWORD` and the API's `ConnectionStrings__OrderRouter` to `.env.example`, and the API service to `docker-compose.yml`.
- **README (OR-025):** `docker compose up -d` is needed before `dotnet test`.

**Result:** Final `docker compose down -v && up -d`, then `dotnet build` (0 warnings/errors), `dotnet test` (260 passed) and `dotnet format --verify-no-changes` all green. Coverage **not measured**: no coverage package is installed beyond `coverlet.collector`, and the plan says to ask first (still open from OR-003). Coverage by inspection: every new non-generated type is exercised by the model or schema tests.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-004-database-schema.md` |
| New | `docker-compose.yml`, `.config/dotnet-tools.json` |
| New (Infrastructure) | `src/OrderRouter.Infrastructure/Persistence/**` (see Target structure), including generated `Migrations/**` |
| New (tests) | `tests/OrderRouter.Tests/Infrastructure/Persistence/OrderRouterDbContextModelTests.cs`, `tests/OrderRouter.Api.IntegrationTests/Persistence/{ThrowawayDatabase,SchemaMigrationTests}.cs` |
| Modified | `src/OrderRouter.Infrastructure/OrderRouter.Infrastructure.csproj` (packages), `tests/OrderRouter.Tests/Architecture/LayerDependencyTests.cs`, possibly `.editorconfig` (generated migrations only) |
| Unchanged | `src/OrderRouter.Domain/**`, `src/OrderRouter.Api/Program.cs`, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Schema is created | `SchemaMigrationTests` (empty throwaway DB → four tables + both indexes) + manual `dotnet ef database update` | 4 (3) |
| AC2: Migrations are repeatable | `SchemaMigrationTests` (migrate twice → no error, no pending migrations, identical schema snapshot) | 4 |
| AC3: Mail-only is derived, not stored | `OrderRouterDbContextModelTests` (model) + `SchemaMigrationTests` (`information_schema.columns`) + SQL review | 2, 3, 4 |

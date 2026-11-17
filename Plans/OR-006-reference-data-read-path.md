# Plan: OR-006: Read reference data for routing

**Ticket:** [Docs/Tickets/OR-006-reference-data-read-path.md](../Docs/Tickets/OR-006-reference-data-read-path.md)
**Depends on:** OR-004 (done, see [OR-004-database-schema.md](OR-004-database-schema.md)); uses the data seeded by OR-005 (done, see [OR-005-reference-data-seeder.md](OR-005-reference-data-seeder.md))
**References:** [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Read path); [ADR-0006](../Docs/ADRs/ADR-0006-reference-data-caching.md) (option A, no cache); Technical Requirements §5.1, FR-05, FR-06, FR-16, NFR-03, NFR-10, DQ-09; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-007 (calls the DI extension from `Program.cs`), OR-010 (eligibility uses `IReferenceDataReader`), OR-015 (orchestration uses both ports; `UNKNOWN_PRODUCT`), OR-027 (end-to-end AC4)

> **Save location:** Phase 0 copies this plan to `Plans/OR-006-reference-data-read-path.md`. That file is the one to update as each phase is done.

## Context

The database exists (OR-004) and the integration-test fixture seeds it (OR-005), but nothing reads it yet. Routing needs two lookups:

1. **Product code → category** (FR-05, FR-16). The answer is either a category or *unknown*, which later becomes `UNKNOWN_PRODUCT` (§5.1). It is never `null` and never throws.
2. **Suppliers for a set of categories** (FR-06). Each supplier comes back as a full OR-002 `Supplier`, with **all** its categories and service areas, so OR-010 can apply ZIP and mail rules in the domain.

OR-006 adds the two **domain ports**, `ICapabilityResolver` and `IReferenceDataReader`, and their **EF Core read-only implementations** in Infrastructure (`AsNoTracking`). They read straight from PostgreSQL. **There is no cache** (ADR-0006 option A, NFR-03).

Out of scope: wiring into `Program.cs`, the connection-string config and `/health` (OR-007); ZIP and mail eligibility rules (OR-010); routing orchestration and the `UNKNOWN_PRODUCT` mapping (OR-015); order tables (OR-026/OR-027); any cache (ADR-0006); new migrations or indexes.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| AC4 (read only) | **Read-path level now.** An integration test records every SQL command the two implementations send, using an EF Core `DbCommandInterceptor`, and asserts that each one is a `SELECT` and that the change tracker is empty afterwards. A source guard test fails if the read-path files ever call `SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql` or `Add`/`Update`/`Remove`. The end-to-end check ("the API handling a routing request") is a **follow-up for OR-015/OR-027**. |
| DI wiring | **Extension only, no `Program.cs` change.** `AddOrderRouterDatabase` also registers `ICapabilityResolver` and `IReferenceDataReader` as **scoped**, which matches the `DbContext` lifetime. OR-007 calls it from `Program.cs`. |
| Resolver shape | **Batch + single.** `ResolveAsync(IEnumerable<string?> productCodes, CancellationToken)` sends **one** query and returns one result per input code, keyed by the input string (ordinal). This follows ADR-0006 option A: one round trip per lookup kind. `ResolveAsync(string? productCode, CancellationToken)` is a thin convenience over the batch method. |
| Whitespace | **Remove all whitespace, then compare without case.** `" ox-port-024 "`, `"OX- PORT-024"` and `"ox-port-024\t"` all resolve to `OX-PORT-024`. Whitespace means `char.IsWhiteSpace`. **Assumption, checked now and guarded by a test:** no stored product code contains whitespace (0 of 1,195 in `Docs/Data/Seed/products.normalized.csv`). The database side therefore only needs `lower(product_code)`, and two different stored codes can't collapse into one key. |

### Other design choices (from the ticket, ADRs, rules and existing code; no question needed)

| Topic | Choice and reason |
|---|---|
| Port location | `src/OrderRouter.Domain/ReferenceData/`. ADR-0002 and the DDD rules say repository interfaces are defined in Domain, and Domain gets **no** EF or Npgsql reference. Both ports are `async` and take a `CancellationToken`. |
| Lookup key (FR-05 rule in Domain) | A new `ProductCodeKey` `readonly record struct` in Domain. `TryCreate(string?)` removes all whitespace and lower-cases with the invariant culture. It returns `false` for null, empty or whitespace-only input, so those inputs are *unknown* without a database call. The matching rule lives in one place and can be unit tested. `Product.Code` stays case-preserving (DQ-09). |
| Result type | `CapabilityResolution`, an immutable `readonly record struct` with `static Unknown`, `static Resolved(Category)`, `bool IsKnown` and `bool TryGetCategory(out Category)`. `default(CapabilityResolution)` is *unknown*, so a missing value is safe. It is never `null` (§5.1). Reading the category of an unknown result through a property throws `InvalidOperationException`, the same pattern as `Category.Value`. |
| Resolver query | `context.Products.AsNoTracking().Where(p => keys.Contains(p.ProductCode.ToLower())).Select(p => new { p.ProductCode, p.Category })`. Npgsql translates this to `lower(product_code) = ANY(@keys)`, which is parameterized and avoids `ILIKE` wildcard problems with `%` and `_`. With no valid keys, it returns all-unknown **without** a query. If two rows match one key (a case-only duplicate, which OR-005 already rejects), it **throws** `InvalidOperationException` naming the codes, because ambiguous data must not route (CLAUDE.md fail safe). That is a data-integrity fault, not an *unknown* product. There is no functional index: about 1,200 rows, and ADR-0006 load testing decides on tuning. |
| Reader API | `Task<IReadOnlyList<Supplier>> GetSuppliersHandlingAsync(IEnumerable<Category> categories, CancellationToken)`. It returns suppliers that handle **any** of the categories, with **full** category and service-area sets (AC3). The result is ordered by `SupplierId` (ordinal) so it is deterministic for the plan search and tests. An empty category set returns an empty list without a query. Unknown categories just return no suppliers. |
| Reader query | `context.Suppliers.AsNoTracking().Where(s => s.Categories.Any(c => keys.Contains(c.Category))).Include(Categories).Include(ServiceAreas).AsSplitQuery()`. Split query avoids the categories × service-areas cartesian blow-up; that's 3 round trips but one lookup. The keys are `Category.Value`, which is already normalized, so this is plain equality on the indexed `(category, supplier_id)`. |
| Row → domain mapping | An `internal static SupplierRowMapper` in Infrastructure: `new Category(row.Category)`, `new ZipRange(ZipCode.FromNumber(start), ZipCode.FromNumber(end))`, `new ServiceArea(...)`, rating `decimal?` → `Supplier` ctor. The OR-002 constructors stay the only place invariants are checked. A row they reject (e.g., a supplier with no service areas) throws `InvalidOperationException` that names the supplier ID, with the `DomainValidationException` as the inner exception. It never skips or repairs a row (NFR-10). Products map the same way: `new Category(row.Category)`. |
| Mail-only | Not read or stored; OR-010 derives it from config (SC-09). Out of scope here. |
| Domain events / money / authorization | None. These are read-only queries over reference data: no state change, no money, no API surface yet. Say so in the review and don't claim them as met. |

### Target structure

```
src/OrderRouter.Domain/ReferenceData/
  ProductCodeKey.cs                       FR-05 lookup key (remove whitespace, lower-case)
  CapabilityResolution.cs                 Resolved(Category) | Unknown, never null
  ICapabilityResolver.cs                  port: product code(s) → resolution
  IReferenceDataReader.cs                 port: categories → suppliers (full aggregates)
src/OrderRouter.Infrastructure/Persistence/
  PersistenceServiceCollectionExtensions.cs   + scoped registration of both ports
  ReferenceData/EfCapabilityResolver.cs       AsNoTracking, one query per batch
  ReferenceData/EfReferenceDataReader.cs      AsNoTracking + split query
  ReferenceData/SupplierRowMapper.cs          rows → Supplier via OR-002 ctors
tests/OrderRouter.Tests/
  Domain/ReferenceData/ProductCodeKeyTests.cs
  Domain/ReferenceData/CapabilityResolutionTests.cs
  Infrastructure/Persistence/ReferenceData/SupplierRowMapperTests.cs
  Infrastructure/Persistence/PersistenceRegistrationTests.cs
  Architecture/ReadPathBoundaryTests.cs       AC4 source guard
tests/OrderRouter.Api.IntegrationTests/Persistence/
  CapabilityResolverTests.cs                  AC1, AC2 on the seeded shared DB
  ReferenceDataReaderTests.cs                 AC3 on the seeded shared DB
  ReadOnlyReferenceDataTests.cs               AC4 (SQL interceptor + change tracker)
  CapturingCommandInterceptor.cs              records executed SQL
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer change (new domain ports plus Infrastructure adapters), so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review in Phase 1;
   - §3 is this plan, and §4 implementation runs through Phases 1–4;
   - §5 post-implementation review in Phase 5.

   Key points:
   - **C#:** C# 14 / .NET 10 / EF Core 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, and the final `return` on its own line. Private constants are camelCase per `.editorconfig`.
   - **Docs and comments:** XML doc comments on every public type and member, with `<example>`/`<code>` on both ports. Comments explain *why* and cite the requirement (e.g. `// FR-05`, `// ADR-0006 option A`). Comment library usage where it isn't obvious (EF Core split query, Npgsql `lower()` translation).
   - **Layers:** Domain gets no EF or Npgsql reference, and the `LayerDependencyTests` must stay green. `Program.cs` and the Api project don't change.
   - **Fail safe:** unknown → `CapabilityResolution.Unknown`, never `null`. Corrupt or ambiguous data → throw with context, never skip or repair.
   - **Tests:** follow the nearby style: `MethodName_Condition_ExpectedResult` names, no `Arrange`/`Act`/`Assert` comments, `TestContext.Current.CancellationToken`. Integration tests use the shared seeded DB through `ReferenceDataFixture` and use **containment** assertions (the DB may hold older rows, OR-005 AC4). Cover edge cases and error paths. Target ≥ 85% coverage of the new code.
   - **Data:** never edit `Docs/Data/*` or `Docs/Data/Seed/*`. Tests only read reference data (NFR-08).
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d`) and run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including the existing architecture, domain, normalizer, model, schema and seeder tests.
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-006-reference-data-read-path.md`;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or library behaviour is unclear or contradicts another (for example, Npgsql doesn't translate `ToLower()` as expected, or a stored code turns out to contain whitespace), stop and ask the engineer. Don't guess.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-006-reference-data-read-path`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-006-reference-data-read-path.md` with this plan.
- [x] Confirm the four decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: Domain analysis, architecture review and the domain ports ✅ Done

- [x] Write the `ddd-implementation` §1 domain analysis and §2 architecture review under **Phase 1 notes**:
  - concepts and terms: product code, lookup key, capability (category), resolution (resolved or unknown), supplier aggregate, service area;
  - aggregate boundary: `Supplier` is always loaded **whole**, with its categories and service areas (AC3). `Product` is read only through the resolver;
  - invariants stay in the OR-002 constructors; the new FR-05 matching rule lives in `ProductCodeKey`;
  - layers: ports in Domain, adapters in Infrastructure, no Application or Api change;
  - no domain events, money, authorization or PHI, and why.
- [x] Add `ProductCodeKey`, `CapabilityResolution`, `ICapabilityResolver` and `IReferenceDataReader` to `src/OrderRouter.Domain/ReferenceData/`, following the design rows above.
- [x] `ProductCodeKeyTests`:
  - `" ox-port-024 "`, `"OX- PORT-024"`, tabs and newlines all give `ox-port-024`;
  - null, empty and whitespace-only input give `TryCreate == false`;
  - equality is ordinal on the key;
  - non-ASCII letters are lower-cased with the invariant culture.
- [x] `CapabilityResolutionTests`:
  - `Resolved(category)` is known, and `TryGetCategory` returns the category;
  - `Unknown` and `default` are both unknown;
  - reading the category of an unknown result throws `InvalidOperationException`;
  - value equality.

**Verify:** working rule 2. `LayerDependencyTests` still pass, which proves Domain got no EF or Npgsql reference.

**Result:** 330 tests, 0 failed; build 0 warnings. `ICapabilityResolver.ResolveAsync` batch returns `IReadOnlyDictionary<string, CapabilityResolution>`.

### Phase 1 notes

**§1 Domain analysis**
- *Terms:* product code (opaque catalog key, case preserved, DQ-09); lookup key (`ProductCodeKey`: no whitespace, lower-case); capability (a `Category`); resolution (`Resolved(Category)` or `Unknown`); supplier aggregate (whole `Supplier` with all categories and its `ServiceArea`).
- *Aggregate boundary:* `Supplier` is always loaded whole (AC3). `Product` is read only through the resolver, which exposes just its category.
- *Invariants:* stay in the OR-002 constructors; the mapper never bypasses them. The only new rule, FR-05 matching, lives in `ProductCodeKey`.
- *Fail safe:* unknown is a value (`default` is unknown), never `null` or an exception. Ambiguous or corrupt stored data throws with context.

**§2 Architecture review**
- Ports in Domain (no EF or Npgsql reference); adapters in Infrastructure; no Application or Api change; DI extension only.
- No domain events, money, authorization or PHI: these are read-only queries over non-patient reference data with no API surface yet.

---

## Phase 2: Capability resolver (AC1, AC2) ✅ Done

- [x] Add `EfCapabilityResolver(OrderRouterDbContext context) : ICapabilityResolver` in `Persistence/ReferenceData/`, following the *Resolver query* row:
  - map distinct `ProductCodeKey`s → one `AsNoTracking` query → `Dictionary<string, CapabilityResolution>` keyed by every original input string (ordinal);
  - inputs with no key, and keys with no match, are `Unknown`;
  - two rows for one key throw `InvalidOperationException`;
  - the single-code overload calls the batch method.
- [x] `CapabilityResolverTests` (integration, shared seeded DB via `ReferenceDataFixture`):
  - **AC1:** `" ox-port-024 "` → `oxygen`, and so do `"OX-PORT-024"` and `"OX- PORT -024"`;
  - exact match only: `"OX-PORT-024"` does not resolve to `OX-PORT-024-PRO`;
  - **AC2:** `"XX-UNKNOWN-999"`, `""`, `"   "` and `null` → `Unknown`, no exception. Wildcard-looking codes such as `"OX-PORT-02_"` and `"%"` → `Unknown`;
  - batch: known and unknown mixed, the same code given twice in different spellings, an empty list (no query sent, checked with the interceptor from Phase 4 or a query counter);
  - the result has one entry per input string and never a `null` value.
- [x] Guard test (unit): no product code in `Docs/Data/Seed/products.normalized.csv` contains whitespace. This protects the *Whitespace* decision.
- [x] Ambiguous-row error path: on a `ThrowawayDatabase`, migrate it, insert `ABC-1` and `abc-1` with raw SQL (bypassing the seeder's check), and assert that resolving `abc-1` throws and the message names both codes. Raw SQL is allowed here because it is a throwaway DB, not reference data.

**Verify:** working rule 2.

**Result:** 349 tests, 0 failed (19 new in Phases 1–2 incl. 4 extra theory cases). Build 0 warnings, format clean. Deviation: a null input in the batch overload has no dictionary key, so it is *absent* from the result (documented on the port; the single-code overload returns `Unknown` for null).

---

## Phase 3: Reference data reader (AC3) ✅ Done

- [x] Add `SupplierRowMapper` (internal) and `EfReferenceDataReader(OrderRouterDbContext context) : IReferenceDataReader`, following the *Reader query* and *Row → domain mapping* rows.
- [x] `SupplierRowMapperTests` (unit, no DB):
  - a full row maps to an equal `Supplier`, with the same categories, segments, rating and mail flag;
  - start ZIP `2130` maps to `"02130"`;
  - a `null` rating stays unrated;
  - a supplier with no service areas or no categories, or a ZIP outside 0–99999, throws `InvalidOperationException` that names the supplier ID and has the domain exception as its inner exception.
- [x] `ReferenceDataReaderTests` (integration, shared seeded DB):
  - **AC3:** for `wheelchair`, every returned supplier `Handles(wheelchair)`, and the list contains every `wheelchair` supplier from the seed file (containment). For each one, the categories and segments **equal** the seed file's (compare with `NormalizedSeedFileReader` output, the pattern from `SeededReferenceDataTests`). `SUP-T001` is included;
  - several categories give the union with no duplicates;
  - an unknown category and an empty set each give an empty list (the empty set sends no query);
  - the result is ordered by supplier ID (ordinal);
  - a supplier that handles both requested categories appears once.

**Verify:** working rule 2.

**Result:** 367 tests, 0 failed. Deviation: `InternalsVisibleTo OrderRouter.Tests` added to the Infrastructure csproj so the internal `SupplierRowMapper` is unit-testable. Supplier ordering is done in memory (ordinal), not in SQL, because PostgreSQL orders by collation.

---

## Phase 4: DI registration and read-only proof (AC4) ✅ Done

- [x] Extend `AddOrderRouterDatabase` to register `ICapabilityResolver` → `EfCapabilityResolver` and `IReferenceDataReader` → `EfReferenceDataReader` as scoped. Update its XML doc. Do **not** register the seeder, and do **not** call `Migrate` (SC-22, SC-33).
- [x] `PersistenceRegistrationTests` (unit):
  - both ports resolve from a scope to the EF implementations, and building the provider and resolving them opens no connection;
  - `ReferenceDataSeeder` is still not resolvable.
- [x] Add `CapturingCommandInterceptor` (test helper) and `ReadOnlyReferenceDataTests` (integration). Resolve a batch and read suppliers for several categories through a context that has the interceptor, then assert:
  - every recorded command starts with `SELECT`, ignoring leading whitespace and case;
  - `context.ChangeTracker.Entries()` is empty.
- [x] `ReadPathBoundaryTests` (unit, source guard, same pattern as `SeedingBoundaryTests`): `EfCapabilityResolver.cs`, `EfReferenceDataReader.cs` and `SupplierRowMapper.cs` contain no `SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql`, `.Add(`, `.Update(`, `.Remove(` or `.Attach(`, and every `DbSet` query in them uses `AsNoTracking`.

**Verify:** working rule 2. The existing `SeedingBoundaryTests` still pass.

**Result:** 376 tests, 0 failed; build 0 warnings; `dotnet format --verify-no-changes` clean. Existing `SeedingBoundaryTests` still pass. The resolver was restructured with LINQ so the strict `.Add(` source guard passes.


---

## Phase 5: Post-implementation review and follow-ups ✅ Done

- [x] Run the `ddd-implementation` §5 review and write a **Review report**, in three parts:
  - **Applies and met:** state each one, don't just claim it;
  - **N/A (say why):** domain events and audit trail, money, authorization, PCI-DSS/SOX/LGPD;
  - **Not verified (be honest):** e.g. the coverage %, and NFR-03 latency (load test, ADR-0006).
- [x] Record follow-ups for other tickets (don't do them here):
  - **OR-007:** call `AddOrderRouterDatabase(builder.Configuration.GetConnectionString("OrderRouter")!)` from `Program.cs`. This now also wires both ports. Never migrate at startup.
  - **OR-010:** use `IReferenceDataReader.GetSuppliersHandlingAsync` and apply ZIP and mail rules in the domain.
  - **OR-015:** map `CapabilityResolution.Unknown` → `UNKNOWN_PRODUCT`; call the **batch** resolver once per request.
  - **OR-015/OR-027:** the end-to-end AC4 test (POST a routing request, then reference-table row counts and `xmin` are unchanged).
  - **ADR-0006:** an expression index on `lower(product_code)` only if load testing shows a need.
- [x] Final run: `docker compose up -d`, then `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root, all green.

### Review report

**Applies and met**
- *Layering / SOLID:* ports in Domain, EF adapters in Infrastructure, constructor injection, `LayerDependencyTests` green.
- *Fail safe (CLAUDE.md, NFR-10):* unknown product, null, empty and wildcard codes give `Unknown`; ambiguous stored codes and invalid stored suppliers throw naming the row. Tested on throwaway databases.
- *AC1–AC4:* see the verification summary; AC4 is proven at the read-path level (SELECT-only SQL, empty change tracker, source guard).
- *ADR-0006 option A:* no cache; one round trip per resolver batch; split query (3 round trips) per supplier read.
- *Tests:* unit and integration, edge and error paths, project naming style, no Arrange/Act/Assert comments.

**N/A**
- *Domain events and audit trail:* no state changes.
- *Money (`decimal`):* none involved.
- *Authorization:* no API surface yet (OR-007).
- *PCI-DSS / SOX / LGPD:* reference data only, no payment data or personal data.

**Not verified**
- *Coverage %:* the ≥ 85% target was not measured. Every public member and error path has a test.
- *NFR-03 latency:* not load tested (ADR-0006).
- *End-to-end AC4:* deferred to OR-015/OR-027.
- *Whitespace assumption:* guarded only against the committed seed file; the shared DB may hold older rows (OR-005 AC4).

### Follow-ups (other tickets, not done here)
- **OR-007:** call `AddOrderRouterDatabase(builder.Configuration.GetConnectionString("OrderRouter")!)` from `Program.cs`. It now also wires both ports. Never migrate at startup.
- **OR-010:** use `IReferenceDataReader.GetSuppliersHandlingAsync` and apply ZIP and mail rules in the domain.
- **OR-015:** map `CapabilityResolution.Unknown` (and codes missing from the batch result, such as null) → `UNKNOWN_PRODUCT`; call the batch resolver once per request.
- **OR-015/OR-027:** end-to-end AC4 test (POST a routing request, then reference-table row counts and `xmin` unchanged).
- **ADR-0006:** an expression index on `lower(product_code)` only if load testing shows a need.

**Result:** final run green: 376 tests, 0 failed; `dotnet build` 0 warnings; `dotnet format --verify-no-changes` clean.


---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-006-reference-data-read-path.md` |
| New (Domain) | `src/OrderRouter.Domain/ReferenceData/{ProductCodeKey,CapabilityResolution,ICapabilityResolver,IReferenceDataReader}.cs` |
| New (Infrastructure) | `src/OrderRouter.Infrastructure/Persistence/ReferenceData/{EfCapabilityResolver,EfReferenceDataReader,SupplierRowMapper}.cs` |
| Modified | `src/OrderRouter.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` (register the ports) |
| New (tests) | see *Target structure* |
| Reused | `OrderRouterDbContext` and the row types (`Persistence/ReferenceData/*Row.cs`); OR-002 ctors (`Supplier`, `Category`, `ZipCode.FromNumber`, `ZipRange`, `ServiceArea`); `ReferenceDataFixture` (shared seeded DB, `QueryAsync`); `ThrowawayDatabase`; `SeedFiles.Real` + `NormalizedSeedFileReader` (expected values); the `SeedingBoundaryTests` source-guard pattern |
| Unchanged | `src/OrderRouter.Api/**`, `src/OrderRouter.Application/**`, migrations, `docker-compose.yml`, `Docs/Data/**`, the seeder |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: Product lookup ignores case and spaces | `ProductCodeKeyTests` + `CapabilityResolverTests` (`" ox-port-024 "` → `oxygen`) | 1, 2 |
| AC2: Unknown product | `CapabilityResolutionTests` + `CapabilityResolverTests` (unknown, empty, null, wildcard → `Unknown`, no throw) | 1, 2 |
| AC3: Suppliers by category | `SupplierRowMapperTests` + `ReferenceDataReaderTests` (`wheelchair`: all handle it, full sets equal the seed file) | 3 |
| AC4: Read only | `ReadOnlyReferenceDataTests` (SELECT-only SQL, empty change tracker) + `ReadPathBoundaryTests` (source guard); end-to-end → OR-015/OR-027 follow-up | 4 |

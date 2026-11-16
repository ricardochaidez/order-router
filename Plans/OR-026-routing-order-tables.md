# Plan: OR-026: Create the tables for stored routing orders

**Ticket:** [Docs/Tickets/OR-026-routing-order-tables.md](../Docs/Tickets/OR-026-routing-order-tables.md)
**Depends on:** OR-004 (done: `OrderRouterDbContext`, initial migration, `ThrowawayDatabase`, `SchemaMigrationTests`).
**References:** [ADR-0007](../Docs/ADRs/ADR-0007-routing-decision-persistence.md) (Schema); [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md); Technical Requirements FR-20, SC-28, SC-21, SC-22; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-027 (inserts into these tables), OR-028 (reads them), OR-021 (log needs the stored `id`).

> **Save location:** this file is `Plans/OR-026-routing-order-tables.md`. Update it as each phase is done.

## Context

ADR-0007 stores every routing decision in three new tables next to the reference data. This ticket adds only the schema: EF Core row types, their mappings and one migration. No routing, endpoint or response change. OR-027 writes to the tables.

The current context has reference-data rows only (`SupplierRow` and friends in `Infrastructure/Persistence/ReferenceData`, mapped with `IEntityTypeConfiguration<T>`, explicit snake_case names, picked up by `ApplyConfigurationsFromAssembly`). The new rows follow the same pattern in a new `Persistence/RoutingOrders` folder.

Out of scope: writing or reading orders (OR-027, OR-028), `ICallerHasher` (OR-027), any API change, the decision log (OR-021).

### Decision made with the engineer
- **`caller_hash` is nullable.** A token with no caller claim has a null caller ID (OR-016 AC11). OR-027 stores `NULL` for it, and OR-028 never matches a `NULL` hash, so such an order can never be read back (fail safe). ADR-0007 shows `caller_hash text`; Phase 2 adds a note that it is nullable.

### Design choices (from ADR-0007 and the existing code)

| Topic | Choice and reason |
|---|---|
| Row types | `RoutingOrderRow`, `RoutingOrderItemRow`, `RoutingOrderWarningRow`: plain classes like `SupplierRow` (persistence shapes, not domain types). They stay `internal` or `public` to match the existing rows. |
| Columns | Exactly the ADR-0007 ER diagram: `routing_orders` (`id` uuid PK, `order_id` text, `caller_hash` text null, `customer_zip` char(5), `mail_order` bool, `status` text, `feasible` bool, `experiment_arms` jsonb, `config_version` text, `search_mode` text null, `created_at` timestamptz); `routing_order_items` (composite PK `routing_order_id` + `line_number`; `product_code`, `quantity`, `category` null, `supplier_id` null, `supplier_name` null, `fulfillment_mode` null, `reason_code` null, `message` null); `routing_order_warnings` (composite PK `routing_order_id` + `ordinal`; `code`, `message`, `unfulfilled_product_codes` text[], `affected_product_codes` text[]). |
| `search_mode` nullable | A not-routed order with no known product runs no search (`RoutingDecision.SearchMode` is null). ADR-0007 lists it as `text`; make it nullable and note it. |
| Ids | No `ValueGeneratedOnAdd` on `id`: the server sets a UUIDv7 in OR-027 (`Guid.CreateVersion7()`). |
| Constraints | Check constraints in the style of `ck_suppliers_rating`: `status` in (`routed`, `partially_routed`, `not_routed`), `search_mode` in (`exact`, `greedy`) or null, `fulfillment_mode` in (`local`, `mail`) or null, `quantity >= 1`, `line_number >= 1`. |
| Relations | Items and warnings cascade-delete with their order (same aggregate, as for supplier children). **No foreign key to `suppliers`** (AC3): `supplier_id` is a plain text column. |
| Indexes | Primary keys plus `ix_routing_orders_order_id` on `order_id` (non-unique, AC2). |
| Migration | One migration `AddRoutingOrderTables` generated with `dotnet ef migrations add` using the existing tool manifest (`.config/dotnet-tools.json`). The API never migrates at startup; only the integration tests do (SC-21, SC-22). |
| Seeder | Untouched. It must never write or delete order tables (AC4). |
| Layering | Infrastructure only. `LayerDependencyTests` stay green. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before writing code. Run `ddd-implementation` §1 and §2 at the start of Phase 1 (state the decision if skipped as a schema-only change). Run §5 in Phase 2.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, switch expressions, `nameof`, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML docs on public types; comments explain *why* and cite the ADR or requirement.
   - **Tests:** xUnit v3, `MethodName_Condition_ExpectedResult` unless nearby tests differ (copy them), no Arrange/Act/Assert comments.
   - **Data:** never edit `Docs/Data/*`. Never edit an existing migration; add a new one.
2. **Tests must pass after each phase.** Start PostgreSQL first (`docker compose up -d postgres`), then from the repo root:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including existing tests.
   - `dotnet format --verify-no-changes`: clean (the known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count or fix it).
3. **Mark each phase as done.** When a phase's checks pass, change its heading from `⬜ Not started` to `✅ Done` in this file, tick its checklist and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize changes and test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - the generated migration touches reference-data tables (it should only add);
   - `SchemaMigrationTests` or the model snapshot disagree with the new mappings;
   - a column in ADR-0007 can't be mapped as written (for example the jsonb or text[] types);
   - the seeder tests show the order tables being touched.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-026-routing-order-tables`).

---

## Phase 1: Row types, mappings and migration ✅ Done

- [x] `ddd-implementation` §1 and §2 (concepts: stored routing order and its items and warnings; persistence only, no domain type).
- [x] Add the three row types and their `IEntityTypeConfiguration<T>` classes in `Persistence/RoutingOrders/`, plus `DbSet`s on `OrderRouterDbContext`.
- [x] Generate the migration `AddRoutingOrderTables`; review it: it only creates the three tables, constraints and the `order_id` index.
- [x] Integration tests (extend `SchemaMigrationTests` style, using `ThrowawayDatabase`):
  - AC1: after migrating an empty database, the three tables exist with the ADR-0007 columns and types, and the `order_id` index exists;
  - AC2: two orders with the same `order_id` are both saved;
  - AC3: no foreign key from the order tables to `suppliers` (inspect constraints);
  - check constraints reject a bad `status`, `fulfillment_mode` or `quantity`;
  - deleting an order removes its items and warnings.

**Verify:** build, test, format. Existing `SchemaMigrationTests` still pass.

**Result:** 1249 tests pass (was 1224 before the new schema tests were added). Migration `AddRoutingOrderTables` only creates the three tables and `ix_routing_orders_order_id`. `search_mode` and `caller_hash` are nullable. Three existing tests changed because they asserted "reference tables only": `SchemaMigrationTests` AC1 (table list; index check limited to `supplier%` tables), `OrderRouterDbContextModelTests` (table list, renamed), `ReferenceDataSeederTests` (migration history now has two entries). Format clean except the known `Program.cs` import issue.

---

## Phase 2: Seeder isolation, docs and review ✅ Done

- [x] AC4: with stored orders present, running the seeder again leaves them unchanged (test in `ReferenceDataSeederTests` style).
- [x] ADR-0007: short implementation note (OR-026): `caller_hash` and `search_mode` are nullable and why.
- [x] Run the `ddd-implementation` §5 review and report honestly. Expected met: no FK to suppliers, snapshot columns, cascade within the order. N/A with reasons: aggregates, domain events, money, authorization. Not verified: anything not measured (for example coverage %).
- [x] Record follow-ups (don't do them here): OR-027 writes, OR-028 reads, retention (OQ-27).
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

**Verify:** all three green.

**Result:** 1250 tests pass. AC4 test added: re-seeding with a renamed and a dropped supplier leaves orders, items and warnings unchanged. ADR-0007 note added.

**§5 review (`ddd-implementation`):**
- **Met:** no foreign key to suppliers (historical record, supplier name is a snapshot column); cascade only inside the stored order; no raw caller identity column (hash only, nullable); database refuses invalid status, mode, quantity and line numbers; migration only adds; seeder untouched.
- **N/A, with reasons:** aggregates and domain events (schema only, no domain type); money; authorization (no endpoint); API surface.
- **Not verified:** coverage % (not measured; no Domain or Application code changed); insert latency (NFR-03, OR-027 and the load test).
- **Follow-ups (not done here):** OR-027 writes the rows; OR-028 reads them; retention (OQ-27).

Format clean except the known `Program.cs` import issue.

---

## Critical files

| Action | Path |
|---|---|
| New | `src/OrderRouter.Infrastructure/Persistence/RoutingOrders/*` (3 rows, 3 configurations); migration `AddRoutingOrderTables`; schema and seeder-isolation tests |
| Modified | `OrderRouterDbContext.cs`; `OrderRouterDbContextModelSnapshot.cs` (generated); ADR-0007 (note) |
| Reused | `SupplierRowConfiguration` (pattern), `ThrowawayDatabase`, `SchemaMigrationTests` |
| Unchanged | Reference-data tables, seeder, Domain, Application, Api, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: tables created | schema test on a migrated empty database | 1 |
| AC2: `order_id` not unique | insert-twice test | 1 |
| AC3: no FK to suppliers | constraint inspection test | 1 |
| AC4: seeding leaves orders alone | seeder test with stored orders | 2 |

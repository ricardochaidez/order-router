# OR-004: Create the PostgreSQL schema and EF Core migrations

**Depends on:** [OR-002](OR-002-domain-reference-model.md)  
**References:** [ADR-0002](../ADRs/ADR-0002-reference-data-database.md) (Schema); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-04, SC-20

## Description

Add PostgreSQL to `docker-compose` and create the EF Core 10 (Npgsql) `OrderRouterDataContext` (one context for reference data and, later, stored orders) with tables `suppliers`, `supplier_categories`, `supplier_service_areas` and `products`, plus the indexes described in ADR-0002. Do **not** store the mail-only flag; the domain works it out at read time.

## Acceptance criteria

### AC1: Schema is created

- **Given** an empty PostgreSQL started with `docker compose up`
- **When** the migrations are applied
- **Then** the four tables and the indexes on `supplier_categories (category, supplier_id)` and `supplier_service_areas (start_zip, end_zip)` exist

### AC2: Migrations are repeatable

- **Given** a database where the migrations already ran
- **When** the migrations are applied again
- **Then** nothing changes and no error is raised

### AC3: Mail-only is derived, not stored

- **Given** the `suppliers` table
- **When** I review its columns
- **Then** there is no mail-only column

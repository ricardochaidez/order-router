# OR-006: Read reference data for routing

**Depends on:** [OR-004](OR-004-database-schema.md)  
**References:** [ADR-0002](../ADRs/ADR-0002-reference-data-database.md) (Read path); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §5.1, FR-05, FR-06, FR-16

## Description

Add the domain ports `ICapabilityResolver` (product code → category, or *unknown*) and `IReferenceDataReader` (suppliers for a set of categories, with their categories and service areas). Implement them in Infrastructure with EF Core read-only queries (`AsNoTracking`). Routing reads straight from the database; there is **no cache** for now (caching is deferred until load testing).

## Acceptance criteria

### AC1: Product lookup ignores case and spaces

- **Given** the product `OX-PORT-024` in category `oxygen`
- **When** I resolve `" ox-port-024 "`
- **Then** the result is the category `oxygen`

### AC2: Unknown product

- **Given** a product code that is not in the database
- **When** I resolve it
- **Then** the result says *unknown* (it is never null and never throws)

### AC3: Suppliers by category

- **Given** the seeded database
- **When** I ask for suppliers handling `wheelchair`
- **Then** every returned supplier handles `wheelchair` and comes with its full categories and service areas

### AC4: Read only

- **Given** the API serving requests
- **When** a routing request is handled
- **Then** no reference-data tables are written (stored orders go to separate tables, see OR-027)

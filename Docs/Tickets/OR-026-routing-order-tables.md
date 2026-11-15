# OR-026: Create the tables for stored routing orders

**Depends on:** [OR-004](OR-004-database-schema.md)  
**References:** [ADR-0007](../ADRs/ADR-0007-routing-decision-persistence.md) (Schema); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-20, SC-28

## Description

Add EF Core entities and a migration for `routing_orders`, `routing_order_items` and `routing_order_warnings` (see ADR-0007). They hold stored routing decisions and are separate from the reference data. There is **no foreign key** to `suppliers`: a stored order is a historical record, and the supplier name is copied in at routing time.

## Acceptance criteria

### AC1: Tables are created

- **Given** an empty PostgreSQL
- **When** the migrations are applied
- **Then** the three tables exist with the columns in ADR-0007 and an index on `routing_orders (order_id)`

### AC2: `order_id` is not unique

- **Given** the `routing_orders` table
- **When** I insert two orders with the same `order_id`
- **Then** both rows are saved

### AC3: No link to suppliers

- **Given** the order tables
- **When** I review their constraints
- **Then** there is no foreign key to `suppliers`

### AC4: Seeding leaves orders alone

- **Given** stored orders in the database
- **When** the integration tests run the seed again
- **Then** the stored orders are unchanged

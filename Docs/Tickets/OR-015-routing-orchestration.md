# OR-015: Wire routing end to end and map the response

**Depends on:** [OR-005](OR-005-reference-data-seeder.md), [OR-009](OR-009-route-endpoint-and-validation.md), [OR-012](OR-012-plan-search-guard-rails.md), [OR-014](OR-014-random-tie-breaker.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-10, FR-14, FR-15, FR-16, DEV-10, §6.2, §9.2, §9.4

## Description

Implement `OrderRoutingService` and the `IRouteOrderService` domain service: resolve products, find eligible suppliers, search plans, apply the policies and tie-break, then map the result to the v1 response. Until OR-016 lands, use an all-control `ExperimentAssignment`. `feasible` is always computed from `status` and never set separately. Return 200 for now; OR-027 stores the decision, changes the success status to 201 and updates the 200 assertions in this ticket's tests.

## Acceptance criteria

### AC1: ORD-001

- **Given** the seeded database and order ORD-001
- **When** it is routed
- **Then** the response is 200, `feasible:true`, `status:"routed"` with one shipment from `SUP-0810`, all items `local`

### AC2: ORD-002

- **Given** order ORD-002
- **When** it is routed
- **Then** there are two shipments: `SUP-0375` (hospital bed, patient lift, commode) and `SUP-042` (BP monitor), all `local`

### AC3: ORD-003

- **Given** order ORD-003
- **When** it is routed
- **Then** one shipment from `SUP-023` or `SUP-065`, all items `mail`

### AC4: Response shape

- **Given** a routed order
- **When** I read the response
- **Then** `routing` is grouped by supplier, items keep their request order, and each item has `product_code`, `quantity`, `category` and `fulfillment_mode`

### AC5: Unknown product (control)

- **Given** a valid order with one unknown product code
- **When** it is routed
- **Then** the response is 200, `feasible:false`, `status:"not_routed"`, empty `routing`, the item in `unfulfilled_items` with `UNKNOWN_PRODUCT`, and one matching line in `errors`

### AC6: Empty database

- **Given** a migrated but empty database
- **When** a valid order is sent
- **Then** the response is 200 with `feasible:false` and every item as `UNKNOWN_PRODUCT` (not a 500)

### AC7: Feasible matches status

- **Given** any routing outcome
- **When** the response is built
- **Then** `feasible` is true only when `status` is `routed`

### AC8: Single-item and duplicate lines

- **Given** a valid order with one line, and an order with two lines for the same `product_code`
- **When** each is routed
- **Then** both are accepted, and the duplicate lines are returned as separate lines from the same supplier (DEV-10)

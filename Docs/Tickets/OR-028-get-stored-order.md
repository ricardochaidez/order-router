# OR-028: Add GET to read a stored order

**Depends on:** [OR-027](OR-027-store-routing-decision.md)  
**References:** [ADR-0007](../ADRs/ADR-0007-routing-decision-persistence.md); [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-21, §9.5, SC-31

## Description

Add `GET /api/v1/route/{id}`. It requires authentication and returns the stored order: the request details, the requested items and the decision. It **never re-routes**. Only the caller that created the order can read it; anyone else gets 404, the same as for an unknown id. This endpoint exists on v1 only, not on the `/api/route` alias.

## Acceptance criteria

### AC1: Read back

- **Given** ORD-002 posted by a caller
- **When** the same caller calls `GET` on the `Location` URL
- **Then** the response is 200 with `id`, `order_id`, `customer_zip`, `mail_order`, `created_at`, `items` in request order, and `feasible`, `status`, `routing`, `unfulfilled_items`, `warnings` and `errors` equal to the POST response

### AC2: No re-routing

- **Given** an order whose supplier was picked by the EXP-4 treatment random tie-break (`SUP-T003`/`SUP-T004`)
- **When** it is read 10 times
- **Then** every response shows the same supplier as the POST response

### AC3: Held-back items are listed

- **Given** an order stored as `not_routed` by the all-or-nothing arm
- **When** it is read
- **Then** `items` lists every requested line, including the routable ones

### AC4: Supplier name snapshot

- **Given** a stored order, then a re-seed that renames its supplier
- **When** the order is read
- **Then** the response shows the supplier name from routing time

### AC5: Other caller

- **Given** an order created by caller A
- **When** caller B reads it
- **Then** the response is 404

### AC6: Unknown or invalid id

- **Given** a random UUID, and the value `abc`
- **When** each is read
- **Then** both return 404

### AC7: No credentials

- **Given** a stored order
- **When** it is read without credentials
- **Then** the response is 401

### AC8: Not on the alias

- **Given** a stored order
- **When** I call `GET /api/route/{id}`
- **Then** the response is 404 (the alias is POST-only)

### AC9: OpenAPI

- **Given** the running API
- **When** I fetch the `v1` OpenAPI document
- **Then** it describes `GET /api/v1/route/{id}` with 200, 401 and 404 responses

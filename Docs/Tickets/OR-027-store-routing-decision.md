# OR-027: Store each routing decision and return its id

**Depends on:** [OR-015](OR-015-routing-orchestration.md), [OR-026](OR-026-routing-order-tables.md)  
**References:** [ADR-0007](../ADRs/ADR-0007-routing-decision-persistence.md); [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-02, FR-20, §9.2, §9.4, SC-28, SC-29, SC-30

## Description

After routing a valid POST, save the order and its decision in **one transaction** with a new server-generated UUIDv7 `id`. Then return **201 Created** with `id` and `order_id` in the body and a `Location: /api/v1/route/{id}` header. Every POST creates a new record, even a retry of the same `order_id`. Add an `ICallerHasher` (HMAC-SHA256 with a server secret) so the caller is stored only as a hash. The secret comes from configuration (`CallerHashing:Secret`), set through environment variables or user-secrets and never committed; `.env.example` gets a dev-only placeholder next to the dev key, and startup fails if it is missing. This ticket also updates OR-015's 200 assertions to 201. If saving fails, return 500 and no decision.

## Acceptance criteria

### AC1: Routed order is stored

- **Given** the seeded database
- **When** ORD-001 is posted
- **Then** the response is 201 with a UUID `id`, `order_id` = `ORD-001` and `Location: /api/v1/route/{id}`, and the database has one order row and one item row per requested line with `supplier_id`, supplier name and `fulfillment_mode`

### AC2: Not-routed order is stored

- **Given** a valid order with one unknown product (all-or-nothing arm)
- **When** it is posted
- **Then** the response is 201 with `status:"not_routed"`, and the unknown line is stored with `UNKNOWN_PRODUCT` and its message

### AC3: Experiment data is stored

- **Given** any valid order
- **When** it is posted
- **Then** the stored order has the experiment arms, `config_version`, `search_mode` and `created_at`

### AC4: Retry creates a new record

- **Given** the same request body
- **When** it is posted twice
- **Then** two different `id` values are returned and two orders are stored

### AC5: Alias also stores

- **Given** a valid order
- **When** it is posted to `/api/route`
- **Then** the response is 201 and `Location` points to `/api/v1/route/{id}`

### AC6: Rejected requests are not stored

- **Given** a 400 or 401 request
- **When** it is handled
- **Then** nothing is stored

### AC7: Save failure

- **Given** the database rejects the insert (simulated in a test)
- **When** a valid order is posted
- **Then** the response is 500 ProblemDetails with no `id` and no partial rows are left (the decision log side is checked in OR-021)

### AC8: Caller stored as a hash

- **Given** a caller with ID `user-123`
- **When** it posts an order
- **Then** `user-123` does not appear in the order tables; only its hash does

### AC9: OpenAPI

- **Given** the running API
- **When** I fetch the `v1` OpenAPI document
- **Then** `POST /api/v1/route` and its alias document 201 with `Location`, 400, 401 and 500, with examples for routed, partially routed and not routed

### AC10: Hash secret not committed

- **Given** the repository
- **When** I look for the caller-hash secret
- **Then** only a placeholder in `.env.example` is committed

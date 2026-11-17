# OR-009: Add the route endpoint, response envelope and request validation

**Depends on:** [OR-008](OR-008-authentication.md)  
**References:** [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-01, FR-02, FR-03, §9, SC-13, SC-15, DQ-15

## Description

Expose `POST /api/v1/route` (canonical) and `POST /api/route` (a permanent alias pinned to v1), both using the same handler, auth and validator. Define the snake_case request and response view models. Validate every request and return **400 with all errors** in the same envelope as a successful response. Valid requests are passed to the routing service (`IRouteOrderService`), which is wired up in OR-015. Publish the OpenAPI document `v1`.

## Acceptance criteria

### AC1: All errors returned at once

- **Given** a request with no `customer_zip` and an empty `items` list
- **When** it is sent
- **Then** the response is 400 with `feasible:false`, `status:"not_routed"`, empty `routing`, `unfulfilled_items` and `warnings`, and both errors listed in `errors`

### AC2: Bad ZIP

- **Given** `customer_zip` = `"2130"`
- **When** it is sent
- **Then** the response is 400 and `errors` names the ZIP problem

### AC3: Bad quantity

- **Given** an item with `quantity` 0
- **When** it is sent
- **Then** the response is 400

### AC4: Empty product code or order ID

- **Given** an empty `product_code` or `order_id`
- **When** it is sent
- **Then** the response is 400

### AC5: Malformed JSON

- **Given** a body that is not valid JSON
- **When** it is sent
- **Then** the response is 400 in the same envelope

### AC6: Extra fields ignored

- **Given** a valid order that also has `priority` and `notes`
- **When** it is sent
- **Then** it is not rejected

### AC7: Unknown product is not a validation error

- **Given** a valid order with a product code not in the catalog
- **When** it is sent
- **Then** it passes validation and goes to routing

### AC8: Alias matches v1

- **Given** the same request
- **When** I send it to `/api/route` and to `/api/v1/route`
- **Then** both responses have the same status code and body

### AC9: Arrays never null

- **Given** any response
- **When** I read it
- **Then** `routing`, `unfulfilled_items`, `warnings` and `errors` are always present (possibly empty)

### AC10: OpenAPI

- **Given** the running API
- **When** I fetch the `v1` OpenAPI document
- **Then** it describes `/api/v1/route` with examples, and lists `/api/route` as an alias

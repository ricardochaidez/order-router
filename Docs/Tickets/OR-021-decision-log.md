# OR-021: Log one routing decision event per request

**Depends on:** [OR-016](OR-016-experiment-assignment.md), [OR-027](OR-027-store-routing-decision.md)  
**References:** [ADR-0005](../ADRs/ADR-0005-experiment-assignment.md) (Exposure / decision logging); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) AB-04, NFR-06, NFR-11

## Description

Emit a structured `RoutingDecisionMade` log event (using `[LoggerMessage]`) for every valid routing request. It is the data source for later A/B analysis. The raw caller ID is never logged, only its HMAC hash from the `ICallerHasher` added in OR-027. Write the event only after the order is saved, and include its `id`. No patient data other than `customer_zip`.

## Acceptance criteria

### AC1: Event fields

- **Given** a routed order
- **When** I read the event
- **Then** it has `id`, `order_id`, `caller_hash`, arm and reason per experiment, chosen suppliers and modes, `shipment_count`, `plan_quality`, `unrated_chosen`, `unfulfilled_count`, `search_mode`, `config_version` and `customer_zip`

### AC2: Tie recorded

- **Given** an order where the tie-breaker ran
- **When** I read the event
- **Then** it lists the tied candidates and the pick

### AC3: Caller pseudonymized

- **Given** a caller with ID `user-123`
- **When** I search the logs
- **Then** `user-123` does not appear; only its hash does

### AC4: Not for rejected requests

- **Given** a 400 or 401 request
- **When** it is handled
- **Then** no `RoutingDecisionMade` event is written

### AC5: Not when storing fails

- **Given** the database rejects the order insert (simulated in a test)
- **When** a valid order is posted
- **Then** the response is 500 and no `RoutingDecisionMade` event is written ([ADR-0007](../ADRs/ADR-0007-routing-decision-persistence.md))

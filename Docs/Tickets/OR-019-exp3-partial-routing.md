# OR-019: EXP-3 treatment: route what we can

**Depends on:** [OR-016](OR-016-experiment-assignment.md), [OR-027](OR-027-store-routing-decision.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-14, §8.1 EXP-3, SC-04, SC-05

## Description

Add `PartialRoutingPolicy`. When some items can't be fulfilled, still route the others and return `status:"partially_routed"` with `feasible:false`, listing the unfulfilled items so a user can act.

## Acceptance criteria

### AC1: Partial route

- **Given** the treatment arm and an order with one routable item and one unknown product
- **When** it is routed
- **Then** the response is 201, `feasible:false`, `status:"partially_routed"`, the routable item is in `routing`, and the unknown one is in `unfulfilled_items` and `errors`

### AC2: Nothing routable

- **Given** the treatment arm and an order where no item is routable
- **When** it is routed
- **Then** the status is `not_routed`

### AC3: Everything routable

- **Given** the treatment arm and a fully routable order
- **When** it is routed
- **Then** the status is `routed` and `feasible` is true

### AC4: Validation still blocks

- **Given** the treatment arm and a request with an invalid ZIP
- **When** it is sent
- **Then** the response is 400 and nothing is routed

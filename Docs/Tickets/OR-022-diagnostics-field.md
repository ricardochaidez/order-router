# OR-022: Add the optional diagnostics field

**Depends on:** [OR-016](OR-016-experiment-assignment.md), [OR-028](OR-028-get-stored-order.md)  
**References:** [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) AB-05, OQ-08

## Description

Add a `diagnostics` object (`experiment_arms`, `search_mode`) to the response, shown only when `Routing:ExposeDiagnostics = true`. It is off by default; clients must not depend on it. The same flag controls `diagnostics` on `GET /api/v1/route/{id}`, built from the stored arms and `search_mode` (requirements §9.5).

## Acceptance criteria

### AC1: Off by default

- **Given** the default configuration
- **When** an order is routed
- **Then** the response has no `diagnostics` field

### AC2: On when enabled

- **Given** `Routing:ExposeDiagnostics = true`
- **When** an order is routed
- **Then** `diagnostics` shows the arm for each experiment and the `search_mode`

### AC3: Same on GET

- **Given** a stored order
- **When** I read it with `GET /api/v1/route/{id}` with the flag off, then on
- **Then** `diagnostics` is absent, then shows the stored arms and `search_mode`

# OR-017: EXP-1 treatment: allow one extra shipment for much better quality

**Depends on:** [OR-016](OR-016-experiment-assignment.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §8.1 EXP-1, SC-01, OQ-10

## Description

Add `BoundedTradeOffPolicy`. It sets `MaxExtraShipments = 1`, and it picks a plan with one more shipment only when that plan's quality is at least `quality_uplift_threshold` (default 2.0) better. Otherwise it behaves like strict priority.

## Acceptance criteria

### AC1: Big uplift wins

- **Given** the treatment arm, a 1-shipment plan at 6.0 and a 2-shipment plan at 9.0
- **When** selection runs
- **Then** the 2-shipment plan is chosen

### AC2: Small uplift loses

- **Given** the treatment arm, a 1-shipment plan at 8.0 and a 2-shipment plan at 9.0
- **When** selection runs
- **Then** the 1-shipment plan is chosen

### AC3: Never two extra

- **Given** the treatment arm and a 3-shipment plan that is far better than the 1-shipment plan
- **When** selection runs
- **Then** the 3-shipment plan is never chosen

### AC4: Threshold from config

- **Given** `QualityUpliftThreshold` changed in `experiments.json`
- **When** the next request is routed
- **Then** the new threshold is used

### AC5: Control unchanged

- **Given** the control arm with the same plans
- **When** selection runs
- **Then** the fewest-shipment plan is chosen

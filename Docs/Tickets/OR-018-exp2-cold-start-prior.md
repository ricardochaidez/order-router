# OR-018: EXP-2 treatment: give unrated suppliers a starting rating

**Depends on:** [OR-016](OR-016-experiment-assignment.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §8.1 EXP-2, SC-02, OQ-10

## Description

Add `ColdStartPriorPolicy`. Unrated suppliers get the configured `unrated_prior` (default 6.5) as their effective rating instead of 0, so new suppliers can win some orders.

## Acceptance criteria

### AC1: Prior applied

- **Given** the treatment arm with the default config
- **When** an unrated supplier's effective rating is computed
- **Then** the result is 6.5

### AC2: Unrated can win

- **Given** the treatment arm, an unrated supplier and a 6.0 supplier competing for the same order
- **When** the order is routed
- **Then** the unrated supplier is chosen

### AC3: Control unchanged

- **Given** the control arm with the same suppliers
- **When** the order is routed
- **Then** the 6.0 supplier is chosen

### AC4: Prior from config

- **Given** `UnratedPrior` changed in `experiments.json`
- **When** the next request is routed
- **Then** the new prior is used

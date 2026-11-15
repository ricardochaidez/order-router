# OR-014: Break full ties at random

**Depends on:** [OR-013](OR-013-routing-policies-control.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-13, NFR-01, NFR-08, SC-17, SC-18

## Description

When several plans are still equal after every ranking rule, pick one uniformly at random so the same supplier doesn't always win. Randomness comes only from an injected `IRandomSource`, so tests can fix it. Return the tied candidates and the pick so they can be logged.

## Acceptance criteria

### AC1: Fixed source in tests

- **Given** three tied plans and a fixed random source that returns 2
- **When** the tie-breaker runs
- **Then** the third plan is chosen

### AC2: No tie, no randomness

- **Given** only one best plan
- **When** routing runs
- **Then** the random source is never called and repeated calls give the same result

### AC3: Fair distribution

- **Given** `SUP-T003` and `SUP-T004` tied, with the real random source
- **When** routing runs 200 times
- **Then** both suppliers appear, and no other supplier ever does

### AC4: Audit data

- **Given** a tie was broken
- **When** the routing decision is returned
- **Then** it includes the tied candidates and the chosen one

# OR-024: Test all eight experiment arm combinations

**Depends on:** [OR-017](OR-017-exp1-bounded-tradeoff.md), [OR-018](OR-018-exp2-cold-start-prior.md), [OR-019](OR-019-exp3-partial-routing.md), [OR-020](OR-020-dependency-warnings.md), [OR-023](OR-023-acceptance-tests.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md) (Negative consequences)

## Description

The three experiments can be combined in 2 × 2 × 2 = 8 ways. Add one test per combination on sentinel data to catch surprising interactions (for example, the cold-start prior together with the bounded trade-off).

## Acceptance criteria

### AC1: Every combination covered

- **Given** a fixed assignment for each of the 8 arm combinations and a fixed random source
- **When** the same test orders are routed
- **Then** each combination returns its expected, documented result

### AC2: All control equals spec

- **Given** every experiment in control
- **When** the sentinel orders are routed
- **Then** the results equal those of the acceptance suite in OR-023

# OR-013: Add the routing policy seams and the control arms

**Depends on:** [OR-011](OR-011-plan-search-exact.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §7.3, FR-11, FR-12, FR-14, AB-03, AB-06

## Description

Create the three policy interfaces from ADR-0001 and their **control** implementations: `UnratedLastPolicy` (unrated = 0), `StrictPriorityPolicy` (fewer shipments, then quality, then local within the similarity band) and `AllOrNothingPolicy`. Add the `RoutingPolicy` record and `IRoutingPolicyFactory`, which builds a policy per request from an `ExperimentAssignment`. Policies are stateless and get their parameters from the assignment, never from configuration.

## Acceptance criteria

### AC1: Unrated last

- **Given** an unrated supplier under the control arm
- **When** its effective rating is computed
- **Then** the result is 0

### AC2: Fewer shipments wins

- **Given** a 1-shipment plan (`SUP-T007`, 6.0) and a 3-shipment plan (10.0 specialists)
- **When** strict priority selects
- **Then** the 1-shipment plan wins

### AC3: Higher quality wins

- **Given** two plans with the same shipment count and qualities 9.0 and 8.0
- **When** strict priority selects
- **Then** the 9.0 plan wins

### AC4: Local preferred when ratings are similar

- **Given** two plans with qualities 9.0 (mail) and 8.7 (local) and a band of 0.5
- **When** strict priority selects
- **Then** the local plan wins

### AC5: Quality wins outside the band

- **Given** two plans with qualities 9.0 (mail) and 8.0 (local) and a band of 0.5
- **When** strict priority selects
- **Then** the 9.0 mail plan wins

### AC6: Plan quality

- **Given** a plan with 2 items at rating 9.0 and 1 item at rating 6.0
- **When** its quality is computed
- **Then** it is the item-weighted mean, 8.0

### AC7: All-or-nothing

- **Given** an order where one item is unfulfillable
- **When** the control unfulfillable policy runs
- **Then** nothing is routed and the status is `not_routed`

### AC8: Default is all control

- **Given** an assignment with every experiment off
- **When** the factory builds a policy
- **Then** it uses `UnratedLastPolicy`, `StrictPriorityPolicy` and `AllOrNothingPolicy`

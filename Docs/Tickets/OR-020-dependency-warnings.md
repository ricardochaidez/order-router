# OR-020: Warn when a routed item needs an unfulfilled one

**Depends on:** [OR-019](OR-019-exp3-partial-routing.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-18, §8.4 (DR-01 to DR-05), SC-08, SC-11

## Description

Load category-level dependency rules (e.g., `hospital bed` requires `patient lift`) from configuration. The shipped rule set is **empty**, with a documented example; clinical ops owns the real rules. Startup checks only that rules are well-formed and **never reads the database**. On a partial route, add a `DEPENDENT_ITEM_UNFULFILLED` warning when a routed item's required category is unfulfilled. Rules never change eligibility or ranking.

## Acceptance criteria

### AC1: Warning on partial route

- **Given** the rule `hospital bed` requires `patient lift`, and a partial route where the bed is routed and the lift is unfulfilled
- **When** the order is routed
- **Then** `warnings` has one `DEPENDENT_ITEM_UNFULFILLED` entry naming the lift in `unfulfilled_product_codes` and the bed in `affected_product_codes`

### AC2: No rules, no warnings

- **Given** the shipped empty rule set
- **When** any order is routed
- **Then** `warnings` is empty

### AC3: Malformed rule blocks startup

- **Given** a rule with an empty category name
- **When** the API starts
- **Then** startup fails with a clear configuration error

### AC4: Unknown category is skipped at routing

- **Given** a rule naming a category that is not in the reference data
- **When** an order is routed
- **Then** the rule is skipped, a warning is logged and the request succeeds

### AC5: Startup does not read data

- **Given** a well-formed rule and an empty database
- **When** the API starts
- **Then** it starts normally

### AC6: Rules don't change routing

- **Given** the same order with and without rules
- **When** it is routed
- **Then** the `routing` section is the same

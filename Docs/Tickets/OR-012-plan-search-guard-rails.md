# OR-012: Add plan-search limits and the greedy fallback

**Depends on:** [OR-011](OR-011-plan-search-exact.md)  
**References:** [ADR-0003](../ADRs/ADR-0003-plan-search-algorithm.md) (Guard rails); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) NFR-03, NFR-05

## Description

Protect latency on very large orders. If an order has more distinct categories than `PlanSearch:MaxExactCategories` (default 8), or the search checks more than `PlanSearch:NodeBudget` combinations (default 200,000), switch to greedy set cover. Log a warning and record `search_mode = greedy`.

## Acceptance criteria

### AC1: Too many categories

- **Given** an order with 9 distinct categories and the default settings
- **When** routing runs
- **Then** the greedy fallback is used, a warning is logged and `search_mode` is `greedy`

### AC2: Budget exceeded

- **Given** a `NodeBudget` set very low in a test
- **When** routing runs on a normal order
- **Then** the greedy fallback is used and logged

### AC3: Normal orders stay exact

- **Given** a sample order with default settings
- **When** routing runs
- **Then** `search_mode` is `exact`

### AC4: Configurable

- **Given** new values for both limits in `appsettings`
- **When** the API runs
- **Then** the new limits are used

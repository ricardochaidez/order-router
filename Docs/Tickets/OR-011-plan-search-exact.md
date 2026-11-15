# OR-011: Search for the fewest-shipment plans

**Depends on:** [OR-010](OR-010-eligibility-rules.md)  
**References:** [ADR-0003](../ADRs/ADR-0003-plan-search-algorithm.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §7.2, FR-10

## Description

Implement the exact cover search from ADR-0003. Group items by category, give each category a bit, drop look-alike suppliers that can never win (dominance pruning, keeping equal-rating twins), then try covers of 1 supplier, then 2, and so on, stopping at the first size that works. It also searches one size further when `MaxExtraShipments = 1`. Return **all** plans that are still in the running, never just the first one found.

## Acceptance criteria

### AC1: Same category stays together

- **Given** an order with two CPAP lines
- **When** plans are built
- **Then** both lines are always assigned to the same supplier

### AC2: Quantity never split

- **Given** a line with quantity 3
- **When** plans are built
- **Then** the whole line goes to one supplier

### AC3: Single cover found

- **Given** the `SUP-T007` generalist and the `SUP-T008`–`SUP-T010` specialists at `00007`
- **When** the search runs for wheelchair + cane + walker
- **Then** the 1-shipment plan with `SUP-T007` is returned and no larger plans are

### AC4: Two-supplier cover

- **Given** order ORD-002 (ZIP 77059, no mail)
- **When** the search runs
- **Then** the minimum is 2 shipments and the plans include `SUP-0375` + `SUP-042`

### AC5: Equal twins kept

- **Given** `SUP-T003` and `SUP-T004` (same category, same rating)
- **When** the search runs
- **Then** plans with each of them are returned

### AC6: Weaker look-alike dropped

- **Given** two suppliers covering the same categories in the same mode with different ratings
- **When** the search runs
- **Then** only the higher-rated one appears in plans

### AC7: Extra shipment on request

- **Given** `MaxExtraShipments = 1`
- **When** the search runs
- **Then** plans with the minimum size and with minimum + 1 are returned

### AC8: Quality bound

- **Given** a similarity band of 0.5
- **When** the search runs
- **Then** no plan whose quality is more than 0.5 below the best plan **with the same shipment count** is returned

### AC9: Band never prunes across shipment counts

- **Given** `MaxExtraShipments = 1`, a band of 0.5, a best 1-shipment plan at 8.0 and a 2-shipment plan at 9.0
- **When** the search runs
- **Then** both plans are returned, so the EXP-1 policy can compare them ([ADR-0003](../ADRs/ADR-0003-plan-search-algorithm.md))

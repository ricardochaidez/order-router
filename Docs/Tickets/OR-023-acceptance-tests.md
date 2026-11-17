# OR-023: Add the end-to-end acceptance test suite

**Depends on:** [OR-015](OR-015-routing-orchestration.md), [OR-019](OR-019-exp3-partial-routing.md), [OR-028](OR-028-get-stored-order.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §6.1, §6.2, NFR-08, NFR-11; [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md)

## Description

Add endpoint-level integration tests against the seeded database for the sample orders, the sentinel suppliers, validation and auth. Run the core cases against **both** `/api/v1/route` and `/api/route`. Tests on random ties must assert the result is *one of* the tied suppliers, never a fixed one.

## Acceptance criteria

### AC1: Sample orders

- **Given** the seeded database
- **When** ORD-001, ORD-002 and ORD-003 are sent to both paths
- **Then** the results match §6.2 on both paths

### AC2: Local exclusive

- **Given** `mail_order=false` at `00001`, wheelchair
- **When** it is routed
- **Then** `SUP-T001` is chosen

### AC3: Local preference

- **Given** `mail_order=false` at `00005`, walker
- **When** it is routed
- **Then** `SUP-T005` is chosen

### AC4: Mail order excludes local-only

- **Given** `mail_order=true` at `00005`, walker
- **When** it is routed
- **Then** `SUP-T005` is not chosen and the item is `mail`

### AC5: Generalist

- **Given** `mail_order=false` at `00007`, wheelchair + cane + walker
- **When** it is routed
- **Then** `SUP-T007` is chosen as one shipment

### AC6: Tied sentinels

- **Given** the tied sentinel pairs `SUP-T003`/`SUP-T004` and `SUP-T011`/`SUP-T012`
- **When** each is routed
- **Then** the winner is always one of its pair (the core picks the lowest supplier ID; EXP-4 treatment tests must not assume a fixed winner)

### AC7: No coverage

- **Given** `mail_order=false` at `00098`, a non-wheelchair category
- **When** it is routed
- **Then** the item is unfulfillable with `NO_SUPPLIER_FOR_CATEGORY_IN_AREA`

### AC8: Auth and validation

- **Given** requests with no token, a bad token, and each invalid field
- **When** they are sent
- **Then** they return 401 or 400 as specified

### AC9: Stored and readable

- **Given** each sample order posted to both paths
- **When** I call `GET` on each `Location` URL with the same credentials
- **Then** the stored decision equals the POST response

### AC10: Coverage

- **Given** the full test run (unit and integration)
- **When** coverage is measured for the Domain projects
- **Then** it is at least 85% (NFR-08)

## Notes

- Coverage measured with `dotnet test --coverage --coverage-output-format cobertura --results-directory <dir>` over both test projects, merging the Cobertura files by file and line: Domain 98.5%, the former Domain project 98.7% (now part of Domain) (baseline and final are the same; the existing suite already exceeded 85%).

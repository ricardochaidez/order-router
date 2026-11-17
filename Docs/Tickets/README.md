# Order Router Tickets

Tickets for the Multi-Supplier Order Router, broken out from the [technical requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) and the [ADRs](../ADRs/README.md). Work them roughly in order; the **Depends on** column shows what must be done first.

| Ticket | Title | Depends on |
|---|---|---|
| [OR-001](OR-001-solution-skeleton.md) | Set up the solution and layer projects | — |
| [OR-002](OR-002-domain-reference-model.md) | Model suppliers, products and ZIP coverage in the domain | OR-001 |
| [OR-003](OR-003-csv-normalization.md) | Parse and normalize the supplier and product CSV files | OR-002 |
| [OR-004](OR-004-database-schema.md) | Create the PostgreSQL schema and EF Core migrations | OR-002 |
| [OR-005](OR-005-reference-data-seeder.md) | Seed reference data by upsert from the integration tests | OR-003, OR-004 |
| [OR-006](OR-006-reference-data-read-path.md) | Read reference data for routing | OR-004 |
| [OR-007](OR-007-health-and-container.md) | Containerize the API and add the health endpoint | OR-004 |
| [OR-008](OR-008-authentication.md) | Require authentication: JWT bearer and a development key | OR-001 |
| [OR-009](OR-009-route-endpoint-and-validation.md) | Add the route endpoint, response envelope and request validation | OR-008 |
| [OR-010](OR-010-eligibility-rules.md) | Find the eligible suppliers for each category | OR-002, OR-006 |
| [OR-011](OR-011-plan-search-exact.md) | Search for the fewest-shipment plans | OR-010 |
| [OR-012](OR-012-plan-search-guard-rails.md) | Add plan-search limits and the greedy fallback | OR-011 |
| [OR-013](OR-013-routing-policies-control.md) | Add the routing policy seams and the control arms | OR-011 |
| [OR-014](OR-014-random-tie-breaker.md) | Break full ties at random (superseded: EXP-4 treatment only) | OR-013 |
| [OR-015](OR-015-routing-orchestration.md) | Wire routing end to end and map the response | OR-005, OR-009, OR-012, OR-014 |
| [OR-016](OR-016-experiment-assignment.md) | Assign experiment arms from reloadable configuration | OR-008, OR-013, OR-015 |
| [OR-017](OR-017-exp1-bounded-tradeoff.md) | EXP-1 treatment: allow one extra shipment for much better quality | OR-016 |
| [OR-018](OR-018-exp2-cold-start-prior.md) | EXP-2 treatment: give unrated suppliers a starting rating | OR-016 |
| [OR-019](OR-019-exp3-partial-routing.md) | EXP-3 treatment: route what we can | OR-016, OR-027 |
| [OR-020](OR-020-dependency-warnings.md) | Warn when a routed item needs an unfulfilled one | OR-019 |
| [OR-021](OR-021-decision-log.md) | Log one routing decision event per request | OR-016, OR-027 |
| [OR-022](OR-022-diagnostics-field.md) | Add the optional diagnostics field | OR-016, OR-028 |
| [OR-023](OR-023-acceptance-tests.md) | Add the end-to-end acceptance test suite | OR-015, OR-019, OR-028 |
| [OR-024](OR-024-experiment-combination-matrix.md) | Test all eight experiment arm combinations | OR-017, OR-018, OR-019, OR-020, OR-023 |
| [OR-025](OR-025-readme-and-docs.md) | Write the README and update AI_PROMPTS.md | OR-023, OR-028 |
| [OR-026](OR-026-routing-order-tables.md) | Create the tables for stored routing orders | OR-004 |
| [OR-027](OR-027-store-routing-decision.md) | Store each routing decision and return its id | OR-015, OR-026 |
| [OR-028](OR-028-get-stored-order.md) | Add GET to read a stored order | OR-027 |

## Suggested milestones

1. **Foundation:** OR-001 to OR-008 (structure, data, container, auth).
2. **Core routing (spec behaviour):** OR-009 to OR-015. At the end of this milestone the service routes orders as the spec describes, with all experiments in control.
3. **Stored orders:** OR-026 to OR-028. POST stores each decision and returns its `id` (201); GET reads it back. Start right after OR-015.
4. **Experiments:** OR-016 to OR-022. EXP-4 (deterministic tie-break, `Exp4TieBreaking`) was added afterwards and has no ticket; see [Docs/Experiments.md](../Experiments.md).
5. **Hardening and docs:** OR-023 to OR-025.

## Not ticketed yet

- **Reference-data caching and load tests** ([ADR-0006](../ADRs/ADR-0006-reference-data-caching.md)): deferred until load testing. Routing reads directly from PostgreSQL (option A).
- **Real item-dependency rule content** (DR-05): owned by clinical ops / product.
- **Permissions / authorization scopes** (SC-16): deferred.

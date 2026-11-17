# Routing results: experiments off vs on

Captured 2026-11-16 against the local docker stack (PostgreSQL 18.1 + API, seeded reference data, 1,100 suppliers plus the 14 test-only `SUP-T*` sentinels the integration tests leave in the database).
Orders: [sample_orders.json](sample_orders.json) (3) and [production_like_orders.json](production_like_orders.json) (22). Raw data, including the decision-log fields: [routing_results.json](routing_results.json).

- **Run A, experiments off:** the shipped `experiments.json` (all three `Enabled: false`). Every decision logged `exp1/exp2/exp3_arm=control, reason=disabled`.
- **Run B, experiments on:** all three `Enabled: true` at `TreatmentPercentage: 100`, default parameters (`QualityUpliftThreshold 2.0`, `UnratedPrior 6.5`, `SimilarityBand 0.5`). Every decision logged `arm=treatment, reason=bucketed`.
- `experiments.json` was restored to the shipped defaults afterwards. `q` is the line-weighted mean rating of the chosen suppliers.
- "tie-break only" means the same shipments and quality, but the random tie-break picked a different equally-ranked supplier (`tied_candidates` in the log). It is not an experiment effect.

| Order | ZIP | Delivery | Experiments OFF | Experiments ON | Compare |
|---|---|---|---|---|---|
| ORD-001 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00, unfulfilled=0 | routed: 1 ship, `SUP-0810:local`, q=6.00, unfulfilled=0 | same |
| ORD-002 | 77059 | local | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15, unfulfilled=0 | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15, unfulfilled=0 | same |
| ORD-003 | 02130 | mail | routed: 1 ship, `SUP-065:mail`, q=10.00, unfulfilled=0 | routed: 1 ship, `SUP-065:mail`, q=10.00, unfulfilled=0 | same |
| PRD-001 | 60610 | local | routed: 1 ship, `SUP-0389:local`, q=5.30, unfulfilled=0 | routed: 2 ship, `SUP-0389:local`, `SUP-0128:local`, q=7.45, unfulfilled=0 | **differs** |
| PRD-002 | 90210 | local | routed: 1 ship, `SUP-0960:local`, q=7.00, unfulfilled=0 | routed: 2 ship, `SUP-1056:local`, `SUP-0371:local`, q=9.33, unfulfilled=0 | **differs** |
| PRD-003 | 30303 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47, unfulfilled=0 | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47, unfulfilled=0 | same |
| PRD-004 | 59001 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60, unfulfilled=0 | routed: 1 ship, `SUP-0741:mail`, q=9.60, unfulfilled=0 | same |
| PRD-005 | 10455 | local | routed: 1 ship, `SUP-035:local`, q=9.00, unfulfilled=0 | routed: 1 ship, `SUP-035:local`, q=9.00, unfulfilled=0 | same |
| PRD-006 | 98101 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.33, unfulfilled=0 | routed: 2 ship, `SUP-007:local`, `SUP-0660:local`, q=7.47, unfulfilled=0 | **differs** |
| PRD-007 | 33101 | mail | routed: 1 ship, `SUP-087:mail`, q=10.00, unfulfilled=0 | routed: 1 ship, `SUP-0875:mail`, q=10.00, unfulfilled=0 | tie-break only |
| PRD-008 | 85001 | local | routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=0 | routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=0 | same |
| PRD-009 | 80202 | local | routed: 1 ship, `SUP-0389:local`, q=5.30, unfulfilled=0 | routed: 2 ship, `SUP-0261:local`, `SUP-0429:local`, q=7.50, unfulfilled=0 | **differs** |
| PRD-010 | 55401 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63, unfulfilled=0 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63, unfulfilled=0 | same |
| PRD-011 | 19104 | local | routed: 1 ship, `SUP-0911:local`, q=7.40, unfulfilled=0 | routed: 1 ship, `SUP-0911:local`, q=7.40, unfulfilled=0 | same |
| PRD-012 | 94103 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00, unfulfilled=0 | routed: 1 ship, `SUP-023:mail`, q=10.00, unfulfilled=0 | same |
| PRD-013 | 37203 | local | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15, unfulfilled=0 | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15, unfulfilled=0 | same |
| PRD-014 | 99950 | local | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05, unfulfilled=0 | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05, unfulfilled=0 | same |
| PRD-015 | 99950 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00, unfulfilled=0 | routed: 1 ship, `SUP-047:mail`, q=10.00, unfulfilled=0 | same |
| PRD-016 | 00601 | local | routed: 1 ship, `SUP-0429:local`, q=7.60, unfulfilled=0 | routed: 1 ship, `SUP-0429:local`, q=7.60, unfulfilled=0 | same |
| PRD-017 | 77059 | local | routed: 1 ship, `SUP-042:local`, q=9.00, unfulfilled=0 | routed: 1 ship, `SUP-042:local`, q=9.00, unfulfilled=0 | same |
| PRD-018 | 60615 | local | routed: 1 ship, `SUP-0970:local`, q=7.20, unfulfilled=0 | routed: 1 ship, `SUP-0970:local`, q=7.20, unfulfilled=0 | same |
| PRD-019 | 00098 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-T014:local`, q=5.00, unfulfilled=1 | **differs** |
| PRD-020 | 10015 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=1 | **differs** |
| PRD-021 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00, unfulfilled=0 | routed: 1 ship, `SUP-0810:local`, q=6.00, unfulfilled=0 | same |
| PRD-022 | 02130 | mail | routed: 1 ship, `SUP-065:mail`, q=10.00, unfulfilled=0 | routed: 1 ship, `SUP-065:mail`, q=10.00, unfulfilled=0 | same |

## What changed with experiments on

| Order | Experiment | Off | On |
|---|---|---|---|
| PRD-001 (knee-replacement kit) | EXP-1 | 1 shipment, `SUP-0389` (5.30) | 2 shipments, `SUP-0389` + `SUP-0128` (9.6); quality 5.30 to 7.45. Consolidation traded for quality because the uplift cleared the 2.0 threshold. |
| PRD-002 (COPD discharge) | EXP-1 | 1 shipment, `SUP-0960` (7.0) | 2 shipments, `SUP-1056` (9.2) + `SUP-0371` (9.4); quality 7.00 to 9.33. |
| PRD-009 (facility bulk order) | EXP-1 | 1 shipment, `SUP-0389` (5.30) | 2 shipments, `SUP-0261` + `SUP-0429`; quality 5.30 to 7.50. |
| PRD-006 (sports injury) | EXP-2 | 2 shipments, `SUP-0970` + `SUP-0911`, q=7.33 | 2 shipments, unrated `SUP-007` + `SUP-0660`, q=7.47. Unrated supplier chosen (`unrated_chosen=1`) because it counted as 6.5. |
| PRD-019 (sentinel ZIP 00098) | EXP-3 | `not_routed`, nothing shipped | `partially_routed`: wheelchair to `SUP-T014`, walker listed as unfulfilled. |
| PRD-020 (unknown product code) | EXP-3 | `not_routed` | `partially_routed`: `WC-STD-001` to `SUP-0251`, `ZZ-FAKE-999` listed as unfulfilled (`UNKNOWN_PRODUCT`). |

Unchanged in both runs: the three sample orders (the `SUP-065` vs `SUP-023` pick for `ORD-003` is a true random tie), single-supplier orders, mail-only orders, and the re-submitted `PRD-021` (a new stored decision with a new `id`, as POST is not idempotent).

## Observations

1. **Default mode matches the documented behaviour.** `ORD-001` to `SUP-0810`, `ORD-002` to `SUP-0375` + `SUP-042` (9.15), and `ORD-003` to a 10.0 mail supplier all match the README.
2. **Unknown product codes (`PRD-020`).** With EXP-3 off the whole order is `not_routed`. With EXP-3 on, the valid line is routed and the bad one comes back as unfulfilled. That is a business outcome and is reported in the body, but it is worth deciding whether an unknown code (a data-quality fault, not a coverage gap) should ever be partially routed, given the repo's "fail safe on compliance" rule.
3. **Alaska ZIP `99950` (`PRD-014`/`PRD-015`).** The seeded suppliers cover every real ZIP, so even with `mail_order=false` the order routes locally. The mail flag only changed the pick (two local suppliers vs one 10.0 mail supplier).
4. **Search mode** was `exact` for every order, including the six-category `PRD-013`.

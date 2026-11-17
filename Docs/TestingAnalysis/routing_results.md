# Routing results: default (control) vs experiments (treatment)

Captured 2026-11-16 against the local docker stack (PostgreSQL 18.1 + API, seeded reference data). Orders: [sample_orders.json](../Data/sample_orders.json) (3) and [production_like_orders.json](../Data/production_like_orders.json) (122), 125 in total. Raw responses and decision-log fields: [routing_results.json](routing_results.json).

## 1. Key findings

- **Most orders are unaffected.** 89 of 125 (71%) get the same suppliers in both runs; 13 more differ only by the random tie-break between equally ranked suppliers (not an experiment effect); **23 (18%) are genuinely changed by an experiment.**
- **`Exp1ConsolidationVsQuality` is the largest effect.** It changed 14 orders, each by adding one shipment (+1) for a mean plan-quality gain of **+2.55** (all above its 2.0 uplift threshold).
- **`Exp3PartialRouting` converts failures into partial results.** The 2 `not_routed` orders become `partially_routed` (1 shipment each, 1 line still unfulfilled). No order that routed before stopped routing.
- **`Exp2UnratedSuppliers` changes 7 orders** by letting an unrated supplier win: 4 clear cases plus 3 probable. Part of its reported quality gain is re-scoring, not necessarily better suppliers (see section 5).
- **Overall:** shipments per order rose from 1.25 to 1.38, and mean plan quality from 8.07 to 8.47 (+0.40). That is the cost-quality trade: more shipments, higher quality.

## 2. How the runs were set up

| | Run A: default (control) | Run B: experiments (treatment) |
|---|---|---|
| `Exp1ConsolidationVsQuality` | `Enabled: false` | `Enabled: true`, 100% treatment, `QualityUpliftThreshold 2.0` |
| `Exp2UnratedSuppliers` | `Enabled: false` | `Enabled: true`, 100% treatment, `UnratedPrior 6.5` |
| `Exp3PartialRouting` | `Enabled: false` | `Enabled: true`, 100% treatment |
| `SimilarityBand` | 0.5 | 0.5 |
| Decision-log arms | `control`, reason `disabled` | `treatment`, reason `bucketed` |
| Config version | `9bf2b426310b` | `fa2d32303821` |

What each experiment swaps:

| Experiment | Control (default) | Treatment |
|---|---|---|
| `Exp1ConsolidationVsQuality` | `StrictPriorityPolicy`: fewest shipments first | `BoundedTradeOffPolicy`: allows one extra shipment if quality beats the strict plan by at least the uplift threshold |
| `Exp2UnratedSuppliers` | `UnratedLastPolicy`: unrated supplier counts as 0 | `ColdStartPriorPolicy`: unrated supplier counts as 6.5 |
| `Exp3PartialRouting` | `AllOrNothingPolicy`: any unfulfillable line means nothing routes | `PartialRoutingPolicy`: route what can be routed, report the rest as unfulfilled |

Notes: `experiments.json` was restored to the shipped defaults afterwards. Search mode was `exact` for all 250 decisions. `q` is the line-weighted mean supplier rating of the chosen plan.

## 3. Headline metrics

| Metric | Default | Experiments | Change |
|---|---|---|---|
| Orders submitted | 125 | 125 | |
| `routed` | 123 | 123 | +0 |
| `partially_routed` | 0 | 2 | +2 |
| `not_routed` | 2 | 0 | -2 |
| Total shipments | 156 | 172 | +16 |
| Mean shipments per order | 1.25 | 1.38 | +0.13 |
| Mean plan quality (orders with a plan) | 8.07 | 8.47 | +0.40 |
| Unfulfilled lines | 2 | 2 | +0 |
| Unrated suppliers chosen | 5 | 9 | +4 |

Order outcomes: same suppliers **89**, tie-break only **13**, changed by an experiment **23**.

## 4. Effect by experiment

Attribution is inferred from the decision log: status change means `Exp3PartialRouting`; one more shipment means `Exp1ConsolidationVsQuality`; one more unrated supplier chosen means `Exp2UnratedSuppliers`. All three ran together, so this is not isolated.

| Experiment | Orders changed | Mean shipments change | Mean quality change | Reading |
|---|---|---|---|---|
| `Exp1ConsolidationVsQuality` | 14 | +1.00 | +2.55 | Buys quality with one extra shipment each time. Fires on orders whose strict plan is low quality (q 4.8 to 8.0). |
| `Exp2UnratedSuppliers` | 4 (+3 probable) | +0.00 | +0.75 (+1.95 incl. probable) | Unrated suppliers now win. Part of the quality change is the 0 to 6.5 re-scoring. |
| `Exp3PartialRouting` | 2 | +1.00 | n/a (no plan before) | `not_routed` becomes `partially_routed`. |

### Orders changed, by experiment

**`Exp1ConsolidationVsQuality`**

| Order | ZIP | Default | Experiments |
|---|---|---|---|
| PRD-001 | 60610 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0389:local`, `SUP-0128:local`, q=7.45 |
| PRD-002 | 90210 | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 2 ship, `SUP-1056:local`, `SUP-0371:local`, q=9.33 |
| PRD-009 | 80202 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0261:local`, `SUP-0429:local`, q=7.50 |
| PRD-031 | 02110 | routed: 1 ship, `SUP-0520:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0835:local`, q=7.80 |
| PRD-032 | 20001 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0908:mail`, `SUP-0199:mail`, q=10.00 |
| PRD-034 | 87102 | routed: 1 ship, `SUP-0636:mail`, q=6.80 | routed: 2 ship, `SUP-0908:mail`, `SUP-0638:mail`, q=10.00 |
| PRD-039 | 04101 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 |
| PRD-049 | 83702 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-055 | 99501 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0270:mail`, `SUP-0199:mail`, q=10.00 |
| PRD-062 | 98101 | routed: 1 ship, `SUP-1042:mail`, q=6.40 | routed: 2 ship, `SUP-023:mail`, `SUP-0741:mail`, q=9.80 |
| PRD-067 | 06103 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-076 | 90012 | routed: 1 ship, `SUP-0425:mail`, q=8.00 | routed: 2 ship, `SUP-0199:mail`, `SUP-086:mail`, q=10.00 |
| PRD-100 | 87102 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-119 | 05401 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 |

**`Exp2UnratedSuppliers`**

| Order | ZIP | Default | Experiments |
|---|---|---|---|
| PRD-006 | 98101 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.33 | routed: 2 ship, `SUP-007:local`, `SUP-0660:local`, q=7.47, unrated=1 |
| PRD-059 | 33130 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 |
| PRD-102 | 50309 | routed: 2 ship, `SUP-0588:local`, `SUP-0389:local`, q=7.46 | routed: 2 ship, `SUP-0588:local`, `SUP-0781:local`, q=7.94, unrated=1 |
| PRD-109 | 33602 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 |

**`Exp3PartialRouting`**

| Order | ZIP | Default | Experiments |
|---|---|---|---|
| PRD-019 | 00098 | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-T014:local`, q=5.00, unfulfilled=1 |
| PRD-020 | 10015 | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=1 |

**`Exp2UnratedSuppliers`** (probable: the order was already served by an unrated supplier, but the prior changed the second supplier chosen)

| Order | ZIP | Default | Experiments |
|---|---|---|---|
| PRD-058 | 64106 | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 |
| PRD-073 | 07102 | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 |
| PRD-075 | 39201 | routed: 2 ship, `SUP-0781:local`, `SUP-0429:local`, q=3.80, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0660:local`, q=7.22, unrated=1 |

## 5. Caveats for interpreting the numbers

1. **Not a statistical A/B test.** Treatment was 100%, so every order saw treatment in run B and control in run A. It is a before/after comparison over a fixed set of orders, which is deterministic, but it says nothing about variance or significance.
2. **Experiments are confounded.** All three ran together. Orders where two policies could both explain the change are attributed by the rule in section 4; a one-experiment-at-a-time rerun would isolate them.
3. **`Exp2UnratedSuppliers` quality is partly re-scoring.** Control counts an unrated supplier as 0, treatment as 6.5, so `q` rises whenever an unrated supplier is in the plan. PRD-033 shows it: the same two suppliers are chosen in both runs, yet q goes from 5.07 to 7.23. Judge this experiment by which suppliers won (unrated count), not by `q` alone; the +0.40 overall quality gain includes this effect.
4. **Control already picks unrated suppliers sometimes** (5 in run A).
5. **Test sentinel in the data.** PRD-019 in run B was routed to `SUP-T014`, one of the 14 test-only `SUP-T*` suppliers that integration tests leave in the database. The `Exp3PartialRouting` row for PRD-019 is real, but the supplier choice is not representative of production data.
6. **Tie-break noise.** 13 orders differ between runs only because equally ranked suppliers are picked randomly; ignore them when judging experiment impact.

## 6. Suggested next steps

- Rerun with each experiment enabled alone to get clean per-experiment effects.
- Rerun with a partial `TreatmentPercentage` (for example 50) to compare arms on concurrent traffic, as the assignment design intends.
- Remove the `SUP-T*` sentinels before capturing, so supplier choices reflect seed data only.
- For `Exp1ConsolidationVsQuality`, review the 2.0 threshold: the 14 triggered cases gained between 2.0 and 3.4 quality, and a threshold of 3.0 would have kept only 3 of them.

## Appendix A: tie-break only (13 orders)

Same shipment count and plan quality, different equally ranked supplier.

| Order | Default | Experiments | Tied candidates |
|---|---|---|---|
| PRD-012 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | SUP-021, SUP-023 |
| PRD-038 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-0875:mail`, q=10.00 | SUP-0199, SUP-023, SUP-065, SUP-087, SUP-0875, SUP-0979 |
| PRD-043 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | SUP-0638, SUP-091 |
| PRD-050 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | SUP-021, SUP-047, SUP-081, SUP-0908 |
| PRD-065 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-028:mail`, q=10.00 | SUP-016, SUP-0199, SUP-0270, SUP-028, SUP-0908 |
| PRD-070 | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | SUP-016, SUP-0199, SUP-0270, SUP-028, SUP-0908 |
| PRD-079 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | SUP-0199, SUP-021, SUP-081 |
| PRD-087 | routed: 1 ship, `SUP-081:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | SUP-021, SUP-047, SUP-081, SUP-0908 |
| PRD-088 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | SUP-021, SUP-047, SUP-081, SUP-0908 |
| PRD-090 | routed: 1 ship, `SUP-028:mail`, q=10.00 | routed: 1 ship, `SUP-023:mail`, q=10.00 | SUP-023, SUP-028, SUP-047, SUP-0647, SUP-0748, SUP-081, SUP-0889 |
| PRD-111 | routed: 1 ship, `SUP-081:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | SUP-023, SUP-028, SUP-047, SUP-0647, SUP-0748, SUP-081, SUP-0889 |
| PRD-115 | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | SUP-016, SUP-0270, SUP-047, SUP-081, SUP-086, SUP-0979 |
| PRD-120 | routed: 1 ship, `SUP-091:mail`, q=10.00 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | SUP-0638, SUP-091 |

## Appendix B: every order

| Order | ZIP | Delivery | Default | Experiments | Result |
|---|---|---|---|---|---|
| ORD-001 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00 | routed: 1 ship, `SUP-0810:local`, q=6.00 | same |
| ORD-002 | 77059 | local | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15 | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15 | same |
| ORD-003 | 02130 | mail | routed: 1 ship, `SUP-065:mail`, q=10.00 | routed: 1 ship, `SUP-065:mail`, q=10.00 | same |
| PRD-001 | 60610 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0389:local`, `SUP-0128:local`, q=7.45 | **Exp1ConsolidationVsQuality** |
| PRD-002 | 90210 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 2 ship, `SUP-1056:local`, `SUP-0371:local`, q=9.33 | **Exp1ConsolidationVsQuality** |
| PRD-003 | 30303 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47 | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47 | same |
| PRD-004 | 59001 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-005 | 10455 | local | routed: 1 ship, `SUP-035:local`, q=9.00 | routed: 1 ship, `SUP-035:local`, q=9.00 | same |
| PRD-006 | 98101 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.33 | routed: 2 ship, `SUP-007:local`, `SUP-0660:local`, q=7.47, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-007 | 33101 | mail | routed: 1 ship, `SUP-065:mail`, q=10.00 | routed: 1 ship, `SUP-065:mail`, q=10.00 | same |
| PRD-008 | 85001 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-009 | 80202 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0261:local`, `SUP-0429:local`, q=7.50 | **Exp1ConsolidationVsQuality** |
| PRD-010 | 55401 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-011 | 19104 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-012 | 94103 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | tie-break only |
| PRD-013 | 37203 | local | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15 | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15 | same |
| PRD-014 | 99950 | local | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05 | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05 | same |
| PRD-015 | 99950 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | same |
| PRD-016 | 00601 | local | routed: 1 ship, `SUP-0429:local`, q=7.60 | routed: 1 ship, `SUP-0429:local`, q=7.60 | same |
| PRD-017 | 77059 | local | routed: 1 ship, `SUP-042:local`, q=9.00 | routed: 1 ship, `SUP-042:local`, q=9.00 | same |
| PRD-018 | 60615 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-019 | 00098 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-T014:local`, q=5.00, unfulfilled=1 | **Exp3PartialRouting** |
| PRD-020 | 10015 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=1 | **Exp3PartialRouting** |
| PRD-021 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00 | routed: 1 ship, `SUP-0810:local`, q=6.00 | same |
| PRD-022 | 02130 | mail | routed: 1 ship, `SUP-065:mail`, q=10.00 | routed: 1 ship, `SUP-065:mail`, q=10.00 | same |
| PRD-023 | 23219 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-024 | 85004 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-025 | 79901 | mail | routed: 1 ship, `SUP-0748:mail`, q=10.00 | routed: 1 ship, `SUP-0748:mail`, q=10.00 | same |
| PRD-026 | 68102 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-027 | 38103 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | same |
| PRD-028 | 29401 | local | routed: 1 ship, `SUP-0261:local`, q=7.40 | routed: 1 ship, `SUP-0261:local`, q=7.40 | same |
| PRD-029 | 94110 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-030 | 88001 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-031 | 02110 | local | routed: 1 ship, `SUP-0520:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0835:local`, q=7.80 | **Exp1ConsolidationVsQuality** |
| PRD-032 | 20001 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0908:mail`, `SUP-0199:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-033 | 35203 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=5.07, unrated=1 | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=7.23, unrated=1 | same |
| PRD-034 | 87102 | mail | routed: 1 ship, `SUP-0636:mail`, q=6.80 | routed: 2 ship, `SUP-0908:mail`, `SUP-0638:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-035 | 95814 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-036 | 50309 | local | routed: 3 ship, `SUP-0588:local`, `SUP-0960:local`, `SUP-0753:local`, q=7.75 | routed: 3 ship, `SUP-0588:local`, `SUP-0960:local`, `SUP-0753:local`, q=7.75 | same |
| PRD-037 | 57104 | local | routed: 3 ship, `SUP-0251:local`, `SUP-0601:local`, `SUP-0911:local`, q=7.74 | routed: 3 ship, `SUP-0251:local`, `SUP-0601:local`, `SUP-0911:local`, q=7.74 | same |
| PRD-038 | 43215 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-0875:mail`, q=10.00 | tie-break only |
| PRD-039 | 04101 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 | **Exp1ConsolidationVsQuality** |
| PRD-040 | 96813 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-041 | 46204 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | same |
| PRD-042 | 55401 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-043 | 33602 | mail | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | tie-break only |
| PRD-044 | 04730 | mail | routed: 1 ship, `SUP-0244:mail`, q=8.60 | routed: 1 ship, `SUP-0244:mail`, q=8.60 | same |
| PRD-045 | 89101 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.58 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.58 | same |
| PRD-046 | 84101 | local | routed: 2 ship, `SUP-0601:local`, `SUP-0699:local`, q=8.30 | routed: 2 ship, `SUP-0601:local`, `SUP-0699:local`, q=8.30 | same |
| PRD-047 | 92101 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-048 | 32202 | mail | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | same |
| PRD-049 | 83702 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-050 | 58102 | mail | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | tie-break only |
| PRD-051 | 44114 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-052 | 21201 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | same |
| PRD-053 | 05401 | local | routed: 2 ship, `SUP-0753:local`, `SUP-0601:local`, q=8.25 | routed: 2 ship, `SUP-0753:local`, `SUP-0601:local`, q=8.25 | same |
| PRD-054 | 63101 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | same |
| PRD-055 | 99501 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0270:mail`, `SUP-0199:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-056 | 73102 | local | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | same |
| PRD-057 | 82414 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-058 | 64106 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 | **Exp2UnratedSuppliers (probable)** |
| PRD-059 | 33130 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-060 | 96799 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0389:local`, q=5.30 | same |
| PRD-061 | 79520 | mail | routed: 1 ship, `SUP-086:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | same |
| PRD-062 | 98101 | mail | routed: 1 ship, `SUP-1042:mail`, q=6.40 | routed: 2 ship, `SUP-023:mail`, `SUP-0741:mail`, q=9.80 | **Exp1ConsolidationVsQuality** |
| PRD-063 | 75201 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-064 | 40202 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-065 | 82001 | mail | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-028:mail`, q=10.00 | tie-break only |
| PRD-066 | 97204 | mail | routed: 1 ship, `SUP-0431:mail`, q=9.30 | routed: 1 ship, `SUP-0431:mail`, q=9.30 | same |
| PRD-067 | 06103 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-068 | 57501 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-069 | 99701 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0389:local`, q=5.30 | same |
| PRD-070 | 59301 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | tie-break only |
| PRD-071 | 80202 | mail | routed: 1 ship, `SUP-0908:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | same |
| PRD-072 | 72201 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-016:mail`, q=10.00 | same |
| PRD-073 | 07102 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 | **Exp2UnratedSuppliers (probable)** |
| PRD-074 | 10001 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-075 | 39201 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0429:local`, q=3.80, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0660:local`, q=7.22, unrated=1 | **Exp2UnratedSuppliers (probable)** |
| PRD-076 | 90012 | mail | routed: 1 ship, `SUP-0425:mail`, q=8.00 | routed: 2 ship, `SUP-0199:mail`, `SUP-086:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-077 | 59101 | mail | routed: 1 ship, `SUP-035:mail`, q=9.00 | routed: 1 ship, `SUP-035:mail`, q=9.00 | same |
| PRD-078 | 78701 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | same |
| PRD-079 | 48226 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | tie-break only |
| PRD-080 | 83201 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=4.56, unrated=1 | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=7.16, unrated=1 | same |
| PRD-081 | 53202 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-082 | 70112 | local | routed: 1 ship, `SUP-0332:local`, q=8.20 | routed: 1 ship, `SUP-0332:local`, q=8.20 | same |
| PRD-083 | 77002 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-084 | 30303 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-085 | 25301 | local | routed: 2 ship, `SUP-0955:local`, `SUP-0484:local`, q=5.55 | routed: 2 ship, `SUP-0955:local`, `SUP-0484:local`, q=5.55 | same |
| PRD-086 | 11201 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-087 | 28202 | mail | routed: 1 ship, `SUP-081:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | tie-break only |
| PRD-088 | 15222 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | tie-break only |
| PRD-089 | 23219 | mail | routed: 1 ship, `SUP-086:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | same |
| PRD-090 | 85004 | mail | routed: 1 ship, `SUP-028:mail`, q=10.00 | routed: 1 ship, `SUP-023:mail`, q=10.00 | tie-break only |
| PRD-091 | 79901 | mail | routed: 1 ship, `SUP-087:mail`, q=10.00 | routed: 1 ship, `SUP-087:mail`, q=10.00 | same |
| PRD-092 | 68102 | mail | routed: 1 ship, `SUP-0647:mail`, q=10.00 | routed: 1 ship, `SUP-0647:mail`, q=10.00 | same |
| PRD-093 | 38103 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-094 | 29401 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-095 | 94110 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-096 | 88001 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-023:mail`, q=10.00 | same |
| PRD-097 | 02110 | local | routed: 2 ship, `SUP-0835:local`, `SUP-1055:local`, q=8.65 | routed: 2 ship, `SUP-0835:local`, `SUP-1055:local`, q=8.65 | same |
| PRD-098 | 20001 | local | routed: 1 ship, `SUP-0261:local`, q=7.40 | routed: 1 ship, `SUP-0261:local`, q=7.40 | same |
| PRD-099 | 35203 | mail | routed: 1 ship, `SUP-0647:mail`, q=10.00 | routed: 1 ship, `SUP-0647:mail`, q=10.00 | same |
| PRD-100 | 87102 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-101 | 95814 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-102 | 50309 | local | routed: 2 ship, `SUP-0588:local`, `SUP-0389:local`, q=7.46 | routed: 2 ship, `SUP-0588:local`, `SUP-0781:local`, q=7.94, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-103 | 57104 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-104 | 43215 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-105 | 04101 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-106 | 96813 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-107 | 46204 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-108 | 55401 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-109 | 33602 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-110 | 04730 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-111 | 89101 | mail | routed: 1 ship, `SUP-081:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | tie-break only |
| PRD-112 | 84101 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-113 | 92101 | mail | routed: 1 ship, `SUP-091:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | same |
| PRD-114 | 32202 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-115 | 83702 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | tie-break only |
| PRD-116 | 58102 | local | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | same |
| PRD-117 | 44114 | local | routed: 1 ship, `SUP-0332:local`, q=8.20 | routed: 1 ship, `SUP-0332:local`, q=8.20 | same |
| PRD-118 | 21201 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-119 | 05401 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 | **Exp1ConsolidationVsQuality** |
| PRD-120 | 63101 | mail | routed: 1 ship, `SUP-091:mail`, q=10.00 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | tie-break only |
| PRD-121 | 99501 | mail | routed: 1 ship, `SUP-091:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | same |
| PRD-122 | 73102 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |

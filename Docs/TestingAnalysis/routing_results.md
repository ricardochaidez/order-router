# Routing results: default (control) vs experiments (treatment)

Captured 2026-11-16 against the local docker stack (PostgreSQL 18.1 + API rebuilt from the current code, seeded reference data). Orders: [sample_orders.json](../Data/sample_orders.json) (3) and [production_like_orders.json](../Data/production_like_orders.json) (122), 125 in total. Raw responses and decision-log fields for all four runs: [routing_results.json](routing_results.json). The control tie-break is deterministic (lowest supplier IDs); random is the `Exp4TieBreaking` treatment (ADR-0001).

**Test suite on the same code:** `dotnet test` passed, 1,575 of 1,575 (unit and integration, 0 failed, 0 skipped).

## 1. Key findings

- **Most orders are unaffected.** 85 of 125 (68%) get the same suppliers with every experiment off and with Exp1 to Exp3 on; 17 more differ only because of `Exp4TieBreaking` (a random pick among equally ranked suppliers instead of the lowest ID); **23 (18%) are changed by `Exp1ConsolidationVsQuality`, `Exp2UnratedSuppliers` or `Exp3PartialRouting`**.
- **`Exp4TieBreaking` only matters on full ties, and changes no outcome other than which supplier wins.** 24 of 125 orders (19%) had a full tie. The random pick differed from the deterministic one on 17 of them (on the others it landed on the lowest ID by chance). Status, shipments and plan quality are identical between run A and run B for every order (confirmed).
- **The treatment spreads ties over more suppliers.** The deterministic core sent the 24 ties to 5 different suppliers (the most frequent was `SUP-016` (5 orders)); the random pick spread them over 14 (the most frequent was `SUP-091` (3 orders)). The cost of repeatability is that the same suppliers always win ties; the benefit is that the same order always gets the same plan.
- **`Exp1ConsolidationVsQuality` is the largest of the other three.** It changed 14 orders, each by adding one shipment (+1) for a mean plan-quality gain of **+2.55**.
- **`Exp3PartialRouting` converts failures into partial results.** The 2 `not_routed` orders become `partially_routed` (1 shipment each, 1 line still unfulfilled each). No order that routed before stopped routing.
- **`Exp2UnratedSuppliers` changes 7 orders** by letting an unrated supplier win: 4 clear cases plus 3 probable. Part of its reported quality gain is re-scoring (see section 6).
- **Overall (default against all four on):** shipments per order rose from 1.25 to 1.38, and mean plan quality from 8.07 to 8.47 (+0.40). Those changes come entirely from the first three experiments (run D and run C match on status, shipments and quality for every order: confirmed).

## 2. How the runs were set up

Four runs over the same orders, in the same order. Each experiment is either off (`Enabled: false`, control) or on at 100% treatment. `experiments.json` hot-reloads; each run waited until the decision log showed the new `config_version` before sending orders.

| | Run A: default | Run B: Exp4 only | Run C: Exp1 to Exp3 | Run D: all four |
|---|---|---|---|---|
| `Exp1ConsolidationVsQuality` | `Enabled: false` | `Enabled: false` | `Enabled: true`, 100% treatment, `QualityUpliftThreshold 2.0` | `Enabled: true`, 100% treatment, `QualityUpliftThreshold 2.0` |
| `Exp2UnratedSuppliers` | `Enabled: false` | `Enabled: false` | `Enabled: true`, 100% treatment, `UnratedPrior 6.5` | `Enabled: true`, 100% treatment, `UnratedPrior 6.5` |
| `Exp3PartialRouting` | `Enabled: false` | `Enabled: false` | `Enabled: true`, 100% treatment | `Enabled: true`, 100% treatment |
| `Exp4TieBreaking` | `Enabled: false` | `Enabled: true`, 100% treatment | `Enabled: false` | `Enabled: true`, 100% treatment |
| `SimilarityBand` | 0.5 | 0.5 | 0.5 | 0.5 |
| Decision-log arms | all `control`, reason `disabled` | only `exp4` `treatment`, reason `bucketed` | `exp1` to `exp3` `treatment` | all `treatment`, reason `bucketed` |
| Config version | `d05f51805642` | `eb7f6d23c19d` | `ee2efcaa1ed2` | `5cfb0f29fbb0` |

What each experiment swaps:

| Experiment | Control (default) | Treatment |
|---|---|---|
| `Exp1ConsolidationVsQuality` | `StrictPriorityPolicy`: fewest shipments first | `BoundedTradeOffPolicy`: allows one extra shipment if quality beats the strict plan by at least the uplift threshold |
| `Exp2UnratedSuppliers` | `UnratedLastPolicy`: unrated supplier counts as 0 | `ColdStartPriorPolicy`: unrated supplier counts as 6.5 |
| `Exp3PartialRouting` | `AllOrNothingPolicy`: any unfulfillable line means nothing routes | `PartialRoutingPolicy`: route what can be routed, report the rest as unfulfilled |
| `Exp4TieBreaking` | `DeterministicTieBreaker`: the tied plan whose supplier IDs sort first (ordinal) | `UniformRandomTieBreaker`: one of the tied plans, uniformly at random |

Notes: `experiments.json` was restored afterwards. Search mode was `exact` for all 500 decisions. `q` is the line-weighted mean supplier rating of the chosen plan. **Why four runs:** with the random tie-break on, two runs differ on tied orders by chance. Run A against run B isolates the tie-break (and shows how it differs from the deterministic core). Run A against run C isolates Exp1 to Exp3, because both break ties deterministically and so cannot differ by chance. Run B and run D each reflect one random draw; a rerun would pick different suppliers among the tied ones.

## 3. Headline metrics

| Metric | A: default | B: Exp4 only | C: Exp1 to Exp3 | D: all four | D vs A |
|---|---|---|---|---|---|
| Orders submitted | 125 | 125 | 125 | 125 |  |
| `routed` | 123 | 123 | 123 | 123 | +0 |
| `partially_routed` | 0 | 0 | 2 | 2 | +2 |
| `not_routed` | 2 | 2 | 0 | 0 | -2 |
| Total shipments | 156 | 156 | 172 | 172 | +16 |
| Mean shipments per order | 1.25 | 1.25 | 1.38 | 1.38 | +0.13 |
| Mean plan quality (orders with a plan) | 8.07 | 8.07 | 8.47 | 8.47 | +0.40 |
| Unfulfilled lines | 2 | 2 | 2 | 2 | +0 |
| Unrated suppliers chosen | 5 | 5 | 9 | 9 | +4 |

Order outcomes (run A against run D): same suppliers **85**, changed by `Exp4TieBreaking` only **17**, changed by Exp1 to Exp3 **23**.

## 4. Effect of Exp1 to Exp3

Measured between run A and run C, which differ in Exp1 to Exp3 only and both break ties deterministically, so no difference here is chance. Attribution is inferred from the decision log: status change means `Exp3PartialRouting`; one more shipment means `Exp1ConsolidationVsQuality`; one more unrated supplier chosen means `Exp2UnratedSuppliers`. All three ran together, so this is not isolated per experiment.

| Experiment | Orders changed | Mean shipments change | Mean plan quality change | Reading |
|---|---|---|---|---|
| `Exp1ConsolidationVsQuality` | 14 | +1.00 | +2.55 | Buys quality with one extra shipment each time. Fires on orders whose strict plan is low quality (q 4.8 to 8.0). |
| `Exp2UnratedSuppliers` | 4 (+3 probable) | +0.00 | +0.75 (+1.95 incl. probable) | Unrated suppliers now win. Part of the quality change is the 0 to 6.5 re-scoring. |
| `Exp3PartialRouting` | 2 | +1.00 | n/a (no plan before) | `not_routed` becomes `partially_routed`. |

### Orders changed, by experiment

**`Exp1ConsolidationVsQuality`**

| Order | ZIP | Exp1 to Exp3 off (run A) | Exp1 to Exp3 on (run C) |
|---|---|---|---|
| PRD-001 | 60610 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0389:local`, `SUP-0128:local`, q=7.45 |
| PRD-002 | 90210 | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 2 ship, `SUP-1056:local`, `SUP-0371:local`, q=9.33 |
| PRD-009 | 80202 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0261:local`, `SUP-0429:local`, q=7.50 |
| PRD-031 | 02110 | routed: 1 ship, `SUP-0520:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0835:local`, q=7.80 |
| PRD-032 | 20001 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0270:mail`, `SUP-0199:mail`, q=10.00 |
| PRD-034 | 87102 | routed: 1 ship, `SUP-0636:mail`, q=6.80 | routed: 2 ship, `SUP-047:mail`, `SUP-0199:mail`, q=10.00 |
| PRD-039 | 04101 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 |
| PRD-049 | 83702 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-055 | 99501 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0270:mail`, `SUP-0199:mail`, q=10.00 |
| PRD-062 | 98101 | routed: 1 ship, `SUP-1042:mail`, q=6.40 | routed: 2 ship, `SUP-023:mail`, `SUP-0741:mail`, q=9.80 |
| PRD-067 | 06103 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-076 | 90012 | routed: 1 ship, `SUP-0425:mail`, q=8.00 | routed: 2 ship, `SUP-016:mail`, `SUP-086:mail`, q=10.00 |
| PRD-100 | 87102 | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 |
| PRD-119 | 05401 | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 |

**`Exp2UnratedSuppliers`**

| Order | ZIP | Exp1 to Exp3 off (run A) | Exp1 to Exp3 on (run C) |
|---|---|---|---|
| PRD-006 | 98101 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.33 | routed: 2 ship, `SUP-007:local`, `SUP-0660:local`, q=7.47, unrated=1 |
| PRD-059 | 33130 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 |
| PRD-102 | 50309 | routed: 2 ship, `SUP-0588:local`, `SUP-0389:local`, q=7.46 | routed: 2 ship, `SUP-0588:local`, `SUP-007:local`, q=7.94, unrated=1 |
| PRD-109 | 33602 | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 |

**`Exp3PartialRouting`**

| Order | ZIP | Exp1 to Exp3 off (run A) | Exp1 to Exp3 on (run C) |
|---|---|---|---|
| PRD-019 | 00098 | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-T014:local`, q=5.00, unfulfilled=1 |
| PRD-020 | 10015 | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=1 |

**`Exp2UnratedSuppliers` (probable: the order was already served by an unrated supplier, but the prior changed the second supplier chosen)**

| Order | ZIP | Exp1 to Exp3 off (run A) | Exp1 to Exp3 on (run C) |
|---|---|---|---|
| PRD-058 | 64106 | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 |
| PRD-073 | 07102 | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 |
| PRD-075 | 39201 | routed: 2 ship, `SUP-0781:local`, `SUP-0429:local`, q=3.80, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0660:local`, q=7.22, unrated=1 |

## 5. Effect of Exp4 (tie-breaking)

Measured between run A and run B, which differ in `Exp4TieBreaking` only.

| Metric | Control (deterministic) | Treatment (random) |
|---|---|---|
| Orders with a full tie | 24 | 24 |
| Tied orders where the pick differs between the two runs | | 17 |
| Different suppliers that won a tie | 5 | 14 |
| Most frequent tie winner | `SUP-016` (5 orders) | `SUP-091` (3 orders) |
| Orders changed in status, shipments or plan quality | | 0 |

The tied orders had 9 with 2 tied plans, 1 with 3 tied plans, 4 with 4 tied plans, 4 with 5 tied plans, 4 with 6 tied plans, 2 with 7 tied plans. The tied candidates are the same in both runs (checked), and in run A the pick was the lowest supplier IDs in ordinal order for **every** tied order.

How often each supplier won a tie (orders won, among the tied orders):

| Supplier | Control (run A) | Treatment (run B) |
|---|---|---|
| `SUP-016` | 5 | 2 |
| `SUP-023` | 5 | 2 |
| `SUP-0199` | 5 | 1 |
| `SUP-021` | 5 | 1 |
| `SUP-0638` | 4 | 2 |
| `SUP-091` | 0 | 3 |
| `SUP-0270` | 0 | 2 |
| `SUP-047` | 0 | 2 |
| `SUP-081` | 0 | 2 |
| `SUP-087` | 0 | 2 |
| `SUP-0908` | 0 | 2 |
| `SUP-065` | 0 | 1 |
| `SUP-0748` | 0 | 1 |
| `SUP-0979` | 0 | 1 |

## 6. Caveats for interpreting the numbers

1. **Not a statistical A/B test.** Treatment was 100%, so every order saw treatment in the treated runs and control in run A. It is a before/after comparison over a fixed set of orders, which is deterministic apart from the random tie-break, but it says nothing about variance or significance.
2. **Exp1 to Exp3 are confounded with each other.** They ran together. Orders where two policies could both explain the change are attributed by the rule in section 4; a one-experiment-at-a-time rerun would isolate them. `Exp4TieBreaking` is isolated by run B.
3. **`Exp4TieBreaking` treatment is one random draw.** Runs B and D each used one draw per tied order. The counts of orders whose pick differs, and the supplier spread, vary from run to run; the deterministic runs (A and C) are fully repeatable.
4. **`Exp2UnratedSuppliers` quality is partly re-scoring.** Control counts an unrated supplier as 0, treatment as 6.5, so `q` rises whenever an unrated supplier is in the plan. Judge this experiment by which suppliers won (unrated count), not by `q` alone; the overall quality gain includes this effect.
   Re-scoring only (same suppliers, different `q`): PRD-033 (5.07 to 7.23), PRD-080 (4.56 to 7.16).
5. **Control already picks unrated suppliers sometimes** (5 in run A).
6. **Test sentinels in the data.** PRD-019 (run C: SUP-T014). The 14 test-only `SUP-T*` suppliers stay in the database after integration tests run, so supplier choices on affected orders are not representative of production data.
7. **`Exp4TieBreaking` outcomes depend on supplier IDs.** The deterministic core favors the lowest IDs, which says nothing about supplier quality, capacity or SLA. The experiment's effect is on fairness between equal suppliers, not on any order-level quality measure, and this capture cannot show whether the suppliers that gain orders accept them.

## 7. Suggested next steps

- Rerun with each of Exp1 to Exp3 enabled alone to get clean per-experiment effects.
- Rerun with a partial `TreatmentPercentage` (for example 50) to compare arms on concurrent traffic, as the assignment design intends. For `Exp4TieBreaking` the metric to compare is the share of tied orders each supplier receives, plus their acceptance rate.
- Remove the `SUP-T*` sentinels before capturing, so supplier choices reflect seed data only.
- For `Exp1ConsolidationVsQuality`, review the 2.0 threshold: the 14 triggered cases gained between 2.0 and 3.4 quality, and a threshold of 3.0 would have kept only 3 of them.

## Appendix A: tie-break only (17 orders)

Same shipment count and plan quality in run A (deterministic pick) and run B (random pick), different equally ranked supplier. Orders that also changed through Exp1 to Exp3 are in section 4.

| Order | Control (deterministic) | Treatment (random) | Tied candidates |
|---|---|---|---|
| PRD-007 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-0979:mail`, q=10.00 | SUP-0199, SUP-023, SUP-065, SUP-087, SUP-0875, SUP-0979 |
| PRD-012 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-023:mail`, q=10.00 | SUP-021, SUP-023 |
| PRD-022 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-065:mail`, q=10.00 | SUP-023, SUP-065 |
| PRD-025 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-0748:mail`, q=10.00 | SUP-0199, SUP-0638, SUP-0748, SUP-086 |
| PRD-038 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-087:mail`, q=10.00 | SUP-0199, SUP-023, SUP-065, SUP-087, SUP-0875, SUP-0979 |
| PRD-048 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-0270:mail`, q=10.00 | SUP-021, SUP-0270, SUP-044, SUP-0647, SUP-0908, SUP-0979 |
| PRD-070 | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | SUP-016, SUP-0199, SUP-0270, SUP-028, SUP-0908 |
| PRD-071 | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0270:mail`, q=10.00 | SUP-016, SUP-0199, SUP-0270, SUP-028, SUP-0908 |
| PRD-087 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | SUP-021, SUP-047, SUP-081, SUP-0908 |
| PRD-088 | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | SUP-021, SUP-047, SUP-081, SUP-0908 |
| PRD-090 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | SUP-023, SUP-028, SUP-047, SUP-0647, SUP-0748, SUP-081, SUP-0889 |
| PRD-091 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-087:mail`, q=10.00 | SUP-0199, SUP-0748, SUP-081, SUP-086, SUP-087 |
| PRD-096 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | SUP-023, SUP-091 |
| PRD-111 | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | SUP-023, SUP-028, SUP-047, SUP-0647, SUP-0748, SUP-081, SUP-0889 |
| PRD-113 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | SUP-0638, SUP-091 |
| PRD-115 | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | SUP-016, SUP-0270, SUP-047, SUP-081, SUP-086, SUP-0979 |
| PRD-120 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | SUP-0638, SUP-091 |

## Appendix B: every order

| Order | ZIP | Delivery | A: default | D: all four on | Result |
|---|---|---|---|---|---|
| ORD-001 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00 | routed: 1 ship, `SUP-0810:local`, q=6.00 | same |
| ORD-002 | 77059 | local | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15 | routed: 2 ship, `SUP-0375:local`, `SUP-042:local`, q=9.15 | same |
| ORD-003 | 02130 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-065:mail`, q=10.00 | same |
| PRD-001 | 60610 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0389:local`, `SUP-0128:local`, q=7.45 | **Exp1ConsolidationVsQuality** |
| PRD-002 | 90210 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 2 ship, `SUP-1056:local`, `SUP-0371:local`, q=9.33 | **Exp1ConsolidationVsQuality** |
| PRD-003 | 30303 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47 | routed: 2 ship, `SUP-0429:local`, `SUP-0251:local`, q=7.47 | same |
| PRD-004 | 59001 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-005 | 10455 | local | routed: 1 ship, `SUP-035:local`, q=9.00 | routed: 1 ship, `SUP-035:local`, q=9.00 | same |
| PRD-006 | 98101 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.33 | routed: 2 ship, `SUP-007:local`, `SUP-0660:local`, q=7.47, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-007 | 33101 | mail | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-087:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-008 | 85001 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-009 | 80202 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 2 ship, `SUP-0261:local`, `SUP-0429:local`, q=7.50 | **Exp1ConsolidationVsQuality** |
| PRD-010 | 55401 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-011 | 19104 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-012 | 94103 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-013 | 37203 | local | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15 | routed: 2 ship, `SUP-0960:local`, `SUP-0389:local`, q=6.15 | same |
| PRD-014 | 99950 | local | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05 | routed: 2 ship, `SUP-0251:local`, `SUP-0960:local`, q=7.05 | same |
| PRD-015 | 99950 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | same |
| PRD-016 | 00601 | local | routed: 1 ship, `SUP-0429:local`, q=7.60 | routed: 1 ship, `SUP-0429:local`, q=7.60 | same |
| PRD-017 | 77059 | local | routed: 1 ship, `SUP-042:local`, q=9.00 | routed: 1 ship, `SUP-042:local`, q=9.00 | same |
| PRD-018 | 60615 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-019 | 00098 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-T014:local`, q=5.00, unfulfilled=1 | **Exp3PartialRouting** |
| PRD-020 | 10015 | local | not_routed: 0 ship, none, q=-, unfulfilled=1 | partially_routed: 1 ship, `SUP-0251:local`, q=7.10, unfulfilled=1 | **Exp3PartialRouting** |
| PRD-021 | 10015 | local | routed: 1 ship, `SUP-0810:local`, q=6.00 | routed: 1 ship, `SUP-0810:local`, q=6.00 | same |
| PRD-022 | 02130 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-023:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-023 | 23219 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-024 | 85004 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-025 | 79901 | mail | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-0748:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-026 | 68102 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-027 | 38103 | mail | routed: 1 ship, `SUP-047:mail`, q=10.00 | routed: 1 ship, `SUP-047:mail`, q=10.00 | same |
| PRD-028 | 29401 | local | routed: 1 ship, `SUP-0261:local`, q=7.40 | routed: 1 ship, `SUP-0261:local`, q=7.40 | same |
| PRD-029 | 94110 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-030 | 88001 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-031 | 02110 | local | routed: 1 ship, `SUP-0520:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0835:local`, q=7.80 | **Exp1ConsolidationVsQuality** |
| PRD-032 | 20001 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0908:mail`, `SUP-0199:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-033 | 35203 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=5.07, unrated=1 | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=7.23, unrated=1 | same |
| PRD-034 | 87102 | mail | routed: 1 ship, `SUP-0636:mail`, q=6.80 | routed: 2 ship, `SUP-047:mail`, `SUP-0199:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-035 | 95814 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-036 | 50309 | local | routed: 3 ship, `SUP-0588:local`, `SUP-0960:local`, `SUP-0753:local`, q=7.75 | routed: 3 ship, `SUP-0588:local`, `SUP-0960:local`, `SUP-0753:local`, q=7.75 | same |
| PRD-037 | 57104 | local | routed: 3 ship, `SUP-0251:local`, `SUP-0601:local`, `SUP-0911:local`, q=7.74 | routed: 3 ship, `SUP-0251:local`, `SUP-0601:local`, `SUP-0911:local`, q=7.74 | same |
| PRD-038 | 43215 | mail | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-0979:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-039 | 04101 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 | **Exp1ConsolidationVsQuality** |
| PRD-040 | 96813 | local | routed: 1 ship, `SUP-0911:local`, q=7.40 | routed: 1 ship, `SUP-0911:local`, q=7.40 | same |
| PRD-041 | 46204 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | same |
| PRD-042 | 55401 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-043 | 33602 | mail | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | same |
| PRD-044 | 04730 | mail | routed: 1 ship, `SUP-0244:mail`, q=8.60 | routed: 1 ship, `SUP-0244:mail`, q=8.60 | same |
| PRD-045 | 89101 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.58 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.58 | same |
| PRD-046 | 84101 | local | routed: 2 ship, `SUP-0601:local`, `SUP-0699:local`, q=8.30 | routed: 2 ship, `SUP-0601:local`, `SUP-0699:local`, q=8.30 | same |
| PRD-047 | 92101 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-048 | 32202 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-0979:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-049 | 83702 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-050 | 58102 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | same |
| PRD-051 | 44114 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-052 | 21201 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | same |
| PRD-053 | 05401 | local | routed: 2 ship, `SUP-0753:local`, `SUP-0601:local`, q=8.25 | routed: 2 ship, `SUP-0753:local`, `SUP-0601:local`, q=8.25 | same |
| PRD-054 | 63101 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | routed: 2 ship, `SUP-0429:local`, `SUP-0699:local`, q=7.70 | same |
| PRD-055 | 99501 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0270:mail`, `SUP-0199:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-056 | 73102 | local | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | same |
| PRD-057 | 82414 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-058 | 64106 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 | **Exp2 probable** |
| PRD-059 | 33130 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-060 | 96799 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0389:local`, q=5.30 | same |
| PRD-061 | 79520 | mail | routed: 1 ship, `SUP-086:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | same |
| PRD-062 | 98101 | mail | routed: 1 ship, `SUP-1042:mail`, q=6.40 | routed: 2 ship, `SUP-023:mail`, `SUP-0741:mail`, q=9.80 | **Exp1ConsolidationVsQuality** |
| PRD-063 | 75201 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-064 | 40202 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-065 | 82001 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-016:mail`, q=10.00 | same |
| PRD-066 | 97204 | mail | routed: 1 ship, `SUP-0431:mail`, q=9.30 | routed: 1 ship, `SUP-0431:mail`, q=9.30 | same |
| PRD-067 | 06103 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-068 | 57501 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-069 | 99701 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0389:local`, q=5.30 | same |
| PRD-070 | 59301 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0908:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-071 | 80202 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-028:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-072 | 72201 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0199:mail`, q=10.00 | same |
| PRD-073 | 07102 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0960:local`, q=3.50, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0588:local`, q=7.10, unrated=1 | **Exp2 probable** |
| PRD-074 | 10001 | local | routed: 1 ship, `SUP-0601:local`, q=8.40 | routed: 1 ship, `SUP-0601:local`, q=8.40 | same |
| PRD-075 | 39201 | local | routed: 2 ship, `SUP-0781:local`, `SUP-0429:local`, q=3.80, unrated=1 | routed: 2 ship, `SUP-0781:local`, `SUP-0660:local`, q=7.22, unrated=1 | **Exp2 probable** |
| PRD-076 | 90012 | mail | routed: 1 ship, `SUP-0425:mail`, q=8.00 | routed: 2 ship, `SUP-0199:mail`, `SUP-023:mail`, q=10.00 | **Exp1ConsolidationVsQuality** |
| PRD-077 | 59101 | mail | routed: 1 ship, `SUP-035:mail`, q=9.00 | routed: 1 ship, `SUP-035:mail`, q=9.00 | same |
| PRD-078 | 78701 | local | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | routed: 2 ship, `SUP-0970:local`, `SUP-0960:local`, q=7.13 | same |
| PRD-079 | 48226 | mail | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | same |
| PRD-080 | 83201 | local | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=4.56, unrated=1 | routed: 2 ship, `SUP-0429:local`, `SUP-0781:local`, q=7.16, unrated=1 | same |
| PRD-081 | 53202 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-082 | 70112 | local | routed: 1 ship, `SUP-0332:local`, q=8.20 | routed: 1 ship, `SUP-0332:local`, q=8.20 | same |
| PRD-083 | 77002 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-084 | 30303 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-085 | 25301 | local | routed: 2 ship, `SUP-0955:local`, `SUP-0484:local`, q=5.55 | routed: 2 ship, `SUP-0955:local`, `SUP-0484:local`, q=5.55 | same |
| PRD-086 | 11201 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-087 | 28202 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-088 | 15222 | mail | routed: 1 ship, `SUP-021:mail`, q=10.00 | routed: 1 ship, `SUP-021:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-089 | 23219 | mail | routed: 1 ship, `SUP-086:mail`, q=10.00 | routed: 1 ship, `SUP-086:mail`, q=10.00 | same |
| PRD-090 | 85004 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-0647:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-091 | 79901 | mail | routed: 1 ship, `SUP-0199:mail`, q=10.00 | routed: 1 ship, `SUP-0748:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-092 | 68102 | mail | routed: 1 ship, `SUP-0647:mail`, q=10.00 | routed: 1 ship, `SUP-0647:mail`, q=10.00 | same |
| PRD-093 | 38103 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-094 | 29401 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-095 | 94110 | local | routed: 1 ship, `SUP-0660:local`, q=9.40 | routed: 1 ship, `SUP-0660:local`, q=9.40 | same |
| PRD-096 | 88001 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-097 | 02110 | local | routed: 2 ship, `SUP-0835:local`, `SUP-1055:local`, q=8.65 | routed: 2 ship, `SUP-0835:local`, `SUP-1055:local`, q=8.65 | same |
| PRD-098 | 20001 | local | routed: 1 ship, `SUP-0261:local`, q=7.40 | routed: 1 ship, `SUP-0261:local`, q=7.40 | same |
| PRD-099 | 35203 | mail | routed: 1 ship, `SUP-0647:mail`, q=10.00 | routed: 1 ship, `SUP-0647:mail`, q=10.00 | same |
| PRD-100 | 87102 | local | routed: 1 ship, `SUP-0955:local`, q=4.80 | routed: 2 ship, `SUP-0970:local`, `SUP-0911:local`, q=7.30 | **Exp1ConsolidationVsQuality** |
| PRD-101 | 95814 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-102 | 50309 | local | routed: 2 ship, `SUP-0588:local`, `SUP-0389:local`, q=7.46 | routed: 2 ship, `SUP-0588:local`, `SUP-007:local`, q=7.94, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-103 | 57104 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-104 | 43215 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |
| PRD-105 | 04101 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-106 | 96813 | local | routed: 1 ship, `SUP-0251:local`, q=7.10 | routed: 1 ship, `SUP-0251:local`, q=7.10 | same |
| PRD-107 | 46204 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-108 | 55401 | local | routed: 1 ship, `SUP-0588:local`, q=8.90 | routed: 1 ship, `SUP-0588:local`, q=8.90 | same |
| PRD-109 | 33602 | local | routed: 1 ship, `SUP-0389:local`, q=5.30 | routed: 1 ship, `SUP-0781:local`, q=6.50, unrated=1 | **Exp2UnratedSuppliers** |
| PRD-110 | 04730 | mail | routed: 1 ship, `SUP-0741:mail`, q=9.60 | routed: 1 ship, `SUP-0741:mail`, q=9.60 | same |
| PRD-111 | 89101 | mail | routed: 1 ship, `SUP-023:mail`, q=10.00 | routed: 1 ship, `SUP-081:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-112 | 84101 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-113 | 92101 | mail | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-114 | 32202 | local | routed: 1 ship, `SUP-0970:local`, q=7.20 | routed: 1 ship, `SUP-0970:local`, q=7.20 | same |
| PRD-115 | 83702 | mail | routed: 1 ship, `SUP-016:mail`, q=10.00 | routed: 1 ship, `SUP-0979:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-116 | 58102 | local | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | routed: 3 ship, `SUP-0960:local`, `SUP-0660:local`, `SUP-0251:local`, q=7.82 | same |
| PRD-117 | 44114 | local | routed: 1 ship, `SUP-0332:local`, q=8.20 | routed: 1 ship, `SUP-0332:local`, q=8.20 | same |
| PRD-118 | 21201 | local | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | routed: 2 ship, `SUP-0911:local`, `SUP-0753:local`, q=7.63 | same |
| PRD-119 | 05401 | mail | routed: 1 ship, `SUP-0680:mail`, q=7.50 | routed: 2 ship, `SUP-0163:mail`, `SUP-081:mail`, q=9.97 | **Exp1ConsolidationVsQuality** |
| PRD-120 | 63101 | mail | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-091:mail`, q=10.00 | tie-break only (Exp4) |
| PRD-121 | 99501 | mail | routed: 1 ship, `SUP-0638:mail`, q=10.00 | routed: 1 ship, `SUP-0638:mail`, q=10.00 | same |
| PRD-122 | 73102 | local | routed: 1 ship, `SUP-0960:local`, q=7.00 | routed: 1 ship, `SUP-0960:local`, q=7.00 | same |

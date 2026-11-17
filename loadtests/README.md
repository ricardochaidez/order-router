# Load tests (k6)

Load tests `POST /api/v1/route` (plus a `GET` read-back of 10% of the created orders) from low traffic to peak, with
the default routing and with all experiments on, and compares the two.

```bash
brew install k6                         # once
docker compose up -d --wait             # API + PostgreSQL, migrated and seeded (README, Getting started)
loadtests/run.sh                        # ladder (low, medium, peak) x (default, experiments) + comparison, ~7 min per mode
loadtests/run.sh --profile smoke        # 15 s sanity check
loadtests/run.sh --profile peak --mode default
loadtests/run.sh --profile peak -- -e PEAK_RPS=150 -e HOLD=3m
```

Results go to `loadtests/results/run-<timestamp>/` (git-ignored): `report.md`, and per mode `summary.json` and
`console.log`. `python3 loadtests/analyze.py <dir>` rebuilds the report.

## What is tested

| Level | Default rate | Meaning |
|---|---|---|
| low | 5 req/s | very low traffic |
| medium | 25 req/s | normal working hours |
| peak | 100 req/s | peak hour |
| stress | 250 req/s | find the breaking point (not part of `ladder`) |

Override with `LOW_RPS`, `MEDIUM_RPS`, `PEAK_RPS`, `STRESS_RPS`, and the timing with `RAMP` (15s) and `HOLD` (90s).
Pick rates for your machine: the defaults are a starting point, not a measured capacity.

- **Open model** (`ramping-arrival-rate`): requests start at the target rate whatever the latency. An overloaded API
  shows as high latency, failures and `dropped_iterations`, not as a quietly lower request rate.
- **Workload:** the 122 orders in `Docs/Data/production_like_orders.json` (1 to 6 items, local and mail order), sent in
  the same order in every run, so default and experiments get identical input.
- **SLO** (per level, on the POST): p95 < 500 ms, p99 < 1 s, failures < 1%, no dropped iterations. Edit the
  thresholds in `route.js` and the constants in `analyze.py` together.

## How the experiments are switched

`run.sh` overwrites `src/OrderRouter.Api/experiments.json` in place (it hot-reloads), with
`loadtests/experiments/default.json` (all off) or `experiments.json` (Exp1, Exp2, Exp3, Exp4 on at 100% treatment), and
restores your original file when it ends. It then POSTs a sentinel order (ZIP `00098`) until the answer proves the
mode is active (`not_routed` under control, `partially_routed` with Exp3), and refuses to run otherwise.

- 100% treatment is deliberate. Assignment is per caller, and the dev key is one caller, so any other percentage
  would put the whole run in a single arm instead of splitting requests.
- If the API reads another copy of the file (for example `dotnet run`, which uses the copy in `bin/`), set
  `EXPERIMENTS_FILE=<that path>`.
- The response does not show the arm. To confirm a run used the intended arm, read `experiment_arms` from `routing_orders` or the `RoutingDecisionMade` log lines.

## Reading the results

- Compare **p95/p99**, not averages. Differences under about 10% at low load are noise; repeat the run before
  concluding anything.
- `dropped_iterations > 0` means the target rate was not sustained: the API (or k6, or the machine) is saturated. The
  level where it first appears is your capacity.
- The comparison also shows the business outcome mix. Experiments change it by design (Exp3 turns some `not_routed`
  into `partially_routed`), so a latency change may come with a different amount of work.

## Caveats

- **Run k6 and the API on different machines when you can.** On one laptop (the API in Docker) they share CPU, and
  the peak level can measure the laptop. Check `dropped_iterations` and CPU while it runs.
- **Every POST stores an order.** A full ladder writes tens of thousands of rows to the dev database. Reset it when
  done: `docker compose down -v`, then migrate and seed again (README, Getting started).
- Development environment: console logging and no response caching make absolute numbers pessimistic. Use the results
  to compare, not as production capacity.

## Results: 2026-11-16, with Exp4 (tie-breaking)

Full ladder (`loadtests/run.sh`) after adding the fourth experiment, `Exp4TieBreaking`. The "experiments" mode now has
Exp1 to Exp4 on. k6 and the API (Docker) ran on the same laptop, development configuration. POST `/api/v1/route`;
every level met the SLO in both modes (0 failures, 0 dropped iterations).

| level | mode | p95 | p99 | max |
|---|---|---|---|---|
| low | default | 67 ms | 92 ms | 140 ms |
| low | experiments | 65 ms | 81 ms | 122 ms |
| medium | default | 31 ms | 44 ms | 129 ms |
| medium | experiments | 30 ms | 43 ms | 109 ms |
| peak | default | 27 ms | 43 ms | 133 ms |
| peak | experiments | 28 ms | 57 ms | 322 ms |

- **No breaking point found.** Peak (100 req/s target) is the top of the ladder and had plenty of headroom (worst p99
  57 ms against a 1 s limit). Use `--profile stress` or a higher `PEAK_RPS` to find capacity.
- **Low is slower than medium and peak** (p95 about 65 ms against about 30 ms). Likely cold-start effects (JIT warm-up,
  connection setup), not a regression. It also has the fewest samples, so it is the noisiest.
- **Experiments cost little, and Exp4 adds nothing measurable.** p95 differences are within about 4% at every level.
  The one outlier is p99 at peak (+34%, 43 to 57 ms), the same pattern as the previous run before Exp4 existed
  (+34%, 84 to 112 ms), and `max` there is a single slow request. Repeat the run before concluding anything.
- **Outcome mix matches the design.** Both modes route 98.4% of orders. The remaining 1.6% is `not_routed` by default
  and `partially_routed` with Exp3 on. Exp4 does not change the status of an order, only which tied supplier wins, and
  the response does not show that; read `experiment_arms` from `routing_orders` or the decision log to see it.
- **`GET` read-back** p95 was 3 to 8 ms.
- **Reported req/s** (1.4, 7.1, 28.3) is about 28% of each target rate. This looks like an average over the whole run
  including ramps, and no iterations were dropped, but it was not checked against `summary.json`.

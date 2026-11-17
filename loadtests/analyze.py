#!/usr/bin/env python3
"""Builds a Markdown report from the k6 summaries in a results folder.

    analyze.py <results-dir>

Reads <dir>/default/summary.json and/or <dir>/experiments/summary.json (k6 --summary-export). With both present it
prints a side-by-side comparison per load level. Standard library only.
"""
import json
import sys
from pathlib import Path

LEVELS = ["smoke", "low", "medium", "peak", "stress"]
# The SLO the k6 thresholds use: POST p95 under 500 ms, p99 under 1 s, under 1% failures, no dropped iterations.
P95_MS, P99_MS, FAIL_RATE = 500, 1000, 0.01


def load(path):
    if not path.exists():
        return None
    return json.loads(path.read_text())["metrics"]


def levels_in(metrics):
    found = {k.split("level:")[1].split(",")[0].rstrip("}") for k in metrics if k.startswith("http_reqs{level:")}
    return [lv for lv in LEVELS if lv in found]


def stats(metrics, level):
    post = f"{{level:{level},endpoint:post}}"
    dur = metrics.get(f"http_req_duration{post}", {})
    reqs = metrics.get(f"http_reqs{post}", {})
    failed = metrics.get(f"http_req_failed{post}", {})
    total = failed.get("passes", 0) + failed.get("fails", 0)
    outcomes = {o: metrics.get(f"order_outcome{{level:{level},outcome:{o}}}", {}).get("count", 0)
                for o in ("routed", "partially_routed", "not_routed")}
    n = sum(outcomes.values()) or 1
    get = metrics.get(f"http_req_duration{{level:{level},endpoint:get}}", {})
    return {
        "rps": reqs.get("rate", 0.0),
        "reqs": reqs.get("count", 0),
        "avg": dur.get("avg"), "med": dur.get("med"), "p90": dur.get("p(90)"), "p95": dur.get("p(95)"),
        "p99": dur.get("p(99)"), "max": dur.get("max"),
        "fail": (failed.get("passes", 0) / total) if total else 0.0,
        "dropped": metrics.get(f"dropped_iterations{{level:{level}}}", {}).get("count", 0),
        "get_p95": get.get("p(95)"),
        "outcomes": {k: v / n for k, v in outcomes.items()},
    }


def fmt(v, unit="ms", digits=1):
    return "n/a" if v is None else f"{v:.{digits}f}{unit}"


def verdict(s):
    problems = []
    if s["p95"] is not None and s["p95"] >= P95_MS: problems.append(f"p95 >= {P95_MS}ms")
    if s["p99"] is not None and s["p99"] >= P99_MS: problems.append(f"p99 >= {P99_MS}ms")
    if s["fail"] >= FAIL_RATE: problems.append("failures >= 1%")
    if s["dropped"] > 0: problems.append(f"{int(s['dropped'])} dropped (load generator or API saturated)")
    return "OK" if not problems else "; ".join(problems)


def delta(a, b):
    if a in (None, 0) or b is None:
        return "n/a"
    return f"{(b - a) / a * 100:+.1f}%"


def main(out):
    root = Path(out)
    runs = {m: load(root / m / "summary.json") for m in ("default", "experiments")}
    runs = {m: v for m, v in runs.items() if v}
    if not runs:
        sys.exit(f"No summary.json found under {root}")

    print(f"# Load test report: {root.name}\n")
    print(f"SLO per level (POST /api/v1/route): p95 < {P95_MS} ms, p99 < {P99_MS} ms, failures < {FAIL_RATE:.0%}, no dropped iterations.\n")
    levels = [lv for lv in LEVELS if all(lv in levels_in(m) for m in runs.values())]

    for mode, metrics in runs.items():
        print(f"## {mode}\n")
        print("| level | req/s | avg | median | p90 | p95 | p99 | max | failed | dropped | GET p95 | verdict |")
        print("|---|---|---|---|---|---|---|---|---|---|---|---|")
        for lv in levels:
            s = stats(metrics, lv)
            print(f"| {lv} | {s['rps']:.1f} | {fmt(s['avg'])} | {fmt(s['med'])} | {fmt(s['p90'])} | {fmt(s['p95'])} | "
                  f"{fmt(s['p99'])} | {fmt(s['max'])} | {s['fail']:.2%} | {int(s['dropped'])} | {fmt(s['get_p95'])} | {verdict(s)} |")
        print()

    if len(runs) == 2:
        d, e = runs["default"], runs["experiments"]
        print("## Experiments vs default\n")
        print("Delta = (experiments - default) / default. Negative latency delta means experiments were faster. "
              "Differences under about 10% at low load are usually noise: check the run-to-run variance before drawing conclusions.\n")
        print("| level | req/s default | req/s exp | median | p95 | p99 | failed (def / exp) | dropped (def / exp) |")
        print("|---|---|---|---|---|---|---|---|")
        for lv in levels:
            a, b = stats(d, lv), stats(e, lv)
            print(f"| {lv} | {a['rps']:.1f} | {b['rps']:.1f} | {fmt(a['med'])} -> {fmt(b['med'])} ({delta(a['med'], b['med'])}) | "
                  f"{fmt(a['p95'])} -> {fmt(b['p95'])} ({delta(a['p95'], b['p95'])}) | "
                  f"{fmt(a['p99'])} -> {fmt(b['p99'])} ({delta(a['p99'], b['p99'])}) | "
                  f"{a['fail']:.2%} / {b['fail']:.2%} | {int(a['dropped'])} / {int(b['dropped'])} |")
        print("\n### Business outcome mix (same orders in the same order in both runs)\n")
        print("| level | mode | routed | partially routed | not routed |")
        print("|---|---|---|---|---|")
        for lv in levels:
            for mode, metrics in runs.items():
                o = stats(metrics, lv)["outcomes"]
                print(f"| {lv} | {mode} | {o['routed']:.1%} | {o['partially_routed']:.1%} | {o['not_routed']:.1%} |")
        print()


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])

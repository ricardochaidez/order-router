#!/usr/bin/env bash
# Runs the k6 load test against a running API and stores the results.
#
#   loadtests/run.sh [--profile ladder] [--mode both] [--base-url http://localhost:8080] [--out DIR]
#
#   --profile  smoke | low | medium | peak | stress | ladder (default ladder = low, medium and peak in one run)
#   --mode     default      all experiments off (the shipped behaviour)
#              experiments  Exp1 to Exp4 on at 100% treatment (loadtests/experiments/experiments.json)
#              both         default, then experiments, then the comparison (default)
#   --out      results folder (default loadtests/results/run-<timestamp>)
#
# Anything after `--` goes to k6 as is, for example: loadtests/run.sh --profile peak -- -e PEAK_RPS=150
# Rates and durations: LOW_RPS, MEDIUM_RPS, PEAK_RPS, STRESS_RPS, RAMP, HOLD (see loadtests/README.md).
#
# Experiments are switched by overwriting the API's experiments.json IN PLACE (it hot-reloads, so no restart). The
# original content is restored when the script ends, also on Ctrl+C. Point EXPERIMENTS_FILE elsewhere if the API reads
# another copy. With 100% treatment every request is in the treatment arm; at any other percentage the single dev-key
# caller would land in ONE arm for the whole run (assignment is per caller, not per request).
set -uo pipefail

cd "$(dirname "$0")/.."
profile=ladder
mode=both
base_url="${BASE_URL:-http://localhost:8080}"
out=""
k6_extra=()
experiments_file="${EXPERIMENTS_FILE:-src/OrderRouter.Api/experiments.json}"
reload_wait="${RELOAD_WAIT:-10}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --profile) profile="${2:?}"; shift ;;
    --mode) mode="${2:?}"; shift ;;
    --base-url) base_url="${2:?}"; shift ;;
    --out) out="${2:?}"; shift ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    --) shift; k6_extra=("$@"); break ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

case "$mode" in default|experiments|both) ;; *) echo "--mode must be default, experiments or both" >&2; exit 2 ;; esac

# The dev key comes from the environment, then .env, then the documented default.
api_key="${API_KEY:-}"
if [[ -z "$api_key" && -f .env ]]; then api_key="$(grep -E '^DEV_API_KEY=' .env | head -1 | cut -d= -f2-)"; fi
api_key="${api_key:-dev-only-change-me}"

for tool in k6 curl python3; do
  command -v "$tool" >/dev/null || { echo "$tool is required but not installed (brew install $tool)." >&2; exit 1; }
done
if ! curl -fsS -m 5 "$base_url/health" >/dev/null; then
  echo "The API does not answer at $base_url/health. Start it (docker compose up -d --wait) and make sure the database is migrated and seeded (README, Getting started)." >&2
  exit 1
fi

out="${out:-loadtests/results/run-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$out"
original_experiments="$(cat "$experiments_file")"
restore() { printf '%s\n' "$original_experiments" > "$experiments_file"; }
trap restore EXIT
trap 'exit 130' INT TERM

# POSTs the Exp3 sentinel order (ZIP 00098: no walker supplier). Control cannot route the walker (not_routed);
# Exp3 treatment routes the wheelchair and reports the walker as unfulfilled (partially_routed).
probe() {
  curl -s -m 10 -X POST "$base_url/api/v1/route" -H "X-Api-Key: $api_key" -H 'Content-Type: application/json' \
    -d '{"order_id":"loadtest-probe","customer_zip":"00098","mail_order":false,"items":[{"product_code":"WC-STD-001","quantity":1},{"product_code":"WK-STD-009","quantity":1}]}' \
    | python3 -c 'import json,sys; print(json.load(sys.stdin).get("status",""))' 2>/dev/null
}

apply_mode() { # $1 = default | experiments
  local want="not_routed"; [[ "$1" == experiments ]] && want="partially_routed"
  # In place (not a rename): a docker single-file mount follows the inode.
  cat "loadtests/experiments/$1.json" > "$experiments_file"
  echo "Applied '$1' experiments; waiting up to ${reload_wait}s for the API to reload..."
  local waited=0 got=""
  while (( waited < reload_wait )); do
    sleep 1; waited=$((waited + 1))
    got="$(probe)"
    [[ "$got" == "$want" ]] && { echo "Verified: probe order is '$got'."; return 0; }
  done
  echo "Could not verify mode '$1': the probe order is '${got:-no answer}', expected '$want'." >&2
  echo "Is the API reading $experiments_file? (the 00098 sentinel ZIP must be seeded; see README, Experiments)" >&2
  return 1
}

run_mode() { # $1 = default | experiments
  local label="$1" arm=control dir="$out/$1"
  [[ "$1" == experiments ]] && arm=treatment
  mkdir -p "$dir"
  apply_mode "$1" || return 1
  echo "== k6: profile=$profile mode=$label -> $dir"
  k6 run loadtests/route.js \
    -e "BASE_URL=$base_url" -e "API_KEY=$api_key" -e "PROFILE=$profile" -e "LABEL=$label" \
    --summary-export "$dir/summary.json" ${k6_extra[@]+"${k6_extra[@]}"} 2>&1 | tee "$dir/console.log"
  local rc=${PIPESTATUS[0]}
  # 99 = a threshold was crossed. That is a finding to analyse, not a failure to run.
  if (( rc != 0 && rc != 99 )); then echo "k6 failed with exit code $rc" >&2; return "$rc"; fi
}

modes=("$mode"); [[ "$mode" == both ]] && modes=(default experiments)
for m in "${modes[@]}"; do
  run_mode "$m" || exit 1
  [[ "$m" != "${modes[${#modes[@]}-1]}" ]] && { echo "Cooling down 30s between modes..."; sleep 30; }
done

python3 loadtests/analyze.py "$out" | tee "$out/report.md"
echo
echo "Results in $out (report.md, per-mode summary.json and console.log)."

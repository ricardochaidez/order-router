#!/usr/bin/env bash
# Runs the whole test suite from the command line (no AI involved) and stores the results in a dedicated folder:
#
#   test-results/run-<timestamp>/
#     summary.md            one row per recorded response (scenario, HTTP status, order status, suppliers, experiment arms)
#     responses/            the full request and response of each scenario, as JSON
#       default-route/        the three sample orders, every experiment off
#       experiments/exp1|exp2|exp3/   control and treatment side by side
#       experiments/combinations/     all 8 arm combinations over the four sentinel orders
#       all-tests/            every response captured by the acceptance tests
#     unit-tests.{trx,html}, integration-tests.{trx,html}   pass/fail result of every test
#     console-*.log         raw console output
#   test-results/latest     link to the newest run
#
# Usage: scripts/run-tests.sh [--no-db] [--only unit|integration]
#   --no-db   do not start PostgreSQL through docker compose (use it when ConnectionStrings__OrderRouter points elsewhere)
#   --only    run just one of the two test projects
# Exit code is 0 only when every test passed.
set -uo pipefail

cd "$(dirname "$0")/.."
start_db=true
only=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-db) start_db=false ;;
    --only) only="${2:-}"; shift ;;
    -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

run_dir="$PWD/test-results/run-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$run_dir"
ln -sfn "$run_dir" test-results/latest
export ORDERROUTER_RESULTS_DIR="$run_dir"

if [[ "$start_db" == true && "$only" != "unit" ]]; then
  echo "Starting PostgreSQL..."
  docker compose up -d --wait postgres || { echo "Could not start PostgreSQL. Start it yourself and use --no-db." >&2; exit 1; }
fi

status=0
run_project() {
  local name="$1" project="$2"
  echo "== $name tests =="
  dotnet run --project "$project" --configuration Release -- \
    -result-trx "$run_dir/$name-tests.trx" -result-html "$run_dir/$name-tests.html" -noColor 2>&1 | tee "$run_dir/console-$name.log"
  [[ ${PIPESTATUS[0]} -eq 0 ]] || status=1
}

[[ -z "$only" || "$only" == "unit" ]] && run_project unit tests/OrderRouter.Tests
[[ -z "$only" || "$only" == "integration" ]] && run_project integration tests/OrderRouter.Api.IntegrationTests

echo
echo "Results: $run_dir"
[[ -f "$run_dir/summary.md" ]] && echo "Summary: $run_dir/summary.md"
[[ $status -eq 0 ]] && echo "ALL TESTS PASSED" || echo "TESTS FAILED"
exit $status

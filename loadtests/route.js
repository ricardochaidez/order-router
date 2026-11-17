// k6 load test for POST /api/v1/route (and a read-back of what was stored).
//
//   k6 run loadtests/route.js -e PROFILE=ladder -e LABEL=default
//
// Normally started through loadtests/run.sh, which also switches the experiments, writes the results to a folder and
// builds the comparison. Environment (all optional):
//   BASE_URL   http://localhost:8080          API_KEY   dev key for X-Api-Key (Development environment only)
//   PROFILE    smoke|low|medium|peak|stress|ladder (default ladder)
//   READ_BACK_RATIO 0..1, share of created orders that are fetched with GET afterwards (default 0.1)
import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { SharedArray } from 'k6/data';
import { Counter } from 'k6/metrics';
import { buildScenarios } from './lib/profiles.js';

const BASE_URL = __ENV.BASE_URL ?? 'http://localhost:8080';
const API_KEY = __ENV.API_KEY ?? 'dev-only-change-me';
const PROFILE = __ENV.PROFILE ?? 'ladder';
const READ_BACK_RATIO = Number(__ENV.READ_BACK_RATIO ?? 0.1);

// 122 realistic orders: 1 to 6 items, local and mail order, rush and standard. Only the fields the API reads are sent.
const orders = new SharedArray('orders', () =>
  JSON.parse(open('../Docs/Data/production_like_orders.json')).map((o) => ({
    order_id: o.order_id,
    customer_zip: o.customer_zip,
    mail_order: o.mail_order,
    items: o.items,
  })),
);

const OUTCOMES = ['routed', 'partially_routed', 'not_routed'];
const outcome = new Counter('order_outcome');

const scenarios = buildScenarios(PROFILE);

// Thresholds double as the way to get a per-level breakdown into the summary: k6 only reports a tagged sub-metric
// when a threshold names it. They are pass/fail signals, not a gate (run.sh does not stop on them).
const thresholds = {
  checks: ['rate>0.99'],
};
for (const level of Object.keys(scenarios)) {
  // The SLO is on the POST (the routing work); the read-back GET is reported on its own.
  thresholds[`http_req_duration{level:${level},endpoint:post}`] = ['p(95)<500', 'p(99)<1000'];
  thresholds[`http_req_failed{level:${level},endpoint:post}`] = ['rate<0.01'];
  thresholds[`http_reqs{level:${level},endpoint:post}`] = ['count>=0'];
  thresholds[`http_req_duration{level:${level},endpoint:get}`] = ['p(95)<500'];
  thresholds[`dropped_iterations{level:${level}}`] = ['count>=0'];
  for (const o of OUTCOMES) {
    thresholds[`order_outcome{level:${level},outcome:${o}}`] = ['count>=0'];
  }
}

export const options = {
  scenarios,
  thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  // The tags of a request: which experiment mode this run is, so runs can be told apart in any k6 output.
  tags: { label: __ENV.LABEL ?? 'unlabelled' },
};

const headers = { 'Content-Type': 'application/json', 'X-Api-Key': API_KEY };

export function route() {
  // Deterministic order sequence: the default and experiments runs send the same orders in the same order.
  const iteration = exec.scenario.iterationInTest;
  const order = orders[iteration % orders.length];
  // A new order_id per request (POST is not idempotent; the server stores every call under a new id regardless).
  const body = JSON.stringify({ ...order, order_id: `${order.order_id}-${exec.scenario.name}-${iteration}` });

  const res = http.post(`${BASE_URL}/api/v1/route`, body, { headers, tags: { name: 'POST /api/v1/route', endpoint: 'post' } });

  let json = null;
  try {
    json = res.json();
  } catch (_) {
    // not JSON: the status check below fails
  }

  const ok = check(res, {
    'POST status is 201': (r) => r.status === 201,
    'POST has Location header': (r) => !!r.headers.Location,
    'POST body has a status': () => json !== null && OUTCOMES.includes(json.status),
  });
  if (!ok || json === null) {
    return;
  }

  outcome.add(1, { outcome: json.status });

  if (Math.random() < READ_BACK_RATIO) {
    const get = http.get(`${BASE_URL}${res.headers.Location}`, { headers, tags: { name: 'GET /api/v1/route/{id}', endpoint: 'get' } });
    check(get, { 'GET status is 200': (r) => r.status === 200 });
  }
}

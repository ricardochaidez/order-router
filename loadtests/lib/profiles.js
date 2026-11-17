// Load profiles. Every level is an open model (ramping-arrival-rate): k6 starts requests at a fixed rate whatever the
// latency, so a slow server shows up as high latency and dropped iterations instead of the test quietly slowing down.
// Rates are requests per second and can be overridden per machine: LOW_RPS, MEDIUM_RPS, PEAK_RPS, STRESS_RPS.
const num = (name, fallback) => Number(__ENV[name] ?? fallback);

export const RAMP = __ENV.RAMP ?? '15s'; // time to reach the level
export const HOLD = __ENV.HOLD ?? '90s'; // time spent at the level (steady state)
const GAP_SECONDS = 15; // quiet time between levels in a ladder, so one level does not bleed into the next

const LEVELS = {
  smoke: num('SMOKE_RPS', 1),
  low: num('LOW_RPS', 5),
  medium: num('MEDIUM_RPS', 25),
  peak: num('PEAK_RPS', 100),
  stress: num('STRESS_RPS', 250),
};

const toSeconds = (d) => {
  const m = /^(\d+)(s|m)$/.exec(d);
  if (!m) throw new Error(`Duration must look like 30s or 2m, got '${d}'`);
  return Number(m[1]) * (m[2] === 'm' ? 60 : 1);
};

const scenario = (name, rps, startTime) => ({
  executor: 'ramping-arrival-rate',
  startRate: 1,
  timeUnit: '1s',
  stages: [
    { target: rps, duration: RAMP },
    { target: rps, duration: HOLD },
  ],
  startTime,
  // Enough VUs to hold the rate even when latency climbs to ~2s; k6 reports a shortfall as dropped_iterations.
  preAllocatedVUs: Math.max(10, rps * 2),
  maxVUs: Math.max(50, rps * 10),
  tags: { level: name },
  exec: 'route',
});

// PROFILE=low|medium|peak|stress|smoke runs one level. PROFILE=ladder runs low, medium and peak back to back.
const PROFILES = {
  smoke: ['smoke'],
  low: ['low'],
  medium: ['medium'],
  peak: ['peak'],
  stress: ['stress'],
  ladder: ['low', 'medium', 'peak'],
};

export function buildScenarios(profile) {
  const names = PROFILES[profile];
  if (!names) {
    throw new Error(`Unknown PROFILE '${profile}'. Use one of: ${Object.keys(PROFILES).join(', ')}`);
  }

  const levelSeconds = toSeconds(RAMP) + toSeconds(HOLD);
  const scenarios = {};
  names.forEach((name, i) => {
    scenarios[name] = scenario(name, LEVELS[name], `${i * (levelSeconds + GAP_SECONDS)}s`);
  });

  return scenarios;
}

export const levelRates = LEVELS;

# OR-025: Write the README and update AI_PROMPTS.md

**Depends on:** [OR-023](OR-023-acceptance-tests.md), [OR-028](OR-028-get-stored-order.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) BR-20, BR-21, FR-13, SC-32, DEV-07, DEV-14; [ADR-0002](../ADRs/ADR-0002-reference-data-database.md) (Getting started); [ADR-0004](../ADRs/ADR-0004-response-contract-evolution.md)

## Description

Document how to build, run and test the service. Most important: after `docker compose up`, the database has no schema and no data until you run the integration tests, so `POST` and `GET` return 500 until then (SC-33). Also explain how to regenerate `Docs/Data/Seed` with the OR-003 generator. Also explain the dev key, the status codes, random ties, the alias and editing experiments. Keep `Docs/AI_PROMPTS.md` up to date with the prompts used.

## Acceptance criteria

### AC1: Getting started works

- **Given** a new developer following only the README
- **When** they copy `.env.example` to `.env`, run `docker compose up`, run the integration tests, then call `POST /api/v1/route` with the dev key
- **Then** they get a 201 routed response for ORD-001 and can read it back with `GET` on the `Location` URL

### AC2: Status codes explained

- **Given** the README
- **When** a reader looks for responses
- **Then** it explains 201/200/400/401/404/500 and that this differs from the original spec's always-200

### AC3: Behaviour notes

- **Given** the README
- **When** a reader looks for routing behaviour
- **Then** it says that on a full tie the lowest supplier IDs win (random only under the EXP-4 treatment, where the first response is authoritative), that `/api/route` is pinned to v1, that every POST (retries included) is stored under a new `id` that only the same caller can read back, and how to turn experiments on or off in `experiments.json`

### AC4: Prompts recorded

- **Given** `Docs/AI_PROMPTS.md`
- **When** a reviewer opens it
- **Then** it lists the significant prompts in the order they were used

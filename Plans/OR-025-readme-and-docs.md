# Plan: OR-025: Write the README and update AI_PROMPTS.md

**Ticket:** [Docs/Tickets/OR-025-readme-and-docs.md](../Docs/Tickets/OR-025-readme-and-docs.md)
**Depends on:** OR-023 (done: `tests/OrderRouter.Api.IntegrationTests/Controllers/V1/*`), OR-028 (done: `GET /api/v1/route/{id}`).
**References:** Technical Requirements BR-20, BR-21, FR-04, FR-13, FR-19, FR-21, SC-18, SC-29, SC-31, SC-32, SC-33, DEV-07, DEV-14, NFR-01; [ADR-0002](../Docs/ADRs/ADR-0002-reference-data-database.md) (Getting started); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md); [ADR-0007](../Docs/ADRs/ADR-0007-routing-decision-persistence.md); `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`.

> **Save location:** Phase 0 copies this plan to `Plans/OR-025-readme-and-docs.md`. Update that file as each phase is done.

## Context

BR-20 asks for a README that explains how to build, run and test the service; BR-21 asks for `Docs/AI_PROMPTS.md` with the significant prompts in the order used. The current [README.md](../README.md) only covers Docker startup, the caller-hash secret and authentication. It is missing: the "tests must run before POST/GET work" warning stated as a required step with its 500 consequence (SC-33), regenerating `Docs/Data/Seed` with the OR-003 generator, status codes and the deviation from the spec's always-200 (DEV-07, DEV-14), random ties and "first response is authoritative" (FR-13, SC-18), the `/api/route` alias pinned to v1 (ADR-0004), "every POST is a new `id` readable only by its caller" (SC-29, FR-21, SC-31), and how to edit `experiments.json`.

**Source of truth is the code, not the tickets or older plans.** Refactors landed after the tickets were implemented (controller-based API: `RouteController`, `HealthController`; interface and file reorganization). Plans may be read for intent, but every statement in the README must be checked against the current code. If code and a ticket/ADR disagree, document the code and flag the difference to the engineer (stop and ask if it looks like a bug).

**Scope:** documentation only (`README.md`, `Docs/AI_PROMPTS.md`). No production or test code changes. The README documents API functionality; the OR-024 experiment-combination matrix (uncommitted test-only work in the tree) is not described.

### Facts already confirmed in the code (re-check in Phase 1)

| Topic | Where | Fact |
|---|---|---|
| Endpoints | `src/OrderRouter.Api/Controllers/V1/RouteController.cs` | `POST /api/v1/route` and `POST /api/route` are attribute routes on the same action; alias is POST-only; `Location` always points at `GET /api/v1/route/{id}` (relative). |
| GET | same | 200 for the caller's own order; one identical 404 ProblemDetails for unknown id, non-UUID id, another caller's order. |
| POST codes | same + `RouteEnvelopeAttribute` | 201 + `Location`; 400 in the route envelope (all validation errors); 500 ProblemDetails. 401 + `WWW-Authenticate` from auth. |
| Health / docs | `Controllers/HealthController.cs`, `Program.cs` | `/health` anonymous; OpenAPI `/openapi/v1.json` and Scalar `/scalar` Development-only. |
| Dev key | `.env.example`, `docker-compose.yml` | `DEV_API_KEY` → `X-Api-Key`; Development only; `CALLER_HASHING_SECRET` required. |
| DB for tests | `tests/OrderRouter.Api.IntegrationTests/Infrastructure/ReferenceDataFixture.cs` | Env var `OrderRouterDataContextFactory.ConnectionStringVariable`, else `DefaultConnectionString` (same DB as compose). |
| Generator | `tools/OrderRouter.DataNormalizer/Program.cs`, `ToolPaths.cs` | Defaults `Docs/Data` → `Docs/Data/Seed`; `--input`/`--output` overrides; exit 0/1; writes nothing on failure. |
| Experiments | `src/OrderRouter.Api/experiments.json`, compose bind mount | `Enabled`, `TreatmentPercentage`, `Salt`, `Targeting` (Callers, ZipPrefixes, ZipRanges), `Parameters`; `Routing.SimilarityBand`; polling file watcher; edit the file in place. |

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before starting. The `ddd-implementation` skill is for new domain behaviour; this ticket changes no code, so state in the Phase 1 result that it was skipped and why. README text must match `CLAUDE.md` REST conventions (status codes, Problem Details, `/api/v1`, UUIDv7 ids, auth on everything but health).
   - **Docs style:** plain short sentences, same tone as the current README; cite requirement/ADR ids (`SC-33`, `ADR-0004`) and link ADRs; commands in fenced blocks that can be copy-pasted from the repo root.
   - **Data:** never edit `Docs/Data/*` (running the generator in Phase 2 must leave `Docs/Data/Seed` unchanged; check with `git status`).
   - **Never invent facts.** Every behaviour, path, header, env var and command in the README must be verified in code or by running it.
2. **Tests must pass after each phase.** Before marking a phase done, from the repo root (PostgreSQL running: `docker compose up -d postgres`):
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: everything passes. If a failure is in the uncommitted OR-024 test files, stop and ask; don't fix it under OR-025.
   - Docs checks: every relative link in the changed Markdown files resolves to an existing file; every command written in the phase was run once and its output matches what the README says.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-025-readme-and-docs.md`, tick its checklist, and add a short **Result** line (test counts, deviations, open points).
4. **Ask before the next phase.** After marking a phase done, stop, summarize the changes and test results, and ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** Known risks:
   - code behaviour that differs from a ticket, ADR or the requirements (e.g. a status code, the alias, the 404 rule);
   - the AC1 walk-through not giving 201 for ORD-001 or the `GET` not returning it;
   - the generator changing `Docs/Data/Seed`;
   - what happens when `experiments.json` is invalid after a reload (document only what the code does).
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `docs/OR-025-readme-and-docs`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-025-readme-and-docs.md` with this plan.

**Verify:** the file exists. No code changes, so no tests.

**Result:** Plan saved to `Plans/OR-025-readme-and-docs.md`. No code changed, so no tests were run.

---

## Phase 1: Fact inventory from the code ✅ Done

Read-only. Builds the list of facts the README will state, so Phases 2–3 only write verified text.

- [x] Re-check every row of *Facts already confirmed* against the code.
- [x] Read and note, from the code: `Startup.cs`, auth extension and `Security/*`, request view models and `ValidationErrorFormatter`/`RouteEnvelopeInvalidModelStateResponse`, response view models, the tie-breaker, experiment options and assigner, `OrderRouter.Api.http`.
- [x] Note the actual `dotnet test` filters available and the connection-string env var name.
- [x] List any code ↔ ticket/ADR/existing-README differences and present them to the engineer.
- [x] Draft the new README outline (section headings only).

**Result:** `dotnet build` 0 warnings, 0 errors. `dotnet test` 1540 passed, 0 failed (unit + integration). `git status` clean: no files changed besides this plan. The `ddd-implementation` skill was skipped: documentation only, no domain behaviour changes.

**Facts confirmed in the code**

- **Endpoints:** `RouteController` has `POST /api/v1/route` and `POST /api/route` on one action; `GET /api/v1/route/{id}` is v1 only (no GET alias). `Location` is a relative path to the v1 GET. GET parses the id with `Guid.TryParseExact(id, "D")` after auth; a bad id, unknown id or another caller's order all give one 404 ProblemDetails ("Order not found").
- **Status codes:** POST 201 (+`Location`), 400 in the route envelope (`status: not_routed`, `feasible: false`, `errors: [...]`, all errors sorted in request order; malformed JSON also 400 in the envelope), 500 ProblemDetails. 401 + `WWW-Authenticate` from the auth middleware. "Could not route" is a 201 with `status` `not_routed`/`partially_routed` (`feasible` is `true` only for `routed`).
- **Health:** `GET /health` is anonymous, plain text `Healthy`/`Unhealthy`, 200/503 (`HealthController`, `HealthStatusMapper`).
- **Docs:** OpenAPI `/openapi/v1.json` and Scalar `/scalar` mapped in Development only, anonymous.
- **JSON:** snake_case properties and enum strings. Request fields: `order_id`, `customer_zip` (exactly 5 digits), `mail_order` (optional), `items[]` (`product_code`, `quantity` >= 1, at least one item).
- **Auth:** JWT bearer (`Authentication:Jwt:Authority`/`Audience`, required outside Development); dev key header `X-Api-Key`, read from `Authentication:DevApiKey` (compose maps `DEV_API_KEY`), only registered in Development.
- **Diagnostics:** `Routing:ExposeDiagnostics` (default `false` in `appsettings.Development.json`) adds a `diagnostics` object to POST and GET responses.
- **Tie-break:** `UniformRandomTieBreaker` picks uniformly via `IRandomSource` only when 2+ plans tie; a single plan is deterministic.
- **Experiments:** `experiments.json` is an optional JSON source with `reloadOnChange`; compose sets `DOTNET_USE_POLLING_FILE_WATCHER`. An invalid reload is rejected: the last-known-good snapshot keeps serving and an Error is logged (`Experiment configuration rejected; keeping the last known good ...`). An invalid file at startup stops the host (`OptionsValidationException`). Reloadable: the `Experiments` section and `Routing:SimilarityBand`. `Routing:NationwideZipRange` is not reloadable.
- **Tests DB:** env var `ConnectionStrings__OrderRouter`, else `Host=localhost;Port=5432;Database=orderrouter;Username=orderrouter;Password=orderrouter-dev-only` (the compose database).
- **Test projects:** `tests/OrderRouter.Tests` (unit) and `tests/OrderRouter.Api.IntegrationTests` (needs PostgreSQL; its `ReferenceDataFixture` creates nothing itself but seeds via `ReferenceDataSeeder`). Runner is Microsoft.Testing.Platform (`global.json`). I did not find a separate migration step in what I read; Phase 2 must confirm what creates the schema (tests) by running the fresh-volume walk-through.
- **Generator:** `dotnet run --project tools/OrderRouter.DataNormalizer`; defaults `Docs/Data` → `Docs/Data/Seed`; `--input`/`--output`; exit 0/1; logs skipped rows; writes nothing on failure.

**Differences between the plan/tickets and the code (documented as the code is)**

1. `HealthController` is at `src/OrderRouter.Api/Controllers/V1/HealthController.cs` and its route is `/health` (not under `/api/v1`), not `Controllers/HealthController.cs`.
2. `Program.cs` is thin; the OpenAPI/Scalar mapping and pipeline live in `Startup.cs`. Auth code lives in `Security/` and `Extensions/AuthenticationServiceCollectionExtensions.cs`, not `Authentication/*`.
3. Request/response models are in `ViewModels/Request|Response`; the 400 formatter is in `Filters/`, not `Routing/`.
4. The plan says the OR-024 matrix is uncommitted; `git status` is clean, so it is already committed. It is still not described in the README.
5. The existing README says the dev key example is `GET /api/v1/route`; that URL is a 404 route (GET needs an id). Phase 2 will fix the example.
6. The existing README says 201 only; it needs the 200/400/404/500 table (Phase 3).

None looks like a bug in the code.

**Draft README outline**

1. order-router (intro, links to requirements and ADRs)
2. Prerequisites
3. Getting started (AC1): env file → compose → `dotnet test` (required, SC-33) → POST → GET
4. Build, test and format (unit vs integration, connection-string override, `localsetup.sh`)
5. Configuration: caller-hash secret, dev key, `dotnet run` and user-secrets
6. Regenerating the seed data
7. Authentication
8. API reference: endpoints and status codes (AC2)
9. Behaviour notes: random ties, `/api/route` alias, stored orders, diagnostics (AC3)
10. Experiments: editing `experiments.json`, invalid edits, Docker bind-mount note
11. AI prompts (link to `Docs/AI_PROMPTS.md`) and further reading

---

## Phase 2: README — getting started, build, test, seed data (AC1) ✅ Done

- [x] Prerequisites (.NET SDK from `global.json`, Docker).
- [x] Getting started, numbered, in the exact AC1 order: `cp .env.example .env` → `docker compose up -d --build --wait` → `dotnet test` (**required**: creates the schema and seeds; until then `POST`/`GET` return 500 while `/health` is Healthy, SC-33) → `POST /api/v1/route` with `X-Api-Key` and ORD-001 from `Docs/Data/sample_orders.json` → `GET` the `Location` URL. Copy-paste `curl` examples.
- [x] Build and test: `dotnet build`, `dotnet test` (unit vs integration, DB connection override env var), `dotnet format --verify-no-changes`, `localsetup.sh`.
- [x] Regenerating `Docs/Data/Seed`: when (source CSVs change), command (`dotnet run --project tools/OrderRouter.DataNormalizer`, `--input`/`--output`), what is logged (skipped rows with row number and reason), failure = nothing written, commit the output, then re-run `dotnet test` to upsert (no deletes, OQ-24).
- [x] Keep the existing caller-hash secret and `dotnet run`/user-secrets notes, merged into the new structure.
- [x] **AC1 walk-through:** ask the engineer before `docker compose down -v` (it deletes the local DB volume); then follow only the README from a fresh volume: confirm POST/GET 500 before tests, 201 for ORD-001 after, and 200 on `GET` of `Location`. Run the generator and confirm `git status` shows no change in `Docs/Data/Seed`.

**Verify:** build + test + walk-through output recorded in Result.

**Result:** `dotnet build` 0 warnings, 0 errors; `dotnet test` 1540 passed (1185 unit + 355 integration). Only `README.md` and this plan changed.

**AC1 walk-through (engineer approved `docker compose down -v`):** from a fresh volume, following only the README: `/health` 200 `Healthy` but `POST` and `GET` 500 before the tests; after `dotnet test`, `POST` of ORD-001 gave `201` with a `Location` header, `routed`, `feasible:true`, `SUP-0810`; `GET` on the `Location` gave `200`. The generator exited 0 and `git status` showed no change in `Docs/Data/Seed`. Also verified: `dotnet test --project <path>` for each project; unit tests pass with an unreachable database; integration tests fail with the "PostgreSQL is not reachable" message when the connection string is wrong.

**Deviations and open points**
- `.env` was replaced by a fresh copy of `.env.example` for the walk-through (the old one lacked `CALLER_HASHING_SECRET`, so the API crashed at start). Old copy kept in the session scratchpad.
- The plan's "SDK from `global.json`" does not match the code: `global.json` only selects the test runner. The README says "the .NET 10 SDK".
- `docker compose up --wait` can return before the API accepts requests (the `api` service has no healthcheck); the README says so.
- The generator also logs removed duplicate products (5 in the current data), not only skipped rows; the README says so.
- **Open:** `dotnet format --verify-no-changes` fails on import ordering in three existing files under `tools/OrderRouter.DataNormalizer` (`Program.cs`, `Csv/CsvSource.cs`, `Output/NormalizedCsvWriter.cs`). Not caused by OR-025 and outside its scope, so the command is not in the README.

---

## Phase 3: README — API reference and behaviour notes (AC2, AC3) ✅ Done

- [x] **Authentication:** JWT bearer in production (`Authentication:Jwt:Authority`/`Audience`), dev key in Development only; `/health` anonymous.
- [x] **Endpoints and status codes (AC2):** table for `POST /api/v1/route`, `POST /api/route`, `GET /api/v1/route/{id}`, `GET /health`: 201 (+`Location`), 200, 400 (all errors at once, route envelope), 401 (+`WWW-Authenticate`), 404 (same response for any id the caller can't see), 500 (ProblemDetails, no internals). State that this differs from the original spec's always-200 (DEV-07, DEV-14), and that "could not route" is a 201 with `feasible:false`, not an error. Point to OpenAPI/Scalar in Development.
- [x] **Behaviour notes (AC3):**
  - Full tie → supplier picked uniformly at random; a retry can pick another; the first response is authoritative (FR-13, SC-18).
  - `/api/route` is a POST-only alias pinned to v1 forever; new callers use `/api/v1/route` (ADR-0004).
  - Every POST, retries included, is stored under a new `id`; only the same caller can read it back; others get 404 (SC-29, FR-21, SC-31).
  - Experiments: how to turn each on/off and target it in `src/OrderRouter.Api/experiments.json` (`Enabled`, `TreatmentPercentage`, `Targeting`, `Parameters`), no restart needed, edit the file in place under Docker (bind-mount inode note), what happens with an invalid edit (per Phase 1 finding); link ADR-0005.
- [x] Short "Further reading" section linking requirements, ADRs, tickets.

**Verify:** build + test; run each `curl` example once (401 without key, 400 with a bad body, 404 with a random UUID, alias POST) and confirm the README matches.

**Result:** `dotnet build` 0 warnings, 0 errors; `dotnet test` 1540 passed. Only `README.md` and this plan changed. All relative links resolve.

**Commands run against the live Docker stack and matched to the README:** `POST` without key and with a wrong key: `401` + `WWW-Authenticate: Bearer`. `POST` with `{"order_id":"ORD-BAD","items":[]}`: `400` with both errors in the envelope. Malformed JSON: `400`, same envelope. `GET` of a random UUID and of `abc`: identical `404` Problem Details. `POST /api/route`: `201` with a `Location` on `/api/v1/route/{id}`; `GET /api/route/abc`: `404`. Unknown product: `201`, `feasible:false`, `UNKNOWN_PRODUCT`. Two identical `POST`s: two different `Location` ids.

**Experiments reload (edited `experiments.json` in place under Docker, then restored it; `git status` shows it unchanged):** `TreatmentPercentage: 150` was rejected within ~8 s with the log `Experiment configuration rejected; keeping the last known good config_version=...`; a valid edit logged `Experiment configuration applied`.

**Not run:** the "another caller gets 404" case (needs a second JWT identity; it is covered by the integration tests). The tie-randomness note is taken from `UniformRandomTieBreaker` and the tests, not from a live run.

**Open points:** the README recommends `docker compose up -d --force-recreate api` to re-attach a detached `experiments.json` mount. I did not reproduce a detached mount; the compose file's own comment says it stays detached "until the container restarts".

---

## Phase 4: `Docs/AI_PROMPTS.md` (AC4) ✅ Done

- [x] Keep the existing prompts and their order; leave the **Questions** section untouched.
- [x] Using git history, the plans and the code, list prompts that seem missing (e.g. the controller-based API refactor, organizing interfaces/files, experiment testing, this OR-025 run) and show the list to the engineer. **Stop and ask** for the verbatim wording; never write prompt text the engineer didn't provide.
- [x] Insert the confirmed prompts in chronological order with the same format (`-- <model>` + `## <title>` + prompt).
- [x] Link `Docs/AI_PROMPTS.md` from the README.

**Verify:** build + test; the file renders and the order matches git history.

**Result:** The engineer chose to add no new prompts, so `Docs/AI_PROMPTS.md` is unchanged (existing prompts and Questions untouched). It is linked from the README "Further reading". AC4 is met only for the prompts already listed. The candidates not recorded: controller-based API refactor, interface and file reorganization, experiment testing, the docs plan and update, and this OR-025 run.

---

## Phase 5: Final review ⬜ Not started

- [ ] Re-read the README end to end against AC1–AC4 and tick each AC in the Result.
- [ ] Check every relative link and every command one last time.
- [ ] `git status` shows only `README.md`, `Docs/AI_PROMPTS.md` and the plan changed by OR-025.

**Verify:** `dotnet build`, `dotnet test` all green; result summary with AC checklist.


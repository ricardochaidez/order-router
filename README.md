# order-router

Order Orchestration Engine for a DME healthcare logistics platform. It takes an order, assigns each line item to a supplier, and stores the decision so it can be read back.

Everything below is for local development only.

## Prerequisites

- The .NET 10 SDK (the Docker image uses 10.0.404).
- Docker with Docker Compose.

## Getting started

Run these from the repository root, in this order.

1. Copy the dev-only settings: `cp .env.example .env`
2. Start PostgreSQL and the API:

   ```bash
   docker compose up -d --build --wait
   ```

   The API listens on `http://localhost:8080` (change it with `API_PORT` in `.env`). `--wait` can return a second or two before the API accepts requests; if the next call is refused, try again.
3. Run the tests. **This step is required.**

   ```bash
   dotnet test
   ```

   Neither `docker compose` nor the API creates the database schema or loads the reference data. Only the integration tests do (they apply the migrations and seed suppliers and products, [SC-33](Docs/BusinessRequirements/Order-Router-Technical-Requirements.md)). Until they have run, `POST` and `GET` on the routing endpoints return `500`, even though `GET /health` says `Healthy`, because `/health` only checks that PostgreSQL is reachable. The tests use the same database as the API, so running them prepares the API's database.
4. Route the order `ORD-001` from `Docs/Data/sample_orders.json`. The dev key comes from `DEV_API_KEY` in `.env`:

   ```bash
   curl -i -X POST http://localhost:8080/api/v1/route \
     -H "X-Api-Key: dev-only-change-me" \
     -H "Content-Type: application/json" \
     -d '{"order_id":"ORD-001","customer_zip":"10015","mail_order":false,"items":[{"product_code":"WC-STD-001","quantity":1},{"product_code":"OX-PORT-024","quantity":1}]}'
   ```

   You get `201 Created`, a `Location` header such as `/api/v1/route/01a1e702-39d4-7286-a7fc-e48b6201faea`, and a body with `"status":"routed"`, `"feasible":true` and supplier `SUP-0810` for both items.
5. Read it back with the `Location` value from step 4:

   ```bash
   curl -i http://localhost:8080/api/v1/route/<id> -H "X-Api-Key: dev-only-change-me"
   ```

   You get `200 OK` with the stored order and the same decision.

To stop everything, run `docker compose down`. To also delete the local database (you must then run `dotnet test` again), run `docker compose down -v`. `./localsetup.sh` runs `docker compose down` and then `docker compose up -d --wait`.

`/health` only checks that PostgreSQL is reachable, so an empty database is healthy. If PostgreSQL is not reachable when the API starts, the API exits with an error. In Development the OpenAPI document is at `http://localhost:8080/openapi/v1.json`.

## Build and test

```bash
dotnet build
dotnet test
```

- `tests/OrderRouter.Tests` holds the unit and architecture tests. It does not need a database.
- `tests/OrderRouter.Api.IntegrationTests` holds the endpoint and persistence tests. It needs PostgreSQL (`docker compose up -d postgres` is enough) and seeds it before the first test.
- By default the integration tests connect to the compose database: `Host=localhost;Port=5432;Database=orderrouter;Username=orderrouter;Password=orderrouter-dev-only`. To use another database, set the `ConnectionStrings__OrderRouter` environment variable. If PostgreSQL is not reachable the test run fails; it is never skipped.
- To run one project: `dotnet test --project tests/OrderRouter.Tests` or `dotnet test --project tests/OrderRouter.Api.IntegrationTests`.

## Configuration

The caller is stored only as an HMAC hash, so the API needs a secret. Without it the API refuses to start.

- **Docker:** `CALLER_HASHING_SECRET` in `.env` (the `.env.example` value is a dev-only placeholder). If you copied `.env.example` before this setting existed, add the line to your `.env` and run `docker compose up -d --force-recreate api`.
- **`dotnet run`:** `dotnet user-secrets set "CallerHashing:Secret" "<value>" --project src/OrderRouter.Api`

## Regenerating the seed data

The API never reads the raw CSVs. It reads the database, which the tests fill from the committed files in `Docs/Data/Seed`. Regenerate those files only when `Docs/Data/suppliers.csv` or `Docs/Data/products.csv` change:

```bash
dotnet run --project tools/OrderRouter.DataNormalizer
```

- It reads `Docs/Data` and writes `Docs/Data/Seed`. Use `--input <dir>` and `--output <dir>` to change either.
- It logs each skipped row with its row number and reason, and each duplicate product it removed (the first row is kept), then a count per file.
- Exit code is `0` on success and `1` on failure. On failure nothing is written.
- The output is deterministic: with unchanged CSVs, the files in `Docs/Data/Seed` do not change.
- After a change, commit the new files and run `dotnet test` again to load them. The seed is an upsert: it adds and updates rows but never deletes rows that were removed from the CSVs.

## Authentication

Every endpoint except `GET /health` (and, in Development, the OpenAPI document and `/scalar`) requires authentication. A missing or invalid credential returns `401` with a `WWW-Authenticate: Bearer` header, before anything else is checked.

- **Production:** send a JWT bearer token (`Authorization: Bearer <token>`). Configure `Authentication:Jwt:Authority` and `Authentication:Jwt:Audience`; the API refuses to start outside Development without them.
- **Development only:** send the key from `DEV_API_KEY` (see `.env.example`) in the `X-Api-Key` header. It is read from `Authentication:DevApiKey` (environment variable or `dotnet user-secrets`), never from a committed file, and is ignored in any other environment.

## API

JSON property names are `snake_case`. Unknown request fields are ignored. In Development, the full contract with examples is in the OpenAPI document (`/openapi/v1.json`) and the Scalar UI (`/scalar`).

| Method and path | Purpose |
|---|---|
| `POST /api/v1/route` | Route an order and store the decision. |
| `POST /api/route` | Alias of the endpoint above, pinned to v1 (see [Behaviour notes](#behaviour-notes)). |
| `GET /api/v1/route/{id}` | Read back a stored order, exactly as decided. |
| `GET /health` | Anonymous. Plain text `Healthy` (`200`) or `Unhealthy` (`503`): checks only that PostgreSQL is reachable. |

Request body of `POST`:

| Field | Rule |
|---|---|
| `order_id` | Required, not blank. |
| `customer_zip` | Required, exactly 5 digits. |
| `mail_order` | Optional, defaults to `false`. |
| `items` | Required, at least one item: `product_code` (required) and `quantity` (required, at least 1). |

### Status codes

This differs from the original spec, which answered `200` for everything ([DEV-07, DEV-14](Docs/BusinessRequirements/Order-Router-Technical-Requirements.md)). Each outcome now has its own HTTP status.

| Status | When | Body |
|---|---|---|
| `201 Created` | `POST` with a valid request, including when the order could not be routed. | The routing decision, with the new `id`. The `Location` header is `/api/v1/route/{id}`, also for the alias. |
| `200 OK` | `GET /api/v1/route/{id}` for an order the caller created. | The stored request and the same decision. |
| `400 Bad Request` | `POST` with an invalid or malformed body. | The routing envelope with `"status":"not_routed"`, `"feasible":false` and every validation error in `errors`, all at once. |
| `401 Unauthorized` | No credential, or an invalid one. | `WWW-Authenticate` header. |
| `404 Not Found` | `GET` of an `id` that does not exist, is not a UUID, or belongs to another caller. | One identical Problem Details response for all three, so existence is never revealed. |
| `500 Internal Server Error` | An unexpected fault, for example the database schema is missing (see [Getting started](#getting-started)). | Problem Details with no internal details. |

"Could not route" is a business outcome, not an error. A valid order for an unknown product returns `201` with `"feasible":false`, `"status":"not_routed"` and the reason in `unfulfilled_items`. `status` is `routed`, `partially_routed` or `not_routed`, and `feasible` is `true` only for `routed`.

Example of a `400`:

```bash
curl -i -X POST http://localhost:8080/api/v1/route \
  -H "X-Api-Key: dev-only-change-me" -H "Content-Type: application/json" \
  -d '{"order_id":"ORD-BAD","items":[]}'
```

```json
{"feasible":false,"status":"not_routed","routing":[],"unfulfilled_items":[],"warnings":[],"errors":["customer_zip is required.","items must contain at least one item."]}
```

## Behaviour notes

- **Ties are random.** When several supplier plans are equal on every rule, one is picked uniformly at random ([FR-13](Docs/BusinessRequirements/Order-Router-Technical-Requirements.md)). Sending the same order again can therefore give a different supplier. The first response is the authoritative one: it is the one stored and returned by `GET`. When there is no tie, the same input always gives the same result.
- **`/api/route` is pinned to v1.** It is a `POST`-only alias with the same handler, validation and contract as `/api/v1/route`, and it never changes. There is no `GET` under `/api/route`. New callers should use `/api/v1/route` ([ADR-0004](Docs/ADRs/ADR-0004-response-contract-evolution.md)).
- **Every POST is a new order.** Each `POST`, retries included, is stored under a new server-generated `id` (UUIDv7). Nothing is de-duplicated by `order_id`. Only the caller that created an order can read it back; anyone else gets `404` ([ADR-0007](Docs/ADRs/ADR-0007-routing-decision-persistence.md)). The caller is stored only as an HMAC hash.
- **Diagnostics are optional.** With `Routing:ExposeDiagnostics` set to `true`, the `201` and `GET` bodies also carry a `diagnostics` object (experiment arms and search mode). It is `false` by default, is read at startup (a change needs a restart), and clients must not depend on it.

## Experiments

Three experiments can change routing for a share of callers. They are configured in [src/OrderRouter.Api/experiments.json](src/OrderRouter.Api/experiments.json) and all start **disabled**. See [ADR-0005](Docs/ADRs/ADR-0005-experiment-assignment.md) for how callers are assigned.

| Key | What the treatment does | Parameter |
|---|---|---|
| `Exp1ConsolidationVsQuality` | Allows one extra shipment when the quality gain is large enough. | `QualityUpliftThreshold` |
| `Exp2UnratedSuppliers` | Gives unrated suppliers a starting rating. | `UnratedPrior` |
| `Exp3PartialRouting` | Routes the items it can instead of failing the whole order. | none |

For each experiment:

- `Enabled`: `true` turns it on, `false` turns it off. A disabled experiment always uses the control behaviour.
- `TreatmentPercentage`: 0 to 100, the share of targeted callers in the treatment arm. `Salt` must not be empty; each experiment has its own, so assignments are independent.
- `Targeting`: limits who is eligible. `Callers` lists caller IDs, `ZipPrefixes` lists ZIP prefixes of 1 to 5 digits, and `ZipRanges` lists inclusive ranges such as `"10000-14999"`. An empty list means everyone.
- `Routing:SimilarityBand` in the same file is reloaded with the rest.

Changes apply without a restart or a deploy:

- Edit the file and save. The API picks up the change within a few seconds (under Docker it polls for changes).
- **Under Docker, edit the file in place.** It is mounted into the container as a single file, which follows the file's inode. An editor or command that replaces the file (for example `sed -i`, or an editor that writes a temp file and renames it) detaches the mount until the container is recreated (`docker compose up -d --force-recreate api`).
- **An invalid edit is rejected.** The API logs an error (`Experiment configuration rejected; keeping the last known good ...`) and keeps serving the last valid configuration. An invalid file at startup stops the API from starting.
- Other `Routing` settings, such as `NationwideZipRange` and `ExposeDiagnostics`, are not reloaded.

Example: turn on `Exp3PartialRouting` for everyone.

```json
"Exp3PartialRouting": {
  "Enabled": true,
  "TreatmentPercentage": 100,
  "Salt": "exp3",
  "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] }
}
```

## Further reading

- [Requirements](Docs/BusinessRequirements/Order-Router-Technical-Requirements.md)
- [Architecture decision records](Docs/ADRs/README.md)
- [Tickets](Docs/Tickets/README.md)
- [AI prompts used to build this service](Docs/AI_PROMPTS.md)

# order-router

Order Orchestration Engine for a DME healthcare logistics platform.

## Run locally with Docker

Everything below is development only.

1. Copy the dev-only settings: `cp .env.example .env`
2. Start PostgreSQL and the API: `docker compose up -d --build --wait`
3. The API listens on `http://localhost:8080` (change it with `API_PORT` in `.env`).
4. Check it: `curl -i http://localhost:8080/health` returns `200 Healthy` when PostgreSQL is reachable and `503 Unhealthy` when it is not. The OpenAPI document is at `http://localhost:8080/openapi/v1.json`.
5. Create and seed the reference data by running the integration tests: `dotnet test`. Neither the API nor `docker compose` migrates or seeds the database, so a fresh volume has no tables until you do this.

`/health` only checks that PostgreSQL is reachable, so an empty database is healthy. If PostgreSQL is not reachable when the API starts, the API exits with an error.

# OR-007: Containerize the API and add the health endpoint

**Depends on:** [OR-004](OR-004-database-schema.md)  
**References:** [ADR-0002](../ADRs/ADR-0002-reference-data-database.md) (Health); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-17, NFR-06, NFR-07, SC-23

## Description

Add a multi-stage Dockerfile on the .NET 10 runtime image that runs as a non-root user, and add the API service to `docker-compose`. The compose file runs the API with `ASPNETCORE_ENVIRONMENT=Development`, so the development key (OR-008) works locally; production never uses this file. The API never applies migrations (SC-33). Add an anonymous `GET /health` that checks **only that dependencies (PostgreSQL) are reachable**. Startup and health never inspect the data, so an empty database is healthy.

## Acceptance criteria

### AC1: Stack starts

- **Given** a clean machine with Docker
- **When** I run `docker compose up`
- **Then** PostgreSQL and the API start and the API listens on the port documented in the README

### AC2: Non-root

- **Given** the running API container
- **When** I check the process user
- **Then** it is not root

### AC3: Healthy

- **Given** PostgreSQL is reachable
- **When** I call `GET /health` with no credentials
- **Then** the response is 200 Healthy

### AC4: Database down

- **Given** PostgreSQL is stopped
- **When** I call `GET /health`
- **Then** the response is 503 Unhealthy

### AC5: Empty data is still healthy

- **Given** a reachable database that is empty or not yet migrated
- **When** I call `GET /health`
- **Then** the response is 200 Healthy

### AC6: Startup does not check data

- **Given** a reachable but empty database
- **When** the API starts
- **Then** it starts normally without reading any reference data

### AC7: Compose runs in Development

- **Given** `.env` copied from `.env.example` and `docker compose up`
- **When** I check the API container
- **Then** `ASPNETCORE_ENVIRONMENT` is `Development` and a request with the dev key from `.env` passes authentication

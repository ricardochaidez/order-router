# OR-008: Require authentication: JWT bearer and a development key

**Depends on:** [OR-001](OR-001-solution-skeleton.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-19, NFR-11, SC-13, SC-14, SC-16, SC-32

## Description

Protect the routing endpoint. In production, callers send a JWT bearer token, which is checked for issuer, audience, lifetime and signature. For local runs there is a simple development key (`X-Api-Key`). It works **only** in the `Development` environment, and its value comes from environment variables or user-secrets, never from committed files. For `docker compose`, commit a `.env.example` with a clearly marked dev-only placeholder and git-ignore `.env` (SC-32). Any authenticated caller is allowed; permissions come later. Use one named authorization policy so a scope check can be added later.

## Acceptance criteria

### AC1: No credentials

- **Given** a request to the routing endpoint with no credentials and an invalid body
- **When** it is sent
- **Then** the response is 401 with a `WWW-Authenticate` header, and no validation or routing runs

### AC2: Bad token

- **Given** a JWT that is expired, has a bad signature or a wrong audience
- **When** it is sent
- **Then** the response is 401

### AC3: Valid token

- **Given** a valid JWT
- **When** it is sent with a valid order
- **Then** the request passes authentication

### AC4: Dev key in Development

- **Given** the `Development` environment with a dev key configured
- **When** a request sends that key in `X-Api-Key`
- **Then** the request passes authentication

### AC5: Dev key impossible in Production

- **Given** the `Production` environment, even with a dev key configured
- **When** a request sends that key
- **Then** the response is 401

### AC6: No real key committed

- **Given** the repository
- **When** I look for the dev key
- **Then** only `.env.example` with a placeholder is committed, and `.env` is listed in `.gitignore`

### AC7: Health stays anonymous

- **Given** no credentials
- **When** I call `GET /health`
- **Then** it is not rejected with 401

### AC8: No secrets in logs

- **Given** requests with tokens and keys
- **When** I search the logs
- **Then** no token or key values appear

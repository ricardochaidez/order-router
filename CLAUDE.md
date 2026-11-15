# Order Router

.NET / C# project. Detailed standards live in `.claude/rules/` and load automatically for matching files:

- `csharp.md` — C# style, naming, nullability, testing, API conventions
- `dotnet-ddd-architecture.md` — DDD, SOLID, layering, financial-domain rules

For non-trivial features (new aggregates, services, or cross-layer changes), use the `ddd-implementation` skill (`/ddd-implementation`).

## Domain Context

This repo is the Order Orchestration Engine for a Durable Medical Equipment (DME) healthcare logistics platform (similar to Synapse Health). It is not e-commerce. It is an asynchronous, compliance-driven workflow orchestrator connecting Prescribers, Patients, Health Plans (Payors), and a network of subcontracted DME Suppliers. Every component should understand where an order sits in the wider lifecycle without coupling tightly to external systems.

### Order lifecycle
Orders move through an event-driven state machine. Respect the upstream preconditions and downstream consequences of each stage:

1. **Ingestion & normalization:** Unify referrals from EHR integrations, web portals, and faxes into a canonical order.
2. **Clinical & compliance gating:** Validate prescriber NPI, match ICD-10 diagnoses to HCPCS codes plus modifiers, verify signed Standard Written Orders, and check "Same-or-Similar" utilization history.
3. **Payer & financial clearance:** Verify coverage, deductibles and co-pays, and manage asynchronous Prior Authorizations (ePA).
4. **Network routing & allocation:** Pick the best supplier by geography, payor network credentialing, product capability, and SLA performance.
5. **Fulfillment & delivery:** Track vendor acceptance, courier milestones, serial number capture, and Proof of Delivery (POD).
6. **Recurring resupply:** Manage rentals and consumable refills. Require patient confirmation before releasing recurring shipments.

### Boundaries
- **Catalog & suppliers:** The orchestrator does not own inventory. It queries supplier capability using HCPCS codes (e.g., `E0601`) and modifiers (e.g., `RR` rental, `NU` new). SKU translation belongs at the fulfillment boundary.
- **Billing (RCM):** Orchestration governs billing readiness. Missing modifiers or an unverified POD blocks claim generation. Never let an order bypass a compliance gate.
- **Engagement:** Workflows can pause for days or weeks waiting on a signature, an insurer decision, or patient confirmation. Keep them durable, stateful, and non-blocking.

### Design principles
- **Idempotency follows HTTP semantics:** create endpoints like `POST /api/v1/route` follow normal REST semantics (see REST API conventions). If this service ever receives notifications from external systems (webhooks, callbacks), make those handlers safe to repeat.
- **Decouple rules from state:** Keep payor rules, routing heuristics, and integration adapters separate from core state-machine transitions so each can change independently.
- **Fail safe on compliance:** If clinical, eligibility, or attestation status is ambiguous, send the order to an exception/review queue. Never auto-advance toward fulfillment.

## REST API conventions
- **Resources, not actions:** name paths with nouns, and let the HTTP method carry the action. Never change a published path; add a new one instead.
- **Method semantics (RFC 9110):**
  - `GET`/`HEAD`: safe (no side effects) and idempotent. Never create or change state.
  - `POST`: creates a new resource, or starts a process. **Not idempotent:** each call creates a new resource with a new server-generated ID, even if the body is identical. Don't add de-duplication unless a requirement asks for it; if one does, use an explicit `Idempotency-Key` request header rather than guessing from the body.
  - `PUT`: full replace at a known URI, idempotent. `PATCH`: partial update. `DELETE`: idempotent.
- **Status codes:**
  - `200` successful read or update with a body; `201 Created` plus a `Location` header for a created resource; `204` success with no body.
  - `400` invalid request (return all validation errors at once); `401` not authenticated, with `WWW-Authenticate`; `403` authenticated but not allowed; `404` not found, also for resources the caller may not see (don't reveal existence); `409` conflict with the current state.
  - `500` unexpected fault, with no internals leaked.
  - Business outcomes of a valid request (e.g. "could not route") are not errors; report them in the body.
- **Errors:** use Problem Details (RFC 9457), unless the endpoint's documented contract defines its own envelope.
- **IDs:** the server generates resource IDs (UUIDv7). Don't expose database internals, or raw caller identities.
- **Versioning:** put the version in the URL (`/api/v1/...`). Within a version, only make additive changes. A breaking change gets a new version, and an old version is retired with `Deprecation` and `Sunset` headers.
- **Contracts:**
  - Be a tolerant reader: ignore unknown request fields.
  - Collections are always present (empty, never `null`).
  - Use one consistent JSON naming policy per API.
  - List endpoints are paginated.
- **Security and docs:** authenticate every endpoint except health checks. Document every endpoint, its status codes and examples in OpenAPI.

## Always
- Use the latest C# (C# 14) and ASP.NET Core 10 features.
- Follow `.editorconfig` for formatting.
- Make only high-confidence suggestions when reviewing code.

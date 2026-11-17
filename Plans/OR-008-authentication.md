# Plan: OR-008: Require authentication: JWT bearer and a development key

**Ticket:** [Docs/Tickets/OR-008-authentication.md](../Docs/Tickets/OR-008-authentication.md)
**Depends on:** OR-001 (done). Builds on OR-007 (done, see [OR-007-health-and-container.md](OR-007-health-and-container.md)), which already added `.env.example`, `.env` in `.gitignore` and `Authentication__DevApiKey` in compose.
**References:** Technical Requirements FR-19, NFR-11, SC-13, SC-14, SC-16, SC-32, §9.3/§9.4; [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md) (the dev key maps to the fixed caller `dev-key`); [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md) (one auth policy for both route paths); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Feeds:** OR-009 (applies the policy to `POST /api/v1/route` and `/api/route`), OR-023 (AC8 auth tests), OR-025 (README)

> **Save location:** Phase 0 copies this plan to `Plans/OR-008-authentication.md`. Update that file as each phase is done.

## Context

The API currently has only `GET /health` (anonymous) and the Development-only OpenAPI document. FR-19 requires every routing endpoint to be authenticated: JWT bearer in production, plus a development key (`X-Api-Key`) that works **only** in `Development` and can never be turned on in production. Unauthenticated calls get `401` with `WWW-Authenticate` before any validation or routing. Any authenticated caller is allowed (SC-16), through **one named authorization policy** so a scope check (403) can be added later.

The routing endpoint itself is created in OR-009, which depends on this ticket. So OR-008 builds the authentication schemes and the policy, makes the policy the **fallback** (secure by default), and proves it with a test-only probe endpoint. OR-009 attaches the policy to the real routes and proves "no validation runs" there.

Out of scope: permissions/scopes (SC-16), HMAC-hashing caller IDs for storage (NFR-11, comes with persistence in OR-027), the route endpoint (OR-009), the full README (OR-025), HTTPS/TLS (ingress).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| No route endpoint yet | **Named policy + fallback policy + test probe.** The policy is also set as `FallbackPolicy`, so every endpoint needs authentication unless it is marked `AllowAnonymous`. Integration tests check AC1–AC5 against a protected probe that exists only in the test host. "No validation runs" on the real route is a **follow-up for OR-009**. |
| JWT key source | **OIDC Authority.** Configuration supplies `Authentication:Jwt:Authority` and `Authentication:Jwt:Audience`. Signing keys and the issuer come from the provider's OIDC metadata (e.g. Entra ID). Tests replace the metadata with a static test configuration (test signing key), so there is no network call. Outside `Development`, startup fails if Authority or Audience is missing. |
| OpenAPI document | **Stays anonymous.** It is mapped only in `Development` and is documentation, so it gets `.AllowAnonymous()`. `ApiHostTests` keeps working without credentials. |

### Other design choices (from the ticket, requirements, ADRs and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Location | Everything lives in `src/OrderRouter.Api/Authentication/`. This is a hosting concern: no domain concepts, aggregates, events or money. Domain, Application and Infrastructure don't change. `LayerDependencyTests` must stay green. |
| Package | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.x (match the existing 10.0.15) in the Api project. Tests use `Microsoft.IdentityModel.JsonWebTokens` (comes with it) to mint tokens. |
| JWT validation | `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true`, `RequireExpirationTime` and `RequireSignedTokens` `true`. Keep the default clock skew. `RequireHttpsMetadata` stays `true` except in `Development`. Options are bound from a `JwtAuthenticationOptions` record (`Authority`, `Audience`) with `ValidateOnStart` outside Development. Missing values → `InvalidOperationException` naming the setting, matching the connection-string pattern in `Program.cs`. |
| Dev key scheme | `DevApiKeyAuthenticationHandler : AuthenticationHandler<DevApiKeyAuthenticationOptions>`, scheme name `DevApiKey`, header `X-Api-Key`. It is **registered only when `builder.Environment.IsDevelopment()` and `Authentication:DevApiKey` is non-empty**. In any other environment the scheme does not exist, so no configuration can turn it on (AC5). If a key is configured outside Development, log one warning that it is ignored (never the value). |
| Dev key source | `Authentication:DevApiKey`, the name compose already passes (`Authentication__DevApiKey`). It comes from environment variables or user-secrets (add `<UserSecretsId>` to the Api csproj). Never from `appsettings*.json`; a guard test enforces that. |
| Dev key check | Compare with `CryptographicOperations.FixedTimeEquals` over SHA-256 hashes of both values (constant time, and no length leak). No header → `NoResult`. Wrong key → `Fail` with a fixed message that never contains the value. Success → principal with `ClaimTypes.NameIdentifier`/`sub` = `dev-key` (ADR-0005). |
| Policy | `AuthorizationPolicyNames.RouteCaller` (`"RouteCaller"`): `AddAuthenticationSchemes(Bearer[, DevApiKey])` + `RequireAuthenticatedUser()`. One named hook for a later scope check (SC-16). The same policy object is set as `FallbackPolicy`. A `RequireRouteCaller()` endpoint extension is what OR-009 calls. |
| 401 shape | Default scheme is `JwtBearer`, so the challenge sends `WWW-Authenticate: Bearer`. The body is empty (§9.4 allows empty or ProblemDetails). |
| Pipeline | `Program.cs` calls `UseAuthentication()` and `UseAuthorization()` explicitly after `UseHttpsRedirection()` and before mapping endpoints. `/health` already has `.AllowAnonymous()`; `MapOpenApi()` gets it too. |
| Logging (AC8) | Never log header values, tokens or keys. Leave `IdentityModelEventSource.ShowPII` and `LogCompleteSecurityArtifact` at their defaults (`false`); a guard test checks no code sets them. |
| OpenAPI | A document transformer adds the `Bearer` security scheme (and `ApiKey` in Development) so OR-009's routes are documented with 401. |

### Target structure

```
.env.example                                     check / tidy dev-key comment (SC-32)
docker-compose.yml                               update the OR-008 comment on Authentication__DevApiKey
README.md                                        + short "Authenticate" note (dev key header); OR-025 expands it
src/OrderRouter.Api/
  OrderRouter.Api.csproj                         + JwtBearer package, + UserSecretsId
  OrderRouter.Api.http                           + X-Api-Key example using a variable (no real key)
  Program.cs                                     AddOrderRouterAuthentication, UseAuthentication/UseAuthorization, OpenAPI AllowAnonymous
  appsettings.json                               + Authentication:Jwt section with empty Authority/Audience (no secrets)
  Authentication/AuthenticationServiceCollectionExtensions.cs   schemes, policy, fallback, startup checks
  Authentication/AuthorizationPolicyNames.cs     "RouteCaller"
  Authentication/JwtAuthenticationOptions.cs
  Authentication/DevApiKeyAuthenticationHandler.cs
  Authentication/DevApiKeyAuthenticationOptions.cs
  Authentication/DevApiKeyDefaults.cs            scheme name, header name, caller id "dev-key"
  Authentication/RouteCallerEndpointExtensions.cs  RequireRouteCaller()
  Authentication/SecuritySchemeTransformer.cs    OpenAPI security schemes
tests/OrderRouter.Tests/
  Api/Authentication/DevApiKeyAuthenticationHandlerTests.cs
  Api/Authentication/JwtAuthenticationOptionsTests.cs
  Architecture/SecretsConfigurationTests.cs      AC6, no ShowPII, no DevApiKey in appsettings
tests/OrderRouter.Api.IntegrationTests/
  Authentication/TestJwt.cs                      test signing key + token builder (valid, expired, bad signature, wrong audience)
  Authentication/AuthApiFactory.cs (or extend Health/ApiFactory)  environment, dev key, JWT test config, probe endpoint
  Authentication/AuthenticationTests.cs          AC1–AC5, AC7
  Authentication/SecretLoggingTests.cs           AC8
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-cutting hosting change, so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review at the start of Phase 1 (expected result: no domain changes; security and compliance are the main concerns);
   - §3 is this plan, and §4 implementation runs through Phases 1–3;
   - §5 post-implementation review in Phase 4.

   Key points:
   - **C#:** C# 14 / .NET 10. File-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, the final `return` on its own line, no null-forgiving `!` on configuration. Follow `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members; comments explain *why* and cite the requirement (e.g. `// FR-19`, `// SC-32`). Mention the purpose of the JwtBearer package where it is used.
   - **REST/API (CLAUDE.md):** every endpoint is authenticated except `/health` (and the Development-only OpenAPI document, as decided). `401` carries `WWW-Authenticate`. No internals leaked.
   - **Secrets:** no real key, token or password committed or logged (NFR-11, SC-32).
   - **Tests:** follow the nearby style: `MethodName_Condition_ExpectedResult` names, no `Arrange`/`Act`/`Assert` comments, `TestContext.Current.CancellationToken`. Cover edge cases and error paths (empty header, wrong key, key with different length, missing config). Target ≥ 85% coverage of the new code.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d postgres`) and run these from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test (architecture, health, startup, schema, seeder, read path, `ApiHostTests`).
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-008-authentication.md`;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or library behaviour is unclear or contradicts another, stop and ask the engineer. Don't guess. Examples: the probe endpoint can't be added to the test host while still going through `Program.cs`'s middleware; the fallback policy changes the status of an existing endpoint; JwtBearer tries to fetch metadata in tests despite the static configuration.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-008-authentication`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-008-authentication.md` with this plan.
- [x] Confirm the three decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. There are no code changes, so there's nothing to test.

**Result:** Plan saved. No code changed, so no tests were run.

---

## Phase 1: JWT bearer, named policy and fallback ✅ Done

Covers AC1 (401 + `WWW-Authenticate`), AC2, AC3, AC7.

- [x] Run `ddd-implementation` §1 and §2; record the short result in this phase's **Result**.
- [x] Add the JwtBearer package; add `JwtAuthenticationOptions`, `AuthorizationPolicyNames`, `RouteCallerEndpointExtensions`.
- [x] Add `AddOrderRouterAuthentication(this WebApplicationBuilder)` (or `IServiceCollection` + environment): JwtBearer as default scheme, the `RouteCaller` policy, `FallbackPolicy` = that policy, fail-fast config check outside Development.
- [x] `Program.cs`: call it; add `UseAuthentication()` / `UseAuthorization()`; `MapOpenApi().AllowAnonymous()`. Add the empty `Authentication:Jwt` section to `appsettings.json`.
- [x] Test infrastructure: `TestJwt` (RSA or symmetric test key; builds valid, expired, bad-signature and wrong-audience tokens) and a factory that sets `JwtBearerOptions.Configuration` to a static `OpenIdConnectConfiguration` (issuer + test key) so no metadata is fetched. Add a test-only probe endpoint protected by `RequireRouteCaller()`. **First spike how to add the probe so it still runs through `Program.cs`'s pipeline**; if that isn't possible cleanly, stop and ask (working rule 5).
- [x] Unit tests: options validation (missing Authority / Audience outside Development throws, naming the setting; Development tolerates them missing).
- [x] Integration tests:
  - no credentials on the probe → `401` + `WWW-Authenticate: Bearer`, and the probe handler did not run (AC1);
  - expired, bad signature, wrong audience → `401` each (AC2);
  - valid token → probe returns success (AC3);
  - `GET /health` with no credentials → not `401` (AC7); `/openapi/v1.json` in Development still `200` (`ApiHostTests`).

**Verify:** working rule 2.

**Result:** DDD §1/§2: no domain, application or infrastructure change; hosting concern only, security/compliance are the main concerns. The probe is added with an `IStartupFilter` that calls `UseEndpoints` after `Program.cs`'s pipeline, so requests go through the real middleware. Deviation: the fallback policy also returns `401` for unmatched paths (not `404`), which is secure by default and reveals no routes; the test asserts that. 426 tests passed.

---

## Phase 2: Development key ✅ Done

Covers AC4, AC5, AC6.

- [x] Add `DevApiKeyDefaults`, `DevApiKeyAuthenticationOptions`, `DevApiKeyAuthenticationHandler` (constant-time compare, `dev-key` caller, no value in any message).
- [x] Register the scheme and add it to the policy **only** in `Development` with a non-empty `Authentication:DevApiKey`; log a value-free warning if a key is configured elsewhere.
- [x] Add `<UserSecretsId>` to the Api csproj. Update the compose comment, `.env.example` comment (still placeholder `dev-only-change-me`), `OrderRouter.Api.http` (`X-Api-Key: {{devApiKey}}` variable, no real key), and a short README note on sending `X-Api-Key`.
- [x] Unit tests for the handler: no header → no result; empty header → fail; wrong key (same and different length) → fail; right key → success with `dev-key`; failure message never contains the sent or configured key.
- [x] Integration tests:
  - Development + key configured + correct `X-Api-Key` → passes (AC4); wrong key → `401`;
  - Production (JWT test config + connection string supplied) + key configured + that key sent → `401` (AC5); valid JWT still passes in Production;
  - Development with no key configured → `X-Api-Key` is ignored → `401`.
- [x] `SecretsConfigurationTests` (unit, source guards like `ContainerConfigurationTests`/`TestRepository.Root`): `.env.example` has `DEV_API_KEY` with a dev-only placeholder; `.gitignore` lists `.env`; no `appsettings*.json` contains `DevApiKey`; `git ls-files` contains no `.env` if a reliable check is possible without shelling out (otherwise check manually and say so) (AC6).

**Verify:** working rule 2.

**Result:** Handler, scheme registration (Development + non-empty key only), user-secrets id, docs and guards done. `.env` is not tracked (`git ls-files` checked manually; a reliable in-test check would need a shell, so it is not unit-tested). 437 tests passed.

---

## Phase 3: No secrets in logs, OpenAPI and container check ✅ Done

Covers AC8, plus documentation and the OR-007 follow-up.

- [x] `SecretLoggingTests`: register a capturing `ILoggerProvider` on the whole host with the minimum level at `Trace`; send requests with a valid token, an expired token, a bad-signature token, the right dev key and a wrong dev key; assert none of those values (or the configured key) appear in any log message or exception text (AC8).
- [x] Guard test: no source sets `IdentityModelEventSource.ShowPII` or `LogCompleteSecurityArtifact`.
- [x] Add the OpenAPI security-scheme transformer (Bearer; ApiKey in Development); test that `/openapi/v1.json` lists them.
- [x] Manual check (OR-007 follow-up): `cp .env.example .env`, `docker compose up -d --build --wait`; `curl` `/health` with no credentials → `200`; a request to a protected path without credentials → `401` with `WWW-Authenticate`; with `X-Api-Key: dev-only-change-me` → not `401` (404 is fine, since no route exists yet); `docker compose logs api` shows no key value.

**Verify:** working rule 2 plus the manual checks above.

**Result:** `SecretLoggingTests`, ShowPII guard and OpenAPI transformer done. Manual compose check: `/health` 200 anonymously; `/api/v1/route` with no key or a wrong key → `401` + `WWW-Authenticate: Bearer`; with `dev-only-change-me` → `404`; `docker compose logs api` has no key value. 441 tests passed.

---

## Phase 4: Post-implementation review and follow-ups ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (layering, fail-safe secure-by-default fallback, REST 401 semantics, no secrets committed or logged, dev key impossible outside Development), what is **N/A** and why (domain events, money, aggregates), and what was **not verified** (e.g. coverage %, a real OIDC provider). Be honest.
- [x] Record follow-ups (don't do them here):
  - **OR-009:** call `RequireRouteCaller()` on `POST /api/v1/route` and `POST /api/route`; add a test that no credentials + an invalid body → `401` and the validator never runs (finishes AC1 on the real route); document 401 on both routes in OpenAPI;
  - **OR-015/ADR-0005:** read the caller ID from `sub` (JWT) or `dev-key`;
  - **OR-023:** reuse `TestJwt` and the factory for the end-to-end auth cases;
  - **OR-025:** README section on JWT settings and the dev key.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** Applies and met: layering (everything in the Api project; `LayerDependencyTests` green), secure-by-default fallback policy, `401` + `WWW-Authenticate`, no secrets committed or logged, dev key scheme not registered outside Development. N/A: domain events, money and aggregates (no domain change). Not verified: coverage % (not measured), a real OIDC provider (tests use a static configuration), JWT behavior behind a real `HTTPS` metadata endpoint. Follow-ups are the OR-009, OR-015, OR-023 and OR-025 items above. Final run: build 0 warnings, 441 tests passed, `dotnet format` clean.
---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-008-authentication.md`, `src/OrderRouter.Api/Authentication/*`, `tests/OrderRouter.Tests/Api/Authentication/*`, `tests/OrderRouter.Tests/Architecture/SecretsConfigurationTests.cs`, `tests/OrderRouter.Api.IntegrationTests/Authentication/*` |
| Modified | `src/OrderRouter.Api/Program.cs`, `src/OrderRouter.Api/OrderRouter.Api.csproj`, `src/OrderRouter.Api/appsettings.json`, `src/OrderRouter.Api/OrderRouter.Api.http`, `docker-compose.yml` (comment), `.env.example` (comment), `README.md` |
| Reused | `Health/ApiFactory` pattern (`WebApplicationFactory<Program>`, `UseEnvironment`, `UseSetting`, `ConfigureTestServices`); `CapturingLogger` idea for the logging test; `TestRepository.Root` source-guard pattern from `ContainerConfigurationTests`; the fail-fast configuration pattern in `Program.cs`; `/health`'s existing `.AllowAnonymous()` |
| Unchanged | `src/OrderRouter.Domain/**`, `src/OrderRouter.Application/**`, `src/OrderRouter.Infrastructure/**`, migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: No credentials → 401 + `WWW-Authenticate`, nothing runs | `AuthenticationTests` on the probe; on the real route → **OR-009 follow-up** | 1, 4 |
| AC2: Bad token → 401 | `AuthenticationTests` (expired, bad signature, wrong audience) | 1 |
| AC3: Valid token passes | `AuthenticationTests` | 1 |
| AC4: Dev key in Development | `AuthenticationTests` + handler unit tests + manual compose check | 2, 3 |
| AC5: Dev key impossible in Production | `AuthenticationTests` (Production + key configured → 401) | 2 |
| AC6: No real key committed | `SecretsConfigurationTests` | 2 |
| AC7: Health stays anonymous | `AuthenticationTests` + existing `HealthEndpointTests` | 1 |
| AC8: No secrets in logs | `SecretLoggingTests` + ShowPII guard + manual `docker compose logs` | 3 |

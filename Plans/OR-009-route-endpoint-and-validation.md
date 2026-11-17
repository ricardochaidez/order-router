# Plan: OR-009: Add the route endpoint, response envelope and request validation

**Ticket:** [Docs/Tickets/OR-009-route-endpoint-and-validation.md](../Docs/Tickets/OR-009-route-endpoint-and-validation.md)
**Depends on:** OR-008 (done, see [OR-008-authentication.md](OR-008-authentication.md)).
**References:** [ADR-0004](../Docs/ADRs/ADR-0004-response-contract-evolution.md); Technical Requirements FR-01, FR-02, FR-03, FR-19, §9.1–§9.4, SC-13, SC-15, DQ-15; `CLAUDE.md` (REST API conventions); `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-015 (implements `IRouteOrderUseCase`, maps the domain outcome to the envelope), OR-027 (switches success to 201 + `Location`, adds `id`), OR-023 (acceptance tests on both paths)

> **Save location:** Phase 0 copies this plan to `Plans/OR-009-route-endpoint-and-validation.md`. Update that file as each phase is done.

## Context

There is no routing endpoint yet. OR-008 built authentication (JWT + Development key), the `RouteCaller` policy (also the fallback) and `RequireRouteCaller()`, and left OR-009 a follow-up: put the policy on the real routes and prove that an unauthenticated invalid request gets `401` without validation running.

OR-009 exposes `POST /api/v1/route` (canonical) and `POST /api/route` (permanent alias pinned to v1), sharing one handler, auth policy and validator (ADR-0004). It defines the snake_case request and v1 response envelope, validates every request and returns **400 with all errors** in the same envelope as a success (SC-15). Valid requests go to `IRouteOrderUseCase`; the real implementation comes in OR-015. The OpenAPI document `v1` describes both paths.

Out of scope: routing logic and mapping domain outcomes (OR-015), storing decisions, `id`, `Location` and 201 (OR-027), `GET /api/v1/route/{id}` (OR-028), `diagnostics` (OR-022), experiment assignment (OR-016).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Valid request before OR-015 | **Placeholder → 500.** Register a temporary `RoutingNotAvailableUseCase` that throws `NotSupportedException`. The global exception handler returns 500 ProblemDetails with no internals. Nothing claims to be routed (fail safe). OR-015 replaces the registration. Tests replace it with a fake. |
| Validation mechanism | **Built-in ASP.NET Core 10 minimal-API validation** (`builder.Services.AddValidation()` + data annotations on the request DTO). Its default ProblemDetails output is rewritten into the v1 envelope for the route endpoints only. |
| Success status in OR-009 | **200 for now.** Matches OR-015 ("Return 200 for now"); OR-027 changes it to 201 + `Location`. |

### Other design choices (from the ticket, requirements, ADRs and rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Layers | **Api** (`src/OrderRouter.Api/Routing/`): wire request DTOs with data annotations, endpoint mapping, envelope writer for 400, OpenAPI transformer. **Application** (`src/OrderRouter.Application/Routing/`): `IRouteOrderUseCase`, `RouteOrderCommand` (validated values), and the v1 response DTOs (`RouteResponse`, `RoutingStatus`, `SupplierRouting`, `RoutedItem`, `UnfulfilledItem`, `RoutingWarning`) so OR-015 can build them from the domain outcome (ADR-0004: "`feasible` is constructed from the domain outcome"). Domain and Infrastructure don't change. `LayerDependencyTests` stays green. |
| Validation location vs. DDD rule | The DDD rule says Application validates input before business logic. With built-in validation the rules run at the Api edge, and the use case only ever receives a `RouteOrderCommand` built from an already validated request. The command factory still guards its invariants (non-empty `order_id`, ≥ 1 item), which is defence in depth, not a second rule set. Record this in the §5 review. |
| JSON naming | `ConfigureHttpJsonOptions`: `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`, `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)` (FR-01, ADR-0004). `/health` is plain text, so it isn't affected. Unknown fields are ignored (STJ default; no `UnmappedMemberHandling.Disallow`) (DQ-15, AC6). |
| Request DTO | `RouteRequest { string? OrderId; string? CustomerZip; bool MailOrder; List<RouteRequestItem>? Items }`, `RouteRequestItem { string? ProductCode; int? Quantity }`. Nullable so a missing field is reported, not defaulted. Rules (FR-03): `order_id` required and not whitespace; `customer_zip` required and `^[0-9]{5}$`; `items` required with ≥ 1 entry; each `product_code` required and not whitespace; each `quantity` required and ≥ 1. `mail_order` is optional and defaults to `false` (FR-03 does not require it; `false` is the stricter mode). Unknown product codes are **not** checked (AC7). |
| Error messages | `errors` is `string[]` (ADR-0004). Each message names the JSON path in snake_case, e.g. `"customer_zip must be exactly 5 digits."`, `"items[0].quantity must be at least 1."`. Order is stable (request order). All errors are returned at once (AC1). |
| Envelope | `RouteResponse` always initialises `routing`, `unfulfilled_items`, `warnings`, `errors` to empty arrays (AC9). `feasible` is a computed property `Status == RoutingStatus.Routed` and is never set separately (ADR-0004). A factory `RouteResponse.Invalid(IReadOnlyList<string> errors)` builds the 400 body (`status:not_routed`). `id` and `order_id` are omitted on 400 (§9.2; `JsonIgnore(WhenWritingNull)` on those two only). |
| 400 for validation failures | The validation filter writes `HttpValidationProblemDetails` through `IProblemDetailsService`. A custom `IProblemDetailsWriter` (`RouteEnvelopeProblemDetailsWriter`) handles only endpoints tagged with a `RouteEnvelopeMetadata` marker: it flattens the validation errors into envelope messages and writes `RouteResponse.Invalid(...)` with status 400. Other endpoints keep normal ProblemDetails. |
| 400 for malformed JSON, empty body, wrong content type | Binding fails before validation (`BadHttpRequestException`). The same writer (or an `IExceptionHandler` for that exception on tagged endpoints) turns it into the envelope with one error, e.g. `"The request body is not valid JSON."`, without echoing the parser message (no internals). Wrong JSON types (e.g. `quantity: "abc"`, `customer_zip: 10015`) are also malformed bodies: one error naming the JSON path. |
| 500 | `AddProblemDetails()` + `UseExceptionHandler()`; unexpected faults (including the placeholder) return 500 ProblemDetails, no stack trace (CLAUDE.md). |
| Endpoint mapping | `RouteEndpoints.MapRouteEndpoints()`: `app.MapGroup("/api/v1").MapPost("/route", Handle)` and `app.MapPost("/api/route", Handle)` with the same static handler, both `.RequireRouteCaller()`, `.WithMetadata(RouteEnvelopeMetadata)`, `.Accepts<RouteRequest>("application/json")`, `.Produces<RouteResponse>(200)`, `.Produces<RouteResponse>(400)`, `.ProducesProblem(500)`. Names: `RouteOrderV1` and `RouteOrderAlias`. No new package (ADR-0004: route groups, not `Asp.Versioning`). |
| OpenAPI | Keep the existing `AddOpenApi()` (document name `v1`). Add an operation transformer for the route endpoints: summary/description, a request example (`ORD-EXAMPLE` from §9.1, with `priority`/`notes` to show they are ignored), response examples for 200 (`not_routed` shape until OR-015 adds routed ones) and 400 (AC1 body). The alias's description says "Alias of `POST /api/v1/route`, permanently pinned to v1." The existing `SecuritySchemeTransformer` adds 401. |
| Auth order | `UseAuthentication`/`UseAuthorization` run before endpoint execution, so binding and validation never run for a 401 (FR-19, OR-008 follow-up). |

### Target structure

```
src/OrderRouter.Application/
  Routing/IRouteOrderUseCase.cs              Task<RouteResponse> RouteAsync(RouteOrderCommand, CancellationToken)
  Routing/RouteOrderCommand.cs               validated order (order_id, zip, mail_order, lines)
  Routing/Contracts/RouteResponse.cs         envelope; feasible computed from status; Invalid(...) factory
  Routing/Contracts/RoutingStatus.cs, SupplierRouting.cs, RoutedItem.cs, UnfulfilledItem.cs, RoutingWarning.cs
src/OrderRouter.Api/
  Program.cs                                 AddValidation, AddProblemDetails, JSON options, exception handler, MapRouteEndpoints, placeholder registration
  OrderRouter.Api.http                       POST examples for /api/v1/route and /api/route (valid, invalid, malformed)
  Routing/RouteRequest.cs, RouteRequestItem.cs   wire DTOs with data annotations
  Routing/RouteEndpoints.cs                  both paths, one handler
  Routing/RouteEnvelopeMetadata.cs           marker for envelope-shaped errors
  Routing/RouteEnvelopeProblemDetailsWriter.cs   400 validation/binding → envelope
  Routing/ValidationErrorFormatter.cs        error keys → snake_case messages, stable order
  Routing/RoutingNotAvailableUseCase.cs      temporary placeholder (removed by OR-015)
  Routing/RouteOpenApiTransformer.cs         examples + alias note
tests/OrderRouter.Tests/
  Application/Routing/RouteResponseTests.cs, RouteOrderCommandTests.cs
  Api/Routing/ValidationErrorFormatterTests.cs
tests/OrderRouter.Api.IntegrationTests/
  Routing/RouteApiFactory.cs                 extends AuthApiFactory setup; swaps IRouteOrderUseCase for a recording fake
  Routing/RouteValidationTests.cs            AC1–AC7, AC9 on both paths ([Theory] over the two paths)
  Routing/RouteAliasTests.cs                 AC8
  Routing/RouteAuthTests.cs                  401 before validation (OR-008 follow-up)
  Routing/RouteOpenApiTests.cs               AC10
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer change (new Application contracts + Api endpoint), so use the `ddd-implementation` skill (`/ddd-implementation`):
   - §1 domain analysis and §2 architecture review at the start of Phase 1;
   - §3 is this plan; §4 implementation runs through Phases 1–4;
   - §5 post-implementation review in Phase 5.

   Key points:
   - **C#:** C# 14 / .NET 10. File-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, final `return` on its own line, records for DTOs. Follow `.editorconfig`.
   - **Docs and comments:** XML doc comments on public types and members (with `<example>` where useful); comments explain *why* and cite the requirement (`// FR-03`, `// SC-15`, `// ADR-0004`).
   - **REST/API (CLAUDE.md):** resource path, version in the URL, `400` lists all errors, `500` leaks nothing, collections never `null`, one JSON naming policy, tolerant reader, every endpoint authenticated and documented in OpenAPI with status codes and examples.
   - **Fail safe:** nothing may produce a success-looking body for a request that was not routed; the placeholder returns 500.
   - **Tests:** follow nearby style: `MethodName_Condition_ExpectedResult`, no `Arrange`/`Act`/`Assert` comments, `TestContext.Current.CancellationToken`, xUnit v3. Every core case runs against **both** paths (ADR-0004). Cover edge cases (whitespace strings, ZIP `"2130"`, `"213000"`, `"21a30"`, `quantity` 0 and negative, `items: null` vs `[]`, empty body, wrong types). Target ≥ 85% coverage of new code.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, start PostgreSQL (`docker compose up -d postgres`) and run from the repo root, plus the phase's own checks:
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: all pass, including every existing test (architecture, health, startup, auth, schema, seeder, read path, `ApiHostTests`).
   - `dotnet format --verify-no-changes`: clean.
3. **Mark each phase as done.** When a phase's checks pass:
   - change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-009-route-endpoint-and-validation.md`;
   - tick its checklist items;
   - add a short **Result** line (test counts, and anything that deviated from the plan).
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule, ticket text, ADR or library behaviour is unclear or contradicts another, stop and ask. Don't guess. Known risks to watch:
   - the .NET 10 validation source generator doesn't pick up the request DTOs, or its error keys aren't snake_case JSON paths;
   - the validation filter's ProblemDetails can't be rewritten per endpoint through `IProblemDetailsWriter`;
   - binding failures (malformed JSON) don't reach `IProblemDetailsService`/an exception handler, so the envelope can't be produced without reading the body manually;
   - global snake_case JSON changes an existing response.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (e.g. `feature/OR-009-route-endpoint`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-009-route-endpoint-and-validation.md` with this plan.
- [x] Confirm the three decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so nothing to test.

**Result:** Plan saved. The three decisions (placeholder → 500, built-in .NET 10 validation, 200 for now) are recorded. No code changed, so no tests were run.

---

## Phase 1: Contracts and use-case seam ✅ Done

Covers the envelope shape (AC9 at the type level) and the `IRouteOrderUseCase` seam.

- [x] Run `ddd-implementation` §1 and §2; record the short result in **Result** (expected: no domain change yet; ubiquitous language = Order, Line item, routing status; the main invariant is `feasible == (status == routed)`).
- [x] Application: `RoutingStatus`, `SupplierRouting`, `RoutedItem`, `UnfulfilledItem`, `RoutingWarning`, `RouteResponse` (arrays default to `[]`, computed `Feasible`, `Invalid(errors)` factory), `RouteOrderCommand` (+ `RouteOrderLine`) with guard checks, `IRouteOrderUseCase`.
- [x] Unit tests: `RouteResponse` arrays never null (including when constructed with nulls via init), `Feasible` true only for `Routed`, `Invalid` gives `not_routed` + the errors; serialization with snake_case options gives exactly the §9.2 field names and `"not_routed"`, and omits `id`/`order_id` when null; command guards.

**Verify:** working rule 2.

**Result:** Unit tests: 377 pass. `ddd-implementation` §1/§2: no domain change; ubiquitous language is Order, Line item, routing status; the main invariant is `feasible == (status == routed)`, held in `RouteResponse` (computed property). Arrays are normalised with the C# 14 `field` keyword. `RouteOrderCommand.CustomerZip` is the existing domain `ZipCode`.

---

## Phase 2: Endpoints, validation and the 400 envelope ✅ Done

Covers AC1–AC7, AC9 on both paths, plus the OR-008 follow-up.

- [x] **Spike first** (working rule 5): confirm with a throwaway test that (a) `AddValidation()` validates `RouteRequest` and nested items and reports all errors, (b) error keys can be mapped to snake_case paths, (c) both validation failures and malformed-JSON binding failures can be turned into the envelope for tagged endpoints. If any of these doesn't work cleanly, stop and ask.
- [x] Api: `RouteRequest`/`RouteRequestItem` with data annotations; `ConfigureHttpJsonOptions` (snake_case, enum converter); `AddValidation()`, `AddProblemDetails()`, `UseExceptionHandler()`; `RouteEnvelopeMetadata`, `ValidationErrorFormatter`, `RouteEnvelopeProblemDetailsWriter` (and an exception handler for `BadHttpRequestException` if needed).
- [x] `RouteEndpoints`: one handler on `/api/v1/route` (group) and `/api/route`, both `RequireRouteCaller()`, maps request → `RouteOrderCommand`, calls `IRouteOrderUseCase`, returns 200 with the envelope.
- [x] `RoutingNotAvailableUseCase` registered in `Program.cs` with a comment that OR-015 replaces it.
- [x] Test factory `RouteApiFactory` (reuses `AuthApiFactory`'s JWT setup and the dev key) that swaps in a recording fake use case.
- [x] Unit tests: `ValidationErrorFormatter` (key → message, ordering).
- [x] Integration tests, `[Theory]` over both paths:
  - AC1: no `customer_zip` + `items: []` → 400, `feasible:false`, `status:"not_routed"`, empty `routing`/`unfulfilled_items`/`warnings`, both errors present; fake use case not called;
  - AC2: `customer_zip:"2130"` → 400, error names `customer_zip` (plus `"213000"`, `"21a30"`);
  - AC3: `quantity:0` (and `-1`, missing) → 400;
  - AC4: empty / whitespace `product_code`, empty `order_id` → 400;
  - AC5: invalid JSON, empty body, wrong type → 400 in the envelope, no parser internals in `errors`;
  - AC6: valid order with `priority` and `notes` → 200, fake use case received the order;
  - AC7: valid order with `XX-UNKNOWN-999` → passes validation, fake use case called;
  - AC9: every 200/400 body above has all four arrays present;
  - auth follow-up: no credentials + invalid body → 401 + `WWW-Authenticate`, no envelope, fake not called;
  - placeholder: without the fake, a valid order → 500 ProblemDetails with no stack trace.

**Verify:** working rule 2.

**Result:** Spike passed: `AddValidation()` validates nested items and reports all errors, keys map to snake_case paths, and both validation failures and malformed-JSON binding failures reach the envelope (via `IProblemDetailsWriter` and an `IExceptionHandler`). 510 tests pass (build 0 warnings, `dotnet format` clean). **Deviations:** (1) a non-JSON `Content-Type` returns **415** with no body, not the 400 envelope. ASP.NET Core rejects it before binding without throwing, so an envelope would need manual body reading. The test pins 415. (2) Added a `NoNullElements` validation attribute so `items: [null]` is reported with the other errors (element validation skips nulls). (3) `ThrowOnBadRequest` is forced on in all environments so malformed bodies behave the same everywhere. (4) The global snake_case policy also renames ProblemDetails extension keys (`traceId` becomes `trace_id`). (5) `AuthApiFactory.Create` gained an optional `configureServices` parameter.

---

## Phase 3: Alias parity ✅ Done

Covers AC8.

- [x] `RouteAliasTests`: for a set of requests (valid, AC1 invalid, malformed JSON, unknown product), send the same body to `/api/route` and `/api/v1/route` with the same fake result and assert identical status code and identical body (byte-for-byte JSON).
- [x] Check both endpoints carry the same metadata (auth policy, envelope marker) so they can't drift.

**Verify:** working rule 2.

**Result:** 8 alias tests pass: identical status and byte-for-byte body for six request shapes, identical 401 challenge, and identical endpoint metadata (method, auth policy, envelope marker, produced status codes). The explicit `Produces(200)` was dropped from the shared configuration because the `Ok<RouteResponse>` return type already infers it (it was registered twice).

---

## Phase 4: OpenAPI v1 and developer docs ✅ Done

Covers AC10.

- [x] `RouteOpenApiTransformer`: summary/description on both operations, alias marked as "pinned to v1", request example (§9.1 + `priority`/`notes`), 200 and 400 response examples, 500 ProblemDetails.
- [x] `RouteOpenApiTests` (Development): `/openapi/v1.json` has `post` on `/api/v1/route` with request and response examples and 200/400/401/500; `/api/route` is listed and its description says it is an alias of `/api/v1/route`; schemas use snake_case names.
- [x] Update `OrderRouter.Api.http` with valid, invalid and malformed examples for both paths (dev key from the variable, nothing committed).
- [x] Manual check: `docker compose up -d --build --wait`; `curl` both paths with the dev key: valid → 500 ProblemDetails (placeholder, expected until OR-015), AC1 body → 400 envelope, no key → 401; `docker compose logs api` shows no key value.

**Verify:** working rule 2 plus the manual check.

**Result:** 521 tests pass. `RouteOpenApiTests` covers examples, 200/400/401/500, alias text and snake_case schemas. Manual check against `docker compose up -d --build --wait`: both paths return 500 ProblemDetails for a valid order (placeholder), the 400 envelope for the AC1 body, and 401 with `WWW-Authenticate: Bearer` without a key; the dev key does not appear in `docker compose logs api`.

---

## Phase 5: Post-implementation review and follow-ups ✅ Done

- [x] `ddd-implementation` §5 review: what **applies and is met** (layering, `feasible` invariant in one place, all errors at once, same envelope, tolerant reader, auth before validation, no internals in 400/500), what is **N/A** and why (domain events, money, aggregates), the validation-location note from *Other design choices*, and what was **not verified** (e.g. coverage %). Be honest.
- [x] Record follow-ups (don't do them here):
  - **OR-015:** implement `IRouteOrderUseCase`, delete `RoutingNotAvailableUseCase` and its 500 test, add routed / not-routed 200 examples to OpenAPI;
  - **OR-027:** 200 → 201 + `Location`, `id`/`order_id` in the body, update OR-009 success assertions and OpenAPI;
  - **OR-023:** reuse `RouteApiFactory` and the two-path `[Theory]` pattern.
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** Final run: `dotnet build` 0 errors and 0 warnings, `dotnet test` 521 passed / 0 failed, `dotnet format --verify-no-changes` clean.

**§5 review**
- *Applies and met:* layering (contracts and seam in Application; wire DTOs, endpoints, writers in Api; Domain and Infrastructure untouched; `LayerDependencyTests` green); the `feasible` invariant lives in one place; every validation error is returned at once in the same envelope; tolerant reader; auth runs before binding and validation (tested on both paths); 400 and 500 leak no parser or stack details; collections are never null; one JSON naming policy; fail safe (the placeholder returns 500, never a routed body).
- *N/A:* domain events, money, aggregates. Nothing is persisted or changes state yet.
- *Validation location:* the rules run at the Api edge (built-in validation), not in Application as the DDD rule suggests. `RouteOrderCommand.Create` guards the invariants again as defence in depth, and the use case only receives a command.
- *Not verified:* code coverage % (no coverage run was done). The paths are covered by unit and integration tests, but the 85% target is unmeasured. Not tested: 401 for a bad (not just missing) JWT on the route paths, which `AuthenticationTests` covers on the probe endpoint.

**Follow-ups (not done here)**
- **OR-015:** implement `IRouteOrderUseCase`, delete `RoutingNotAvailableUseCase` and its 500 test, add routed and not-routed 200 examples to OpenAPI.
- **OR-027:** 200 becomes 201 + `Location`, add `id` and `order_id` to the body, update OR-009 success assertions and OpenAPI.
- **OR-023:** reuse `RouteApiFactory` and the two-path `[Theory]` pattern.
- Decide whether a wrong `Content-Type` should be a 400 envelope (needs manual body reading) or stay 415.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-009-route-endpoint-and-validation.md`, `src/OrderRouter.Application/Routing/**`, `src/OrderRouter.Api/Routing/**`, `tests/OrderRouter.Tests/Application/Routing/*`, `tests/OrderRouter.Tests/Api/Routing/*`, `tests/OrderRouter.Api.IntegrationTests/Routing/*` |
| Modified | `src/OrderRouter.Api/Program.cs`, `src/OrderRouter.Api/OrderRouter.Api.http` |
| Reused | `RequireRouteCaller()` (`src/OrderRouter.Api/Authentication/RouteCallerEndpointExtensions.cs`); `SecuritySchemeTransformer` (adds 401 + security to the new operations); `AuthApiFactory` + `TestJwt` (`tests/OrderRouter.Api.IntegrationTests/Authentication/`); `ApiHostTests` pattern for fetching `/openapi/v1.json` |
| Unchanged | `src/OrderRouter.Domain/**`, `src/OrderRouter.Infrastructure/**`, migrations, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: All errors at once | `RouteValidationTests` (both paths) | 2 |
| AC2: Bad ZIP | `RouteValidationTests` | 2 |
| AC3: Bad quantity | `RouteValidationTests` | 2 |
| AC4: Empty product code / order ID | `RouteValidationTests` | 2 |
| AC5: Malformed JSON → same envelope | `RouteValidationTests` | 2 |
| AC6: Extra fields ignored | `RouteValidationTests` (fake receives the order) | 2 |
| AC7: Unknown product reaches routing | `RouteValidationTests` (fake called) | 2 |
| AC8: Alias matches v1 | `RouteAliasTests` | 3 |
| AC9: Arrays never null | `RouteResponseTests` + `RouteValidationTests` | 1, 2 |
| AC10: OpenAPI v1 with examples and alias | `RouteOpenApiTests` + manual check | 4 |
| OR-008 follow-up: 401 before validation | `RouteAuthTests` | 2 |

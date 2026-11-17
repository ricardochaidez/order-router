# Plan: OR-016: Assign experiment arms from reloadable configuration

**Ticket:** [Docs/Tickets/OR-016-experiment-assignment.md](../Docs/Tickets/OR-016-experiment-assignment.md)
**Depends on:** OR-008 (JWT + dev key; the dev key already emits `sub = dev-key`), OR-013 (`ExperimentAssignment`, `RoutingParameters`, `RoutingPolicyFactory`), OR-015 (`RouteOrderService` with the temporary `AllControl`). All three are done.
**References:** [ADR-0005](../Docs/ADRs/ADR-0005-experiment-assignment.md) (port, algorithm, reload, `config_version`); Technical Requirements §8.2, AB-01, AB-02, AB-03, SC-25, SC-26, NFR-11, OQ-25; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Feeds:** OR-017/018/019 (treatment arms; they replace the factory throws and read `Parameters`), OR-021 (logs arms, reasons, `config_version`), OR-022 (diagnostics), OR-025 (README: how to edit `experiments.json`), OR-027 (stores arms and `config_version`).

> **Save location:** Phase 0 copies this plan to `Plans/OR-016-experiment-assignment.md`. Update that file as each phase is done.

## Context

Every request still runs on `ExperimentAssignment.AllControl`; see the `// TEMPORARY until OR-016` line in `src/OrderRouter.Domain/Services/RouteOrderService.cs`. SC-25 and SC-26 require experiments that:
- can be switched on or off without a deploy, through a reloadable `experiments.json`;
- are targeted by caller and by customer ZIP prefix or range;
- are then split per caller with a stable salted SHA-256 bucket;
- sit behind a vendor-agnostic port, so a Statsig or LaunchDarkly adapter can be added later.

OR-016 adds the `IExperimentAssigner` port and the `ConfigurationExperimentAssigner` adapter, which keeps a last-known-good snapshot. It also adds `ConfigVersion` and the exposure reasons to `ExperimentAssignment` and wires the caller ID from the authenticated principal.

Out of scope:
- treatment policies (OR-017–019; the factory keeps throwing for treatment);
- the decision log and `caller_hash` (OR-021, OR-027);
- `diagnostics` (OR-022);
- storing arms (OR-027);
- the README section (OR-025).

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Treatment arms not built yet | **Keep `RoutingPolicyFactory` throwing** for treatment. The shipped `experiments.json` is all disabled (AB-03). AC6/9/10 are verified by assigner tests and by host tests that swap in a **recording policy factory**: it captures the `ExperimentAssignment` and builds control policies. **Known risk, documented:** until OR-017–019 land, enabling an experiment in a real deployment makes targeted treatment requests return 500. |
| Client ID claim | Caller ID = `sub`, then `client_id` (RFC 9068), then `azp` (OIDC). Blank values count as missing. No claim → `null` → bucket on `order_id` (AB-01, AC11). The dev key maps to `dev-key` through its existing `sub` claim. |
| Parameters | `experiments.json` carries `Exp1ConsolidationVsQuality.Parameters.QualityUpliftThreshold`, `Exp2UnratedSuppliers.Parameters.UnratedPrior` and `Routing:SimilarityBand` (ADR-0005 example). The assigner builds `RoutingParameters` from them, and a missing value takes the `RoutingParameters` default. Invalid values are rejected and the last good config stays. `Routing:NationwideZipRange` stays in `appsettings.json`, bound once at startup (not reloadable). |

### Design choices (from the ticket, ADR-0005, earlier plans and the rules; no question needed)

| Topic | Choice and reason |
|---|---|
| Layering | Same rule as OR-015: **the Domain has all business rules**. The targeting, bucketing and per-experiment evaluation are pure Domain code. The port `IExperimentAssigner` and `AssignmentContext` live in **Domain** (ADR-0005 calls it an application port). The config binding, validation, snapshot swap, `config_version` and logging live in **Infrastructure** (`ConfigurationExperimentAssigner`). Api only extracts the principal and registers. `LayerDependencyTests` must stay green. |
| Domain types (`Domain/Entities/Experiments/`) | `ExposureReason` enum: `Disabled`, `NotTargeted`, `Bucketed`. It serializes as `disabled`, `not_targeted` and `bucketed` under the snake_case policy.<br>`ExperimentReasons(Exp1, Exp2, Exp3)` mirrors `ExperimentArms`.<br>`ExperimentTargeting(Callers, ZipPrefixes, ZipRanges)` is a value object. Callers are matched with ordinal comparison, and an empty list means everyone. A prefix has 1–5 ASCII digits. Ranges are domain `ZipRange`s. When prefixes and ranges are both empty, the experiment applies everywhere.<br>`ExperimentDefinition(Id, Enabled, TreatmentPercentage 0–100, Salt non-empty, Targeting)` reports every error in one `DomainValidationException`, the same pattern as `RoutingParameters`.<br>`ExperimentBucketer.Bucket(id, salt, unit)` computes `SHA256(UTF8("{id}:{salt}:{unit}"))`, reads the first 8 bytes with **`BinaryPrimitives.ReadUInt64BigEndian`** (explicit byte order, so results match across platforms, AC4), and takes the result `% 100`. `string.GetHashCode` is never used.<br>`ExperimentConfiguration(exp1, exp2, exp3, RoutingParameters, ConfigVersion)` is the immutable snapshot. Its `Assign(string? callerId, ZipCode zip, string orderId)` → `ExperimentAssignment` follows the ADR flowchart for each experiment: disabled → targeted → bucket on `callerId ?? orderId` → `bucket < pct`. |
| Experiment IDs | They are the config section keys `Exp1ConsolidationVsQuality`, `Exp2UnratedSuppliers` and `Exp3PartialRouting`, the same names as the `ExperimentArms` properties. Each experiment has its own ID and salt, so arms are independent (AC8). |
| `ExperimentAssignment` | Gains `Reasons` (`ExperimentReasons`) and `ConfigVersion` (non-empty string). `AllControl` stays, with reasons `Disabled` and `ConfigVersion = "defaults"`, and is still used by tests and fallbacks. Update `ExperimentAssignmentTests` for the new constructor. |
| Port (`Domain/Helpers/ and Domain/Interfaces/`) | `IExperimentAssigner { ExperimentAssignment Assign(AssignmentContext context); }` is synchronous and never throws on a valid context. `AssignmentContext(string? CallerId, ZipCode CustomerZip, string OrderId)`. |
| Caller ID | `CallerIdentity.Resolve(ClaimsPrincipal)` lives in Domain and uses only BCL `System.Security.Claims`; the order is the one in *Decisions*. `RouteOrderCommand` gains a trailing `string? CallerId = null`, and `Create` gains an optional `callerId`, so existing calls still compile. `RouteController.RouteOrder` binds `ClaimsPrincipal user` and passes the resolved ID. The raw caller ID is never logged (NFR-11). |
| Service | `RouteOrderService` takes `IExperimentAssigner`. It calls `Assign(new AssignmentContext(command.CallerId, command.CustomerZip, command.OrderId))` and passes the result to `policyFactory.Create`. The `TEMPORARY` line is removed. |
| Configuration shape (`experiments.json`) | Same as the ADR-0005 example, with every experiment `Enabled: false`, `TreatmentPercentage: 0`, salts `exp1`/`exp2`/`exp3`, empty targeting, default parameters, and `"Routing": { "SimilarityBand": 0.5 }`. The options classes (Infrastructure) are bound to the **configuration root**, so `Experiments` and `Routing:SimilarityBand` reload as one snapshot. Unknown keys are ignored. A missing experiment section counts as disabled (fail safe, AB-03). |
| Validation | One mapper, `ExperimentMappingExtensions.TryBuild(options, out ExperimentConfiguration?, out errors)`, turns options into the domain snapshot. The startup validator (`ValidateOnStart`) and the reload path both use it, so the two can never disagree. It rejects:<br>• a percentage outside 0–100 (AC7);<br>• an empty salt;<br>• a ZIP range that is not 5 digits, such as `2100-2199` (AC12, via `ZipRange.TryParse`, no padding);<br>• a prefix that is not 1–5 digits;<br>• invalid `RoutingParameters`.<br>All errors are reported at once. Error text never echoes caller values (NFR-11); ZIP values from config are fine. |
| Reload and last known good | `ConfigurationExperimentAssigner` (singleton) builds its first snapshot from `IOptionsMonitor<ExperimentsOptions>.CurrentValue` and subscribes with `OnChange`. A valid change swaps the immutable snapshot atomically (`Volatile.Write`) and logs `ExperimentsConfigApplied(config_version)` at Information. An invalid change keeps the old snapshot and logs `ExperimentsConfigRejected(kept config_version, errors)` at Error (AC7, AC12). Logging uses `[LoggerMessage]`. The subscription is disposed with the assigner. **Spike first (Phase 3):** check whether `OptionsMonitor` throws inside its change callback when a registered `IValidateOptions` fails. If it does, keep validation out of the monitor: validate at startup with a hosted check that calls the same mapper, and validate on reload in the assigner. That stays faithful to ADR-0005's behaviour, but if it means departing from its wording, stop and ask. |
| `config_version` | The first 12 lowercase hex characters of SHA-256 over the canonical JSON of the accepted snapshot. The JSON is built from parsed values in a fixed property order (System.Text.Json), so a whitespace-only edit keeps the version and any value change alters it (AC6). |
| File loading | `builder.Configuration.AddJsonFile("experiments.json", optional: true, reloadOnChange: true)`. It is inserted so that environment variables and the command line still override it (ADR-0005). The file is copied to the output and the publish folder. A missing file means all disabled, the fail-safe default. `docker-compose.yml` mounts it read-only into `/app` and sets `DOTNET_USE_POLLING_FILE_WATCHER=true`. |
| DI | Infrastructure gets a new `AddOrderRouterExperiments()` extension that binds the options, adds the validator and `ValidateOnStart`, and registers `IExperimentAssigner` → `ConfigurationExperimentAssigner` as a singleton. Api calls it in `Startup.cs`, and `Startup.cs` names no Domain types. |
| Tests | xUnit v3, `MethodName_Condition_ExpectedResult`, in the nearby style with no Arrange/Act/Assert comments. Domain tests are pure. Assigner tests use a small test `IOptionsMonitor` that can trigger `OnChange`. Host tests extend `RouteApiFactory`/`AuthApiFactory`:<br>• a **temp experiments file** added through `ConfigureAppConfiguration` with `reloadOnChange`;<br>• a **`RecordingPolicyFactory`** that captures the assignment and returns the control `RoutingPolicy`;<br>• `TestJwt` overloads for a custom `sub`, for `client_id`/`azp`, and for no caller claim;<br>• the existing `CapturingLogger` for the error log.<br>Reload tests poll with a timeout rather than a fixed sleep. Target ≥ 85% coverage of the new Domain/Domain code; measure it or say it was not measured. |

### Target structure

```
src/OrderRouter.Domain/Entities/Experiments/
  ExposureReason.cs, ExperimentReasons.cs                 reasons
  ExperimentTargeting.cs, ExperimentDefinition.cs         rules + validation
  ExperimentBucketer.cs                                   salted SHA-256 bucket
  ExperimentConfiguration.cs                              snapshot + Assign (pure)
  ExperimentAssignment.cs                                 + Reasons, ConfigVersion
src/OrderRouter.Domain/Helpers/
  IExperimentAssigner.cs, AssignmentContext.cs, CallerIdentity.cs
src/OrderRouter.Domain/Services/
  RouteOrderCommand.cs (+CallerId), RouteOrderService.cs (uses assigner)
src/OrderRouter.Infrastructure/Services/
  ExperimentsOptions.cs, ExperimentMappingExtensions.cs, ExperimentsOptionsValidator.cs
  ConfigVersion.cs, ConfigurationExperimentAssigner.cs, ExperimentsServiceCollectionExtensions.cs
src/OrderRouter.Api/
  experiments.json (new, all disabled), Startup.cs, OrderRouter.Api.csproj (copy file)
  Controllers/V1/RouteController.cs (User → CallerId)
docker-compose.yml                                        mount + polling watcher
tests/OrderRouter.Tests/Domain/Entities/Experiments/   Targeting, Definition, Bucketer, Configuration, Assignment
tests/OrderRouter.Tests/Domain/Services/          CallerIdentityTests, RouteOrderServiceTests (updated)
tests/OrderRouter.Tests/Infrastructure/Services/   Mapper/Validator, ConfigVersion, ConfigurationExperimentAssigner, shipped-file test
tests/OrderRouter.Api.IntegrationTests/Controllers/V1/               ExperimentAssignmentHostTests, RecordingPolicyFactory, TestJwt overloads
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Before writing code, read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md`, and follow them. This is a cross-layer feature, so use the `ddd-implementation` skill (`/ddd-implementation`): run §1 domain analysis and §2 architecture review at the start of Phase 1; §3 is this plan; §4 implementation runs through Phases 1–4; run the §5 post-implementation review in Phase 5.
   - **C#:** C# 14 / .NET 10, file-scoped namespaces, newline before every opening brace, `is null` / `is not null`, pattern matching and switch expressions, `nameof`, records for value objects, final `return` on its own line, `.editorconfig`.
   - **Docs and comments:** XML doc comments (with `<example>` where useful) on public types. Comments explain *why* and cite the requirement or ADR (`// AB-01`, `// ADR-0005`). Explain in a comment why the code uses `System.Security.Cryptography` and why it never uses `string.GetHashCode`.
   - **Fail safe:** invalid or ambiguous config never reaches a request. Keep the last good snapshot, and with no config at all, use control. The request path never throws because of config.
   - **Privacy:** never log a raw caller ID or a `Callers` list value (NFR-11). Log only `config_version` and validation messages that don't echo them.
   - **Data:** never edit `Docs/Data/*`.
2. **Tests must pass after each phase.** Before marking a phase done, run these from the repo root. Start PostgreSQL first with `docker compose up -d postgres` so the integration tests run too.
   - `dotnet build`: 0 errors, 0 new warnings.
   - `dotnet test`: everything passes, including every existing test. The only intended changes to existing tests are the `ExperimentAssignment` constructor update (Phase 1) and the `RouteOrderService` constructor update (Phase 2). Record any other change in the phase result.
   - `dotnet format --verify-no-changes`: clean. The known import-order issue in `src/OrderRouter.Api/Program.cs` predates this work; don't count it and don't fix it here.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-016-experiment-assignment.md`, tick its checklist items, and add a short **Result** line with test counts and any deviation.
4. **Ask before the next phase.** After marking a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If an instruction, rule, ticket text, ADR or behaviour is unclear or contradicts another, stop and ask. Known risks:
   - the Phase 3 spike shows the ADR's `ValidateOnStart` + `IValidateOptions` cannot keep the last good snapshot without deviating from ADR-0005's wording;
   - a `LayerDependencyTests` or `ReadPathBoundaryTests` objection to the new types;
   - the JwtBearer handler rejecting a test token with no `sub`;
   - configuration source ordering that lets `experiments.json` override environment variables;
   - the container file watcher not seeing edits through the single-file bind mount.
6. **No commits unless the engineer asks.** If asked, branch off `main` first (for example `feature/OR-016-experiment-assignment`).

---

## Phase 0: Save the plan ✅ Done

- [x] Create `Plans/OR-016-experiment-assignment.md` with this plan.
- [x] Confirm the three engineer decisions are recorded under *Decisions confirmed with the engineer*.

**Verify:** the file exists. No code changes, so there is nothing to test.

**Result:** Plan saved. The three engineer decisions are recorded: the factory keeps throwing for treatment and tests use a recording factory; the caller ID is `sub` → `client_id` → `azp`; all three parameters come from `experiments.json`. No code changed, so no tests were run.

---

## Phase 1: Domain assignment rules ✅ Done

- [x] Run `ddd-implementation` §1 and §2. Concepts: experiment, targeting, unit of randomization, bucket, arm, exposure reason, config snapshot. There is no aggregate, no persistence and no event, and the rules stay apart from state.
- [x] Add `ExposureReason`, `ExperimentReasons`, `ExperimentTargeting`, `ExperimentDefinition`, `ExperimentBucketer` and `ExperimentConfiguration` (see *Design choices*).
- [x] Extend `ExperimentAssignment` with `Reasons` and `ConfigVersion` (both validated), and keep `AllControl`. Update `ExperimentAssignmentTests`.
- [x] Tests:
  - AC2: prefix `021`, ZIP `10015` → control, `NotTargeted`.
  - AC3: 50%, one caller, 20 different `order_id`s → one arm.
  - AC4: a known-answer vector, i.e. a fixed (id, salt, unit) → a hard-coded bucket constant, computed once and pinned. A second vector proves that the salt and the ID change the bucket.
  - AC5: for 1,000 callers, the treatment set at 20% ⊆ the treatment set at 50%.
  - AC8: 10,000 callers, two experiments at 50%: each cell of the 2×2 table is about 25% within a stated tolerance. The result is deterministic.
  - AC9: callers `[portal-client]` at 100% → `portal-client` treatment / `Bucketed`, `other-client` control / `NotTargeted`.
  - AC10: range `30000-30999` at 100% → `30500` treatment, `31000` `NotTargeted`.
  - AC11: `callerId` null → the bucket equals the bucket of `order_id`, every time.
  - Disabled → `Disabled`, whatever the targeting. Enabled at 0% → control / `Bucketed`. Caller list non-empty and no caller → `NotTargeted`.
  - Validation: percentage −1 and 101, empty salt, a 4-digit prefix and a 6-digit prefix, all errors in one exception.
  - Prefix and range edges: inclusive range ends; a prefix matched against the 5-digit text.
- [x] `LayerDependencyTests` still green.

**Verify:** build, test, format.

**Result:** Domain types added; 1141 tests pass (1084 before). Deviation: the plan lists "a 4-digit prefix" as invalid but also defines a prefix as 1–5 digits, so a 4-digit prefix such as `0210` is valid; tests cover empty, 6-digit and non-digit prefixes instead. `ExperimentAssignment` keeps the old 2-argument constructor through optional `reasons`/`configVersion` parameters (defaults `all disabled` / `"defaults"`), so `OrderRoutingServiceTests` and `RoutingPolicyFactoryTests` needed no change. Format shows only the known `Program.cs` import-order issue.

---

## Phase 2: Domain port, caller ID and service ✅ Done

- [x] Add `IExperimentAssigner`, `AssignmentContext` and `CallerIdentity`.
- [x] Add `CallerId` to `RouteOrderCommand` (trailing optional) and to `Create`.
- [x] `RouteOrderService`: inject `IExperimentAssigner`, build the context, and remove the `TEMPORARY` `AllControl` line. Update the `RouteOrderServiceTests` constructor.
- [x] Register nothing new in `AddOrderRouterRouting`; the assigner comes from Infrastructure (Phase 3). Note that in the XML doc of the extension.
- [x] Tests:
  - `CallerIdentityTests`: `sub` wins; `client_id` when there is no `sub`; `azp` when there is neither; blank `sub` falls through; no claims → null; a dev-key principal → `dev-key`.
  - `RouteOrderServiceTests`: the fake assigner receives the caller ID, ZIP and order ID; the factory receives exactly the assignment the assigner returned.

**Verify:** build, test, format. The host cannot start until Phase 3 registers the assigner, so if integration tests fail only because `IExperimentAssigner` is not registered, register a temporary `AllControl` assigner in Api for this phase and record it.

**Result:** Port, `CallerIdentity`, `RouteOrderCommand.CallerId` and the service done; 990 unit tests pass. Integration tests could not start the host until Phase 3 registered the assigner, so no temporary registration was added; the phases were completed back to back.

---

## Phase 3: Infrastructure: options, validation, reload ✅ Done

- [x] **Spike:** write a test that shows how `IOptionsMonitor` behaves on a reload that fails validation. Pick the reload design from *Design choices* according to the result. If it deviates from ADR-0005's wording, stop and ask.
- [x] Add `ExperimentsOptions` (root-bound), `ExperimentMappingExtensions`, `ExperimentsOptionsValidator`, `ConfigVersion`, `ConfigurationExperimentAssigner` (atomic swap, last known good, `[LoggerMessage]` applied/rejected events) and `AddOrderRouterExperiments`.
- [x] Tests:
  - Mapper/validator: valid config; AC7 (150); AC12 (`2100-2199`); a bad prefix; an empty salt; invalid parameters (`UnratedPrior` 11, `SimilarityBand` −1); all errors at once; missing sections → disabled with defaults; error text without caller values.
  - `ConfigVersion`: stable for the same values, changes when any value changes, ignores whitespace and formatting.
  - Assigner (with the test monitor): AC6 (a valid change → new arms and new `config_version` on the next `Assign`); AC7 and AC12 (an invalid change → previous arms and version kept, one Error log); recovery (invalid, then valid → applied); `Parameters` come from config.
  - Shipped file: parse `src/OrderRouter.Api/experiments.json`. It is valid and every experiment is disabled (AC1).

**Verify:** build, test, format.

**Result:** Infrastructure done. **Spike result:** a failing registered validator makes `IOptionsMonitor` throw inside its reload callback and never notify listeners, so validation is called directly (startup check and assigner) through the one mapper; same behaviour as ADR-0005, recorded in its implementation note. Binding errors are also recorded, not thrown. Mapper reports every error at once, including with invalid targeting. Added package `Microsoft.Extensions.Options.ConfigurationExtensions`. The spike test stays as documentation. 1184 tests pass.

---

## Phase 4: API wiring, container and host tests ✅ Done

- [x] Add `src/OrderRouter.Api/experiments.json` (all disabled) and copy it to the output and publish folders.
- [x] `Startup.cs`: add the file source with `reloadOnChange`, keeping env and command-line precedence; call `AddOrderRouterExperiments()`; remove any temporary Phase 2 registration.
- [x] `RouteController.RouteOrder`: read `User`, call `CallerIdentity.Resolve`, pass the ID into the command.
- [x] `docker-compose.yml`: mount `experiments.json` read-only into `/app` and set `DOTNET_USE_POLLING_FILE_WATCHER=true`.
- [x] Test support: the `RecordingPolicyFactory`, a temp-file option in the factory, and `TestJwt` overloads (custom `sub`, `client_id`, `azp`, no caller claim).
- [x] `ExperimentAssignmentHostTests` (real host, seeded DB, recording factory):
  - AC1: shipped config → every experiment control / `disabled`, and the response is still 200 (existing end-to-end tests stay green).
  - AC6: the temp file goes from disabled to enabled at 100% → the next request's captured assignment is treatment and `config_version` differs.
  - AC7 and AC12: write `TreatmentPercentage: 150`, then the range `2100-2199` → the captured arms and version are unchanged and an Error log is captured.
  - AC9: JWT `sub` `portal-client` vs `other-client`.
  - AC10: ZIP `30500` vs `31000`.
  - AC11: a JWT with no `sub`/`client_id`/`azp` → the same `order_id` gives the same arm across requests.
  - The dev key → caller `dev-key`.
  - Startup with an invalid shipped-shape file fails (`ValidateOnStart`).
- [x] Manual check: run `docker compose up -d --build --wait` and POST ORD-001 with the dev key → 200. Then edit the mounted `experiments.json`, for example change a salt while staying disabled, and confirm the `ExperimentsConfigApplied` log shows a new `config_version` without a restart. Write an invalid value and confirm `ExperimentsConfigRejected`. Restore the file afterwards.

**Verify:** build, test, format.

**Result:** Wiring, `experiments.json`, compose and host tests done (12 host tests incl. source-order and startup failure); 1197 tests pass. Manual docker check passed: ORD-001 → 200 with the dev key; an in-place salt edit logged `Experiment configuration applied. config_version=ae100382ede2`; `TreatmentPercentage: 150` logged the rejected event and kept that version; file restored. **Finding:** an edit that replaces the file (`sed -i`) detaches the single-file bind mount (container then sees no file), so edits must be in place; documented in compose and the ADR note. A malformed file on reload does not crash the host and keeps the last good snapshot (the provider fails before notifying); no guard code was needed.

---

## Phase 5: Docs and post-implementation review ✅ Done

- [x] ADR-0005: add an implementation note (OR-016) covering:
  - the client ID claims, `sub` → `client_id` → `azp`;
  - experiment IDs are the section keys;
  - the big-endian bucket read;
  - the 12-hex `config_version`;
  - the reload design chosen in the Phase 3 spike;
  - the known risk of enabling an experiment before OR-017–019.
- [x] Run the `ddd-implementation` §5 review and report it honestly:
  - **met:** rules in the Domain, a port in Domain, the adapter in Infrastructure, DIP, fail safe, deterministic hashing, no PII in logs;
  - **N/A, with reasons:** aggregates, domain events, money, persistence, authorization;
  - **not verified:** treatment routing (OR-017–019), decision log (OR-021), multi-instance reload timing, coverage % if it was not measured.
- [x] Record follow-ups (don't do them here):
  - OR-017/018/019 replace the factory throws and remove the 500 risk;
  - OR-021 logs `Reasons` and `ConfigVersion`, plus `caller_hash` from OR-027;
  - OR-022 exposes the arms;
  - OR-025 documents editing `experiments.json` and the polling watcher;
  - OR-027 stores the arms and `config_version`;
  - OQ-25 (single service identity).
- [x] Final run: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, all green.

**Result:** ADR-0005 implementation note added. Line coverage of the new/changed Domain and Infrastructure code from unit tests: 100% (branch coverage not measured). Final build/test/format results are in the hand-off message. §5 review: met — rules in Domain, port in Domain, adapter in Infrastructure, DIP, fail safe, deterministic hashing, no caller IDs in logs. N/A — aggregates, domain events, money, persistence, authorization. Not verified — treatment routing (OR-017..019), decision log (OR-021), multi-instance reload timing. Follow-ups: OR-017/018/019 replace the factory throws; OR-021 logs `Reasons`, `ConfigVersion` and `caller_hash`; OR-022 exposes arms; OR-025 documents editing `experiments.json` (in-place edits, polling watcher); OR-027 stores arms and `config_version`; OQ-25.

---

## Critical files

| Action | Path |
|---|---|
| New | `Plans/OR-016-experiment-assignment.md`; the Domain, Infrastructure and test files in *Target structure*; `src/OrderRouter.Api/experiments.json` |
| Modified | `src/OrderRouter.Domain/Entities/Experiments/ExperimentAssignment.cs`; `src/OrderRouter.Domain/Models/RouteOrderCommand.cs`, `RouteOrderService.cs`; `src/OrderRouter.Api/Startup.cs`, `OrderRouter.Api.csproj`, `Controllers/V1/RouteController.cs`; `docker-compose.yml`; `tests/.../ExperimentAssignmentTests.cs`, `RouteOrderServiceTests.cs`, `Authentication/TestJwt.cs`, `Routing/RouteApiFactory.cs`; ADR-0005 (note) |
| Reused | `ZipCode`, `ZipRange` (`Domain/ReferenceData`, no padding); `RoutingParameters` and its validation; `DomainValidationException` with the all-errors pattern; `ExperimentArms`; `RoutingPolicyFactory` (control path, unchanged); `DevApiKeyDefaults.CallerId` (`dev-key`); `RoutingOptionsValidator` and `PlanSearchOptionsValidator` (validator pattern); `AuthApiFactory`, `RouteApiFactory`, `TestJwt`, `CapturingLogger`, `ReferenceDataFixture` |
| Unchanged | `RoutingPolicyFactory` (still throws for treatment), policies, `OrderRoutingService`, persistence, `Docs/Data/**` |

## Verification summary (ticket acceptance criteria)

| AC | Verified by | Phase |
|---|---|---|
| AC1: default is control / `disabled` | `ExperimentConfiguration` tests, shipped-file test, host test | 1, 3, 4 |
| AC2: prefix `021` vs ZIP `10015` → `not_targeted` | Domain tests | 1 |
| AC3: same caller, 20 orders, same arm | Domain tests | 1 |
| AC4: stable across processes | Known-answer bucket vector (big-endian SHA-256; no `GetHashCode`) | 1 |
| AC5: ramp-up only adds | Subset property test over 1,000 callers | 1 |
| AC6: reload without restart; `config_version` changes | Assigner test with test monitor; host temp-file test; manual docker check | 3, 4 |
| AC7: invalid edit (150) keeps last good + error log | Validator, assigner and host tests | 3, 4 |
| AC8: arms independent | 2×2 distribution test over 10,000 callers | 1 |
| AC9: caller targeting `portal-client` | Domain test; host test with JWT `sub` | 1, 4 |
| AC10: ZIP range `30000-30999` | Domain test; host test | 1, 4 |
| AC11: no caller ID → `order_id` bucket | Domain test; `CallerIdentityTests`; host test with no caller claim | 1, 2, 4 |
| AC12: 4-digit range rejected, last good kept | Validator, assigner and host tests | 3, 4 |

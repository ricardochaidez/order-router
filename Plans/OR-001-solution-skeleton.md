# Plan: OR-001 — Set up the solution and layer projects

**Ticket:** [Docs/Tickets/OR-001-solution-skeleton.md](../Docs/Tickets/OR-001-solution-skeleton.md)
**References:** Technical Requirements §13.1 (C-01), NFR-08; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`; `CLAUDE.md`
**Plan file location (once approved):** `Plans/OR-001-solution-skeleton.md`

## Context

The repo has only the ASP.NET Core template project (`order-router.csproj` at the root, with the `/weatherforecast` sample). Every later ticket (OR-002 onward) assumes a layered DDD solution. OR-001 creates that structure with no business logic: a .NET 10 solution with Domain, Application, Infrastructure and Api projects, a unit test project and an integration test project. It also adds an architecture test that enforces the layer rules, so everyone builds on the same structure.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Layout and names | `src/OrderRouter.{Domain,Application,Infrastructure,Api}`, `tests/OrderRouter.{UnitTests,IntegrationTests}`. This matches `tools/OrderRouter.DataNormalizer` from OR-003. `order-router.csproj` is moved and renamed to `src/OrderRouter.Api/OrderRouter.Api.csproj`. |
| Test stack | xUnit v3. No architecture-test library. |
| Architecture test (AC2) | Read each layer's `.csproj` and assert the **exact** `ProjectReference` set. Also run a **reflection** check that no forbidden assembly is referenced at compile time. A reflection check alone can't prove the *required* references, because the compiler drops references to unused projects. |
| Template sample | Remove `/weatherforecast` and its `.http` request. Keep the OpenAPI wiring. |
| `.editorconfig` | It doesn't exist yet, so this plan creates one that encodes the `csharp.md` formatting rules. |

### Target dependency graph (AC2)

```
Domain          → (nothing)
Application     → Domain
Infrastructure  → Domain, Application
Api             → Application, Infrastructure
UnitTests       → all four src projects (needed to load assemblies for the reflection check)
IntegrationTests→ Api
```

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` before you write code, and follow them. Key points for this ticket:
   - C# 14 / .NET 10. Use file-scoped namespaces, a newline before every opening brace, `is null` / `is not null`, and PascalCase / camelCase / `I`-prefix naming.
   - Add XML doc comments on every public type, and comments that explain *why* a design choice was made.
   - Test names follow `MethodName_Condition_ExpectedResult`. Don't write `Arrange` / `Act` / `Assert` comments.
   - Use the standard .NET templates (`dotnet new ...`) to create projects.
   - Use the `ddd-implementation` skill's process at the scale this ticket needs. Phase 1 records a short domain analysis (no aggregates, no domain events, layers only). Phase 6 runs the skill's post-implementation review and honestly reports which checks don't apply.
2. **Tests must pass after each phase.** Before you mark a phase done, run `dotnet build` and `dotnet test` from the repo root, plus each phase's own checks. All must succeed with **0 errors**. Starting in Phase 1, `dotnet format --verify-no-changes` must also pass.
3. **Mark each phase as done.** When a phase's checks pass, change its heading status from `⬜ Not started` to `✅ Done` in `Plans/OR-001-solution-skeleton.md`, and tick its checklist items.
4. **Ask before the next phase.** After you mark a phase done, stop. Summarize what changed and the test results, then ask the engineer for approval before you start the next phase.
5. **Stop and ask if something is ambiguous or conflicts.** If any instruction, rule or ticket text is unclear or contradicts another, stop and ask the engineer. Don't guess.
6. No commits unless the engineer asks. If asked, branch off `main` first (for example `feature/OR-001-solution-skeleton`).

---

## Phase 0 — Save the plan ✅ Done

- [x] Create `Plans/OR-001-solution-skeleton.md` with this plan's contents. That file is the one whose status gets updated from now on.

**Verify:** the file exists. There's no code yet, so there's nothing to test.

---

## Phase 1 — Repository foundations and the Api project move ✅ Done

- [x] Write a short `ddd-implementation` domain analysis in the plan file, under Phase 1 notes. It covers: no domain concepts or aggregates in this ticket; the affected layers are all four; no security or compliance impact beyond keeping layers isolated.
- [x] Add `.editorconfig` at the root that encodes `csharp.md`:
  - `csharp_style_namespace_declarations = file_scoped:warning`
  - `csharp_new_line_before_open_brace = all`
  - The `is null` preference
  - Naming rules: interfaces `I`-prefixed, private fields camelCase, public members PascalCase
  - `using` directives outside the namespace
  - 4-space indent and a final newline. Also UTF-8, and 2-space indent for `*.json`/`*.csproj`/`*.props`/`*.slnx`.
- [x] Add `Directory.Build.props` at the root with the shared settings: `TargetFramework net10.0`, `LangVersion 14`, `Nullable enable`, `ImplicitUsings enable`, `EnforceCodeStyleInBuild true`, `GenerateDocumentationFile true`. Leave `TreatWarningsAsErrors` off for now, and add a comment saying why.
- [x] Move the Api project with `git mv` so history is kept:
  - `order-router.csproj` → `src/OrderRouter.Api/OrderRouter.Api.csproj`
  - Move `Program.cs`, `appsettings*.json` and `Properties/launchSettings.json` with it
  - `order-router.http` → `src/OrderRouter.Api/OrderRouter.Api.http`
- [x] In the Api `.csproj`, set `RootNamespace` to `OrderRouter.Api`, and remove the properties that now come from `Directory.Build.props`.
- [x] Clean up `Program.cs`:
  - Remove the WeatherForecast endpoint, the `summaries` array and the `WeatherForecast` record.
  - Keep `AddOpenApi` / `MapOpenApi` and `UseHttpsRedirection`.
  - Add brief comments that explain the composition-root role.
  - .NET 10 makes the top-level `Program` class public automatically for `WebApplicationFactory`. Only add `public partial class Program;` if Phase 4 shows it's needed.
- [x] Update the `.http` file: remove the weather request and keep the host variable. Rename the variable to `@OrderRouter_Api_HostAddress`.
- [x] Delete the old untracked root `bin/` and `obj/` folders. They are gitignored build output.
- [x] Create the solution `OrderRouter.slnx` with `dotnet new sln -n OrderRouter` (.NET 10 creates `.slnx`), then run `dotnet sln add src/OrderRouter.Api`.

**Domain analysis (ddd-implementation):** no domain concepts, aggregates or domain events in this ticket; all four layers are affected, but only structurally; no security or compliance impact beyond keeping layers isolated.

**Result:** `dotnet build` 0 warnings / 0 errors; `dotnet format --verify-no-changes` clean; `GET /openapi/v1.json` returned 200 and `/weatherforecast` returned 404 in Development; `dotnet test` has nothing to run yet.

**Verify:**
- `dotnet build` succeeds.
- `dotnet format --verify-no-changes` passes.
- `dotnet run --project src/OrderRouter.Api` starts, `GET /openapi/v1.json` returns 200 in Development, and `/weatherforecast` returns 404.
- There are no test projects yet, so `dotnet test` has nothing to run.

---

## Phase 2 — Domain, Application and Infrastructure projects ✅ Done

- [x] Create the projects with `dotnet new classlib`:
  - `src/OrderRouter.Domain`
  - `src/OrderRouter.Application`
  - `src/OrderRouter.Infrastructure`
- [x] Delete the template `Class1.cs` files, and remove the properties that duplicate `Directory.Build.props`.
- [x] Add one marker type to each layer: `DomainAssemblyMarker`, `ApplicationAssemblyMarker` and `InfrastructureAssemblyMarker`.
  - Each is a `public static class` with an XML doc that explains why it exists: tests and DI scanning use it to get the layer's `Assembly`.
  - It contains no business logic. The Api layer uses `Program` instead.
- [x] Add the project references from the AC2 graph (`dotnet add reference`):
  - Application → Domain
  - Infrastructure → Domain and Application
  - Api → Application and Infrastructure
- [x] Add all three projects to `OrderRouter.slnx`, in `src` solution folders.

**Result:** `dotnet build` 4 projects, 0 warnings / 0 errors; `dotnet format --verify-no-changes` clean; `dotnet list reference` matches the AC2 graph exactly; `dotnet test` has no test projects yet.

**Verify:**
- `dotnet build` builds all 4 projects with 0 errors.
- `dotnet format --verify-no-changes` passes.
- `dotnet list <proj> reference` shows exactly the AC2 graph.

---

## Phase 3 — Test projects with placeholder tests (AC3) ✅ Done

- [x] Create `tests/OrderRouter.Tests` and `tests/OrderRouter.Api.IntegrationTests`. Only the xUnit **v2** template (`xunit`) is installed, so create each with `dotnet new xunit` and then switch it to xUnit v3:
  - Replace `xunit` with `xunit.v3`.
  - Keep `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` (v3-compatible versions).
  - Set `<OutputType>Exe</OutputType>`, which xUnit v3 requires.
  - Pin explicit versions.
  - If installing `xunit.v3.templates` is the better choice, ask the engineer first, because it changes their global templates.
- [x] UnitTests references all four `src` projects. IntegrationTests references Api and adds `Microsoft.AspNetCore.Mvc.Testing` (10.0.x).
- [x] Add placeholder tests:
  - UnitTests: `Placeholder_WhenRun_Passes`. It's a trivial assertion, with an XML/inline comment saying it gets replaced by real tests in OR-002+.
  - IntegrationTests: `GetOpenApiDocument_InDevelopment_ReturnsOk`. It uses `WebApplicationFactory<Program>` with the `Development` environment and calls `GET /openapi/v1.json`. This proves the host composes and boots without adding business logic.
- [x] Add both projects to `OrderRouter.slnx`, in `tests` solution folders.

**Result:** `dotnet build` 6 projects, 0 warnings / 0 errors; `dotnet test` 2 passed, 0 failed; `dotnet format --verify-no-changes` clean. Deviation: xUnit v3 runs on Microsoft.Testing.Platform, and the .NET 10 SDK refuses `dotnet test` unless opted in, so a root `global.json` with `"test": {"runner": "Microsoft.Testing.Platform"}` was added (no SDK pin).

**Verify:**
- `dotnet build` succeeds.
- `dotnet test` runs both projects and reports **2 passed, 0 failed**.
- `dotnet format --verify-no-changes` passes.

---

## Phase 4 — Architecture test that enforces the layer references (AC2) ✅ Done

- [x] Add `tests/OrderRouter.Tests/Architecture/LayerDependencyTests.cs`.
- [x] Add a project-reference check, which is the main AC2 assertion:
  - Find the repo root by walking up from `AppContext.BaseDirectory` until `OrderRouter.slnx` is found.
  - Parse `src/<Layer>/<Layer>.csproj` with `System.Xml.Linq`, and collect the file names of its `ProjectReference` `Include` values.
  - Use a `[Theory]` with one row per layer to assert the **exact** expected set:
    - Domain → ∅
    - Application → {Domain}
    - Infrastructure → {Domain, Application}
    - Api → {Application, Infrastructure}
  - Example test name: `ProjectReferences_ForLayer_MatchAllowedSet`.
- [x] Add a compile-time reflection check. It loads each layer through its marker type or `Program`, then reads `Assembly.GetReferencedAssemblies()` and asserts that no **forbidden** `OrderRouter.*` assembly appears. For example, Domain references no `OrderRouter.*` assembly. Example test name: `ReferencedAssemblies_ForLayer_ContainNoForbiddenLayer`.
  - Add a comment explaining why this check only covers forbidden references: the compiler drops references to unused projects, so a required reference can't be proven here.
- [x] Do a one-time manual check that the test actually catches violations, then revert it:
  - Temporarily add a forbidden reference (Domain → Application) and confirm the test fails.
  - Revert the change.
  - Note the result in the plan file.

**Negative check result:** the plan's example (Domain → Application) can't be built: MSBuild rejects it as a circular dependency before any test runs. That holds for every forbidden edge among the layers except one that skips a layer, so I used Api → Domain instead. `ProjectReferences_ForLayer_MatchAllowedSet(layer: "Api")` failed as expected (1 of 10 failed); the change was reverted and all 10 pass again. The reflection test is not triggered by this case, because the compiler drops the unused reference.

**Result:** `dotnet test` 10 passed, 0 failed (2 placeholders + 4 + 4 architecture rows); `dotnet format --verify-no-changes` clean.

**Verify:**
- `dotnet test` passes every test: the placeholders plus all architecture theory rows.
- `dotnet format --verify-no-changes` passes.

---

## Phase 5 — Fresh-clone verification (AC1, AC2, AC3 end to end) ✅ Done

- [x] Clone the working tree into the scratchpad. Use `git clone` of a temporary commit, or `git stash` + copy of the tracked and new files, so nothing in `bin/` or `obj/` carries over. Then from the clone's root run:
  - `dotnet build`: every project builds with 0 errors (AC1)
  - `dotnet test`: placeholder tests in both projects pass (AC3), and the architecture tests pass (AC2)
- [x] Confirm that `git status` in the real repo shows only the intended changes: the moved files, the new projects, the root config files and the `Plans/` file. Nothing from `bin/` or `obj/` should appear.

**Verify:** both commands succeed in the clean clone. Record the output summary in the plan file.

**Result:** the working tree (tracked + untracked, gitignore respected; no `bin/`/`obj/`) was copied to the scratchpad. There, `dotnet build` gave 0 warnings / 0 errors, and `dotnet test` gave 10 passed, 0 failed. `git status` in the real repo shows only the intended changes (moves, new projects, root config files incl. `global.json`, `Plans/`, `.claude/`); nothing from `bin/` or `obj/`. `Docs/AI_PROMPTS.md` was already modified before this work.

---

## Phase 6 — Post-implementation review (`ddd-implementation` §5) ✅ Done

- [x] Run the skill's review checklist and report honestly in the plan file:
  - **Applies:** layering and DIP direction, naming conventions, XML docs on public types, test naming, and the `.editorconfig` / `dotnet format` result.
  - **N/A for this ticket:** aggregates, domain events, money / `decimal`, and compliance. Mark each as N/A with a reason; don't claim it was met.
- [x] Note the follow-ups that belong to other tickets, so nobody adds them here:
  - The README build/run/test section is OR-025.
  - The container and health checks are OR-007.
  - Authentication is OR-008.
- [x] Final `dotnet build` and `dotnet test` from the repo root: all green.

### Review report

**Applies**
| Check | Result |
|---|---|
| Layering / DIP direction | Met. Domain → ∅, Application → Domain, Infrastructure → Domain + Application, Api → Application + Infrastructure; enforced by `LayerDependencyTests`. |
| Naming conventions | Met. PascalCase types, `I`-prefix rule and camelCase private fields set in `.editorconfig`; the one private field (`allowedReferences`, `layerAssemblies`) is camelCase. |
| XML docs on public types | Met for all `src` types (the three markers). Partly met in tests: `LayerDependencyTests` is documented, `PlaceholderTests`, `ApiHostTests` and the `Layers` member are not. Test projects have `GenerateDocumentationFile` off; I treated test classes as not public APIs. Say if you want docs added. |
| Test naming / no AAA comments | Met. `MethodName_Condition_ExpectedResult`; no Arrange/Act/Assert comments. |
| `.editorconfig` / `dotnet format` | Met. `dotnet format --verify-no-changes` is clean. Note: the `.editorconfig` naming and style rules are untested beyond that; `TreatWarningsAsErrors` is off by design. |
| Async / DI / error handling | Only what exists: the integration test is async and `FindRepositoryRoot` throws a clear exception. No DI beyond the template's. |
| Tests written and passing | Met. 10 passed, 0 failed. |

**N/A for this ticket:** aggregates and domain logic (no domain concepts yet); domain events and audit trail (no state changes); `decimal` / money and rounding (no money); transaction boundaries (no persistence); compliance (PCI-DSS, SOX, LGPD; no regulated data handled).

**Deviations from the plan:** root `global.json` was added so `dotnet test` works with xUnit v3 on the .NET 10 SDK (Phase 3); the negative check used Api → Domain because Domain → Application is a circular reference MSBuild rejects (Phase 4).

**Follow-ups for other tickets (not done here):** README build/run/test section is OR-025; container and health checks are OR-007; authentication is OR-008.

**Final result:** `dotnet build` 0 warnings / 0 errors; `dotnet test` 10 passed, 0 failed; `dotnet format --verify-no-changes` clean.

---

## Critical files

| Action | Path |
|---|---|
| New | `.editorconfig`, `Directory.Build.props`, `OrderRouter.slnx`, `Plans/OR-001-solution-skeleton.md` |
| Moved, then edited | `order-router.csproj` → `src/OrderRouter.Api/OrderRouter.Api.csproj`; `Program.cs`, `appsettings*.json`, `Properties/launchSettings.json` → `src/OrderRouter.Api/`; `order-router.http` → `src/OrderRouter.Api/OrderRouter.Api.http` |
| New projects | `src/OrderRouter.Domain`, `src/OrderRouter.Application`, `src/OrderRouter.Infrastructure`, `tests/OrderRouter.Tests`, `tests/OrderRouter.Api.IntegrationTests` |
| New tests | `tests/OrderRouter.Tests/PlaceholderTests.cs`, `tests/OrderRouter.Tests/Architecture/LayerDependencyTests.cs`, `tests/OrderRouter.Api.IntegrationTests/ApiHostTests.cs` |

## Verification summary (ticket acceptance criteria)

| AC | How it's verified |
|---|---|
| AC1: Solution builds | `dotnet build` from the root of a clean clone: 6 projects, 0 errors (Phase 5) |
| AC2: Layer references enforced | `LayerDependencyTests`: the exact csproj `ProjectReference` sets plus the reflection check for forbidden references, both passing. A manual negative test shows a violation fails (Phase 4) |
| AC3: Tests run | `dotnet test`: one placeholder test in each test project runs and passes (Phase 3, re-checked in Phase 5) |

# OR-001: Set up the solution and layer projects

**Depends on:** None  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §13.1 (C-01); `.claude/rules/dotnet-ddd-architecture.md`

## Description

Create the .NET 10 solution with one project per layer: **Domain**, **Application**, **Infrastructure** and **Api** (the existing `order-router.csproj` becomes the Api project). Add a unit test project and an integration test project. No business logic yet. This gives everyone the same structure to build on.

## Acceptance criteria

### AC1: Solution builds

- **Given** a fresh clone of the repository
- **When** I run `dotnet build`
- **Then** every project builds with no errors

### AC2: Layer references are enforced

- **Given** the solution is built
- **When** the architecture test checks project references
- **Then** Domain references no other project, Application references only Domain, Infrastructure references Domain and Application, and Api references Application and Infrastructure

### AC3: Tests run

- **Given** the two test projects
- **When** I run `dotnet test`
- **Then** a placeholder test in each project runs and passes

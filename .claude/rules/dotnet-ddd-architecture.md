---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/Program.cs"
  - "**/Startup.cs"
  - "**/*.razor"
---

# DDD & .NET Architecture Standards

For the full analyze → plan → implement → review process, use the `ddd-implementation` skill.

## Domain-Driven Design
- Use consistent ubiquitous language across code and docs.
- Keep bounded contexts with clear responsibilities.
- Aggregates define consistency/transaction boundaries; authorize at the aggregate level.
- Business logic belongs in the domain layer (rich models). Services only orchestrate I/O through ports and hold no rule.
- Publish domain events for business-significant state changes; they also form the audit trail.

## SOLID
SRP, OCP, LSP, ISP, DIP. Depend on abstractions; constructor-inject dependencies.

## Layers
There are three projects and no Application project: `Api` → `Infrastructure` → `Domain` (`Api` also references `Domain`), enforced by `LayerDependencyTests`.
- **Domain (`OrderRouter.Domain`):** references no other project. `Entities/` (aggregates, immutable value objects), `Models/` (commands and other plain models), `Interfaces/` (every port, flat), `Services/` (stateless domain services and the orchestrating services such as `RouteOrderService`), `Helpers/`, `Sections/` (options and validators). Orchestration of I/O through ports lives here; it holds the business rules, domain events and specifications.
- **Infrastructure (`OrderRouter.Infrastructure`):** references Domain. EF Core `OrderRouterDataContext`, `Models/` (`*Entity`), `EntityTypeConfiguration/`, `Migrations/`, `Providers/` (implementations of the Domain read/write ports), `Services/` (adapters), `Mappers/` (entity ↔ domain), `HostedServices/`, `Extensions/`.
- **Api (`OrderRouter.Api`):** references Domain and Infrastructure. Controller-based endpoints in `Controllers/V<n>/`, `ViewModels/Request` and `ViewModels/Response` (the wire contract; never expose domain entities), `Mappers/` (domain → view model, no business rules), `Filters/`, `Security/`, `OpenApi/`, `Constants/`, `Extensions/` (DI registration), `Startup.cs`. Input is validated at this edge (data annotations) before a command reaches the Domain; the Domain still guards its invariants.

## .NET Practices
- `async`/`await` for I/O; built-in DI; LINQ for data manipulation.
- Consistent exception handling and logging.
- Modern C# (records, pattern matching).
- Right-sized aggregates; efficient queries and indexing; cache respecting data volatility.

## Financial Domain
- Use `decimal` for all money; currency-aware value objects; explicit rounding rules; keep precision through calculation chains.
- Strong consistency within an aggregate; eventual consistency across aggregates via domain events; sagas and compensation for distributed transactions.
- Capture financial operations as domain events; immutable audit trails; support regulatory reporting and data lineage.
- Encapsulate calculations in domain services; use specifications for complex criteria.
- Compliance considerations: PCI-DSS, SOX, LGPD.

## Testing
- Naming: `MethodName_Condition_ExpectedResult()` (if nearby tests use a different style, `csharp.md` says to follow nearby tests — prefer the existing style and flag the mismatch).
- Unit tests for domain logic in isolation; integration tests for aggregate boundaries, persistence, and service integrations; acceptance tests for full scenarios.
- Target at least 85% coverage for the Domain project.
- Cover error scenarios and boundary conditions; test aggregates, value objects (immutability/equality), domain services and events. Unit tests live in `tests/OrderRouter.Tests` (mirroring the `src` folders); host and database tests in `tests/OrderRouter.Api.IntegrationTests`.

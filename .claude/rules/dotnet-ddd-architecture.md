---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/Program.cs"
  - "**/*.razor"
---

# DDD & .NET Architecture Standards

For the full analyze → plan → implement → review process, use the `ddd-implementation` skill.

## Domain-Driven Design
- Use consistent ubiquitous language across code and docs.
- Keep bounded contexts with clear responsibilities.
- Aggregates define consistency/transaction boundaries; authorize at the aggregate level.
- Business logic belongs in the domain layer (rich models), not application services.
- Publish domain events for business-significant state changes; they also form the audit trail.

## SOLID
SRP, OCP, LSP, ISP, DIP. Depend on abstractions; constructor-inject dependencies.

## Layers
- **Domain:** aggregates, immutable value objects, stateless domain services, domain events, specifications.
- **Application:** orchestrates domain operations, DTOs, validates all input before business logic.
- **Infrastructure:** repositories (interfaces defined in the domain), event bus, ORM mappers, external adapters.

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
- Target at least 85% coverage for domain and application layers.
- Cover error scenarios and boundary conditions; test aggregates, value objects (immutability/equality), domain services, events, and application services.

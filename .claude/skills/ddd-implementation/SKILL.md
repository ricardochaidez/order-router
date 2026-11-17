---
name: ddd-implementation
description: Structured DDD/SOLID workflow for designing and implementing non-trivial .NET features (new aggregates, domain services, cross-layer changes). Use before writing code for new domain behavior; skip for small edits.
---

# DDD Implementation Workflow

Standards live in `.claude/rules/dotnet-ddd-architecture.md` and `.claude/rules/csharp.md`. This skill is the process. If any point below can't be explained clearly, stop and ask the user.

## 1. Domain Analysis (before coding)
State explicitly:
- Domain concepts involved and their relationships
- Aggregate boundaries and consistency requirements
- Ubiquitous-language terms used
- Business rules and invariants to enforce
- Which layers are affected (Domain / Infrastructure / Api)
- Security and compliance considerations

## 2. Architecture Review
Check:
- Do aggregate boundaries hold? Are domain rules encapsulated?
- Is each class single-responsibility? Do dependencies point at abstractions?
- Which domain events decouple the design?
- Authorization at the aggregate level?

## 3. Implementation Plan
Outline:
- Aggregates/entities created or modified, and domain events published
- Files to create/modify, with justification
- Interface and class structure
- Test cases (naming per rules; confirm test category: unit / integration / acceptance)
- Error handling and validation strategy
- Performance and scalability notes

Present the plan to the user and get confirmation before large changes.

## 4. Implement
1. Domain model and ubiquitous language
2. Aggregate boundaries and invariants
3. Domain services that orchestrate through ports, with input validated at the Api edge (view models) and invariants guarded in the Domain
4. Async + DI throughout
5. Tests, including edge cases and error scenarios
6. Domain events where they aid decoupling
7. Brief documentation of domain decisions and trade-offs

## 5. Post-Implementation Review
Verify and report honestly (list anything not met rather than claiming it):
- Aggregates model business concepts; terminology is consistent
- SOLID followed; domain logic lives in aggregates
- Domain events published and handled
- Tests written and passing; naming follows conventions
- `decimal` and rounding used for money; transaction boundaries correct
- Audit trail via domain events; compliance (PCI-DSS, SOX, LGPD) addressed where relevant
- Async, DI, and error handling follow .NET practices

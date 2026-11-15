---
paths:
  - "**/*.cs"
---

# C# Development

## C# Instructions
- Always use the latest version C#, currently C# 14 features.
- Write clear and concise comments for each function.

## General Instructions
- Make only high confidence suggestions when reviewing code changes.
- Write code with good maintainability practices, including comments on why certain design decisions were made.
- Handle edge cases and write clear exception handling.
- For libraries or external dependencies, mention their usage and purpose in comments.

## Naming Conventions

- Follow PascalCase for component names, method names, and public members.
- Use camelCase for private fields and local variables.
- Prefix interface names with "I" (e.g., IUserService).

## Formatting

- Apply code-formatting style defined in `.editorconfig`.
- Prefer file-scoped namespace declarations and single-line using directives.
- Insert a newline before the opening curly brace of any code block (e.g., after `if`, `for`, `while`, `foreach`, `using`, `try`, etc.).
- Ensure that the final return statement of a method is on its own line.
- Use pattern matching and switch expressions wherever possible.
- Use `nameof` instead of string literals when referring to member names.
- Ensure that XML doc comments are created for any public APIs. When applicable, include `<example>` and `<code>` documentation in the comments.

## Project Setup and Structure

- Use the appropriate .NET project templates when creating a new project.
- Keep the purpose of each generated file and folder clear so the project structure stays understandable.
- Organize code using feature folders or domain-driven design principles.
- Maintain proper separation of concerns with models, services, and data access layers.
- Use the Program.cs and configuration system in ASP.NET Core 10, including environment-specific settings.

## Nullable Reference Types

- Declare variables non-nullable, and check for `null` at entry points.
- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

## Data Access Patterns

- Implement the data access layer using Entity Framework Core.
- Choose among SQL Server, SQLite, and In-Memory providers appropriately for development and production.
- Use the repository pattern where it is beneficial.
- Implement database migrations and data seeding.
- Use efficient query patterns to avoid common performance issues.

## Authentication and Authorization

- Implement authentication using JWT Bearer tokens.
- Follow OAuth 2.0 and OpenID Connect concepts as they relate to ASP.NET Core.
- Implement role-based and policy-based authorization.
- Integrate with Microsoft Entra ID (formerly Azure AD).
- Secure both controller-based and Minimal APIs consistently.

## Validation and Error Handling

- Implement model validation using data annotations and FluentValidation.
- Customize the validation pipeline and validation responses as needed.
- Use a global exception handling strategy via middleware.
- Create consistent error responses across the API.
- Use problem details (RFC 9457) for standardized error responses.

## API Versioning and Documentation

- Implement and apply an API versioning strategy.
- Implement Swagger/OpenAPI with proper documentation.
- Document endpoints, parameters, responses, and authentication.
- Apply versioning in both controller-based and Minimal APIs.
- Write meaningful API documentation that helps consumers.

## Logging and Monitoring

- Implement structured logging using Serilog or other providers.
- Use logging levels appropriately.
- Integrate with Application Insights for telemetry collection.
- Implement custom telemetry and correlation IDs for request tracking.
- Monitor API performance, errors, and usage patterns.

## Testing

- Always include test cases for critical paths of the application.
- Create unit tests.
- Do not emit "Act", "Arrange" or "Assert" comments.
- Copy existing style in nearby files for test method names and capitalization.
- Use integration testing approaches for API endpoints.
- Mock dependencies for effective testing.
- Test authentication and authorization logic.
- Apply test-driven development principles to API development.

## Performance Optimization

- Implement caching strategies (in-memory, distributed, response caching).
- Use asynchronous programming patterns for API performance.
- Implement pagination, filtering, and sorting for large data sets.
- Implement compression and other performance optimizations.
- Measure and benchmark API performance.

## Deployment and DevOps

- Containerize APIs using .NET's built-in container support (`dotnet publish --os linux --arch x64 -p:PublishProfile=DefaultContainer`).
- Understand the differences between manual Dockerfile creation and .NET's container publishing features.
- Use CI/CD pipelines for .NET applications.
- Target deployment to Azure App Service, Azure Container Apps, or other hosting options.
- Implement health checks and readiness probes.
- Use environment-specific configurations for different deployment stages.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Scalar.AspNetCore;
using OrderRouter.Api.Authentication;
using OrderRouter.Api.Health;
using OrderRouter.Api.Routing;
using OrderRouter.Application.Routing;
using OrderRouter.Infrastructure.Persistence;

// Composition root: the Api project is the only place that wires Application and Infrastructure
// together, so the inner layers never depend on the host or on each other's implementations.
var builder = WebApplication.CreateBuilder(args);

// OpenAPI document generation. Every endpoint added by later tickets is documented through it.
builder.Services.AddOpenApi(options => options.AddOperationTransformer<RouteOpenApiTransformer>());

// JWT bearer in production, plus the X-Api-Key dev key in Development only. The RouteCaller policy is also the
// fallback, so every endpoint needs authentication unless it opts out with AllowAnonymous (FR-19).
builder.AddOrderRouterAuthentication();

// One JSON naming policy for the whole API: snake_case properties and snake_case enum strings (FR-01, ADR-0004).
// Unknown request fields stay ignored (tolerant reader, DQ-15) because UnmappedMemberHandling is left at Skip.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// Built-in minimal-API validation (data annotations on the request DTOs). The writer is registered before
// AddProblemDetails so it wins for the route endpoints and rewrites the 400 into the v1 envelope (SC-15).
builder.Services.AddValidation();
builder.Services.AddSingleton<IProblemDetailsWriter, RouteEnvelopeProblemDetailsWriter>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MalformedBodyExceptionHandler>();

// Throw on binding failures in every environment (the default is Development only), so the exception handler
// always produces the same 400 envelope for malformed bodies.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// TEMPORARY: OR-015 replaces this registration with the real routing use case. Until then a valid request
// returns 500 rather than a body that claims to be routed (fail safe).
builder.Services.AddSingleton<IRouteOrderUseCase, RoutingNotAvailableUseCase>();

// ConnectionStrings:OrderRouter comes from appsettings.Development.json in Development, or from the
// ConnectionStrings__OrderRouter environment variable. There is deliberately no default elsewhere, so a
// mis-configured deployment fails at startup instead of silently using a dev password.
var connectionString = builder.Configuration.GetConnectionString("OrderRouter");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("The connection string 'ConnectionStrings:OrderRouter' is not configured.");
}

// The API never migrates or seeds; that is an explicit, separate step (SC-22, SC-33).
builder.Services.AddOrderRouterDatabase(connectionString);
builder.Services.AddHealthChecks().AddOrderRouterDatabaseHealthCheck();

// Exit at startup if PostgreSQL is unreachable, using the same check as GET /health. Connectivity only: an
// empty or unmigrated database starts normally (SC-23, SC-33).
builder.Services.AddHostedService<DependencyStartupCheck>();

var app = builder.Build();

// The OpenAPI document and the Scalar UI (/scalar) are only exposed in Development.
if (app.Environment.IsDevelopment())
{
    // Documentation only, and only mapped in Development, so it stays anonymous.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// Unexpected faults become 500 ProblemDetails with no internals (CLAUDE.md); malformed route bodies become 400.
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Authentication runs before anything else touches the request: no credentials, no validation, no routing.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoint();
app.MapRouteEndpoints();

app.Run();

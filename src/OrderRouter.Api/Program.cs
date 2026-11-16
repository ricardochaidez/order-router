using Microsoft.Extensions.Configuration;
using OrderRouter.Api.Authentication;
using OrderRouter.Api.Health;
using OrderRouter.Infrastructure.Persistence;

// Composition root: the Api project is the only place that wires Application and Infrastructure
// together, so the inner layers never depend on the host or on each other's implementations.
var builder = WebApplication.CreateBuilder(args);

// OpenAPI document generation. Every endpoint added by later tickets is documented through it.
builder.Services.AddOpenApi();

// JWT bearer in production, plus the X-Api-Key dev key in Development only. The RouteCaller policy is also the
// fallback, so every endpoint needs authentication unless it opts out with AllowAnonymous (FR-19).
builder.AddOrderRouterAuthentication();

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

// The OpenAPI document is only exposed in Development.
if (app.Environment.IsDevelopment())
{
    // Documentation only, and only mapped in Development, so it stays anonymous.
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

// Authentication runs before anything else touches the request: no credentials, no validation, no routing.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoint();

app.Run();

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.Health;

/// <summary>Maps the anonymous liveness/dependency probe <c>GET /health</c> (SC-23).</summary>
internal static class HealthEndpoint
{
    /// <summary>The probe path. It is the only endpoint that is anonymous (OR-008 keeps it so).</summary>
    public const string Path = "/health";

    /// <summary>
    /// Maps <c>GET /health</c>. It runs the dependency-tagged checks (PostgreSQL reachability only) and returns
    /// <c>200 Healthy</c> or <c>503 Unhealthy</c> as plain text. Reference data is never read, so an empty or
    /// unmigrated database is still healthy (ADR-0002).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, HandleAsync)
            .AllowAnonymous()
            .WithName("GetHealth")
            .WithSummary("Reports whether the API can reach its database.")
            .WithDescription("Anonymous. Checks only that PostgreSQL is reachable; an empty or unmigrated database is healthy.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .Produces<string>(StatusCodes.Status503ServiceUnavailable, "text/plain");

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(HealthCheckService healthChecks, HttpContext context, CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains(HealthTags.Dependency), cancellationToken);
        var (statusCode, body) = HealthStatusMapper.Map(report.Status);

        // Same as the built-in health middleware: a probe result must never be served from a cache.
        context.Response.Headers.CacheControl = "no-store";

        return Results.Text(body, "text/plain", statusCode: statusCode);
    }
}

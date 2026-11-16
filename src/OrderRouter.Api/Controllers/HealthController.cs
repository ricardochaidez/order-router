using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderRouter.Api.Health;
using OrderRouter.Api.Health.Mappers;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.Controllers;

/// <summary>The anonymous liveness/dependency probe <c>GET /health</c> (SC-23).</summary>
/// <param name="healthChecks">Runs the registered health checks.</param>
[ApiController]
// The only anonymous endpoint (OR-008 keeps it so); everything else falls under the RouteCaller fallback policy.
[AllowAnonymous]
[Route(Path)]
public sealed class HealthController(HealthCheckService healthChecks) : ControllerBase
{
    /// <summary>The probe path.</summary>
    public const string Path = "health";

    /// <summary>
    /// Runs the dependency-tagged checks (PostgreSQL reachability only) and returns <c>200 Healthy</c> or
    /// <c>503 Unhealthy</c> as plain text. Reference data is never read, so an empty or unmigrated database is still
    /// healthy (ADR-0002).
    /// </summary>
    /// <param name="cancellationToken">Aborts when the caller disconnects.</param>
    /// <returns>The status word as plain text.</returns>
    [HttpGet(Name = "GetHealth")]
    [EndpointSummary("Reports whether the API can reach its database.")]
    [EndpointDescription("Anonymous. Checks only that PostgreSQL is reachable; an empty or unmigrated database is healthy.")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, MediaTypeNames.Text.Plain)]
    [ProducesResponseType<string>(StatusCodes.Status503ServiceUnavailable, MediaTypeNames.Text.Plain)]
    // Same as the built-in health middleware: a probe result must never be served from a cache.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ContentResult> Get(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains(HealthTags.Dependency), cancellationToken);
        var (statusCode, body) = HealthStatusMapper.Map(report.Status);

        return new ContentResult { Content = body, ContentType = MediaTypeNames.Text.Plain, StatusCode = statusCode };
    }
}

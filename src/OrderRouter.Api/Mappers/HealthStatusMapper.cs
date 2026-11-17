using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderRouter.Api.Mappers;

/// <summary>Maps an aggregate health status to the HTTP status code and plain-text body of <c>GET /health</c>.</summary>
internal static class HealthStatusMapper
{
    /// <summary>
    /// Healthy is <c>200</c>; everything else is <c>503</c>. Degraded counts as unhealthy because the only
    /// check is a required dependency, and an unknown state must fail safe. The body is only the status word,
    /// so exception text and connection details are never returned.
    /// </summary>
    public static (int StatusCode, string Body) Map(HealthStatus status)
        => status switch
        {
            HealthStatus.Healthy => (StatusCodes.Status200OK, nameof(HealthStatus.Healthy)),
            _ => (StatusCodes.Status503ServiceUnavailable, nameof(HealthStatus.Unhealthy)),
        };
}

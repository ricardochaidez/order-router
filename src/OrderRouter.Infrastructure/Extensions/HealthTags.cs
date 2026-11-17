namespace OrderRouter.Infrastructure.Extensions;

/// <summary>Tags that group health checks, so the endpoint and the startup check run the same set.</summary>
public static class HealthTags
{
    /// <summary>Marks a check that probes a required external dependency (PostgreSQL). Used by <c>GET /health</c> and the startup check.</summary>
    public const string Dependency = "dependency";
}

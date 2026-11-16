namespace OrderRouter.Api.Authentication;

/// <summary>Applies the routing authorization policy to endpoints.</summary>
internal static class RouteCallerEndpointExtensions
{
    /// <summary>
    /// Requires the <see cref="AuthorizationPolicyNames.RouteCaller"/> policy. The policy is also the fallback, so
    /// this makes the intent explicit on the routing endpoints (OR-009 uses it on both route paths, ADR-0004).
    /// </summary>
    public static TBuilder RequireRouteCaller<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(AuthorizationPolicyNames.RouteCaller);
}

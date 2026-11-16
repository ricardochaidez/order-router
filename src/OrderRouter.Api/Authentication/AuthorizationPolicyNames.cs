namespace OrderRouter.Api.Authentication;

/// <summary>Names of the authorization policies. One policy covers every routing endpoint (FR-19, SC-16).</summary>
internal static class AuthorizationPolicyNames
{
    /// <summary>
    /// Any authenticated caller may route. It is a named policy so a scope check (403) can be added later
    /// without touching the endpoints (SC-16).
    /// </summary>
    public const string RouteCaller = "RouteCaller";
}

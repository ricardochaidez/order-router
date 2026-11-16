using System.Security.Claims;

namespace OrderRouter.Application.Routing.Experiments;

/// <summary>
/// Reads the caller ID from the authenticated principal. It uses only <c>System.Security.Claims</c> (part of the
/// BCL), so Application stays free of any ASP.NET reference.
/// </summary>
public static class CallerIdentity
{
    /// <summary>
    /// Claims in priority order: <c>sub</c>, then <c>client_id</c> (RFC 9068 access tokens), then <c>azp</c> (OIDC
    /// authorized party). The dev API key principal carries <c>sub = dev-key</c>, so it resolves with no special case.
    /// </summary>
    public static IReadOnlyList<string> ClaimTypes { get; } = ["sub", "client_id", "azp"];

    /// <summary>Returns the caller ID, or <c>null</c> when no claim has a non-blank value (then the order ID is the unit, AB-01).</summary>
    /// <param name="principal">The authenticated principal, or <c>null</c>.</param>
    public static string? Resolve(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        foreach (var claimType in ClaimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;

            // A blank value counts as missing, so the next claim gets its turn.
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

namespace OrderRouter.Api.Authentication;

/// <summary>
/// JWT bearer settings bound from <c>Authentication:Jwt</c>. Signing keys and the issuer come from the OIDC
/// metadata published by <see cref="Authority"/> (for example Microsoft Entra ID), so no key is configured here.
/// </summary>
/// <param name="Authority">The OIDC authority (issuer base URL).</param>
/// <param name="Audience">The audience that tokens must be issued for.</param>
internal sealed record JwtAuthenticationOptions(string? Authority = null, string? Audience = null)
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Authentication:Jwt";

    /// <summary>
    /// Fails fast when a required setting is missing, naming the setting so a mis-configured deployment stops at
    /// startup instead of rejecting every caller (or accepting the wrong ones). Development tolerates missing
    /// values so the dev key alone is enough locally.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required setting is missing outside Development.</exception>
    public void Validate(bool isDevelopment)
    {
        if (isDevelopment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Authority))
        {
            throw new InvalidOperationException($"The setting '{SectionName}:{nameof(Authority)}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"The setting '{SectionName}:{nameof(Audience)}' is not configured.");
        }
    }
}

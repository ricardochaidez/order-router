using Microsoft.AspNetCore.Authentication;

namespace OrderRouter.Api.Security;

/// <summary>Options for <see cref="DevApiKeyAuthenticationHandler"/>.</summary>
internal sealed class DevApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The key a caller must send in <c>X-Api-Key</c>. Never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

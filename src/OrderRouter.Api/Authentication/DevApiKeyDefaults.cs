namespace OrderRouter.Api.Authentication;

/// <summary>Constants for the development-only API key scheme (FR-19).</summary>
internal static class DevApiKeyDefaults
{
    /// <summary>The authentication scheme name.</summary>
    public const string Scheme = "DevApiKey";

    /// <summary>The request header that carries the key.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>The fixed caller ID every dev-key request maps to (ADR-0005).</summary>
    public const string CallerId = "dev-key";

    /// <summary>The configuration key that holds the expected key. Never put it in appsettings files (SC-32).</summary>
    public const string ConfigurationKey = "Authentication:DevApiKey";
}

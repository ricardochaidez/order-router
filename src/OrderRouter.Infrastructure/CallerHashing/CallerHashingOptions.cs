namespace OrderRouter.Infrastructure.CallerHashing;

/// <summary>
/// Settings for <see cref="HmacCallerHasher"/>, bound from the <c>CallerHashing</c> section (OR-027).
/// The secret comes from an environment variable or user-secrets and is never committed (AC10, NFR-11).
/// </summary>
public sealed class CallerHashingOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "CallerHashing";

    /// <summary>Full configuration key of the secret, used in error messages.</summary>
    public const string SecretKey = SectionName + ":Secret";

    /// <summary>The HMAC key. There is no default: a missing secret stops the host at startup.</summary>
    public string? Secret { get; set; }
}

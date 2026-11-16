using Microsoft.Extensions.Options;

namespace OrderRouter.Infrastructure.CallerHashing;

/// <summary>
/// Fails startup when the caller-hash secret is missing. Without it the service would either store raw caller IDs
/// or hash them with a known key, so refusing to start is the safe choice (OR-027 AC10, NFR-11).
/// </summary>
public sealed class CallerHashingOptionsValidator : IValidateOptions<CallerHashingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CallerHashingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.Secret)
            ? ValidateOptionsResult.Fail(
                $"{CallerHashingOptions.SecretKey} is not configured. Set it through the CallerHashing__Secret environment variable or user-secrets; it is never committed.")
            : ValidateOptionsResult.Success;
    }
}

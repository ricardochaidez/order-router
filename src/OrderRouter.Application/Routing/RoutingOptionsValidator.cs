using Microsoft.Extensions.Options;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Application.Routing;

/// <summary>Stops the host at startup when the nationwide ZIP range is missing or malformed (fail fast).</summary>
public sealed class RoutingOptionsValidator : IValidateOptions<RoutingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RoutingOptions options)
    {
        try
        {
            _ = ZipRange.Parse(options.NationwideZipRange);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ValidateOptionsResult.Fail(
                $"{RoutingOptions.SectionName}:{nameof(RoutingOptions.NationwideZipRange)} is not a valid ZIP range: {exception.Message}");
        }

        return ValidateOptionsResult.Success;
    }
}

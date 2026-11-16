namespace OrderRouter.Application.Routing;

/// <summary>
/// The <c>Routing</c> configuration section. Validated at startup by <see cref="RoutingOptionsValidator"/>.
/// </summary>
public sealed class RoutingOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Routing";

    /// <summary>Default for <see cref="NationwideZipRange"/> (FR-09).</summary>
    public const string DefaultNationwideZipRange = "00100-99999";

    /// <summary>
    /// A supplier whose service area is exactly this range, and that can mail order, is mail-only (FR-09, SC-09).
    /// Configuration, not data: it is never stored (ADR-0002).
    /// </summary>
    public string NationwideZipRange { get; set; } = DefaultNationwideZipRange;
}

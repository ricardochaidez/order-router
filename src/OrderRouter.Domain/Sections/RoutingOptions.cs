
namespace OrderRouter.Domain.Sections;

/// <summary>
/// The <c>Routing</c> configuration section. Validated at startup by <see cref="RoutingOptionsValidator"/>.
/// </summary>
public sealed class RoutingOptions
{
    /// <summary>Name of the configuration section.</summary>
    public static readonly string Name = "Routing";

    /// <summary>Default for <see cref="NationwideZipRange"/> (FR-09).</summary>
    public const string DefaultNationwideZipRange = "00100-99999";

    /// <summary>
    /// A supplier whose service area is exactly this range, and that can mail order, is mail-only (FR-09, SC-09).
    /// Configuration, not data: it is never stored (ADR-0002).
    /// </summary>
    public string NationwideZipRange { get; set; } = DefaultNationwideZipRange;

    /// <summary>
    /// When <see langword="true"/>, the 201 and the GET 200 bodies carry an optional <c>diagnostics</c> object
    /// (experiment arms and the search mode). Off by default, and clients must not depend on it (AB-05, OQ-08).
    /// It is read at startup, so a change needs a restart.
    /// </summary>
    public bool ExposeDiagnostics { get; set; }
}


namespace OrderRouter.Domain.Sections;

/// <summary>
/// The experiment settings as written in <c>experiments.json</c> (SC-25), bound to the configuration <b>root</b> so the
/// <c>Experiments</c> section and <c>Routing:SimilarityBand</c> reload together as one snapshot. Unknown keys are ignored
/// (tolerant reader), and a missing experiment section counts as disabled (fail safe, AB-03).
/// </summary>
public sealed class ExperimentsOptions
{
    /// <summary>The three experiments.</summary>
    public ExperimentsSection Experiments { get; set; } = new();

    /// <summary>The reloadable part of the <c>Routing</c> section. <c>NationwideZipRange</c> is not reloadable and not read here.</summary>
    public RoutingSection Routing { get; set; } = new();

    /// <summary>
    /// Set when the configuration could not be bound (for example text where a number is expected). It is not a
    /// configuration key (the binder never fills it); the mapper reports it so a malformed edit is rejected, never thrown.
    /// </summary>
    public string? BindError { get; set; }
}

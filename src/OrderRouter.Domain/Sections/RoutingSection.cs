
namespace OrderRouter.Domain.Sections;

/// <summary>The reloadable <c>Routing</c> keys.</summary>
public sealed class RoutingSection
{
    /// <summary>Quality band for plans of one shipment count.</summary>
    public decimal? SimilarityBand { get; set; }
}

using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>
/// Ranking keys 2 to 4 (similarity band, most local lines, highest quality) within one shipment count.
/// Shared by the EXP-1 control and treatment so the two arms cannot drift apart (ADR-0003: the band never crosses counts).
/// </summary>
internal static class PlanRanking
{
    /// <summary>Returns every plan still tied after band, locality and quality. The plans must share a shipment count.</summary>
    /// <param name="plans">Non-empty plans with the same shipment count.</param>
    /// <param name="similarityBand">How far below the best quality a plan may be and still count as similar.</param>
    public static IReadOnlyList<RoutingPlan> BestWithinSize(IReadOnlyList<RoutingPlan> plans, decimal similarityBand)
    {
        // FR-12: the band is not transitive, so find the best quality first and compare every plan to it.
        var floor = plans.Max(p => p.Quality) - similarityBand;
        var similar = plans.Where(p => p.Quality >= floor).ToList();
        var mostLocal = similar.Max(p => p.LocalLineCount);
        var local = similar.Where(p => p.LocalLineCount == mostLocal).ToList();
        var bestQuality = local.Max(p => p.Quality);

        return local.Where(p => p.Quality == bestQuality).ToList().AsReadOnly();
    }
}

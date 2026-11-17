namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>
/// All plans still in the running, so the tie-break (FR-13) and policies (OR-013) can choose.
/// No cover means an empty list: the search never invents a plan.
/// </summary>
/// <param name="Plans">Plans ordered by shipment count, then supplier ids (ordinal).</param>
/// <param name="MinimumShipments">The fewest shipments that cover the order, or 0 when no cover exists. In greedy mode: the greedy plan's count, not guaranteed to be the minimum.</param>
/// <param name="Mode">Which algorithm produced the result (feeds <c>search_mode</c>).</param>
/// <param name="FallbackReason">Why greedy ran; <see langword="null"/> when the search was exact.</param>
public sealed record PlanSearchResult(
    IReadOnlyList<RoutingPlan> Plans,
    int MinimumShipments,
    SearchMode Mode = SearchMode.Exact,
    SearchFallbackReason? FallbackReason = null);

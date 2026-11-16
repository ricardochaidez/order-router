namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>
/// All plans still in the running, so the tie-break (FR-13) and policies (OR-013) can choose.
/// No cover means an empty list: the search never invents a plan.
/// </summary>
/// <param name="Plans">Plans ordered by shipment count, then supplier ids (ordinal).</param>
/// <param name="MinimumShipments">The fewest shipments that cover the order, or 0 when no cover exists.</param>
public sealed record PlanSearchResult(IReadOnlyList<RoutingPlan> Plans, int MinimumShipments);

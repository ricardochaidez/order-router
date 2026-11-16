using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>EXP-3 seam: what to do when some lines cannot be fulfilled (ADR-0001).</summary>
public interface IUnfulfillablePolicy
{
    /// <summary>Decides the outcome.</summary>
    /// <param name="plan">Plan for the routable lines, or null when there is none.</param>
    /// <param name="unfulfilled">Lines that cannot be fulfilled.</param>
    RoutingOutcome Apply(RoutingPlan? plan, IReadOnlyList<UnfulfilledLine> unfulfilled);
}

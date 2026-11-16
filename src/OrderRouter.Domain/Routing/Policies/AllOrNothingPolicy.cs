using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>Control arm of EXP-3: any unfulfillable line means nothing is routed (AB-03).</summary>
public sealed class AllOrNothingPolicy : IUnfulfillablePolicy
{
    private AllOrNothingPolicy()
    {
    }

    /// <summary>Shared instance; the policy has no state.</summary>
    public static AllOrNothingPolicy Instance { get; } = new();

    /// <inheritdoc />
    public RoutingOutcome Apply(RoutingPlan? plan, IReadOnlyList<UnfulfilledLine> unfulfilled)
    {
        ArgumentNullException.ThrowIfNull(unfulfilled);

        // Fail safe: no plan is never "routed", and the plan is dropped when any line is unfulfilled.
        return plan is null || unfulfilled.Count > 0
            ? new RoutingOutcome(RoutingOutcomeStatus.NotRouted, null, unfulfilled)
            : new RoutingOutcome(RoutingOutcomeStatus.Routed, plan, unfulfilled);
    }
}

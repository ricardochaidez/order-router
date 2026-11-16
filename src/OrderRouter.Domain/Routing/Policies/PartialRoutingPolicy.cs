using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>
/// Treatment arm of EXP-3: route the lines that can be routed and report the rest as unfulfilled (FR-14, SC-05).
/// </summary>
public sealed class PartialRoutingPolicy : IUnfulfillablePolicy
{
    private PartialRoutingPolicy()
    {
    }

    /// <summary>Shared instance; the policy has no state.</summary>
    public static PartialRoutingPolicy Instance { get; } = new();

    /// <inheritdoc />
    public RoutingOutcome Apply(RoutingPlan? plan, IReadOnlyList<UnfulfilledLine> unfulfilled)
    {
        ArgumentNullException.ThrowIfNull(unfulfilled);

        // Fail safe: no plan is never "routed" or "partial"; unfulfilled lines are never routed.
        if (plan is null)
        {
            return new RoutingOutcome(RoutingOutcomeStatus.NotRouted, null, unfulfilled);
        }

        var status = unfulfilled.Count > 0 ? RoutingOutcomeStatus.PartiallyRouted : RoutingOutcomeStatus.Routed;

        return new RoutingOutcome(status, plan, unfulfilled);
    }
}

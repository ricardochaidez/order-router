using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>Final routing result for an order after the unfulfillable policy.</summary>
public sealed record RoutingOutcome
{
    /// <summary>Creates an outcome.</summary>
    /// <param name="status">The status.</param>
    /// <param name="plan">The plan, when something is routed.</param>
    /// <param name="unfulfilled">Lines not routed; empty when none.</param>
    /// <exception cref="DomainValidationException">
    /// The status does not match the data: <see cref="RoutingOutcomeStatus.Routed"/> needs a plan and no unfulfilled lines;
    /// <see cref="RoutingOutcomeStatus.PartiallyRouted"/> needs both; <see cref="RoutingOutcomeStatus.NotRouted"/> needs no plan.
    /// </exception>
    public RoutingOutcome(RoutingOutcomeStatus status, RoutingPlan? plan, IReadOnlyList<UnfulfilledLine>? unfulfilled)
    {
        var lines = unfulfilled ?? [];
        var valid = status switch
        {
            RoutingOutcomeStatus.Routed => plan is not null && lines.Count == 0,
            RoutingOutcomeStatus.PartiallyRouted => plan is not null && lines.Count > 0,
            RoutingOutcomeStatus.NotRouted => plan is null,
            _ => false,
        };

        if (!valid)
        {
            throw new DomainValidationException(
                $"Invalid routing outcome: status {status} does not match plan {(plan is null ? "absent" : "present")} and {lines.Count} unfulfilled line(s).");
        }

        Status = status;
        Plan = plan;
        Unfulfilled = lines;
    }

    /// <summary>The status.</summary>
    public RoutingOutcomeStatus Status { get; }

    /// <summary>The plan; null when nothing is routed.</summary>
    public RoutingPlan? Plan { get; }

    /// <summary>Lines not routed; never null.</summary>
    public IReadOnlyList<UnfulfilledLine> Unfulfilled { get; }
}

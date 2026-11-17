using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>The four policies for one request, plus the assignment that produced them (kept for the decision log, AB-04).</summary>
public sealed record RoutingPolicy
{
    /// <summary>Creates a policy set.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RoutingPolicy(
        IEffectiveRatingPolicy rating,
        IPlanSelectionPolicy selection,
        IUnfulfillablePolicy unfulfillable,
        ITieBreaker tieBreaker,
        ExperimentAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(unfulfillable);
        ArgumentNullException.ThrowIfNull(tieBreaker);
        ArgumentNullException.ThrowIfNull(assignment);

        Rating = rating;
        Selection = selection;
        Unfulfillable = unfulfillable;
        TieBreaker = tieBreaker;
        Assignment = assignment;
    }

    /// <summary>EXP-2 policy.</summary>
    public IEffectiveRatingPolicy Rating { get; }

    /// <summary>EXP-1 policy.</summary>
    public IPlanSelectionPolicy Selection { get; }

    /// <summary>EXP-3 policy.</summary>
    public IUnfulfillablePolicy Unfulfillable { get; }

    /// <summary>EXP-4 policy: ranking key 5.</summary>
    public ITieBreaker TieBreaker { get; }

    /// <summary>The assignment these policies were built from.</summary>
    public ExperimentAssignment Assignment { get; }
}

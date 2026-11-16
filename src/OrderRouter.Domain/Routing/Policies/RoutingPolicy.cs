using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>The three policies for one request, plus the assignment that produced them (kept for the decision log, AB-04).</summary>
public sealed record RoutingPolicy
{
    /// <summary>Creates a policy set.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RoutingPolicy(
        IEffectiveRatingPolicy rating,
        IPlanSelectionPolicy selection,
        IUnfulfillablePolicy unfulfillable,
        ExperimentAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(unfulfillable);
        ArgumentNullException.ThrowIfNull(assignment);

        Rating = rating;
        Selection = selection;
        Unfulfillable = unfulfillable;
        Assignment = assignment;
    }

    /// <summary>EXP-2 policy.</summary>
    public IEffectiveRatingPolicy Rating { get; }

    /// <summary>EXP-1 policy.</summary>
    public IPlanSelectionPolicy Selection { get; }

    /// <summary>EXP-3 policy.</summary>
    public IUnfulfillablePolicy Unfulfillable { get; }

    /// <summary>The assignment these policies were built from.</summary>
    public ExperimentAssignment Assignment { get; }
}

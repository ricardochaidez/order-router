using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>
/// Maps each experiment arm to its policy. Stateless; builds a new immutable policy set per request.
/// </summary>
/// <remarks>
/// A treatment ticket (OR-017, OR-018, OR-019) adds one policy class and replaces its
/// <see cref="NotSupportedException"/> below with that class (AB-06). Nothing else changes.
/// </remarks>
public sealed class RoutingPolicyFactory : IRoutingPolicyFactory
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="assignment"/> is null.</exception>
    /// <exception cref="NotSupportedException">A treatment arm is requested before it exists (fail safe: no mislabelled routing).</exception>
    /// <exception cref="ArgumentOutOfRangeException">An arm value is not defined.</exception>
    public RoutingPolicy Create(ExperimentAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var arms = assignment.Arms;

        IPlanSelectionPolicy selection = arms.Exp1ConsolidationVsQuality switch
        {
            ExperimentArm.Control => new StrictPriorityPolicy(assignment.Parameters.SimilarityBand),
            ExperimentArm.Treatment => throw NotAvailable("EXP-1"),
            _ => throw OutOfRange(nameof(ExperimentArms.Exp1ConsolidationVsQuality), arms.Exp1ConsolidationVsQuality),
        };

        IEffectiveRatingPolicy rating = arms.Exp2UnratedSuppliers switch
        {
            ExperimentArm.Control => UnratedLastPolicy.Instance,
            ExperimentArm.Treatment => throw NotAvailable("EXP-2"),
            _ => throw OutOfRange(nameof(ExperimentArms.Exp2UnratedSuppliers), arms.Exp2UnratedSuppliers),
        };

        IUnfulfillablePolicy unfulfillable = arms.Exp3PartialRouting switch
        {
            ExperimentArm.Control => AllOrNothingPolicy.Instance,
            ExperimentArm.Treatment => throw NotAvailable("EXP-3"),
            _ => throw OutOfRange(nameof(ExperimentArms.Exp3PartialRouting), arms.Exp3PartialRouting),
        };

        return new RoutingPolicy(rating, selection, unfulfillable, assignment);
    }

    private static NotSupportedException NotAvailable(string experiment)
        => new($"The treatment arm of {experiment} is not implemented yet.");

    private static ArgumentOutOfRangeException OutOfRange(string experiment, ExperimentArm arm)
        => new(experiment, arm, "Unknown experiment arm.");
}

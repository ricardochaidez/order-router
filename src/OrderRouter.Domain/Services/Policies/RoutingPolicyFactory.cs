using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Domain.Services.Policies;

/// <summary>
/// Maps each experiment arm to its policy. Stateless; builds a new immutable policy set per request.
/// </summary>
/// <remarks>
/// Each treatment arm is one policy class and one line here (AB-06).
/// EXP-1 treatment (OR-017) is done: <see cref="BoundedTradeOffPolicy"/> gets the band and uplift threshold from the assignment.
/// EXP-2 treatment (OR-018) is done: <see cref="ColdStartPriorPolicy"/> gets the prior from the assignment.
/// EXP-3 treatment (OR-019) is done: <see cref="PartialRoutingPolicy"/>.
/// EXP-4: control (the core behavior) is <see cref="DeterministicTieBreaker"/>; treatment is the injected random tie-breaker.
/// </remarks>
/// <param name="treatmentTieBreaker">The EXP-4 treatment tie-breaker (uniform random). It exists only for this experiment; the core never uses it.</param>
public sealed class RoutingPolicyFactory(ITieBreaker treatmentTieBreaker) : IRoutingPolicyFactory
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="assignment"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An arm value is not defined.</exception>
    public RoutingPolicy Create(ExperimentAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var arms = assignment.Arms;

        IPlanSelectionPolicy selection = arms.Exp1ConsolidationVsQuality switch
        {
            ExperimentArm.Control => new StrictPriorityPolicy(assignment.Parameters.SimilarityBand),
            ExperimentArm.Treatment => new BoundedTradeOffPolicy(assignment.Parameters.SimilarityBand, assignment.Parameters.QualityUpliftThreshold),
            _ => throw OutOfRange(nameof(ExperimentArms.Exp1ConsolidationVsQuality), arms.Exp1ConsolidationVsQuality),
        };

        IEffectiveRatingPolicy rating = arms.Exp2UnratedSuppliers switch
        {
            ExperimentArm.Control => UnratedLastPolicy.Instance,
            ExperimentArm.Treatment => new ColdStartPriorPolicy(assignment.Parameters.UnratedPrior),
            _ => throw OutOfRange(nameof(ExperimentArms.Exp2UnratedSuppliers), arms.Exp2UnratedSuppliers),
        };

        IUnfulfillablePolicy unfulfillable = arms.Exp3PartialRouting switch
        {
            ExperimentArm.Control => AllOrNothingPolicy.Instance,
            ExperimentArm.Treatment => PartialRoutingPolicy.Instance,
            _ => throw OutOfRange(nameof(ExperimentArms.Exp3PartialRouting), arms.Exp3PartialRouting),
        };

        ITieBreaker tieBreaker = arms.Exp4TieBreaking switch
        {
            ExperimentArm.Control => DeterministicTieBreaker.Instance,
            ExperimentArm.Treatment => treatmentTieBreaker,
            _ => throw OutOfRange(nameof(ExperimentArms.Exp4TieBreaking), arms.Exp4TieBreaking),
        };

        return new RoutingPolicy(rating, selection, unfulfillable, tieBreaker, assignment);
    }

    private static ArgumentOutOfRangeException OutOfRange(string experiment, ExperimentArm arm)
        => new(experiment, arm, "Unknown experiment arm.");
}

namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>The arm chosen for each experiment on one request (ADR-0005).</summary>
/// <param name="Exp1ConsolidationVsQuality">EXP-1: plan selection.</param>
/// <param name="Exp2UnratedSuppliers">EXP-2: effective rating of unrated suppliers.</param>
/// <param name="Exp3PartialRouting">EXP-3: what happens to unfulfillable lines.</param>
public sealed record ExperimentArms(
    ExperimentArm Exp1ConsolidationVsQuality,
    ExperimentArm Exp2UnratedSuppliers,
    ExperimentArm Exp3PartialRouting)
{
    /// <summary>Every experiment on control: the routing described by the spec (AB-03).</summary>
    public static ExperimentArms AllControl { get; } = new(ExperimentArm.Control, ExperimentArm.Control, ExperimentArm.Control);
}

namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>The exposure reason for each experiment on one request; mirrors <see cref="ExperimentArms"/> (ADR-0005).</summary>
/// <param name="Exp1ConsolidationVsQuality">EXP-1 reason.</param>
/// <param name="Exp2UnratedSuppliers">EXP-2 reason.</param>
/// <param name="Exp3PartialRouting">EXP-3 reason.</param>
public sealed record ExperimentReasons(
    ExposureReason Exp1ConsolidationVsQuality,
    ExposureReason Exp2UnratedSuppliers,
    ExposureReason Exp3PartialRouting)
{
    /// <summary>Every experiment disabled, which is the default when nothing is configured (AB-03).</summary>
    public static ExperimentReasons AllDisabled { get; } = new(ExposureReason.Disabled, ExposureReason.Disabled, ExposureReason.Disabled);
}

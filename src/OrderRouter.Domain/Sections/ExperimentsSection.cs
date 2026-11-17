
namespace OrderRouter.Domain.Sections;

/// <summary>The <c>Experiments</c> section. Property names are the experiment IDs and match <c>ExperimentArms</c>.</summary>
public sealed class ExperimentsSection
{
    /// <summary>EXP-1: consolidation versus quality.</summary>
    public ExperimentOptions? Exp1ConsolidationVsQuality { get; set; }

    /// <summary>EXP-2: unrated suppliers.</summary>
    public ExperimentOptions? Exp2UnratedSuppliers { get; set; }

    /// <summary>EXP-3: partial routing.</summary>
    public ExperimentOptions? Exp3PartialRouting { get; set; }
}

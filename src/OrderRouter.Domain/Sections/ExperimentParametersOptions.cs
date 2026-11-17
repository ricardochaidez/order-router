
namespace OrderRouter.Domain.Sections;

/// <summary>Experiment parameters; each is read from the experiment that owns it.</summary>
public sealed class ExperimentParametersOptions
{
    /// <summary>EXP-1 only: quality gain that justifies one extra shipment.</summary>
    public decimal? QualityUpliftThreshold { get; set; }

    /// <summary>EXP-2 only: effective rating of an unrated supplier.</summary>
    public decimal? UnratedPrior { get; set; }
}

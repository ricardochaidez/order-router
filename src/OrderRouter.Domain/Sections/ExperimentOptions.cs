
namespace OrderRouter.Domain.Sections;

/// <summary>One experiment's settings.</summary>
public sealed class ExperimentOptions
{
    /// <summary>Whether the experiment runs.</summary>
    public bool Enabled { get; set; }

    /// <summary>Share of targeted callers in treatment, 0 to 100.</summary>
    public int TreatmentPercentage { get; set; }

    /// <summary>Bucket salt; required and non-empty.</summary>
    public string? Salt { get; set; }

    /// <summary>Who and where the experiment applies to.</summary>
    public ExperimentTargetingOptions Targeting { get; set; } = new();

    /// <summary>Policy parameters; a missing value takes the <c>RoutingParameters</c> default.</summary>
    public ExperimentParametersOptions Parameters { get; set; } = new();
}

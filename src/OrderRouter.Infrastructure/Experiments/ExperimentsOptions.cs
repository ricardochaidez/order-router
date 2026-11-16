namespace OrderRouter.Infrastructure.Experiments;

/// <summary>
/// The experiment settings as written in <c>experiments.json</c> (SC-25), bound to the configuration <b>root</b> so the
/// <c>Experiments</c> section and <c>Routing:SimilarityBand</c> reload together as one snapshot. Unknown keys are ignored
/// (tolerant reader), and a missing experiment section counts as disabled (fail safe, AB-03).
/// </summary>
public sealed class ExperimentsOptions
{
    /// <summary>The three experiments.</summary>
    public ExperimentsSection Experiments { get; set; } = new();

    /// <summary>The reloadable part of the <c>Routing</c> section. <c>NationwideZipRange</c> is not reloadable and not read here.</summary>
    public RoutingSection Routing { get; set; } = new();

    /// <summary>
    /// Set when the configuration could not be bound (for example text where a number is expected). It is not a
    /// configuration key (the setter is not public); the mapper reports it so a malformed edit is rejected, never thrown.
    /// </summary>
    public string? BindError { get; internal set; }
}

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

/// <summary>Targeting lists. Empty means everyone / everywhere.</summary>
public sealed class ExperimentTargetingOptions
{
    /// <summary>Allowed caller IDs.</summary>
    public List<string> Callers { get; set; } = [];

    /// <summary>ZIP prefixes of 1 to 5 digits.</summary>
    public List<string> ZipPrefixes { get; set; } = [];

    /// <summary>Inclusive ZIP ranges written as <c>NNNNN-NNNNN</c> (no padding is added).</summary>
    public List<string> ZipRanges { get; set; } = [];
}

/// <summary>Experiment parameters; each is read from the experiment that owns it.</summary>
public sealed class ExperimentParametersOptions
{
    /// <summary>EXP-1 only: quality gain that justifies one extra shipment.</summary>
    public decimal? QualityUpliftThreshold { get; set; }

    /// <summary>EXP-2 only: effective rating of an unrated supplier.</summary>
    public decimal? UnratedPrior { get; set; }
}

/// <summary>The reloadable <c>Routing</c> keys.</summary>
public sealed class RoutingSection
{
    /// <summary>Quality band for plans of one shipment count.</summary>
    public decimal? SimilarityBand { get; set; }
}

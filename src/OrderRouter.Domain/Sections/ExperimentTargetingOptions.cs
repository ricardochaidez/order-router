
namespace OrderRouter.Domain.Sections;

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

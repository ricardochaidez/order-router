using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>
/// Numeric values the policies use. They arrive with the assignment so a policy never reads configuration (ADR-0001).
/// </summary>
public sealed record RoutingParameters
{
    /// <summary>Default for <see cref="UnratedPrior"/> (EXP-2).</summary>
    public const decimal DefaultUnratedPrior = 6.5m;

    /// <summary>Default for <see cref="QualityUpliftThreshold"/> (EXP-1).</summary>
    public const decimal DefaultQualityUpliftThreshold = 2.0m;

    /// <summary>Creates parameters, reporting every invalid field in one exception.</summary>
    /// <param name="similarityBand">Quality band for plans of one shipment count; 0 or more.</param>
    /// <param name="qualityUpliftThreshold">Quality gain that justifies one extra shipment (EXP-1); above 0.</param>
    /// <param name="unratedPrior">Effective rating of an unrated supplier under EXP-2; 1 to 10.</param>
    /// <exception cref="DomainValidationException">One or more values are invalid.</exception>
    public RoutingParameters(
        decimal similarityBand = PlanSearchRequest.DefaultSimilarityBand,
        decimal qualityUpliftThreshold = DefaultQualityUpliftThreshold,
        decimal unratedPrior = DefaultUnratedPrior)
    {
        var errors = new List<string>();

        if (similarityBand < 0m)
        {
            errors.Add($"similarity band must not be negative (received {similarityBand})");
        }

        if (qualityUpliftThreshold <= 0m)
        {
            errors.Add($"quality uplift threshold must be greater than 0 (received {qualityUpliftThreshold})");
        }

        if (unratedPrior is < 1m or > 10m)
        {
            errors.Add($"unrated prior must be between 1 and 10 inclusive (received {unratedPrior})");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid routing parameters: {string.Join("; ", errors)}.");
        }

        SimilarityBand = similarityBand;
        QualityUpliftThreshold = qualityUpliftThreshold;
        UnratedPrior = unratedPrior;
    }

    /// <summary>The documented defaults: 0.5, 2.0 and 6.5.</summary>
    public static RoutingParameters Default { get; } = new();

    /// <summary>Quality band for plans of one shipment count.</summary>
    public decimal SimilarityBand { get; }

    /// <summary>Quality gain that justifies one extra shipment (EXP-1, OR-017).</summary>
    public decimal QualityUpliftThreshold { get; }

    /// <summary>Effective rating of an unrated supplier under EXP-2 (OR-018).</summary>
    public decimal UnratedPrior { get; }
}

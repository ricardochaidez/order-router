
namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>Everything the plan search needs: what is demanded, who can supply it, and two policy values.</summary>
public sealed record PlanSearchRequest
{
    /// <summary>One bit per category in a <see cref="uint"/> mask (ADR-0003). Orders above the exact-search limit (<see cref="PlanSearchLimits"/>) use the greedy path, which reuses the same masks.</summary>
    public const int MaxCategories = 32;

    /// <summary>Default for <see cref="SimilarityBand"/> (ADR-0001).</summary>
    public const decimal DefaultSimilarityBand = 0.5m;

    /// <summary>Creates a request, reporting every invalid field in one exception.</summary>
    /// <param name="demand">One entry per demanded category; at least one, no duplicates, at most 32.</param>
    /// <param name="candidates">Eligible suppliers; unique ids. May be empty (then no plan exists).</param>
    /// <param name="similarityBand">How far below the best plan of the same shipment count a plan may be; 0 or more.</param>
    /// <param name="maxExtraShipments">0, or 1 to also return plans with one more shipment than the minimum.</param>
    /// <exception cref="DomainValidationException">One or more fields are invalid.</exception>
    public PlanSearchRequest(
        IEnumerable<CategoryDemand>? demand,
        IEnumerable<SupplierCandidate>? candidates,
        decimal similarityBand = DefaultSimilarityBand,
        int maxExtraShipments = 0)
    {
        var errors = new List<string>();
        var demandList = demand?.ToList() ?? [];
        var candidateList = candidates?.ToList() ?? [];

        if (demandList.Count == 0)
        {
            errors.Add("demand must contain at least one category");
        }

        if (demandList.Count > MaxCategories)
        {
            errors.Add($"demand must contain at most {MaxCategories} categories (received {demandList.Count})");
        }

        if (demandList.Any(d => d is null) || candidateList.Any(c => c is null))
        {
            errors.Add("demand and candidates must not contain null entries");
        }
        else
        {
            foreach (var duplicate in demandList.GroupBy(d => d.Category).Where(g => g.Count() > 1))
            {
                errors.Add($"category '{duplicate.Key}' is demanded more than once");
            }

            foreach (var duplicate in candidateList.GroupBy(c => c.SupplierId, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                errors.Add($"supplier id '{duplicate.Key}' appears more than once");
            }
        }

        if (similarityBand < 0m)
        {
            errors.Add($"similarity band must not be negative (received {similarityBand})");
        }

        if (maxExtraShipments is not (0 or 1))
        {
            errors.Add($"max extra shipments must be 0 or 1 (received {maxExtraShipments})");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid plan search request: {string.Join("; ", errors)}.");
        }

        Demand = demandList.AsReadOnly();
        Candidates = candidateList.AsReadOnly();
        SimilarityBand = similarityBand;
        MaxExtraShipments = maxExtraShipments;
    }

    /// <summary>Per-category demand.</summary>
    public IReadOnlyList<CategoryDemand> Demand { get; }

    /// <summary>Eligible suppliers.</summary>
    public IReadOnlyList<SupplierCandidate> Candidates { get; }

    /// <summary>Quality band, applied within one shipment count only (FR-12).</summary>
    public decimal SimilarityBand { get; }

    /// <summary>0 or 1 extra shipments beyond the minimum (ADR-0001).</summary>
    public int MaxExtraShipments { get; }
}
